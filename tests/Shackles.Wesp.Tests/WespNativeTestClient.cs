using System.Reflection;
using System.Runtime.InteropServices;
using Shackles.Wesp.Interop;

namespace Shackles.Wesp.Tests;

internal static class WespNativeTestClient
{
    internal static string RequireBundledClient()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            Assert.Inconclusive("The bundled WESP client requires Windows x64.");
        }

        var expectedValue = typeof(WespNativeTestClient).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "WespClientExpected")
            ?.Value;
        Assert.IsTrue(
            bool.TryParse(expectedValue, out var expected),
            "The test build must declare whether it intentionally includes the WESP client.");
        var libraryPath = Path.Combine(AppContext.BaseDirectory, NativeMethods.WespLibrary);
        if (!expected)
        {
            Assert.IsFalse(
                File.Exists(libraryPath),
                $"This build omitted WESP, but a stale client remains in the test output: {libraryPath}");
            Assert.Inconclusive(
                "This build intentionally omits WESP. Add an authorized x64 espclient.dll to " +
                "local-dependencies/wesp/ and rebuild to run native WESP tests.");
        }

        Assert.IsTrue(
            File.Exists(libraryPath),
            $"The build expected the WESP client, but it was not copied to the test output: {libraryPath}");
        return libraryPath;
    }
}
