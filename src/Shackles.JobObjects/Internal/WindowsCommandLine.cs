using System.Text;
using Shackles.Internal;

namespace Shackles.JobObjects.Internal;

internal static class WindowsCommandLine
{
    internal static string Build(string executablePath, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var result = new StringBuilder();
        WindowsArgumentQuoting.AppendQuotedArgument(result, executablePath);

        foreach (var argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument);
            if (argument.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException("A process argument cannot contain a null character.", nameof(arguments));
            }

            result.Append(' ');
            WindowsArgumentQuoting.AppendQuotedArgument(result, argument);
        }

        if (result.Length > 32_766)
        {
            throw new ArgumentException("The Windows command line must be shorter than 32,767 characters.", nameof(arguments));
        }

        return result.ToString();
    }
}
