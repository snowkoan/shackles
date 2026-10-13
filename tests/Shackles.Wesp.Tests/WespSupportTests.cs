using System.Runtime.InteropServices;
using Shackles.Wesp.Internal;
using Shackles.Wesp.Interop;

namespace Shackles.Wesp.Tests;

[TestClass]
public sealed class WespSupportTests
{
    private const string GenericFilterExport = "EspCreateFilter";
    private static readonly string[] FileExports =
    [
        "EspCreateFileObjectFilter",
        "EspIsFileObjectPropertySupported"
    ];
    private static readonly string[] RegistryExports =
    [
        "EspCreateRegistryKeyFilter",
        "EspIsRegistryKeyPropertySupported"
    ];

    [TestMethod]
    public void BaseSessionDoesNotRequireResourceOrGenericFilterExports()
    {
        var exports = WespSupport.GetRequiredExports(CreatePolicy());

        Assert.IsTrue(exports.Contains("EspConnectClient"));
        Assert.IsTrue(exports.Contains("EspCreateProcessFilter"));
        Assert.IsTrue(exports.Contains("EspRemoveAllRulesForClient"));
        Assert.IsTrue(exports.Contains("EspQueryProcessProperties"));
        Assert.IsTrue(exports.Contains("EspFreeMemory"));
        Assert.IsFalse(exports.Contains("EspIsProcessPropertySupported"));
        AssertResourceExports(exports, file: false, registry: false, generic: false);
    }

    [TestMethod]
    public void BundledClientAcceptsEncodedProcessContextKeyInFilter()
    {
        var libraryPath = WespNativeTestClient.RequireBundledClient();
        var comparison = new NativeContextKeyComparison
        {
            ComparisonType = EspContextKeyComparisonType.Integer,
            IntegerComparisonType = EspIntegerComparisonType.Equals,
            SourceType = EspValueSourceType.Raw,
            RawValue = 1
        };
        var library = NativeLibrary.Load(libraryPath);
        try
        {
            var createFilter = Marshal.GetDelegateForFunctionPointer<CreateProcessFilter>(
                NativeLibrary.GetExport(library, "EspCreateProcessFilter"));
            var closeFilter = Marshal.GetDelegateForFunctionPointer<CloseFilter>(
                NativeLibrary.GetExport(library, "EspCloseFilter"));
            nint filter = 0;
            try
            {
                var hresult = createFilter(
                    0x80000000u | WespRuleCompiler.PolicyContextKey,
                    EspComparisonType.ContextKey,
                    in comparison,
                    out filter);

                Assert.IsTrue(
                    hresult >= 0,
                    $"EspCreateProcessFilter rejected the encoded context-key property ID with 0x{hresult:X8}.");
                Assert.AreNotEqual(0, filter);
            }
            finally
            {
                if (filter != 0)
                {
                    Assert.IsTrue(closeFilter(filter) >= 0);
                }
            }
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    [TestMethod]
    public void BlockedFileRequiresOnlyFileSpecificExports()
    {
        var exports = WespSupport.GetRequiredExports(CreatePolicy(
            blockedFiles: [@"C:\blocked"]));

        AssertResourceExports(exports, file: true, registry: false, generic: false);
    }

    [TestMethod]
    public void ReadOnlyFileAlsoRequiresGenericFilterExport()
    {
        var exports = WespSupport.GetRequiredExports(CreatePolicy(
            readOnlyFiles: [@"C:\read-only"]));

        AssertResourceExports(exports, file: true, registry: false, generic: true);
    }

    [TestMethod]
    public void UncBlockingRequiresOnlyFileSpecificExports()
    {
        var exports = WespSupport.GetRequiredExports(CreatePolicy(
            blockUncPaths: true));

        AssertResourceExports(exports, file: true, registry: false, generic: false);
    }

    [TestMethod]
    public void BlockedChildNameRequiresOnlyFileSpecificExports()
    {
        var exports = WespSupport.GetRequiredExports(CreatePolicy(
            blockedChildExecutables: [@"C:\Tools\blocked.exe"]));

        AssertResourceExports(exports, file: true, registry: false, generic: false);
    }

    [TestMethod]
    public void BlockedRegistryKeyRequiresOnlyRegistrySpecificExports()
    {
        var exports = WespSupport.GetRequiredExports(CreatePolicy(
            blockedRegistry: [@"\REGISTRY\MACHINE\Software\Blocked"]));

        AssertResourceExports(exports, file: false, registry: true, generic: false);
    }

    [TestMethod]
    public void ReadOnlyRegistryKeyAlsoRequiresGenericFilterExport()
    {
        var exports = WespSupport.GetRequiredExports(CreatePolicy(
            readOnlyRegistry: [@"\REGISTRY\MACHINE\Software\ReadOnly"]));

        AssertResourceExports(exports, file: false, registry: true, generic: true);
    }

    private static NormalizedWespPolicy CreatePolicy(
        IReadOnlyList<string>? blockedFiles = null,
        IReadOnlyList<string>? readOnlyFiles = null,
        IReadOnlyList<string>? blockedRegistry = null,
        IReadOnlyList<string>? readOnlyRegistry = null,
        IReadOnlyList<string>? blockedChildExecutables = null,
        bool blockUncPaths = false) =>
        new(
            blockedFiles ?? [],
            readOnlyFiles ?? [],
            blockedRegistry ?? [],
            readOnlyRegistry ?? [],
            BlockedChildExecutables: blockedChildExecutables ?? [],
            BlockUncPaths: blockUncPaths);

    private static void AssertResourceExports(
        IReadOnlyCollection<string> exports,
        bool file,
        bool registry,
        bool generic)
    {
        foreach (var export in FileExports)
        {
            Assert.AreEqual(file, exports.Contains(export), export);
        }

        foreach (var export in RegistryExports)
        {
            Assert.AreEqual(registry, exports.Contains(export), export);
        }

        Assert.AreEqual(generic, exports.Contains(GenericFilterExport), GenericFilterExport);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateProcessFilter(
        uint propertyId,
        EspComparisonType comparisonType,
        in NativeContextKeyComparison comparison,
        out nint filter);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CloseFilter(nint filter);
}
