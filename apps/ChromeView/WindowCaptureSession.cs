using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.System.WinRT;
using WinRT;

namespace FloatSoda.Samples.ChromeView;

/// <summary>
/// 指定されたトップレベルウィンドウを Windows Graphics Capture で連続的にキャプチャし、
/// 各フレームを<see cref="SKImage"/>として<see cref="FrameUpdated"/>へ流します。
/// </summary>
/// <remarks>
/// <para>
/// 1インスタンスは1ウィンドウを受け持ちます。WGCのフレーム表面は BGRA8 で受けるため、
/// SkiaSharp 側での色変換は不要です(<see cref="SKColorType.Bgra8888"/>と一致します)。
/// フレームレートはコンストラクタ引数の <c>fpsLimit</c> で上限をかけ、
/// それより高い頻度で入ったフレームは破棄されます。
/// </para>
/// <para>
/// スレッドモデル: WGC はフレームをスレッド プールから配送するため、内部処理はすべて
/// WGC 配下のスレッドで行われます。共有リソースは lock で保護されているため、
/// <see cref="Start"/> / <see cref="Dispose"/> は任意のスレッドから呼び出せます。
/// <see cref="FrameUpdated"/> / <see cref="Failed"/> もスレッド プール上で発生します
/// (ウィジェット側が <c>SetState</c> で受け取る、WatchWidget と同じパターン)。
/// </para>
/// </remarks>
public sealed class WindowCaptureSession : IDisposable
{
    /// <summary>新しいフレームが得られたときに発生します。</summary>
    /// <remarks>
    /// <para>
    /// 配信される<see cref="SKImage"/>は本クラスが所有権を持ち、リング 3 枚で破棄されます。
    /// 受信側は即座に描画へするなどして使ってください。
    /// リングがあるため「直近 3 フレームまでの<see cref="SKImage"/>は有効」を前提にできます。
    /// </para>
    /// </remarks>
    public event Action<SKImage>? FrameUpdated;

    /// <summary>キャプチャを続けられなくなったときに発生します。</summary>
    /// <remarks>発生以後はこのセッションを再使用できません。原因は引数の文字列で受け取れます。</remarks>
    public event Action<string>? Failed;

    private readonly int _frameInterval;
    private readonly Queue<FramePinned> _ring = new();
    private readonly object _sync = new();

    private ID3D11Device? _d3dDevice;
    private ID3D11DeviceContext? _context;
    private IDirect3DDevice? _device;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private ID3D11Texture2D? _staging;
    // _stagingWidth/Height と同様にフレーム処理と排他のため _sync で保護する。
    private nint _currentHandle;
    private nint? _pendingTarget;
    private uint _stagingWidth;
    private uint _stagingHeight;
    private long _lastEmitTimestamp;
    private volatile bool _disposed;

