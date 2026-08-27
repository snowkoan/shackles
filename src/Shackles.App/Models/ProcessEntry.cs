namespace Shackles.App.Models;

internal sealed record ProcessEntry(
    int ProcessId,
    string Name,
    string? ImagePath,
    int? SessionId,
    long WorkingSetBytes,
    long? CreationTimeUtcFileTime,
    ProcessIdentityQueryFailure? IdentityQueryFailure,
    bool IsCurrentProcess)
{
    public bool IsAssignable => !IsCurrentProcess && CreationTimeUtcFileTime.HasValue;

    public string AssignmentHint => IsCurrentProcess
        ? "Shackles cannot assign itself to a job."
        : IdentityQueryFailure is not null
            ? IdentityQueryFailure.AssignmentBlockedMessage
            : !CreationTimeUtcFileTime.HasValue
                ? "Windows returned no creation timestamp. Shackles cannot establish the PID-reuse guard, so assignment was not attempted."
                : $"{ImagePath ?? "Executable path was unavailable; this does not affect identity verification."}{Environment.NewLine}" +
                  "PID identity verified. Assignment rights are checked separately; Windows reports any access failure per process.";

    public string WorkingSetDisplay => SizeFormatter.Format(WorkingSetBytes);
}

internal sealed record ProcessIdentity(int ProcessId, long CreationTimeUtcFileTime);

internal sealed record ProcessIdentityCaptureResult(
    long? CreationTimeUtcFileTime,
    ProcessIdentityQueryFailure? Failure);

internal sealed record ProcessIdentityQueryFailure(
    string Operation,
    int NativeErrorCode,
    string Message)
{
    public string AssignmentBlockedMessage => Operation == "OpenProcess" && NativeErrorCode == 5
        ? "Windows denied Shackles the limited process access required to read the creation time " +
          "(PROCESS_QUERY_LIMITED_INFORMATION). OpenProcess failed (5): Access is denied. " +
          "Assignment was not attempted because the PID identity could not be verified."
        : $"{EnsureSentence(Message)} Assignment was not attempted because the PID identity could not be verified.";

    private static string EnsureSentence(string value) => value.EndsWith('.')
        ? value
        : $"{value}.";
}

internal static class SizeFormatter
{
    public static string Format(long bytes)
    {
        if (bytes < 0)
        {
            return "—";
        }

        string[] suffixes = ["B", "KiB", "MiB", "GiB", "TiB"];
        var value = (double)bytes;
        var suffixIndex = 0;
        while (value >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            value /= 1024;
            suffixIndex++;
        }

        return suffixIndex == 0
            ? $"{value:0} {suffixes[suffixIndex]}"
            : $"{value:0.#} {suffixes[suffixIndex]}";
    }
}
