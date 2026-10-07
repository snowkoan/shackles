using System.Globalization;
using System.Windows;
using Shackles.App.Infrastructure;

namespace Shackles.App.Tests;

[TestClass]
public sealed class JobLayoutTests
{
    [TestMethod]
    public void WrappedJobChromeExpandsWorkspaceMinimum()
    {
        var converter = new JobWorkspaceMinimumHeightConverter();

        var height = converter.Convert([150d, 180d, 500d, Visibility.Visible], typeof(double), null!, CultureInfo.InvariantCulture);

        Assert.AreEqual(852d, height);
    }

    [TestMethod]
    public void CompactChromeRetainsDefaultWorkspaceMinimum()
    {
        var converter = new JobWorkspaceMinimumHeightConverter();

        var height = converter.Convert([120d, 140d, 280d, Visibility.Visible], typeof(double), null!, CultureInfo.InvariantCulture);

        Assert.AreEqual(650d, height);
    }

    [TestMethod]
    public void NoSelectedJobIgnoresPreviousDetailsHeight()
    {
        var converter = new JobWorkspaceMinimumHeightConverter();

        var height = converter.Convert([120d, 140d, 900d, Visibility.Collapsed], typeof(double), null!, CultureInfo.InvariantCulture);

        Assert.AreEqual(650d, height);
    }
}
