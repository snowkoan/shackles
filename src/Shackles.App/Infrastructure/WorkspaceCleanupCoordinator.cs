namespace Shackles.App.Infrastructure;

internal static class WorkspaceCleanupCoordinator
{
    public static void Dispose(IEnumerable<(string Name, Action Dispose)> workspaces)
    {
        var errors = new List<Exception>();
        foreach (var (name, dispose) in workspaces)
        {
            try { dispose(); }
            catch (Exception ex) { errors.Add(new InvalidOperationException($"{name}: {ex.Message}", ex)); }
        }

        if (errors.Count != 0)
        {
            throw new AggregateException("Some workspace resources could not be closed. Retry cleanup.", errors);
        }
    }

    public static async Task<IReadOnlyList<string>> CloseAsync(
        IEnumerable<(string Name, Func<Task<IReadOnlyList<string>>> Close)> workspaces)
    {
        var warnings = new List<string>();
        foreach (var (name, close) in workspaces)
        {
            try
            {
                warnings.AddRange((await close().ConfigureAwait(true)).Select(message => $"{name}: {message}"));
            }
            catch (Exception ex)
            {
                // A workspace failure must not prevent cleanup of the others.
                warnings.Add($"{name}: {ex.Message}");
            }
        }
        return warnings;
    }
}
