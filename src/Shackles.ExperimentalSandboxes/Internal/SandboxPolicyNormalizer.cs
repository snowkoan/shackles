namespace Shackles.ExperimentalSandboxes.Internal;

internal static class SandboxPolicyNormalizer
{
    internal static ExperimentalSandboxOptions Normalize(
        ExperimentalSandboxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var displayName = options.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException(
                "A sandbox name is required.",
                nameof(options));
        }

        if (displayName.Length > 128 || displayName.Contains('\0'))
        {
            throw new ArgumentException(
                "The sandbox name must be 128 characters or fewer and cannot " +
                "contain a null character.",
                nameof(options));
        }

        ArgumentNullException.ThrowIfNull(options.CapabilityNames);
        ArgumentNullException.ThrowIfNull(options.FileSystemRules);
        var capabilities = options.CapabilityNames
            .Select(capability => capability?.Trim() ?? string.Empty)
            .Where(capability => capability.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var capability in capabilities)
        {
            if (capability.Contains(',') || capability.Contains('\0'))
            {
                throw new ArgumentException(
                    $"Capability '{capability}' is invalid. Enter one capability " +
                    "name per item and do not use commas.",
                    nameof(options));
            }
        }

        var rules = options.FileSystemRules
            .Select(NormalizeRule)
            .GroupBy(rule => rule.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(rule => RestrictionRank(rule.Access))
                .First())
            .OrderBy(rule => rule.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var proxyUrl = string.IsNullOrWhiteSpace(options.ProxyUrl)
            ? null
            : options.ProxyUrl.Trim();
        if (options.NetworkMode == ExperimentalSandboxNetworkMode.Proxy)
        {
            if (proxyUrl is null ||
                !Uri.TryCreate(proxyUrl, UriKind.Absolute, out var proxy) ||
                proxy.Scheme is not ("http" or "https"))
            {
                throw new ArgumentException(
                    "Proxy mode requires an absolute HTTP or HTTPS proxy URL.",
                    nameof(options));
            }
        }

        if (!options.UseAppContainer)
        {
            if (capabilities.Length > 0 || rules.Length > 0 ||
                options.NetworkMode != ExperimentalSandboxNetworkMode.Blocked ||
                options.LeastPrivilege)
            {
                throw new ArgumentException(
                    "Capabilities, filesystem policy, network policy, and least-" +
                    "privilege mode require AppContainer isolation.",
                    nameof(options));
            }
        }

        if (options.UseAppContainer &&
            options.IntegrityLevel != ExperimentalSandboxIntegrityLevel.SystemDefault)
        {
            throw new ArgumentException(
                "AppContainer sandboxes must use the system-default (Low) integrity level.",
                nameof(options));
        }

        return options with
        {
            DisplayName = displayName,
            ProxyUrl = proxyUrl,
            CapabilityNames = capabilities,
            FileSystemRules = rules
        };
    }

    internal static ExperimentalSandboxFileRule NormalizeRule(
        ExperimentalSandboxFileRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (string.IsNullOrWhiteSpace(rule.Path) || rule.Path.Contains('\0'))
        {
            throw new ArgumentException(
                "Every filesystem rule requires a valid directory path.",
                nameof(rule));
        }

        var path = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(rule.Path.Trim()));
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(
                $"The sandbox policy directory does not exist: {path}");
        }

        return rule with { Path = path };
    }

    private static int RestrictionRank(ExperimentalSandboxFileAccess access) =>
        access switch
        {
            ExperimentalSandboxFileAccess.Deny => 3,
            ExperimentalSandboxFileAccess.ReadOnly => 2,
            _ => 1
        };
}
