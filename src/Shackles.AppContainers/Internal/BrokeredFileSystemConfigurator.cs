using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Shackles.AppContainers.Internal;

internal interface IBrokeredFileSystemConfigurator
{
    BrokeredFileSystemSupport Support { get; }

    void AddPolicy(string appContainerName, TrackedAclGrant grant);

    string? TryClearPolicy(string appContainerName);
}

internal static class BrokeredFileSystemSupportProbe
{
    internal static BrokeredFileSystemSupport Probe()
    {
        var osVersion = Environment.OSVersion.Version;
        if (!OperatingSystem.IsWindows())
        {
            return new BrokeredFileSystemSupport(
                BrokeredFileSystemAvailability.PlatformNotSupported,
                "Brokered File System policy is available only on Windows.",
                osVersion,
                null,
                null,
                false,
                Array.Empty<string>());
        }

        var windowsDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDirectory))
        {
            return new BrokeredFileSystemSupport(
                BrokeredFileSystemAvailability.WindowsDirectoryUnavailable,
                "Windows did not report its installation directory, so Shackles " +
                "cannot resolve bfscfg.exe safely.",
                osVersion,
                null,
                null,
                false,
                Array.Empty<string>());
        }

        return Probe(windowsDirectory, osVersion, File.Exists);
    }

    internal static BrokeredFileSystemSupport Probe(
        string windowsDirectory,
        Version osVersion,
        Func<string, bool> fileExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowsDirectory);
        ArgumentNullException.ThrowIfNull(osVersion);
        ArgumentNullException.ThrowIfNull(fileExists);

        var fullWindowsDirectory = Path.GetFullPath(windowsDirectory);
        var configurationToolPath = Path.Combine(
            fullWindowsDirectory,
            "System32",
            "bfscfg.exe");
        var driverPath = Path.Combine(
            fullWindowsDirectory,
            "System32",
            "drivers",
            "bfs.sys");
        var toolPresent = fileExists(configurationToolPath);
        var driverPresent = fileExists(driverPath);
        var warnings = new List<string>
        {
            "BFS and bfscfg.exe are experimental, partially documented Windows " +
            "interfaces. File presence confirms only that Shackles can attempt " +
            "the operation; Windows remains authoritative."
        };

        if (!driverPresent)
        {
            warnings.Add(
                "The bfs.sys driver file was not found. bfscfg.exe may still " +
                "provide a more specific error, but a usable broker is not confirmed.");
        }

        if (osVersion.Build is >= 26200 and < 26600)
        {
            warnings.Add(
                "This is a Windows 11 25H2 build. Microsoft MXC reports that " +
                "bfscfg.exe can deadlock 25H2 hosts; a process timeout cannot " +
                "guarantee recovery from a kernel-side stall. Use a disposable " +
                "test machine for this experiment.");
        }

        return new BrokeredFileSystemSupport(
            toolPresent
                ? BrokeredFileSystemAvailability.Available
                : BrokeredFileSystemAvailability.ConfigurationToolMissing,
            toolPresent
                ? "The OS-shipped bfscfg.exe is present. Shackles can attempt " +
                  "Brokered File System policy for an explicitly selected sandbox."
                : $"The OS-shipped bfscfg.exe was not found at " +
                  $"'{configurationToolPath}'.",
            osVersion,
            toolPresent ? configurationToolPath : null,
            driverPath,
            driverPresent,
            warnings);
    }
}

