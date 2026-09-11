using System.Windows;

namespace Shackles.App;

public partial class App : Application
{
    internal const string WespWorkspaceArgument = "--workspace=wesp";

    internal static bool ShouldOpenWespWorkspace =>
        Environment.GetCommandLineArgs().Any(argument => string.Equals(
            argument,
            WespWorkspaceArgument,
            StringComparison.OrdinalIgnoreCase));
}
