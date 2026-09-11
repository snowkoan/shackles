using System.Runtime.InteropServices;
using Shackles.Wesp.Interop;

namespace Shackles.Wesp.Internal;

internal static class WespPatternMatcher
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int StringMatchesPattern(
        in NativeUnicodeString value,
        in NativeUnicodeString pattern,
        out int matches);

    internal static unsafe bool Matches(
        string libraryPath,
        string value,
        string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(pattern);
        ValidateLength(value, nameof(value));
        ValidateLength(pattern, nameof(pattern));

        var library = NativeLibrary.Load(libraryPath);
        try
        {
            var export = NativeLibrary.GetExport(library, "EspStringMatchesPattern");
            var matchesPattern = Marshal.GetDelegateForFunctionPointer<StringMatchesPattern>(export);
            fixed (char* valueBuffer = value)
            fixed (char* patternBuffer = pattern)
            {
                var nativeValue = new NativeUnicodeString
                {
                    LengthBytes = checked((ushort)(value.Length * sizeof(char))),
                    Buffer = (nint)valueBuffer
                };
                var nativePattern = new NativeUnicodeString
                {
                    LengthBytes = checked((ushort)(pattern.Length * sizeof(char))),
                    Buffer = (nint)patternBuffer
                };
                var result = matchesPattern(
                    in nativeValue,
                    in nativePattern,
                    out var matches);
                if (result < 0)
                {
                    Marshal.ThrowExceptionForHR(result);
                }

                return matches != 0;
            }
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static void ValidateLength(string value, string parameterName)
    {
        if (value.Length > ushort.MaxValue / sizeof(char))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "WESP strings cannot exceed the ESP_UNICODE_STRING byte-length limit.");
        }
    }
}
