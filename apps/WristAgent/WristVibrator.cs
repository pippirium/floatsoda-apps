using System.Text;
using FloatSoda.OVR;
using FloatSoda.OVR.Overlay;

namespace FloatSoda.Samples.WristAgent;

/// <summary>
/// 要求が腕 UI へ届いたときにコントローラーを振動させる触覚通知。
/// </summary>
/// <remarks>
/// FloatSoda の Widget ツリーからはオーバーレイハンドルが隠されているため、
/// 本サンプルは <c>OpenVR.Overlay.FindOverlay</c> で自身のオーバーレイを探します。
/// キーは <c>WindowKeyGenerator</c>(internal)と同一規約のスネークケースを複製して
/// 算出します(CrromeView サンプルと同じサンプル内複製パターン)。
/// </remarks>
public sealed class WristVibrator : IDisposable
{
    private readonly ApprovalBroker _broker;
    private OverlayVibration? _vibration;

    public WristVibrator(ApprovalBroker broker)
    {
        _broker = broker;
        broker.Changed += VibrateIfPending;
    }

    private void VibrateIfPending()
    {
        if (_broker.Pending.Count == 0) return;

        if (!EnsureVibration()) return;

        try
        {
            _vibration!.Trigger(TimeSpan.FromMilliseconds(120), 1200f, 0.6f);
        }
        catch (Exception e)
        {
            Console.WriteLine($"振動送出に失敗しました: {e.Message}");
        }
    }

    private bool EnsureVibration()
    {
        if (_vibration is not null) return true;

        var handle = FindSelfOverlayHandle();
        if (handle == 0) return false;

        _vibration = new OverlayVibration(handle);
        return true;
    }

    private static ulong FindSelfOverlayHandle()
    {
        var key = $"{ToSnakeCase(AssemblyName)}.wrist_agent";
        var handle = 0UL;
        return OpenVR.Overlay.FindOverlay(key, ref handle) == EVROverlayError.None ? handle : 0;
    }

    private static string AssemblyName =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "wrist_agent_app";

    // src/FloatSoda/Core/WindowKeyGenerator.cs (internal) と同一規約のスネークケース化。
    // サンプル内複製: コアの internal API に依存しないため。
    private static string ToSnakeCase(string text)
    {
        var builder = new StringBuilder(text.Length + 8);

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

    public void Dispose()
    {
        _broker.Changed -= VibrateIfPending;
        _vibration = null;
    }
}
