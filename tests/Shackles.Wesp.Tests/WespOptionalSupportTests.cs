using System.Reflection.PortableExecutable;
using Shackles.Wesp.Interop;

namespace Shackles.Wesp.Tests;

[TestClass]
public sealed class WespOptionalSupportTests
{
    private static readonly string AppDirectory = Path.Combine(Path.GetTempPath(), "shackles-support", "app");
    private static readonly string SystemDirectory = Path.Combine(Path.GetTempPath(), "shackles-support", "system32");
    private static readonly string AppClient = Path.Combine(AppDirectory, NativeMethods.WespLibrary);
    private static readonly string SystemClient = Path.Combine(SystemDirectory, NativeMethods.WespLibrary);
    private static readonly string[] ConnectClientExport = ["EspConnectClient"];

    [TestMethod]
    public void MissingClientReportsBothRuntimeLocationsWithoutLoadingAnything()
    {
        var result = WespSupport.ProbeLibrarySearch(
            null, AppDirectory, SystemDirectory, _ => false,
            _ => throw new AssertFailedException("A missing client must not be loaded."));

        Assert.AreEqual(WespAvailability.ClientLibraryMissing, result.Availability);
        Assert.IsFalse(result.IsAvailable);
        CollectionAssert.AreEqual(new[] { AppClient, SystemClient }, result.CheckedClientLibraryPaths.ToArray());
        Assert.AreEqual(SystemClient, result.ClientLibraryPath);
        StringAssert.Contains(result.Summary, SystemClient);
    }

    [TestMethod]
    public void MissingExplicitOverrideDoesNotFallBackToInstalledClient()
    {
        var configured = Path.Combine(Path.GetTempPath(), "configured-wesp", NativeMethods.WespLibrary);
        var result = WespSupport.ProbeLibrarySearch(
            configured, AppDirectory, SystemDirectory,
            path => path == AppClient || path == SystemClient,
            _ => throw new AssertFailedException("An explicit override must not silently fall back."));

        Assert.AreEqual(WespAvailability.ClientLibraryMissing, result.Availability);
        Assert.AreEqual(Path.GetFullPath(configured), result.ClientLibraryPath);
        CollectionAssert.AreEqual(new[] { Path.GetFullPath(configured) }, result.CheckedClientLibraryPaths.ToArray());
    }

    [TestMethod]
    public void InstalledSystemClientWorksWhenLocalPackageOmittedIt()
    {
        var inspected = new List<string>();
        var result = WespSupport.ProbeLibrarySearch(
            null, AppDirectory, SystemDirectory, path => path == SystemClient,
            path =>
            {
                inspected.Add(path);
                return new WespSupportInfo(WespAvailability.Available, "Available", path, []);
            });

        Assert.IsTrue(result.IsAvailable);
        Assert.AreEqual(SystemClient, result.ClientLibraryPath);
        CollectionAssert.AreEqual(new[] { SystemClient }, inspected);
        CollectionAssert.AreEqual(new[] { AppClient, SystemClient }, result.CheckedClientLibraryPaths.ToArray());
    }

    [TestMethod]
    public void BrokenAppLocalClientIsReportedInsteadOfHiddenBySystemFallback()
    {
        var result = WespSupport.ProbeLibrarySearch(
            null, AppDirectory, SystemDirectory, _ => true,
            path => new WespSupportInfo(WespAvailability.ClientLibraryInvalid, "Invalid DLL", path, []));

        Assert.AreEqual(WespAvailability.ClientLibraryInvalid, result.Availability);
        Assert.AreEqual(AppClient, result.ClientLibraryPath);
        CollectionAssert.AreEqual(new[] { AppClient }, result.CheckedClientLibraryPaths.ToArray());
    }

    [TestMethod]
    public void MalformedClientIsDiagnosedWithoutCallingItsCode()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shackles-invalid-wesp-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllText(path, "This is not a DLL.");
            var result = WespSupport.ProbeClientLibrary(path, ConnectClientExport, "Missing session APIs.");

            Assert.AreEqual(WespAvailability.ClientLibraryInvalid, result.Availability);
            Assert.IsFalse(result.IsAvailable);
            StringAssert.Contains(result.Summary, "authorized x64");
            StringAssert.Contains(result.Summary, "refresh support");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [DataRow(Machine.I386, PEMagic.PE32)]
    [DataRow(Machine.Arm64, PEMagic.PE32Plus)]
    public void OtherArchitecturesHaveAnActionableClientError(Machine machine, PEMagic magic)
    {
        var result = WespSupport.CheckClientImage(AppClient, true, true, machine, magic);

        Assert.IsNotNull(result);
        Assert.AreEqual(WespAvailability.ClientLibraryArchitectureNotSupported, result.Availability);
        Assert.IsFalse(result.IsAvailable);
        StringAssert.Contains(result.Summary, "x64");
        StringAssert.Contains(result.Summary, AppClient);
    }

    [TestMethod]
    public void MissingExportsDescribeTheClientProblemAndReplacement()
    {
        var result = WespSupport.InspectLibraryExports(
            AppClient, ["EspRegisterClient", "EspConnectClient"],
            export => export == "EspRegisterClient", "Missing session APIs.");

        Assert.AreEqual(WespAvailability.RequiredExportMissing, result.Availability);
        Assert.IsFalse(result.IsAvailable);
        CollectionAssert.AreEqual(ConnectClientExport, result.MissingExports.ToArray());
        StringAssert.Contains(result.Summary, "EspConnectClient");
        StringAssert.Contains(result.Summary, "Replace it");
    }
}
