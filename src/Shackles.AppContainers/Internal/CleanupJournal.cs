using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using Shackles.AppContainers.Interop;

namespace Shackles.AppContainers.Internal;

internal sealed record CleanupJournalRecord
{
    public int FormatVersion { get; init; } = 1;

    public required int OwnerProcessId { get; init; }

    public required long OwnerCreationTimeFileTimeUtc { get; init; }

    public required string DisplayName { get; init; }

    public required string ProfileName { get; init; }

    public required string Sid { get; init; }

    public List<TrackedAclGrant> Grants { get; init; } = [];
}

internal sealed class CleanupJournal
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _path;
    private CleanupJournalRecord _record;

    private CleanupJournal(string path, CleanupJournalRecord record)
    {
        _path = path;
        _record = record;
    }

    internal string Path => _path;

    internal static string DefaultDirectory => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Shackles",
        "AppContainerCleanup");

    internal static CleanupJournal Create(
        string directory,
        AppContainerIdentity identity,
        string displayName)
    {
        Directory.CreateDirectory(directory);
        using var current = Process.GetCurrentProcess();
        var record = new CleanupJournalRecord
        {
            OwnerProcessId = Environment.ProcessId,
            OwnerCreationTimeFileTimeUtc = current.StartTime.ToUniversalTime().ToFileTimeUtc(),
            DisplayName = displayName,
            ProfileName = identity.ProfileName,
            Sid = identity.Sid
        };
        var path = System.IO.Path.Combine(directory, $"{identity.ProfileName}.json");
        var journal = new CleanupJournal(path, record);
        journal.Persist();
        return journal;
    }

    internal void Track(TrackedAclGrant grant)
    {
        lock (_gate)
        {
            if (_record.Grants.Any(item => string.Equals(item.Key, grant.Key, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            _record.Grants.Add(grant);
            PersistCore();
        }
    }

    internal void Delete()
    {
        lock (_gate)
        {
            File.Delete(_path);
        }
    }

    internal static AppContainerRecoveryResult RecoverStale(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return new AppContainerRecoveryResult(0, Array.Empty<string>());
        }

        var recovered = 0;
        var warnings = new List<string>();
        foreach (var path in Directory.EnumerateFiles(directory, "Shackles.*.json"))
        {
            CleanupJournalRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<CleanupJournalRecord>(File.ReadAllText(path), SerializerOptions);
            }
            catch (Exception exception)
            {
                warnings.Add($"Could not read cleanup journal '{System.IO.Path.GetFileName(path)}': {exception.Message}");
                continue;
            }

            var sidBytes = Array.Empty<byte>();
            string? validationError = "invalid content";
            if (record is null ||
                !IsSafeRecord(record, path, out sidBytes, out validationError))
            {
                warnings.Add($"Ignored cleanup journal '{System.IO.Path.GetFileName(path)}': {validationError ?? "invalid content"}");
                continue;
            }

            if (IsOwnerStillRunning(record.OwnerProcessId, record.OwnerCreationTimeFileTimeUtc))
            {
                continue;
            }

            var cleanupWarnings = new List<string>();
            foreach (var grant in record.Grants.AsEnumerable().Reverse())
            {
                var warning = AclGrantManager.TryRevoke(grant, sidBytes);
                if (warning is not null)
                {
                    cleanupWarnings.Add(warning);
                }
            }

            var profileWarning = AppContainerIdentity.TryDelete(record.ProfileName);
            if (profileWarning is not null)
            {
                cleanupWarnings.Add(profileWarning);
            }

            if (cleanupWarnings.Count == 0)
            {
                try
                {
                    File.Delete(path);
                    recovered++;
                }
                catch (Exception exception)
                {
                    warnings.Add($"Recovered '{record.DisplayName}', but could not remove its journal: {exception.Message}");
                }
            }
            else
            {
                warnings.AddRange(cleanupWarnings.Select(item => $"Recovery for '{record.DisplayName}': {item}"));
            }
        }

        return new AppContainerRecoveryResult(recovered, warnings);
    }

    private void Persist()
    {
        lock (_gate)
        {
            PersistCore();
        }
    }

    private void PersistCore()
    {
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_record, SerializerOptions));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static bool IsSafeRecord(
        CleanupJournalRecord record,
        string path,
        out byte[] sidBytes,
        out string? error)
    {
        sidBytes = Array.Empty<byte>();
        error = null;
        if (record.FormatVersion != 1 ||
            string.IsNullOrWhiteSpace(record.ProfileName) ||
            string.IsNullOrWhiteSpace(record.Sid) ||
            record.Grants is null)
        {
            error = "required cleanup fields are missing or unsupported";
            return false;
        }

        var suffix = record.ProfileName.StartsWith("Shackles.", StringComparison.Ordinal)
            ? record.ProfileName["Shackles.".Length..]
            : string.Empty;
        if (suffix.Length != 32 || !suffix.All(Uri.IsHexDigit))
        {
            error = "the profile name is outside Shackles' generated namespace";
            return false;
        }

        if (!string.Equals(
                System.IO.Path.GetFileNameWithoutExtension(path),
                record.ProfileName,
                StringComparison.Ordinal))
        {
            error = "the journal filename does not match its profile";
            return false;
        }

        var result = NativeMethods.DeriveAppContainerSidFromAppContainerName(record.ProfileName, out var rawSid);
        if (result < 0 || rawSid == 0)
        {
            error = $"Windows could not derive the journal profile SID (HRESULT 0x{result:X8})";
            return false;
        }

        try
        {
            var derived = new SecurityIdentifier(rawSid);
            if (!string.Equals(derived.Value, record.Sid, StringComparison.Ordinal))
            {
                error = "the stored SID does not match the generated profile name";
                return false;
            }

            sidBytes = new byte[derived.BinaryLength];
            derived.GetBinaryForm(sidBytes, 0);
            return true;
        }
        finally
        {
            _ = NativeMethods.FreeSid(rawSid);
        }
    }

    private static bool IsOwnerStillRunning(int processId, long expectedCreationTime)
    {
        if (processId <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return process.StartTime.ToUniversalTime().ToFileTimeUtc() == expectedCreationTime && !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch
        {
            // If identity cannot be inspected, retaining a journal is safer than tearing down a
            // potentially live sandbox owned by another Shackles process.
            return true;
        }
    }
}
