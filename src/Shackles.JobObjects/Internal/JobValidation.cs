namespace Shackles.JobObjects.Internal;

internal static class JobValidation
{
    internal static void ValidateName(string? name, string parameterName)
    {
        if (name is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A named job must have a non-empty name.", parameterName);
        }

        if (name.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("A job name cannot contain a null character.", parameterName);
        }

        const int maximumJobNameLength = 260;
        if (name.Length > maximumJobNameLength)
        {
            throw new ArgumentException($"A job name cannot exceed {maximumJobNameLength} characters.", parameterName);
        }

        var unqualifiedName = name.StartsWith("Global\\", StringComparison.Ordinal) ||
                              name.StartsWith("Local\\", StringComparison.Ordinal)
            ? name[7..]
            : name;
        if (string.IsNullOrWhiteSpace(unqualifiedName) || unqualifiedName.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A job name cannot contain a backslash except for an exact leading Global\\ or Local\\ prefix.",
                parameterName);
        }
    }

    internal static long ToPositiveTicks(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The time limit must be greater than zero.");
        }

        return value.Ticks;
    }

    internal static TimeSpan FromNonNegativeTicks(long ticks) => ticks <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks(ticks);

    internal static nuint ToNativeSize(ulong value, string parameterName)
    {
        if (IntPtr.Size == 4 && value > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value does not fit in a 32-bit process.");
        }

        return (nuint)value;
    }
}
