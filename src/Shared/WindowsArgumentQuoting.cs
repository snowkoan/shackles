using System.Text;

namespace Shackles.Internal;

internal static class WindowsArgumentQuoting
{
    internal static string QuoteArgument(string value)
    {
        var result = new StringBuilder(value.Length + 2);
        AppendQuotedArgument(result, value);
        return result.ToString();
    }

    // Inverse of CommandLineToArgvW/MSVC argument parsing for direct process launches.
    internal static void AppendQuotedArgument(StringBuilder output, string value)
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
                output.Append('\\', checked((backslashes * 2) + 1));
                output.Append('"');
                backslashes = 0;
                continue;
            }

            output.Append('\\', backslashes);
            output.Append(character);
            backslashes = 0;
        }

        output.Append('\\', checked(backslashes * 2));
        output.Append('"');
    }
}
