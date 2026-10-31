using System.Numerics;
using FloatSoda.Apps.ScreenView;

namespace ScreenView.Test;

public sealed class OverlayGrabTransformTests
{
    [Fact]
    public void ConvertRotationConvention_位置を保持して往復変換できる()
    {
        var pose = Pose(Matrix4x4.CreateRotationX(0.3f) * Matrix4x4.CreateRotationY(0.7f), new(1, 2, 3));
        var converted = OverlayGrabTransform.ConvertRotationConvention(pose);
        Assert.Equal(pose.Translation, converted.Translation);
        AssertPose(pose, OverlayGrabTransform.ConvertRotationConvention(converted));
    }

    [Fact]
    public void TryCalculate_OpenVRの列ベクトル回転を実機の向きと同じ順に合成する()
    {
        var rawOverlay = Pose(Matrix4x4.CreateRotationX(0.3f), new(0, 1, -2));
        var rawHand = Pose(Matrix4x4.CreateRotationY(0.7f), new(1, 0.5f, 0.2f));
        var worldDelta = Matrix4x4.CreateRotationZ(0.6f);
        var rawNow = Pose(worldDelta * Matrix4x4.CreateRotationY(0.7f), rawHand.Translation + Vector3.UnitX);
        Assert.True(OverlayGrabTransform.TryCalculate(
            OverlayGrabTransform.ConvertRotationConvention(rawOverlay),
            OverlayGrabTransform.ConvertRotationConvention(rawHand),
            OverlayGrabTransform.ConvertRotationConvention(rawNow), out var result));
        var expected = Pose(worldDelta * Matrix4x4.CreateRotationX(0.3f), rawOverlay.Translation + Vector3.UnitX);
        AssertPose(expected, OverlayGrabTransform.ConvertRotationConvention(result));
    }

    [Fact]
    public void TryCalculate_掴んだ瞬間に位置や向きを変えない()
    {
        var overlay = Pose(Matrix4x4.CreateRotationY(0.7f), new(2, 1, -3));
        var hand = Pose(Matrix4x4.CreateRotationX(0.3f), new(0.4f, 0.8f, -0.2f));
        Assert.True(OverlayGrabTransform.TryCalculate(overlay, hand, hand, out var result));
        AssertPose(overlay, result);
    }

    [Fact]
    public void TryCalculate_平行移動では向きを変えない()
    {
        var overlay = Pose(Matrix4x4.CreateRotationZ(0.4f), new(2, 1, -3));
        var hand = Pose(Matrix4x4.CreateRotationY(0.7f), new(1, 2, 3));
        var moved = hand;
        moved.Translation += new Vector3(0.2f, -0.3f, 0.4f);
        var expected = overlay;
        expected.Translation += new Vector3(0.2f, -0.3f, 0.4f);
        Assert.True(OverlayGrabTransform.TryCalculate(overlay, hand, moved, out var result));
        AssertPose(expected, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TryCalculate_三軸の回転でもパネルの中心を動かさない(int axis)
    {
        var rotation = axis switch { 0 => Matrix4x4.CreateRotationX(1.1f), 1 => Matrix4x4.CreateRotationY(1.1f), _ => Matrix4x4.CreateRotationZ(1.1f) };
        var overlay = Pose(Matrix4x4.Identity, new(0, 1, -2));
        var hand = Pose(Matrix4x4.Identity, new(0.4f, 0.5f, -0.2f));
        var rotated = Pose(rotation, hand.Translation);
        Assert.True(OverlayGrabTransform.TryCalculate(overlay, hand, rotated, out var result));
        AssertPose(Pose(rotation, overlay.Translation), result);
    }

    [Fact]
    public void TryCalculate_初期の手とパネルが回転済みでもワールド空間の差分を適用する()
    {
        var overlay = Pose(Matrix4x4.CreateRotationX(0.5f), new(0, 1, -2));
        var hand = Pose(Matrix4x4.CreateRotationY(0.9f), new(0.4f, 0.5f, -0.2f));
        var delta = Matrix4x4.CreateRotationZ(0.6f);
        var now = Pose(Matrix4x4.CreateRotationY(0.9f) * delta, hand.Translation + Vector3.UnitX);
        var expected = Pose(Matrix4x4.CreateRotationX(0.5f) * delta, overlay.Translation + Vector3.UnitX);
        Assert.True(OverlayGrabTransform.TryCalculate(overlay, hand, now, out var result));
        AssertPose(expected, result);
    }

    [Fact]
    public void TryCalculate_同じ姿勢を繰り返しても誤差が蓄積しない()
    {
        var overlay = Pose(Matrix4x4.CreateRotationY(0.2f), new(0, 1, -2));
        var hand = Pose(Matrix4x4.CreateRotationX(0.7f), new(1, 0.4f, -0.1f));
        var now = Pose(Matrix4x4.CreateRotationX(0.7f) * Matrix4x4.CreateRotationZ(0.6f), hand.Translation);
        Assert.True(OverlayGrabTransform.TryCalculate(overlay, hand, now, out var first));
        for (var i = 0; i < 1000; i++)
        {
            Assert.True(OverlayGrabTransform.TryCalculate(overlay, hand, now, out var next));
            Assert.Equal(first, next);
        }
    }

    [Fact]
    public void TryCalculate_逆行列を作れない手の姿勢は拒否する() =>
        Assert.False(OverlayGrabTransform.TryCalculate(Matrix4x4.Identity, default, Matrix4x4.Identity, out _));

    [Fact]
    public void TryCalculate_現在の手の回転が潰れた姿勢は拒否する() =>
        Assert.False(OverlayGrabTransform.TryCalculate(Matrix4x4.Identity, Matrix4x4.Identity, default, out _));

    [Fact]
    public void TryCalculate_非有限の姿勢は拒否する()
    {
        var invalid = Matrix4x4.Identity;
        invalid.M11 = float.NaN;
        Assert.False(OverlayGrabTransform.TryCalculate(Matrix4x4.Identity, Matrix4x4.Identity, invalid, out _));
    }

    private static Matrix4x4 Pose(Matrix4x4 rotation, Vector3 position) { rotation.Translation = position; return rotation; }
    private static void AssertPose(Matrix4x4 expected, Matrix4x4 actual)
    {
        foreach (var point in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
            Assert.True(Vector3.Distance(Vector3.Transform(point, expected), Vector3.Transform(point, actual)) < 0.00001f,
                $"期待: {expected}, 実際: {actual}");
    }
}
