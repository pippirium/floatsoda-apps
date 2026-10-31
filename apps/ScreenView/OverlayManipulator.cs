using System.Numerics;
using FloatSoda.Apps.ControllerInput;
using System.Reflection;
using System.Runtime.InteropServices;
using FloatSoda.OVR;
using FloatSoda.OVR.Exceptions;
using FloatSoda.OVR.Math;
using FloatSoda.OVR.Overlay;

namespace FloatSoda.Apps.ScreenView;

/// <summary>
/// ワールド空間に浮かぶオーバーレイ パネルをコントローラで操作する入力ループ。
/// </summary>
/// <remarks>
/// <para>操作割り当て(<c>--toggle</c> / <c>--switch</c> で変更可能な長押し割当と固定割り当て。SteamVR 入力経由ではない素ポーリング):</para>
/// <list type="bullet">
/// <item>グリップ(左右どちらでも) — パネルを掴んで移動。握っている間、そのコントローラの移動差分がそのままパネルの平行移動になる</item>
/// <item>右スティック上下(掴んでいないとき) — パネル幅を連続的に拡縮(高さはソース縦横比に自動追従)</item>
/// <item>左 グリップ+スティック押し込み 長押し(0.7 秒) — パネルの表示/非表示(<c>--toggle</c> で変更可)</item>
/// <item>右 グリップ+スティック押し込み 長押し(0.3 秒) — キャプチャ対象を選ぶピッカーの開閉(<c>--switch</c> で変更可)</item>
/// </list>
/// <para>
/// SteamVR の入力(SteamVR Input)を使わないため VRChat などへの入力横取りは発生しません
/// (<c>ControllerPointerSystem</c> と同じ素ポーリングの思想)。FloatSoda 本体は変更せず
/// <c>OpenVR.Overlay.FindOverlay</c> で自アプリのオーバーレイ ハンドルを引き合わせて
/// 変換・幅 API を直接呼びます。
/// </para>
/// <para>
/// ポーリングはバックグラウンド スレッドで回します。SteamVR が切断している間は
/// 例外を握って再試行します。
/// </para>
/// </remarks>
public sealed class OverlayManipulator : IDisposable
{
    private readonly VrControllerReader _shortcutReader = new();
    private readonly ControllerScrollState _scrollState = new();
    public event Action<int>? ScrollRequested;
    private static readonly ulong GripMask = 1UL << (int)EVRButtonId.k_EButton_Grip;
    private static readonly ulong MenuMask = 1UL << (int)EVRButtonId.k_EButton_ApplicationMenu;
    private static readonly ulong StickClickMask = 1UL << (int)EVRButtonId.k_EButton_Axis1;
    private static readonly uint ControllerStateSize = (uint)Marshal.SizeOf<VRControllerState_t>();

    /// <summary>スティック倒しを無視する範囲(±の遊び)。</summary>
    private const float StickDeadZone = 0.15f;

    /// <summary>スティック全倒し時の幅の変化量(m/s)。</summary>
    private const float WidthChangeSpeed = 0.5f;

    /// <summary>幅の下限(m)。これ未満にはしない(視認できなくなるため)。</summary>
    private const float MinWidth = 0.15f;

    /// <summary>幅の上限(m)。実用範囲での安全止め。</summary>
    private const float MaxWidth = 4f;

    /// <summary>OpenVR と手動指定幅の差がこの値以下なら再設定しない(m)。</summary>
    private const float WidthRestoreTolerance = 0.001f;

    private readonly string _overlayKey;
    private readonly string _overlayName;
    private readonly ToggleBinding _toggle;
    private readonly ToggleBinding _switch;
    private readonly bool _switchEnabled = true;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _sync = new();
    private readonly object _lifecycleSync = new();
    private Task? _runTask;
    private bool _disposed;

    private float _toggleHold;
    private bool _toggleFired;
    private float _switchHold;
    private bool _switchFired;

    private ulong _handle = OpenVR.k_ulOverlayHandleInvalid;
    private Matrix4x4 _baseline;
    private ETrackingUniverseOrigin _origin = ETrackingUniverseOrigin.TrackingUniverseStanding;
    private uint _dragDeviceIndex = OpenVR.k_unTrackedDeviceIndexInvalid;
    private Vector3 _lastDevicePosition;
    private long _lastTick;

