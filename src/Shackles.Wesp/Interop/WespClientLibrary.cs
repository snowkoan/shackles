namespace Shackles.Wesp.Interop;

internal static class WespClientLibrary
{
    internal const string OverrideEnvironmentVariable = "SHACKLES_WESP_CLIENT_DLL";

    internal static IReadOnlyList<string> GetSearchPaths() =>
        GetSearchPaths(
            Environment.GetEnvironmentVariable(OverrideEnvironmentVariable),
            AppContext.BaseDirectory,
            Environment.SystemDirectory);

    internal static IReadOnlyList<string> GetSearchPaths(
        string? configured,
        string appBaseDirectory,
        string systemDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appBaseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemDirectory);

        return !string.IsNullOrWhiteSpace(configured)
            ? [Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured))]
            : [Path.Combine(appBaseDirectory, NativeMethods.WespLibrary),
               Path.Combine(systemDirectory, NativeMethods.WespLibrary)];
    }

    internal static string GetCandidatePath() =>
        GetCandidatePath(
            Environment.GetEnvironmentVariable(OverrideEnvironmentVariable),
            AppContext.BaseDirectory,
            Environment.SystemDirectory,
            File.Exists);

    internal static string GetCandidatePath(
        string? configured,
        string appBaseDirectory,
        string systemDirectory,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        var paths = GetSearchPaths(configured, appBaseDirectory, systemDirectory);
        return paths.Count == 1 || fileExists(paths[0])
            ? paths[0]
            : paths[1];
    }
}
