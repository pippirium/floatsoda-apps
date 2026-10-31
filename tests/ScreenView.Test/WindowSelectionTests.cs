using FloatSoda.Apps.ScreenView;
using Xunit;

namespace ScreenView.Test;

public class WindowSelectionTests
{
    private static readonly WindowInfo[] Windows =
    [
        new((nint)100, "Editor", "notepad", 101),
        new((nint)200, "Browser", "chrome", 202),
    ];

    [Fact]
    public void PicksByTitleWithoutProcessFilter()
        => Assert.Equal((nint)100, WindowLocator.Pick(Windows, "Editor", null)?.Handle);

    [Fact]
    public void PicksByPidWithoutProcessFilter()
        => Assert.Equal((nint)200, WindowLocator.Pick(Windows, null, null, 202)?.Handle);

    [Fact]
    public void RequiresAtLeastOneFilter()
        => Assert.Throws<ArgumentException>(() => WindowLocator.Pick(Windows, null, null));

    [Fact]
    public void FindReturnsOnlyMatchingWindow()
    {
        Assert.Equal((nint)200, WindowLocator.Find(Windows, (nint)200)?.Handle);
        Assert.Null(WindowLocator.Find(Windows, (nint)999));
        Assert.Null(WindowLocator.Find(Windows, 0));
    }
}
