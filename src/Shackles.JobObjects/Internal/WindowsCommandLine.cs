using System.Text;

namespace Shackles.JobObjects.Internal;

internal static class WindowsCommandLine
{
    internal static string Build(string executablePath, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var result = new StringBuilder();
        AppendQuotedArgument(result, executablePath);

        foreach (var argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument);
            if (argument.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException("A process argument cannot contain a null character.", nameof(arguments));
            }

            result.Append(' ');
            AppendQuotedArgument(result, argument);
        }

        if (result.Length > 32_766)
        {
            throw new ArgumentException("The Windows command line must be shorter than 32,767 characters.", nameof(arguments));
        }

        return result.ToString();
    }

    // This is the inverse of CommandLineToArgvW/MSVC parsing. The process is launched directly,
    // without cmd.exe or PowerShell, so metacharacters never become shell syntax.
    private static void AppendQuotedArgument(StringBuilder output, string value)
    {
        output.Append('"');
        var backslashes = 0;

        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                output.Append('\\', (backslashes * 2) + 1);
                output.Append('"');
                backslashes = 0;
                continue;
            }

            output.Append('\\', backslashes);
            backslashes = 0;
            output.Append(character);
        }

        output.Append('\\', backslashes * 2);
        output.Append('"');
    }
}
