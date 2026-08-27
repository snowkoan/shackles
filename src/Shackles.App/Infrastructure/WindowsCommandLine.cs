using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Shackles.App.Infrastructure;

internal static class WindowsCommandLine
{
    private const int MaximumCommandLineCharacters = 32_767;

    public static IReadOnlyList<string> ParseArguments(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return Array.Empty<string>();
        }

        if (commandLine.Length > MaximumCommandLineCharacters - 64)
        {
            throw new ArgumentException("The argument list exceeds the Windows command-line length limit.", nameof(commandLine));
        }

        // Prefix a synthetic executable so every user-supplied token is parsed using
        // the normal non-argv[0] CommandLineToArgvW quoting rules.
        var pointer = CommandLineToArgvW($"\"shackles-argument-parser.exe\" {commandLine}", out var argumentCount);
        if (pointer == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not parse the arguments.");
        }

        try
        {
            var result = new string[Math.Max(0, argumentCount - 1)];
            for (var index = 1; index < argumentCount; index++)
            {
                var argumentPointer = Marshal.ReadIntPtr(pointer, index * IntPtr.Size);
                result[index - 1] = Marshal.PtrToStringUni(argumentPointer) ?? string.Empty;
            }

            return result;
        }
        finally
        {
            _ = LocalFree(pointer);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CommandLineToArgvW(string commandLine, out int argumentCount);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);
}