    /// <summary>
    /// WinRT の IDirect3DSurface 等の表面からネイティブ D3D テクスチャを引き出す COM インターフェース。
    /// </summary>
    /// <remarks>
    /// IID は公式メタデータ(windowsgraphicsdirectxdirect3d11interop.rdl)の
    /// <c>a9b3d012-3df2-4ee3-b8d1-8695f457d3c1</c>(周知の資料に広く出回る
    /// <c>A9B953AA-…</c> は誤りで、QI に失敗するため注意)。
    /// </remarks>
    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        /// <summary>WinRT の表面に対応するネイティブ COM インターフェースのポインタを取得します。</summary>
        [PreserveSig]
        HRESULT GetInterface(ref Guid iid, out nint texture);
    }

    /// <summary>
    /// <see cref="GraphicsCaptureItem"/> の activation factory から、ウィンドウ単位のキャプチャ対象を
    /// 取得するCOMインターフェースです(WinRT の API 面には等価機能がないためこの形式を使う)。
    /// </summary>
    /// <remarks>
    /// IID は公式メタデータ(Microsoft.Windows.SDK.Win32Metadata / windowsgraphicscaptureinterop.rdl)の
    /// <c>3628E81B-3CAC-4C60-B7F4-23CE0E0C3356</c>。IUnknown を継承するため、
    /// メソッドは vtable の4番目以降に並ぶ。
    /// </remarks>
    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        /// <summary>ウィンドウ単位のキャプチャ対象を取得します。</summary>
        /// <param name="window">対象のトップレベルウィンドウのハンドル。</param>
        /// <param name="itemId">GraphicsCaptureItem のデフォルトインターフェースのIID。</param>
        /// <param name="item">取得した GraphicsCaptureItem (IUnknown) のポインタ。失敗時は0。</param>
        [PreserveSig]
        HRESULT CreateForWindow(nint window, ref Guid itemId, out nint item);
    }

    /// <summary>セッションを構築します。</summary>
    /// <param name="fpsLimit">発行するフレームの上限(fps)。既定は30。</param>
    public WindowCaptureSession(int fpsLimit = 30)
    {
        _fpsLimit = Math.Max(1, fpsLimit);
        _frameInterval = Math.Max(1, 1000 / _fpsLimit);
    }

    private int _fpsLimit;

    /// <summary>キャプチャを開始します。</summary>
    /// <remarks>
    /// 即座に戻り、キャプチャはバックグラウンドで始まります。
    /// 失敗と対象ウィンドウの閉鎖は<see cref="Failed"/>で通知されます。
    /// </remarks>
    /// <remarks>
    /// 開始処理は意図的にスレッド プールへ逃がします。D3D/WGC の COM オブジェクトは
    /// すべて MTA 上で作って MTA 上で使う必要があり、FloatSoda アプリのメインスレッドは
    /// STA (<c>FloatSodaHostedService</c> を参照) のため、ここで直接 RunAsync を走らせると
    /// RCW がアパートメントをまたいで壊れます(<c>--save-frames</c> では MTA のため発生しない)。
    /// </remarks>
    public void Start(nint hwnd) => _ = Task.Run(() => RunAsync(hwnd));

    /// <summary>キャプチャ対象を別のトップレベル ウィンドウへ切り替えます。</summary>
    /// <remarks>
    /// D3D デバイスとフレーム プールは再利用し、GraphicsCaptureItem とセッションだけを張り替えます。
    /// <see cref="Start"/> の初期化が未完了の間の呼び出しは、初期化完了後に反映されます。
    /// 切替に失敗した(対象が取得できなかった)場合は現状のキャプチャが続行されます。
    /// </remarks>
    /// <param name="hwnd">切替先のトップレベル ウィンドウ ハンドル。</param>
    /// <returns>切替が確定した場合は true。</returns>
    public bool SwitchTarget(nint hwnd)
    {
        if (_disposed || hwnd == 0) return false;

        lock (_sync)
        {
            if (_device is null)
            {
                _pendingTarget = hwnd;
                return true;
            }

            return SwitchTargetCore(hwnd);
        }
    }

    /// <summary>現在キャプチャしているウィンドウ ハンドル(<see cref="Start"/>Immediately後は切替先を反映)。</summary>
    public nint CurrentHandle
    {
        get
        {
            lock (_sync)
            {
                return _pendingTarget ?? _currentHandle;
            }
        }
    }

    /// <summary>ロックを保持した状態で対象を張り替えます。失敗時は現状維持(コンソールに記録)。</summary>
    private bool SwitchTargetCore(nint hwnd)
    {
        if (hwnd == _currentHandle) return true;

        var item = CreateCaptureItemForWindow(hwnd);
        if (item is null)
        {
            Console.Error.WriteLine($"[ChromeView] 切替先 (HWND=0x{hwnd:X}) のキャプチャ対象を作成できませんでした。現状を維持します。");
            return false;
        }

        var oldSession = _session;
        var oldPool = _pool;
        var oldItem = _item;

        _session = null;
        _pool = null;
        _item = null;

        oldSession?.Dispose();
        if (oldItem is not null)
        {
            oldItem.Closed -= OnItemClosed;
        }

        _item = item;
        _item.Closed += OnItemClosed;

        // このSDK投影には FramePool.Reconfigure が無いため、プールごと作り直す(初期生成と同じ経路)。
        oldPool?.FrameArrived -= OnFrameArrived;
        oldPool?.Dispose();
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _device!,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            _item.Size);
        _pool.FrameArrived += OnFrameArrived;
        _session = _pool.CreateCaptureSession(_item);
        _session.IsCursorCaptureEnabled = true;
        _session.StartCapture();
        _currentHandle = hwnd;
        return true;
    }

    /// <summary>キャプチャを停止し、保持しているリソースを開放します。</summary>
    /// <remarks>
    /// リングが保持している<see cref="SKImage"/>は、ここで全て破棄されます。
    /// この時点で描画に使用中の画像が完全に解放される保証はありません。
    /// </remarks>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            // プールを先に null 化してから破棄する。既にロック内にいる旧プールの
            // OnFrameArrived に TryGetNextFrame を呼ばせない(ID 一致チェックで先に弾かれる)。
            lock (_sync)
            {
                _session?.Dispose();
                _session = null;
                _pool?.Dispose();
                _pool = null;
                if (_item is not null)
                {
                    _item.Closed -= OnItemClosed;
                    _item = null;
                }
                _device?.Dispose();
                _device = null;
            }
        }
        finally
        {
            if (_context is not null)
            {
                Marshal.ReleaseComObject(_context);
                _context = null;
            }
            if (_d3dDevice is not null)
            {
                Marshal.ReleaseComObject(_d3dDevice);
                _d3dDevice = null;
            }
            DrainRing();
        }
    }

    private async Task RunAsync(nint hwnd)
    {
        try
        {
            EnsureDevice();

            _item = CreateCaptureItemForWindow(hwnd);
            if (_item is null)
            {
                NotifyFailed($"指定されたウィンドウ(HWND=0x{hwnd:X})からキャプチャ対象を取得できませんでした。");
                return;
            }

            _item.Closed += OnItemClosed;

            // CreateFreeThreadedはdispatcherを要求しない。専用dispatcherを用意せずに使える。
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _device!,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                _item.Size);
            _pool.FrameArrived += OnFrameArrived;

            _session = _pool.CreateCaptureSession(_item);
            _session.IsCursorCaptureEnabled = true;
            _session.StartCapture();

            // 開始前に切替が先行していた(デバイス初期化中だった)場合はここで反映する。
            lock (_sync)
            {
                _currentHandle = hwnd;
                if (_pendingTarget is { } pending)
                {
                    _pendingTarget = null;
                    SwitchTargetCore(pending);
                }
            }
        }
        catch (Exception ex)
        {
            NotifyFailed($"キャプチャの開始に失敗しました: {ex}");
            Dispose();
        }
    }

    /// <summary>
    /// WinRT 20H1 (19041) のAPI面に <see cref="GraphicsCaptureItem.CreateForWindowAsync"/> 相当がないため、
    /// activation factory に COM インターフェース <see cref="IGraphicsCaptureItemInterop"/> を結び付けて作る。
    /// </summary>
    private static GraphicsCaptureItem? CreateCaptureItemForWindow(nint hwnd)
    {
        // GraphicsCaptureItem (デフォルトインターフェース) のIID。
        // 注意: typeof(GraphicsCaptureItem).GUID は CsWinRT 投影が合成したハッシュGUIDであるため使えない。
        // 正しいIIDは WinRT 契約 (UniversalApiContract.winmd) の GuidAttribute から取った実物。
        Guid itemId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

        // GraphicsCaptureItem の runtime class 名から activation factory を取得する。
        const string tableName = "Windows.Graphics.Capture.GraphicsCaptureItem";
        using var className = UnsafeCreateHString(tableName);
        var factoryObject = CreateActivationFactoryObject(className);
        try
        {
            var interop = (IGraphicsCaptureItemInterop)factoryObject;
            var hr = interop.CreateForWindow(hwnd, ref itemId, out nint itemUnk);
            hr.ThrowOnFailure();
            if (itemUnk == 0) return null;

            try
            {
                // 投影型への変換は投影型自身の FromAbi 経由(MarshalInterface.FromAbi はCCW構築で失敗した)。
                return GraphicsCaptureItem.FromAbi(itemUnk);
            }
            finally
            {
                Marshal.Release(itemUnk);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factoryObject);
        }
    }

    /// <summary>ランタイムクラス名から activation factory (RCW) を取得します。失敗時は例外を投げます。</summary>
    private static unsafe object CreateActivationFactoryObject(Windows.Win32.WindowsDeleteStringSafeHandle className)
    {
        Guid factoryIid = typeof(IActivationFactory).GUID;
        var factoryIidPtr = &factoryIid;
        Marshal.ThrowExceptionForHR(PInvoke.RoGetActivationFactory(TransformHString(className), factoryIidPtr, out object factory));
        return factory;
    }

    private static unsafe Windows.Win32.WindowsDeleteStringSafeHandle UnsafeCreateHString(string value)
    {
        Marshal.ThrowExceptionForHR(PInvoke.WindowsCreateString(value, (uint)value.Length, out var handle));
        return handle;
    }

    private static Windows.Win32.System.WinRT.HSTRING TransformHString(Windows.Win32.WindowsDeleteStringSafeHandle safeHandle) =>
        new(safeHandle.DangerousGetHandle());

    private void OnItemClosed(GraphicsCaptureItem captureItem, object? args) =>
        NotifyFailed("キャプチャ対象のウィンドウが閉じられました。");

    private void EnsureDevice()
    {
        lock (_sync)
        {
            if (_device is not null) return;

            // WGCとの整合性のため、デバイスは常にBGRAサポート付きで作る。
            var flags = D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT;

            unsafe
            {
                var hr = PInvoke.D3D11CreateDevice(
                    null,
                    D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
                    default,
                    flags,
                    null,
                    0,
                    // D3D11_SDK_VERSION は 7 (Win8 以降で不変)。
                    7,
                    out _d3dDevice,
                    null,
                    out _context);
                hr.ThrowOnFailure();
            }

            _device = CreateWinRTDevice(_d3dDevice!);
        }
    }

    private static IDirect3DDevice CreateWinRTDevice(ID3D11Device d3dDevice)
    {
        var dxgiDevice = (IDXGIDevice)d3dDevice;

        Marshal.ThrowExceptionForHR(PInvoke.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out Windows.Win32.System.WinRT.IInspectable winrtUnknown));

        // WinRT API へ渡すには投影型に変換しておく必要がある(RCW のまま渡すと CCW 構築に失敗する)。
        var abi = Marshal.GetIUnknownForObject(winrtUnknown);
        try
        {
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(abi);
        }
        finally
        {
            Marshal.Release(abi);
        }
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool pool, object? args)
    {
        if (_disposed) return;
        try
        {
            lock (_sync)
            {
                // 対象切替で破棄された旧プールから遅れて届いたイベントは無視する。
                // (旧プールの Dispose と GPU 側のイベント配送が競合すると、ここでの
                // TryGetNextFrame が ObjectDisposedException を出すため。)
                if (!ReferenceEquals(pool, _pool)) return;

                // 複数たまっている場合は最新1枚だけ見ればよいので、残りは破棄する。
                Direct3D11CaptureFrame? latest = null;
                while (true)
                {
                    var next = pool.TryGetNextFrame();
                    if (next is null) break;
                    latest?.Dispose();
                    latest = next;
                }
                if (latest is null) return;

                try
                {
                    var timestamp = Stopwatch.GetTimestamp();
                    var elapsedMs = _lastEmitTimestamp == 0
                        ? double.PositiveInfinity
                        : (timestamp - _lastEmitTimestamp) * 1000d / Stopwatch.Frequency;

                    // fpsLimit 未満の間隔は描画を起こさずに捨ててGPU負荷を抑える。
                    if (elapsedMs < _frameInterval) return;
                    _lastEmitTimestamp = timestamp;

                    var image = ExtractFrame(latest);
                    if (image is not null) FrameUpdated?.Invoke(image);
                }
                finally
                {
                    latest.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            NotifyFailed($"フレーム処理に失敗しました: {ex}");
        }
    }

    /// <summary>
    /// WGCの表面を Skia 画像に変換します。GPU 表面 → staging テクスチャ (CPU可読) → byte 配列という流れです。
    /// </summary>
    /// <remarks>
    /// 投影ラッパ経由の <c>frame.Surface</c> は CsWinRT が管理オブジェクト(CCW)を返すため、
    /// ネイティブのフレーム ABI ポインタに落として <see cref="IDirect3D11CaptureFrameNative"/> 経由で
    /// 生のIDirect3DSurface ポインタを取り出します。
    /// </remarks>
    private SKImage? ExtractFrame(Direct3D11CaptureFrame frame)
    {
        var tex = (ID3D11Texture2D)Marshal.GetObjectForIUnknown(GetNativeTexturePointer(frame))!;
        try
        {
            tex.GetDesc(out var desc);

            EnsureStaging(desc);
            _context!.CopyResource(_staging!, tex);

            _context.Map(_staging!, 0, D3D11_MAP.D3D11_MAP_READ, 0, out var mapped);
            try
            {
                int length = checked((int)mapped.RowPitch * (int)desc.Height);
                var buffer = ArrayPool<byte>.Shared.Rent(length);
                unsafe
                {
                    Marshal.Copy((nint)mapped.pData, buffer, 0, length);
                }

                var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                var info = new SKImageInfo((int)desc.Width, (int)desc.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                var image = SKImage.FromPixels(info, pinned.AddrOfPinnedObject(), (int)mapped.RowPitch)
                            ?? throw new InvalidOperationException("SKImageの生成に失敗しました。");
                EnqueueToRing(buffer, pinned, image);
                return image;
            }
            finally
            {
                _context.Unmap(_staging!, 0);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(tex);
        }
    }

    private unsafe nint GetNativeTexturePointer(Direct3D11CaptureFrame frame)
    {
        // 投影ラッパの frame から生の ABI ポインタを取り出し、COM 経路で表面を取得する。
        nint frameAbi = ((IWinRTObject)frame).NativeObject.ThisPtr;
        var frameNative = (IDirect3D11CaptureFrameNative)Marshal.GetObjectForIUnknown(frameAbi);
        Marshal.ThrowExceptionForHR(frameNative.Surface(out var surfaceAbi));
        try
        {
            var access = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(surfaceAbi);
            Guid iid = typeof(ID3D11Texture2D).GUID;
            Marshal.ThrowExceptionForHR(access.GetInterface(ref iid, out var texture));
            return texture;
        }
        finally
        {
            Marshal.Release(surfaceAbi);
        }
    }

    private void EnsureStaging(D3D11_TEXTURE2D_DESC desc)
    {
        // OnFrameArrivedが常にlockの中から呼び出すため、ここで二重lockは要らない。
        if (_staging is not null && _stagingWidth == desc.Width && _stagingHeight == desc.Height) return;

        if (_staging is not null)
        {
            Marshal.ReleaseComObject(_staging);
            _staging = null;
        }

        var stagingDesc = new D3D11_TEXTURE2D_DESC
        {
            Width = desc.Width,
            Height = desc.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = desc.Format,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
            Usage = D3D11_USAGE.D3D11_USAGE_STAGING,
            BindFlags = 0,
            CPUAccessFlags = D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ,
            MiscFlags = 0,
        };
        _d3dDevice!.CreateTexture2D(stagingDesc, null, out _staging);
        _stagingWidth = desc.Width;
        _stagingHeight = desc.Height;
    }

    /// <summary>画像とピン留めをリングに紐づけて登録します。リングの上限を超えたものは破棄します。</summary>
    private void EnqueueToRing(byte[] buffer, GCHandle pinned, SKImage image)
    {
        var entry = new FramePinned(buffer, pinned, image);
        _ring.Enqueue(entry);
        while (_ring.Count > 3)
        {
            var released = _ring.Dequeue();
            released.Image.Dispose();
            released.Pin.Free();
            ArrayPool<byte>.Shared.Return(released.Buffer);
        }
    }

    private void DrainRing()
    {
        while (_ring.Count > 0)
        {
            var entry = _ring.Dequeue();
            entry.Image.Dispose();
            entry.Pin.Free();
            ArrayPool<byte>.Shared.Return(entry.Buffer);
        }
    }

    private void NotifyFailed(string message) => Failed?.Invoke(message);

    private sealed record FramePinned(byte[] Buffer, GCHandle Pin, SKImage Image);

    /// <summary>
    /// <see cref="Direct3D11CaptureFrame"/> の生 ABI ポインタを扱う COM インターフェース。
    /// vtable 順に先頭3メソッド(SystemRelativeTime get/put、ContentSize)を宣言してから
    /// Surface を呼び出す必要がある。
    /// IID fa50c623-38da-4b32-acf3-fa9734ad800e は UniversalApiContract.winmd の GuidAttribute から取得。
    /// </summary>
    [ComImport]
    [Guid("FA50C623-38DA-4B32-ACF3-FA9734AD800E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3D11CaptureFrameNative
    {
        /// <summary>フレームのシステム相対時刻 (propget)。</summary>
        [PreserveSig]
        HRESULT get_SystemRelativeTime(out long value);

        /// <summary>フレームのシステム相対時刻 (propput)。</summary>
        [PreserveSig]
        HRESULT put_SystemRelativeTime(in long value);

        /// <summary>フレームの内容サイズ (propget)。</summary>
        [PreserveSig]
        HRESULT get_ContentSize(out SizeInt32 value);

        /// <summary>GPU 表面 (propget) の生ポインタを取得します。</summary>
        [PreserveSig]
        HRESULT Surface(out nint surface);
    }
}
