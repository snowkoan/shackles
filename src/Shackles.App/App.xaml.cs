using System.Windows;

namespace Shackles.App;

public partial class App : Application
{
    internal const string WespWorkspaceArgument = "--workspace=wesp";
    internal const string WfpWorkspaceArgument = "--workspace=wfp";

    internal static bool ShouldOpenWespWorkspace =>
        Environment.GetCommandLineArgs().Any(argument => string.Equals(
            argument,
            WespWorkspaceArgument,
            StringComparison.OrdinalIgnoreCase));

    internal static bool ShouldOpenWfpWorkspace =>
        Environment.GetCommandLineArgs().Any(argument => string.Equals(
            argument,
            WfpWorkspaceArgument,
            StringComparison.OrdinalIgnoreCase));
}