    /// <summary>
    /// スティックで指定した物理幅(m)。未操作なら null として engine の自動サイズ設定に任せる。
    /// キャプチャ解像度の変更で engine が幅を再計算しても、この値は変更しない。
    /// </summary>
    private float? _userWidthInMeters;
    private bool _handleMissingLogged;
    private bool _errorLogged;

    /// <summary>ピッカー開閉要求(<c>--switch</c> 割当の長押し成立時)。</summary>
    public event Action? TargetSwitchRequested;

    /// <summary>オーバーレイ パネルを操作可能にします。</summary>
    /// <param name="overlayTitle">対象オーバーレイのタイトル(<see cref="WindowWidget.Title"/>)。キーはこれから導出します。</param>
    public OverlayManipulator(string overlayTitle, string? toggleSpec = "L:stick+grip@700", string? switchSpec = "R:stick+grip@300")
    {
        _overlayName = overlayTitle;
        _toggle = ParseToggleSpec(toggleSpec ?? "");
        _switch = ParseToggleSpec(switchSpec ?? "");
        // 同一割当だと長押し成立時に両方発火してしまうため、ピッカー開閉側を無効化して告知する。
        if (_switch == _toggle)
        {
            _switchEnabled = false;
            Console.Error.WriteLine("[OverlayManipulator] ピッカー開閉(--switch)の割当がパネル表示切替(--toggle)と同一のため、ピッカー開閉を無効にします。");
        }
        // FloatSodaのWindowKeyGeneratorと同じ規則(エントリアセンブリ名 + タイトルのスネークケース)。
        var assemblyName = Assembly.GetEntryAssembly()?.GetName().Name ?? "sample_app";
        _overlayKey = $"{ToSnakeCase(assemblyName)}.{ToSnakeCase(overlayTitle)}";
    }

