namespace Shackles.Wesp.Interop;

internal static class WespClientLibrary
{
    internal const string OverrideEnvironmentVariable = "SHACKLES_WESP_CLIENT_DLL";

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
        ArgumentException.ThrowIfNullOrWhiteSpace(appBaseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemDirectory);
        ArgumentNullException.ThrowIfNull(fileExists);

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(configured));
        }

        var appLocal = Path.Combine(
            appBaseDirectory,
            NativeMethods.WespLibrary);
        return fileExists(appLocal)
            ? appLocal
            : Path.Combine(systemDirectory, NativeMethods.WespLibrary);
    }
}
