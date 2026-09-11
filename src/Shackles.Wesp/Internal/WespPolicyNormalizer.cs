using System.Security.Principal;

namespace Shackles.Wesp.Internal;

internal sealed record NormalizedWespPolicy(
    IReadOnlyList<string> BlockedFilePaths,
    IReadOnlyList<string> ReadOnlyFilePaths,
    IReadOnlyList<string> BlockedRegistryKeys,
    IReadOnlyList<string> ReadOnlyRegistryKeys,
    IReadOnlyList<string> BlockedChildExecutables,
    bool BlockUncPaths = false);

internal static class WespPolicyNormalizer
{
    internal static NormalizedWespPolicy Normalize(WespPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var blockedFiles = NormalizeDirectories(
            policy.BlockedFilePaths,
            nameof(policy.BlockedFilePaths));
        var readOnlyFiles = NormalizeDirectories(
                policy.ReadOnlyFilePaths,
                nameof(policy.ReadOnlyFilePaths))
            .Where(path => !blockedFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var blockedRegistry = NormalizeRegistryKeys(
            policy.BlockedRegistryKeys,
            nameof(policy.BlockedRegistryKeys));
        var readOnlyRegistry = NormalizeRegistryKeys(
                policy.ReadOnlyRegistryKeys,
                nameof(policy.ReadOnlyRegistryKeys))
            .Where(path => !blockedRegistry.Contains(path, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        return new NormalizedWespPolicy(
            blockedFiles,
            readOnlyFiles,
            blockedRegistry,
            readOnlyRegistry,
            NormalizeFiles(policy.BlockedChildExecutables, nameof(policy.BlockedChildExecutables)),
            policy.BlockUncPaths);
    }

    private static IReadOnlyList<string> NormalizeRegistryKeyPaths(string keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath) || keyPath.Contains('\0'))
        {
            throw new WespException(
                WespOperation.ValidatePolicy,
                "A valid registry key path is required.");
        }

        var trimmed = keyPath.Trim().TrimEnd('\\');
        if (trimmed.StartsWith("Computer\\", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[9..];
        }

        if (trimmed.StartsWith("\\REGISTRY\\", StringComparison.OrdinalIgnoreCase))
        {
            var nativeTail = trimmed[10..];
            ValidateRegistrySegments(nativeTail, keyPath);
            var nativeRootSeparator = nativeTail.IndexOf('\\');
            var nativeRoot = nativeRootSeparator < 0
                ? nativeTail
                : nativeTail[..nativeRootSeparator];
            if (!string.Equals(nativeRoot, "MACHINE", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(nativeRoot, "USER", StringComparison.OrdinalIgnoreCase))
            {
                throw new WespException(
                    WespOperation.ValidatePolicy,
                    "Canonical registry paths must begin with \\REGISTRY\\MACHINE or \\REGISTRY\\USER.");
            }

            return [NormalizeUserClassesPath(trimmed)];
        }

        var separator = trimmed.IndexOf('\\');
        var root = separator < 0 ? trimmed : trimmed[..separator];
        var subKey = separator < 0 ? string.Empty : trimmed[(separator + 1)..];
        ValidateRegistrySegments(subKey, keyPath);
        var normalizedPaths = root.ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" =>
                new[] { CombineRegistryPath("\\REGISTRY\\MACHINE", subKey) },
            "HKU" or "HKEY_USERS" =>
                NormalizeUsersPath(subKey),
            "HKCU" or "HKEY_CURRENT_USER" =>
                NormalizeCurrentUserPath(subKey),
            "HKCR" or "HKEY_CLASSES_ROOT" =>
                new[]
                {
                    CombineRegistryPath($"\\REGISTRY\\USER\\{GetCurrentUserSid()}_Classes", subKey),
                    CombineRegistryPath("\\REGISTRY\\MACHINE\\Software\\Classes", subKey)
                },
            "HKCC" or "HKEY_CURRENT_CONFIG" => throw new WespException(
                WespOperation.ValidatePolicy,
                "HKCC is a symbolic view whose native target can change. Use its canonical \\REGISTRY\\MACHINE path for this WESP preview."),
            _ => throw new WespException(
                WespOperation.ValidatePolicy,
                $"Unsupported registry root '{root}'. Use HKCU, HKLM, HKCR, HKU, or a canonical \\REGISTRY path.")
        };

        return normalizedPaths;
    }

    private static string CombineRegistryPath(string root, string subKey) =>
        subKey.Length == 0 ? root : $"{root}\\{subKey}";

    private static IReadOnlyList<string> NormalizeCurrentUserPath(string subKey)
    {
        var userRoot = $"\\REGISTRY\\USER\\{GetCurrentUserSid()}";
        if (subKey.Length == 0)
        {
            // HKCU is a logical view. Its Software\Classes subtree is mounted
            // beside the SID hive rather than beneath it in the native layout.
            return [userRoot, $"{userRoot}_Classes"];
        }

        return [NormalizeUserClassesPath(CombineRegistryPath(userRoot, subKey))];
    }

    private static IReadOnlyList<string> NormalizeUsersPath(string subKey)
    {
        const string usersRoot = "\\REGISTRY\\USER";
        if (subKey.Length == 0)
        {
            return [usersRoot];
        }

        var path = CombineRegistryPath(usersRoot, subKey);
        if (!subKey.Contains('\\') && IsUserSidHiveName(subKey))
        {
            // HKU\<SID> has the same logical classes split as HKCU.
            return [path, $"{path}_Classes"];
        }

        return [NormalizeUserClassesPath(path)];
    }

    private static string NormalizeUserClassesPath(string path)
    {
        const string userPrefix = "\\REGISTRY\\USER\\";
        const string classesSubKey = "Software\\Classes";
        if (!path.StartsWith(userPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var userPath = path[userPrefix.Length..];
        var separator = userPath.IndexOf('\\');
        if (separator < 0)
        {
            return path;
        }

        var hiveName = userPath[..separator];
        var subKey = userPath[(separator + 1)..];
        var isClassesPath =
            string.Equals(subKey, classesSubKey, StringComparison.OrdinalIgnoreCase) ||
            subKey.StartsWith(classesSubKey + "\\", StringComparison.OrdinalIgnoreCase);
        if (!IsUserSidHiveName(hiveName) ||
            !isClassesPath)
        {
            return path;
        }

        var remaining = subKey.Length == classesSubKey.Length
            ? string.Empty
            : subKey[(classesSubKey.Length + 1)..];
        return CombineRegistryPath(
            $"{userPrefix}{hiveName}_Classes",
            remaining);
    }

    private static bool IsUserSidHiveName(string hiveName)
    {
        if (hiveName.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            _ = new SecurityIdentifier(hiveName);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void ValidateRegistrySegments(string path, string originalPath)
    {
        if (path.Length != 0 &&
            path.Split('\\').Any(segment => segment.Length == 0))
        {
            throw new WespException(
                WespOperation.ValidatePolicy,
                $"The registry key path contains an empty segment: {originalPath}");
        }
    }

    private static string GetCurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ??
               throw new WespException(
                   WespOperation.ValidatePolicy,
                   "The current Windows user SID could not be resolved for the registry rule.");
    }

    internal static string NormalizeRootExecutable(string path)
    {
        var normalized = NormalizePath(path, "root executable");
        if (!File.Exists(normalized))
        {
            throw new WespException(
                WespOperation.ValidatePolicy,
                $"The executable was not found: {normalized}");
        }

        return normalized;
    }

    internal static string NormalizeWorkingDirectory(string? path, string executablePath)
    {
        var selected = string.IsNullOrWhiteSpace(path)
            ? Path.GetDirectoryName(executablePath)
            : NormalizePath(path, "working directory");
        selected ??= Environment.SystemDirectory;
        if (!Directory.Exists(selected))
        {
            throw new WespException(
                WespOperation.ValidatePolicy,
                $"The working directory was not found: {selected}");
        }

        return selected;
    }

    private static string[] NormalizeDirectories(
        IReadOnlyList<string>? paths,
        string parameterName)
    {
        if (paths is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        var normalized = paths
            .Select(path => NormalizePath(path, parameterName))
            .Select(TrimTrailingDirectorySeparators)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var path in normalized)
        {
            if (!Directory.Exists(path))
            {
                throw new WespException(
                    WespOperation.ValidatePolicy,
                    $"The protected folder was not found: {path}");
            }
        }

        return normalized;
    }

    private static string[] NormalizeFiles(
        IReadOnlyList<string>? paths,
        string parameterName)
    {
        if (paths is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        var normalized = paths
            .Select(path => NormalizePath(path, parameterName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var path in normalized)
        {
            if (string.IsNullOrWhiteSpace(Path.GetFileName(path)))
            {
                throw new WespException(
                    WespOperation.ValidatePolicy,
                    $"A blocked child application path must include a file name: {path}");
            }
        }

        return normalized;
    }

    private static string[] NormalizeRegistryKeys(
        IReadOnlyList<string>? paths,
        string parameterName)
    {
        if (paths is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        return paths
            .SelectMany(NormalizeRegistryKeyPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizePath(string path, string description)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0'))
        {
            throw new WespException(
                WespOperation.ValidatePolicy,
                $"A valid {description} path is required.");
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
            var fullPath = Path.GetFullPath(expanded);
            if (fullPath.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
            {
                return "\\\\" + fullPath[8..];
            }

            return fullPath.StartsWith("\\\\?\\", StringComparison.OrdinalIgnoreCase)
                ? fullPath[4..]
                : fullPath;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new WespException(
                WespOperation.ValidatePolicy,
                $"The {description} path is invalid: {path}",
                innerException: exception);
        }
    }

    private static string TrimTrailingDirectorySeparators(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