    /// <summary>入力ループをバックグラウンドで開始します。</summary>
    public void Start()
    {
        lock (_lifecycleSync)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(OverlayManipulator));
            _runTask ??= Task.Run(RunAsync);
        }
    }

    /// <summary>入力ループを停止し、実行中の OpenVR 呼び出しが終わるまで待機します。</summary>
    public void StopAndWait()
    {
        Task? runTask;
        lock (_lifecycleSync)
        {
            if (_disposed) return;
            _disposed = true;
            runTask = _runTask;
            try
            {
                _cts.Cancel();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"[OverlayManipulator] 停止通知に失敗しました: {exception.Message}");
            }
        }

        try
        {
            runTask?.GetAwaiter().GetResult();
        }
        finally
        {
            _cts.Dispose();
        }
    }

    /// <summary>入力ループを停止し、実行中の OpenVR 呼び出しが終わるまで待機します。</summary>
    public void Dispose() => StopAndWait();

    private async Task RunAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    Poll();
                }
                catch (Exception ex)
                {
                    // 起動直後は System/Overlay インターフェースが暗黙初期化で失敗することがある。
                    // 一度だけ出して、以降は静かに再試行する。
                    if (!_errorLogged)
                    {
                        _errorLogged = true;
                        Console.Error.WriteLine($"[OverlayManipulator] 一時的なポーリング失敗(再試行します): {ex.Message}");
                    }
                }

                await Task.Delay(15, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 停止要求は正常系。
        }
    }

    private void Poll()
    {
        if (_handle == OpenVR.k_ulOverlayHandleInvalid && !TryFindOverlay()) return;

        var poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        OpenVR.System.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0f, poses);

        var leftIndex = TrackedDevice.LeftController.ResolveIndex();
        var rightIndex = TrackedDevice.RightController.ResolveIndex();
        var left = ReadControllerState(leftIndex);
        var right = ReadControllerState(rightIndex);
        var dt = ComputeDt();

        // ---- 掴み移動(左を優先。両方握った場合は最初の手を維持) ----
        var leftGrip = left is { } leftState && (leftState.ulButtonPressed & GripMask) != 0;
        var rightGrip = right is { } rightState && (rightState.ulButtonPressed & GripMask) != 0;

        if (_dragDeviceIndex != OpenVR.k_unTrackedDeviceIndexInvalid)
        {
            var gripHeld = _dragDeviceIndex == leftIndex ? leftGrip
                : _dragDeviceIndex == rightIndex ? rightGrip
                : false;

            if (!gripHeld)
            {
                // 放す。ベースラインは最新の絶対座標のまま残る。
                _dragDeviceIndex = OpenVR.k_unTrackedDeviceIndexInvalid;
            }
            else if (TryGetDevicePosition(_dragDeviceIndex, poses, out var position))
            {
                _baseline.Translation += position - _lastDevicePosition;
                _lastDevicePosition = position;
                WriteTransform();
            }
        }
        else if (leftGrip)
        {
            TryStartDrag(leftIndex, poses);
        }
        else if (rightGrip)
        {
            TryStartDrag(rightIndex, poses);
        }

        // ---- 右スティックの幅調整と、キャプチャ解像度変更後の手動幅復元 ----
        var shortcutFrame = _shortcutReader.Read();
        var scroll = _scrollState.Update(shortcutFrame, Environment.TickCount64 / 1000.0);
        if (scroll != 0) ScrollRequested?.Invoke(scroll);
        // スクロール用修飾キーを押している間は幅を変えない。手動幅の復元は継続する。
        ApplyOverlayWidth(shortcutFrame.Modifier ? null : right, dt);

        // ---- 長押し割当: パネル表示/非表示(--toggle)、ピッカー開閉(--switch) ----
        ApplyHoldBinding(_toggle, left, right, dt, ref _toggleHold, ref _toggleFired, TogglePanelVisible);
        if (_switchEnabled)
        {
            ApplyHoldBinding(_switch, left, right, dt, ref _switchHold, ref _switchFired, RequestTargetSwitch);
        }
    }

    /// <summary>
    /// 右スティックではオーバーレイの「物理幅」だけを変更する。
    /// 高さは OpenVR がキャプチャテクスチャの縦横比から決めるため、画像は引き伸ばされない。
    /// </summary>
    /// <remarks>
    /// engine の OverlayWindow.Resize は画像のピクセル幅 / Dpm から物理幅を再設定する。
    /// 手動変更後は希望幅を保持し、キャプチャ先の切替・リサイズで上書きされた場合だけ再適用する。
    /// スティック操作中の基準にも希望幅を使い、engine の一時的な上書きで幅が飛ぶのを防ぐ。
    /// 一度も手動変更していない間は engine 側の初期幅・自動更新を尊重する。
    /// </remarks>
    private void ApplyOverlayWidth(VRControllerState_t? right, float dt)
    {
        // 掴み移動中は右スティックで拡縮しないが、既存の手動幅は引き続き維持する。
        // Quest系は旧 OpenVR 規約(上=−y)で来るため符号を反転し、「上=拡大」に揃える。
        var y = _dragDeviceIndex == OpenVR.k_unTrackedDeviceIndexInvalid && right is { } stick
            ? -ReadThumbstickY(stick)
            : 0f;
        var isResizing = MathF.Abs(y) > StickDeadZone;
        if (!isResizing && _userWidthInMeters is null) return;

        var actualWidth = 0f;
        OpenVR.Overlay.GetOverlayWidthInMeters(_handle, ref actualWidth).ThrowIfError();
        if (!float.IsFinite(actualWidth) || actualWidth <= 0f) return;

        if (isResizing)
        {
            var baseline = _userWidthInMeters ?? actualWidth;
            _userWidthInMeters = Math.Clamp(baseline + y * WidthChangeSpeed * dt, MinWidth, MaxWidth);
        }

        // エンジンの再描画・解像度変更で物理幅が戻ったときだけ書き戻す。
        // 毎ポーリングで Set すると不要な OpenVR 呼び出しが発生するため差分を確認する。
        if (_userWidthInMeters is { } preferred &&
            MathF.Abs(preferred - actualWidth) > WidthRestoreTolerance)
        {
            OpenVR.Overlay.SetOverlayWidthInMeters(_handle, preferred).ThrowIfError();
        }
    }

    /// <summary>長押し割当(--toggle / --switch 形式)の判定を実行します。</summary>
    /// <remarks>
    /// ボタンが(指定あればグリップも)押され続けた時間が <c>HoldMs</c> 以上になった
    /// 一度だけ <paramref name="fire"/> を呼びます。ホールド必須の判定にすることにより、
    /// 他アプリ(VRChat など)の単発ボタンと被っても事故的に発火しない構造にしてあります。
    /// </remarks>
    private void ApplyHoldBinding(ToggleBinding binding, VRControllerState_t? left, VRControllerState_t? right, float dt, ref float holdSeconds, ref bool fired, Action fire)
    {
        var state = binding.LeftHand ? left : right;
        if (state is null)
        {
            holdSeconds = 0f;
            fired = false;
            return;
        }

        var buttonHeld = (state.Value.ulButtonPressed & binding.ButtonMask) != 0;
        var gripHeld = (state.Value.ulButtonPressed & GripMask) != 0;
        // +grip 指定時は「ボタンを押しながらグリップも握り」の組合せのみを有効入力とする。
        var engaged = buttonHeld && (!binding.RequireGrip || gripHeld);

        if (!engaged)
        {
            holdSeconds = 0f;
            fired = false;
            return;
        }

        holdSeconds += dt;
        if (fired || holdSeconds * 1000f < binding.HoldMs) return;

        fired = true;
        fire();
    }

    /// <summary>ピッカー開閉(--switch)の割当が成立したときの処理。軽く振動して開閉を要求します。</summary>
    private void RequestTargetSwitch()
    {
        var hapticDevice = TrackedDevice.RightController.ResolveIndex();
        if (hapticDevice != OpenVR.k_unTrackedDeviceIndexInvalid)
        {
            OpenVR.System.TriggerHapticPulse(hapticDevice, 0, 500);
        }
        TargetSwitchRequested?.Invoke();
    }

    /// <summary>パネルの表示/非表示を切り替えます。</summary>
    private void TogglePanelVisible()
    {
        var visible = OpenVR.Overlay.IsOverlayVisible(_handle);
        if (visible)
        {
            OpenVR.Overlay.HideOverlay(_handle).ThrowIfError();
            Console.WriteLine("[ScreenView] パネルを非表示にしました(再度コマンドで表示)");
        }
        else
        {
            OpenVR.Overlay.ShowOverlay(_handle).ThrowIfError();
            Console.WriteLine("[ScreenView] パネルを表示しました");
        }
    }

    /// <summary><c>--toggle</c> で受け付ける割当の解析結果。</summary>
    /// <param name="LeftHand">ボタンを判定する手(true=左)。</param>
    /// <param name="ButtonMask">発火対象ボタンのビットマスク。</param>
    /// <param name="RequireGrip">グリップ同時押しを必須とするか。</param>
    /// <param name="HoldMs">発火までに押し続ける時間(ms)。</param>
    private readonly record struct ToggleBinding(bool LeftHand, ulong ButtonMask, bool RequireGrip, int HoldMs);

    /// <summary>
    /// トグル割当の文字列を解析します。形式: <c>&lt;手&gt;:&lt;ボタン&gt;[+grip][@&lt;ms&gt;]</c>
    /// 例: <c>L:stick+grip</c> / <c>R:menu</c> / <c>R:trigger@500</c>。
    /// 手は L|R、ボタンは stick(スティック押し込み)| a | trigger | menu | grip。
    /// </summary>
    private static ToggleBinding ParseToggleSpec(string spec)
    {
        var invalid = $"トグル割当(--toggle)の形式が不正です: \"{spec}\"。例: L:stick+grip@700 / R:menu@0 の手:";
        if (string.IsNullOrWhiteSpace(spec)) throw new ArgumentException(invalid);
        spec = spec.Trim();

        var colon = spec.IndexOf(':');
        if (colon != 1) throw new ArgumentException(invalid);

        var leftHand = spec[0] is 'L' or 'l';
        if (!leftHand && spec[0] is not ('R' or 'r')) throw new ArgumentException(invalid);

        var rest = spec[2..];
        var at = rest.IndexOf('@');
        var HoldMs = 0;
        if (at >= 0)
        {
            if (!int.TryParse(rest[(at + 1)..], out HoldMs) || HoldMs < 0) throw new ArgumentException(invalid);
            rest = rest[..at];
        }

        var tokens = rest.Split('+');
        if (tokens.Length is < 1 or > 2) throw new ArgumentException(invalid);

        // 第2トークンは grip 修飾子のみ(「ボタン+grip」の形を許す。grip を2つ書くのは不可)。
        var requireGrip = false;
        if (tokens.Length == 2)
        {
            if (!tokens[1].Trim().Equals("grip", StringComparison.OrdinalIgnoreCase) || tokens[0].Trim().Equals("grip", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(invalid);
            requireGrip = true;
            tokens = tokens[..1];
        }

        // grip が修飾子の場合はボタン マスクに混ぜない(混ゼると「グリップだけで発火」になる)。
        ulong mask = 0;
        foreach (var token in tokens)
        {
            switch (token.Trim().ToLowerInvariant())
            {
                case "stick":
                    mask |= StickClickMask;
                    break;
                case "trigger":
                    mask |= 1UL << (int)EVRButtonId.k_EButton_SteamVR_Trigger;
                    break;
                case "a":
                    mask |= 1UL << (int)EVRButtonId.k_EButton_A;
                    break;
                case "menu":
                    mask |= MenuMask;
                    break;
                case "grip":
                    mask |= GripMask;
                    break;
                default:
                    throw new ArgumentException(invalid);
            }
        }

        return new ToggleBinding(leftHand, mask, requireGrip, HoldMs);
    }

    /// <summary>スティック(縦倒し)を取得する。Quest系ドライバでは axis0(x,y) が実体で、
    /// axis1 は旧規約の重複割当のため値が落ちていることがある。どちらかに値がある軸を用いる。</summary>
    private static float ReadThumbstickY(VRControllerState_t state) =>
        MathF.Abs(state.rAxis0.x) + MathF.Abs(state.rAxis0.y) > 0.01f ? state.rAxis0.y : state.rAxis1.y;

    /// <summary>自アプリのオーバーレイが見つかったか。見つからない間は false を返して再試行します。</summary>
    private bool TryFindOverlay()
    {
        ulong handle = OpenVR.k_ulOverlayHandleInvalid;
        var error = OpenVR.Overlay.FindOverlay(_overlayKey, ref handle);
        if (error != EVROverlayError.None)
        {
            if (!_handleMissingLogged)
            {
                _handleMissingLogged = true;
                Console.Error.WriteLine($"[OverlayManipulator] オーバーレイ ({_overlayKey}) が未作成のため、作成を待っています…");
            }
            return false;
        }

        Console.Error.WriteLine($"[OverlayManipulator] オーバーレイ ({_overlayName}) の操作を開始しました");
        _handle = handle;
        return true;
    }

    private bool TryStartDrag(uint deviceIndex, TrackedDevicePose_t[] poses)
    {
        var origin = ETrackingUniverseOrigin.TrackingUniverseStanding;
        var matrix = new HmdMatrix34_t();
        OpenVR.Overlay.GetOverlayTransformAbsolute(_handle, ref origin, ref matrix).ThrowIfError();
        if (!TryGetDevicePosition(deviceIndex, poses, out var position)) return false;

        _origin = origin;
        _baseline = matrix.ToMatrix4x4();
        _lastDevicePosition = position;
        _dragDeviceIndex = deviceIndex;
        return true;
    }

    /// <summary>ドラッグ結果(ベースラインの平行移動)を OpenVR へ書き戻します。</summary>
    private void WriteTransform()
    {
        // 平行移動のみ更新しているので回転はドラッグ開始時のまま(スティック調整は幅のみ)。
        var matrix = _baseline.ToHmdMatrix34_t();
        OpenVR.Overlay.SetOverlayTransformAbsolute(_handle, _origin, ref matrix).ThrowIfError();
    }

    private static VRControllerState_t? ReadControllerState(uint deviceIndex)
    {
        if (deviceIndex == OpenVR.k_unTrackedDeviceIndexInvalid) return null;

        var state = default(VRControllerState_t);
        // ボタンビットは role 割当のある実機コントローラ前提。未接続は false を返す。
        return OpenVR.System.GetControllerState(deviceIndex, ref state, ControllerStateSize) ? state : null;
    }

    private static bool TryGetDevicePosition(uint deviceIndex, TrackedDevicePose_t[] poses, out Vector3 position)
    {
        position = default;
        if (deviceIndex >= poses.Length) return false;

        var pose = poses[deviceIndex];
        if (!pose.bPoseIsValid) return false;

        var matrix = pose.mDeviceToAbsoluteTracking.ToMatrix4x4();
        position = matrix.Translation;
        return true;
    }

    private float ComputeDt()
    {
        var now = Environment.TickCount64;
        var dt = _lastTick == 0 ? 0.016f : Math.Min((now - _lastTick) / 1000f, 0.1f);
        _lastTick = now;
        return dt;
    }

    private static string ToSnakeCase(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length + 8);

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (char.IsUpper(c))
            {
                var isBoundary = i > 0 &&
                                 (char.IsLower(text[i - 1]) || char.IsDigit(text[i - 1]) ||
                                  (char.IsUpper(text[i - 1]) && i + 1 < text.Length && char.IsLower(text[i + 1])));

                if (isBoundary && builder.Length > 0 && builder[^1] != '_')
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }
        }

        if (builder.Length > 0 && builder[^1] == '_')
        {
            builder.Length--;
        }

        return builder.ToString();
    }
}
