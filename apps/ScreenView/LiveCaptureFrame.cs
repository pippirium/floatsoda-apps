using FloatSoda.Abstractions.Geometries;
using FloatSoda.Core;
using FloatSoda.Geometrics;
using FloatSoda.Rendering.Layers;
using FloatSoda.RenderObjects;
using FloatSoda.Widgets;
using SkiaSharp;

namespace FloatSoda.Apps.ScreenView;

/// <summary>ライブ画像を描画スレッドで読み取り、過去のフレームを描画命令に保持しないウィジェット。</summary>
public sealed record LiveCaptureFrame : LeafRenderObjectWidget<RenderLiveCaptureFrame>
{
    public required WindowCaptureSession Session { get; init; }
    public required SKSize ContentSize { get; init; }
    public BoxFit Fit { get; init; } = BoxFit.Contain;
    public Alignment Alignment { get; init; } = Alignment.Center;

    public override RenderLiveCaptureFrame CreateRenderObject() => new(Session, ContentSize, Fit, Alignment);
    public override void UpdateRenderObject(RenderLiveCaptureFrame renderObject) => renderObject.Update(Session, ContentSize, Fit, Alignment);
}

public sealed class RenderLiveCaptureFrame(WindowCaptureSession session, SKSize contentSize, BoxFit fit, Alignment alignment) : RenderBox
{
    private WindowCaptureSession _session = session;
    private SKSize _contentSize = contentSize;
    private BoxFit _fit = fit;
    private Alignment _alignment = alignment;
    public override SKSize Size { get; protected set; }

    public void Update(WindowCaptureSession source, SKSize size, BoxFit newFit, Alignment newAlignment)
    {
        var resize = _contentSize != size;
        _session = source;
        _contentSize = size;
        _fit = newFit;
        _alignment = newAlignment;
        if (resize) MarkNeedsLayout();
        MarkNeedsPaint();
    }

    public override void PerformLayout() => Size = Constraints.Constrain(_contentSize);
    public override bool HitTestSelf(Offset position) => true;
    protected override double ComputeMinIntrinsicWidth(double height) => _contentSize.Width;
    protected override double ComputeMaxIntrinsicWidth(double height) => _contentSize.Width;
    protected override double ComputeMinIntrinsicHeight(double width) => _contentSize.Height;
    protected override double ComputeMaxIntrinsicHeight(double width) => _contentSize.Height;

    public override void Paint(PaintingContext context, Offset offset)
    {
        var bounds = SKRect.Create((float)offset.X, (float)offset.Y, Size.Width, Size.Height);
        var source = _session;
        context.PushLayer(new LiveCaptureLayer(draw => source.WithLatestFrame(draw), bounds, _fit, _alignment),
            (_, _) => { }, Offset.Zero, bounds);
    }
}

/// <summary>描画時に最新フレームを取得するため、待ち行列の複製に過去のSKImageを保持しません。</summary>
public sealed class LiveCaptureLayer(Action<Action<SKImage>> withFrame, SKRect bounds, BoxFit fit, Alignment alignment) : ContainerLayer
{
    public override void Layout(LayerContext context) => PaintBounds = bounds;
    public override ILayer Clone() => new LiveCaptureLayer(withFrame, bounds, fit, alignment);

    public override void Paint(LayerContext context) => withFrame(image =>
    {
        var canvas = context.Canvas;
        var imageSize = new SKSize(image.Width, image.Height);
        var destinationSize = new SKSize(bounds.Width, bounds.Height);
        // SKPictureへの記録を介さず、その場で描画する。古い画像のネイティブ参照を残さない。
        var fitted = ApplyFit(fit, imageSize, destinationSize);
        if (fitted.Source.IsEmpty || fitted.Destination.IsEmpty) return;
        var from = alignment.ComputeOffset(imageSize, fitted.Source);
        var to = alignment.ComputeOffset(destinationSize, fitted.Destination);
        canvas.DrawImage(image,
            SKRect.Create((float)from.X, (float)from.Y, fitted.Source.Width, fitted.Source.Height),
            SKRect.Create(bounds.Left + (float)to.X, bounds.Top + (float)to.Y, fitted.Destination.Width, fitted.Destination.Height));
    });

    // エンジンのBoxFit計算はinternalなので、同じ幾何規則で描画範囲を求める。
    private static (SKSize Source, SKSize Destination) ApplyFit(BoxFit value, SKSize input, SKSize output)
    {
        if (input.IsEmpty || output.IsEmpty) return (SKSize.Empty, SKSize.Empty);
        var wider = output.Width / output.Height > input.Width / input.Height;
        var contained = wider
            ? new SKSize(input.Width * output.Height / input.Height, output.Height)
            : new SKSize(output.Width, input.Height * output.Width / input.Width);
        var covered = wider
            ? new SKSize(input.Width, input.Width * output.Height / output.Width)
            : new SKSize(input.Height * output.Width / output.Height, input.Height);
        var clipped = new SKSize(Math.Min(input.Width, output.Width), Math.Min(input.Height, output.Height));
        return value switch
        {
            BoxFit.Fill => (input, output),
            BoxFit.Contain => (input, contained),
            BoxFit.Cover => (covered, output),
            BoxFit.FitWidth => wider ? (covered, output) : (input, contained),
            BoxFit.FitHeight => wider ? (input, contained) : (covered, output),
            BoxFit.None => (clipped, clipped),
            BoxFit.ScaleDown => (input, input.Width <= output.Width && input.Height <= output.Height ? input : contained),
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
    }
}
