using System.Runtime.InteropServices;
using FloatSoda.OVR;
using FloatSoda.OVR.Input;
using FloatSoda.OVR.Overlay;

namespace FloatSoda.Apps.ControllerInput;

/// <summary>OpenVR初期化後に使用します。ジョイスティックの軸番号をデバイスの宣言から取得します。</summary>
public sealed class VrControllerReader
{
    private static readonly uint StateSize = (uint)Marshal.SizeOf<VRControllerState_t>();
    private static readonly ulong Grip = 1UL << (int)EVRButtonId.k_EButton_Grip;
    private static readonly ulong A = 1UL << (int)EVRButtonId.k_EButton_A;
    // Oculus Touchの右Bは旧OpenVRのApplicationMenuとして公開される。
    private static readonly ulong B = 1UL << (int)EVRButtonId.k_EButton_ApplicationMenu;
    private uint _left = OpenVR.k_unTrackedDeviceIndexInvalid;
    private uint _right = OpenVR.k_unTrackedDeviceIndexInvalid;
    private int _leftAxis = -1;
    private int _rightAxis = -1;

    public ControllerFrame Read()
    {
        var leftIndex = TrackedDevice.LeftController.ResolveIndex();
        var rightIndex = TrackedDevice.RightController.ResolveIndex();
        if (leftIndex != _left || _leftAxis < 0)
        {
            _left = leftIndex;
            _leftAxis = FindStick(leftIndex);
            if (_leftAxis >= 0) Console.WriteLine($"[ControllerShortcuts] 左スティック: axis{_leftAxis}");
        }
        if (rightIndex != _right || _rightAxis < 0)
        {
            _right = rightIndex;
            _rightAxis = FindStick(rightIndex);
            if (_rightAxis >= 0) Console.WriteLine($"[ControllerShortcuts] 右スティック: axis{_rightAxis}");
        }
        if (_leftAxis < 0 || _rightAxis < 0) return default;
        var left = default(VRControllerState_t);
        var right = default(VRControllerState_t);
        if (!OpenVR.System.GetControllerState(leftIndex, ref left, StateSize) ||
            !OpenVR.System.GetControllerState(rightIndex, ref right, StateSize)) return default;
        var leftStick = Axis(left, _leftAxis);
        var rightStick = Axis(right, _rightAxis);
        var click = 1UL << ((int)EVRButtonId.k_EButton_Axis0 + _leftAxis);
        var modifier = (left.ulButtonPressed & click) != 0
            && MathF.Abs(leftStick.x) < 0.3f && MathF.Abs(leftStick.y) < 0.3f
            && (left.ulButtonPressed & Grip) == 0 && (right.ulButtonPressed & Grip) == 0;
        return new(true, modifier, (right.ulButtonPressed & A) != 0, (right.ulButtonPressed & B) != 0,
            -rightStick.y);
    }

    private static int FindStick(uint device)
    {
        if (device == OpenVR.k_unTrackedDeviceIndexInvalid) return -1;
        for (var i = 0; i < 5; i++)
        {
            var error = ETrackedPropertyError.TrackedProp_Success;
            var type = OpenVR.System.GetInt32TrackedDeviceProperty(device,
                (ETrackedDeviceProperty)((int)ETrackedDeviceProperty.Prop_Axis0Type_Int32 + i), ref error);
            if (error == ETrackedPropertyError.TrackedProp_Success && type == (int)EVRControllerAxisType.k_eControllerAxis_Joystick)
                return i;
        }
        return -1;
    }

    private static VRControllerAxis_t Axis(VRControllerState_t state, int axis) => axis switch
    {
        0 => state.rAxis0, 1 => state.rAxis1, 2 => state.rAxis2, 3 => state.rAxis3, _ => state.rAxis4,
    };
}
