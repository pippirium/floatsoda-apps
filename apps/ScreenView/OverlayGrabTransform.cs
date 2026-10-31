using System.Numerics;

namespace FloatSoda.Apps.ScreenView;

/// <summary>掴み開始時の姿勢から、パネルの中心を保った移動と回転を計算します。</summary>
public static class OverlayGrabTransform
{
    /// <summary>
    /// エンジンのOpenVR行列変換が保持する3x3成分を、System.Numericsの行ベクトル規約へ変換します。
    /// 平行移動は保持します。同じ変換をもう一度適用するとOpenVRへ戻せます。
    /// </summary>
    public static Matrix4x4 ConvertRotationConvention(Matrix4x4 pose)
    {
        var position = pose.Translation;
        pose.Translation = Vector3.Zero;
        pose = Matrix4x4.Transpose(pose);
        pose.Translation = position;
        return pose;
    }

    public static bool TryCalculate(Matrix4x4 overlayAtGrab, Matrix4x4 controllerAtGrab,
        Matrix4x4 controllerNow, out Matrix4x4 result)
    {
        result = overlayAtGrab;
        if (!IsFinite(overlayAtGrab) || !IsFinite(controllerAtGrab) || !IsFinite(controllerNow)) return false;

        var initialRotation = controllerAtGrab;
        initialRotation.Translation = Vector3.Zero;
        var currentRotation = controllerNow;
        currentRotation.Translation = Vector3.Zero;
        if (MathF.Abs(currentRotation.GetDeterminant()) < 0.000001f) return false;
        if (!Matrix4x4.Invert(initialRotation, out var inverse)) return false;

        var overlayRotation = overlayAtGrab;
        overlayRotation.Translation = Vector3.Zero;
        // System.Numericsは行ベクトル。コントローラーのワールド空間の回転差分を右から掛ける。
        result = overlayRotation * inverse * currentRotation;
        // 回転でパネルを手の周りへ公転させず、従来の平行移動と同じ中心位置を維持する。
        result.Translation = overlayAtGrab.Translation + controllerNow.Translation - controllerAtGrab.Translation;
        return IsFinite(result);
    }

    private static bool IsFinite(Matrix4x4 m) =>
        float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M13) && float.IsFinite(m.M14)
        && float.IsFinite(m.M21) && float.IsFinite(m.M22) && float.IsFinite(m.M23) && float.IsFinite(m.M24)
        && float.IsFinite(m.M31) && float.IsFinite(m.M32) && float.IsFinite(m.M33) && float.IsFinite(m.M34)
        && float.IsFinite(m.M41) && float.IsFinite(m.M42) && float.IsFinite(m.M43) && float.IsFinite(m.M44);
}