internal sealed class BrokeredFileSystemConfigurator :
    IBrokeredFileSystemConfigurator
{
    private const string FailureMarker =
        "Unable to perform policy operation";
    private const int OutputLimit = 16 * 1024;
    private static readonly TimeSpan OperationTimeout =
        TimeSpan.FromSeconds(10);

    internal BrokeredFileSystemConfigurator()
        : this(BrokeredFileSystemSupportProbe.Probe())
    {
    }

    internal BrokeredFileSystemConfigurator(
        BrokeredFileSystemSupport support)
    {
        ArgumentNullException.ThrowIfNull(support);
        Support = support;
    }

    public BrokeredFileSystemSupport Support { get; }

    public void AddPolicy(
        string appContainerName,
        TrackedAclGrant grant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appContainerName);
        ArgumentNullException.ThrowIfNull(grant);
        if (grant.Kind != TrackedGrantKind.FileSystem)
        {
            throw new ArgumentException(
                "BFS policy can be added only for a file-system grant.",
                nameof(grant));
        }

        Run(
            BuildAddArguments(appContainerName, grant),
            AppContainerOperation.ConfigureBrokeredFileSystem,
            $"add the {(grant.FileSystemAccess == FileSystemGrantAccess.ReadExecute ? "read-only" : "read/write")} " +
            $"BFS rule for '{grant.Target}'");
    }

    public string? TryClearPolicy(string appContainerName)
    {
        try
        {
            Run(
                BuildClearArguments(appContainerName),
                AppContainerOperation.ClearBrokeredFileSystem,
                $"clear BFS policy for AppContainer '{appContainerName}'");
            return null;
        }
        catch (Exception exception)
        {
            return $"Could not clear Brokered File System policy for " +
                   $"AppContainer '{appContainerName}': {exception.Message}";
        }
    }

    internal static string[] BuildAddArguments(
        string appContainerName,
        TrackedAclGrant grant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appContainerName);
        ArgumentNullException.ThrowIfNull(grant);
        if (grant.Kind != TrackedGrantKind.FileSystem)
        {
            throw new ArgumentException(
                "BFS arguments require a file-system grant.",
                nameof(grant));
        }

        var arguments = new List<string>
        {
            "--addpolicy",
            grant.FileSystemAccess == FileSystemGrantAccess.ReadExecute
                ? "--policybrokerreadonly"
                : "--policybroker",
            "--filename",
            grant.Target,
            "--appid",
            appContainerName,
            "--entrytype",
            grant.IsDirectory ? "directory" : "file"
        };
        if (grant.IsDirectory && !IsSystemDriveRoot(grant.Target))
        {
            arguments.Add("--containerinherit");
        }

        return arguments.ToArray();
    }

    internal static string[] BuildClearArguments(string appContainerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appContainerName);
        return ["--clearpolicy", "--appid", appContainerName];
    }

    private void Run(
        IReadOnlyList<string> arguments,
        AppContainerOperation operation,
        string description)
    {
        var toolPath = Support.ConfigurationToolPath;
        if (!Support.IsAvailable || string.IsNullOrWhiteSpace(toolPath))
        {
            throw new AppContainerException(operation, Support.Summary);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = toolPath,
            WorkingDirectory = Path.GetDirectoryName(toolPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var output = new BoundedProcessOutput(OutputLimit);
        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = false
        };
        process.OutputDataReceived += (_, eventArgs) =>
            output.Append("stdout", eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) =>
            output.Append("stderr", eventArgs.Data);

        try
        {
            if (!process.Start())
            {
                throw new AppContainerException(
                    operation,
                    $"Windows did not start '{toolPath}' to {description}.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(
                    checked((int)OperationTimeout.TotalMilliseconds)))
            {
                TryTerminate(process);
                throw new AppContainerException(
                    operation,
                    $"'{toolPath}' did not exit within " +
                    $"{OperationTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} " +
                    $"seconds while trying to {description}. The BFS policy state " +
                    "is uncertain; close the sandbox to retry cleanup.");
            }

            // Flush asynchronous output handlers after the process handle signals.
            process.WaitForExit();
            var captured = output.GetText();
            if (process.ExitCode != 0 || output.ContainsFailureMarker)
            {
                var diagnostic = captured.Length == 0
                    ? "bfscfg.exe produced no diagnostic output."
                    : captured;
                throw new AppContainerException(
                    operation,
                    $"bfscfg.exe exited with code {process.ExitCode} while " +
                    $"trying to {description}. {diagnostic}");
            }
        }
        catch (AppContainerException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AppContainerException(
                operation,
                $"Could not run the OS-shipped bfscfg.exe to {description}.",
                innerException: exception);
        }
    }

    private static bool IsSystemDriveRoot(string path) =>
        string.Equals(
            Path.GetFullPath(path),
            @"C:\",
            StringComparison.OrdinalIgnoreCase);

    private static void TryTerminate(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            _ = process.WaitForExit(1_000);
        }
        catch
        {
            // The timeout error remains authoritative. A stuck kernel-side BFS
            // operation may prevent ordinary process termination.
        }
    }

    private sealed class BoundedProcessOutput
    {
        private readonly object _gate = new();
        private readonly int _limit;
        private readonly StringBuilder _text = new();
        private bool _truncated;

        internal BoundedProcessOutput(int limit)
        {
            _limit = limit;
        }

        internal bool ContainsFailureMarker { get; private set; }

        internal void Append(string stream, string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (_gate)
            {
                if (line.Contains(FailureMarker, StringComparison.OrdinalIgnoreCase))
                {
                    ContainsFailureMarker = true;
                }

                if (_text.Length >= _limit)
                {
                    _truncated = true;
                    return;
                }

                var prefix = $"{stream}: ";
                var remaining = _limit - _text.Length;
                if (remaining <= prefix.Length)
                {
                    _truncated = true;
                    return;
                }

                _text.Append(prefix);
                remaining = _limit - _text.Length;
                var length = Math.Min(line.Length, remaining);
                _text.Append(line.AsSpan(0, length));
                if (length < line.Length)
                {
                    _truncated = true;
                    return;
                }

                if (_text.Length < _limit)
                {
                    _text.AppendLine();
                }
            }
        }

        internal string GetText()
        {
            lock (_gate)
            {
                var result = _text.ToString().Trim();
                return _truncated
                    ? result + " [output truncated]"
                    : result;
            }
        }
    }
}
