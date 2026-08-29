using System.Runtime.InteropServices;
using Shackles.ExperimentalSandboxes.Interop;

namespace Shackles.ExperimentalSandboxes.Internal;

internal sealed class SandboxNativeApi : IDisposable
{
    private SandboxNativeApi(
        nint module,
        CreateProcessInSandboxDelegate create,
        QuerySandboxSupportDelegate? query)
    {
        Module = module;
        Create = create;
        Query = query;
    }

    internal nint Module { get; private set; }

    internal CreateProcessInSandboxDelegate Create { get; }

    internal QuerySandboxSupportDelegate? Query { get; }

    internal static bool TryLoad(
        out SandboxNativeApi? api,
        out string? failure,
        out bool libraryPresent,
        out bool createExportPresent)
    {
        api = null;
        failure = null;
        libraryPresent = false;
        createExportPresent = false;
        if (!OperatingSystem.IsWindows())
        {
            failure = "The experimental process sandbox is available only on Windows.";
            return false;
        }

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "processmodel.dll");
        if (!File.Exists(path))
        {
            failure = $"Windows did not provide {path}.";
            return false;
        }

        libraryPresent = true;
        nint module;
        try
        {
            module = NativeLibrary.Load(path);
        }
        catch (Exception exception)
        {
            failure = $"Could not load processmodel.dll from System32: {exception.Message}";
            return false;
        }

        try
        {
            if (!NativeLibrary.TryGetExport(
                    module,
                    "Experimental_CreateProcessInSandbox",
                    out var createAddress))
            {
                failure =
                    "processmodel.dll does not export Experimental_CreateProcessInSandbox.";
                return false;
            }

            createExportPresent = true;
            var create = Marshal.GetDelegateForFunctionPointer<
                CreateProcessInSandboxDelegate>(createAddress);
            QuerySandboxSupportDelegate? query = null;
            if (NativeLibrary.TryGetExport(
                    module,
                    "Experimental_QuerySandboxSupport",
                    out var queryAddress))
            {
                query = Marshal.GetDelegateForFunctionPointer<
                    QuerySandboxSupportDelegate>(queryAddress);
            }

            api = new SandboxNativeApi(module, create, query);
            module = 0;
            return true;
        }
        finally
        {
            if (module != 0)
            {
                NativeLibrary.Free(module);
            }
        }
    }

    public void Dispose()
    {
        var module = Module;
        Module = 0;
        if (module != 0)
        {
            NativeLibrary.Free(module);
        }
    }
}
