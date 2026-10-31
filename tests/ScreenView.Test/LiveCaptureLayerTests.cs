using FloatSoda.Apps.ScreenView;
using FloatSoda.Geometrics;
using FloatSoda.Rendering.Layers;
using FloatSoda.RenderObjects;
using FloatSoda.RenderObjects.Painting;
using FloatSoda.Abstractions.Geometries;
using SkiaSharp;

namespace ScreenView.Test;

public sealed class LiveCaptureLayerTests
{
    [Theory]
    [InlineData(BoxFit.Fill)]
    [InlineData(BoxFit.Contain)]
    [InlineData(BoxFit.Cover)]
    [InlineData(BoxFit.FitWidth)]
    [InlineData(BoxFit.FitHeight)]
    [InlineData(BoxFit.None)]
    [InlineData(BoxFit.ScaleDown)]
    public void Paint_縦横比が異なっても全BoxFitで既存RenderImageと一致する(BoxFit fit)
    {
        using var bitmap = new SKBitmap(6, 4);
        for (var y = 0; y < 4; y++) for (var x = 0; x < 6; x++)
            bitmap.SetPixel(x, y, new SKColor((byte)(x * 40), (byte)(y * 60), 100));
        using var image = SKImage.FromBitmap(bitmap);
        using var actual = SKSurface.Create(new SKImageInfo(9, 7));
        using var expected = SKSurface.Create(new SKImageInfo(9, 7));
        actual.Canvas.Clear(SKColors.Black);
        expected.Canvas.Clear(SKColors.Black);
        var bounds = new SKRect(0, 0, 9, 7);
        new LiveCaptureLayer(draw => draw(image), bounds, fit, Alignment.Center).Paint(new LayerContext(actual.Canvas));
        var root = new ContainerLayer();
        var recording = new PaintingContext(root, bounds);
        var reference = new RenderImage { Image = image, Fit = fit, Alignment = Alignment.Center };
        reference.Layout(BoxConstraints.Tight(new SKSize(9, 7)));
        reference.Paint(recording, Offset.Zero);
        recording.StopRecordingIfNeeded();
        root.Layout(new LayerContext(expected.Canvas));
        root.Paint(new LayerContext(expected.Canvas));
        using var actualImage = actual.Snapshot();
        using var expectedImage = expected.Snapshot();
        using var a = SKBitmap.FromImage(actualImage);
        using var b = SKBitmap.FromImage(expectedImage);
        Assert.Equal(b.Pixels, a.Pixels);
        foreach (var picture in root.Children.OfType<PictureLayer>()) picture.Picture?.Dispose();
    }

    [Theory]
    [InlineData(4, 4, false, false)]
    [InlineData(8, 8, false, false)]
    [InlineData(4, 4, true, false)]
    [InlineData(4, 4, false, true)]
    public void Paint_等倍拡縮移動クリップで従来と同じ画素を描く(int width, int height, bool translate, bool clip)
    {
        using var bitmap = new SKBitmap(4, 4);
        for (var y = 0; y < 4; y++) for (var x = 0; x < 4; x++)
            bitmap.SetPixel(x, y, (x + y) % 2 == 0 ? SKColors.Red : SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var actual = SKSurface.Create(new SKImageInfo(12, 12));
        using var expected = SKSurface.Create(new SKImageInfo(12, 12));
        foreach (var canvas in new[] { actual.Canvas, expected.Canvas })
        {
            canvas.Clear(SKColors.Black);
            if (translate) canvas.Translate(2, 3);
            if (clip) canvas.ClipRect(new SKRect(0, 0, 2, 2));
        }
        var bounds = SKRect.Create(width, height);
        var layer = new LiveCaptureLayer(draw => draw(image), bounds, BoxFit.Contain, Alignment.Center);
        layer.Paint(new LayerContext(actual.Canvas));
        expected.Canvas.DrawImage(image, new SKRect(0, 0, 4, 4), bounds);
        using var actualImage = actual.Snapshot();
        using var expectedImage = expected.Snapshot();
        using var a = SKBitmap.FromImage(actualImage);
        using var b = SKBitmap.FromImage(expectedImage);
        Assert.Equal(b.Pixels, a.Pixels);
    }

    [Fact]
    public void Clone_過去の画像を固定せず描画時の最新画像を使う()
    {
        using var redBitmap = new SKBitmap(4, 4);
        using var blueBitmap = new SKBitmap(4, 4);
        redBitmap.Erase(SKColors.Red);
        blueBitmap.Erase(SKColors.Blue);
        using var red = SKImage.FromBitmap(redBitmap);
        using var blue = SKImage.FromBitmap(blueBitmap);
        var latest = red;
        var layer = new LiveCaptureLayer(draw => draw(latest), new SKRect(0, 0, 4, 4), BoxFit.Contain, Alignment.Center);
        var snapshot = layer.Clone();
        latest = blue;
        red.Dispose();
        using var surface = SKSurface.Create(new SKImageInfo(4, 4));
        snapshot.Paint(new LayerContext(surface.Canvas));
        using var rendered = surface.Snapshot();
        using var pixels = SKBitmap.FromImage(rendered);
        Assert.All(pixels.Pixels, color => Assert.Equal(SKColors.Blue, color));
    }
}
