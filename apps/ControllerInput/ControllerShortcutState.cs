namespace FloatSoda.Apps.ControllerInput;

/// <summary>ScrollYは上方向が正。両コントローラーが読めない場合はAvailable=falseです。</summary>
public readonly record struct ControllerFrame(bool Available, bool Modifier, bool A, bool B, float ScrollY);
public enum ControllerVoiceCommand { Start, Stop, Send }

/// <summary>修飾キー付きの長押しを、一度の押下につき一回だけ発火させます。</summary>
public sealed class ControllerShortcutState
{
    private ControllerVoiceCommand? _candidate;
    private double _since;
    private bool _armed;

    public ControllerVoiceCommand? Update(ControllerFrame frame, double seconds)
    {
        if (!frame.Available)
        {
            _armed = false;
            _candidate = null;
            return null;
        }
        if (!frame.Modifier || (!frame.A && !frame.B))
        {
            _armed = true;
            _candidate = null;
            return null;
        }
        if (!_armed || MathF.Abs(frame.ScrollY) > 0.3f)
        {
            _candidate = null;
            return null;
        }
        var command = frame.A && frame.B ? ControllerVoiceCommand.Send
            : frame.A ? ControllerVoiceCommand.Start : ControllerVoiceCommand.Stop;
        if (_candidate != command)
        {
            _candidate = command;
            _since = seconds;
        }
        if (seconds - _since < (command == ControllerVoiceCommand.Send ? 1.0 : 0.5)) return null;
        _armed = false;
        _candidate = null;
        return command;
    }
}

/// <summary>修飾キーを押したスクロールを毎秒約7回に制限します。</summary>
public sealed class ControllerScrollState
{
    private double _next;
    private int _previous;
    public int Update(ControllerFrame frame, double seconds)
    {
        var direction = frame.Available && frame.Modifier && !frame.A && !frame.B && MathF.Abs(frame.ScrollY) > 0.3f
            ? -Math.Sign(frame.ScrollY) : 0;
        if (direction == 0) { _previous = 0; _next = 0; return 0; }
        if (direction == _previous && seconds < _next) return 0;
        _previous = direction;
        _next = seconds + 0.15;
        return direction;
    }
}
