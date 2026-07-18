using WinForge.Localization;

namespace WinForge.Core.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void Arabic_resources_load_and_select_rtl()
    {
        var localizer = new Localizer();
        localizer.SetCulture("ar-SA");
        Assert.Equal("لوحة المعلومات", localizer["Dashboard"]);
        Assert.True(localizer.IsRightToLeft);
    }

    [Fact]
    public void English_resources_select_ltr()
    {
        var localizer = new Localizer();
        localizer.SetCulture("en-US");
        Assert.Equal("Dashboard", localizer["Dashboard"]);
        Assert.False(localizer.IsRightToLeft);
    }
}
