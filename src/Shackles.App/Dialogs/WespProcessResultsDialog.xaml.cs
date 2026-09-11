using System.Windows;
using Shackles.App.Models;
using Shackles.Wesp;

namespace Shackles.App.Dialogs;

public sealed partial class WespProcessResultsDialog : Window
{
    internal WespProcessResultsDialog(
        IReadOnlyList<WespProcessApplyOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        InitializeComponent();

        ResultsList.ItemsSource = outcomes;
        var applied = outcomes.Count(outcome =>
            outcome.Status == WespApplyProcessStatus.Applied);
        var alreadyApplied = outcomes.Count(outcome =>
            outcome.Status == WespApplyProcessStatus.AlreadyApplied);
        var failed = outcomes.Count(outcome =>
            outcome.Status == WespApplyProcessStatus.Failed);
        SummaryText.Text =
            $"{applied} applied • {alreadyApplied} already using session • {failed} failed";
    }
}

internal sealed record WespProcessApplyOutcome(
    ProcessEntry Process,
    WespApplyProcessStatus Status,
    string ErrorMessage)
{
    public string ProcessName => Process.Name;

    public int ProcessId => Process.ProcessId;

    public string StatusDisplay => Status switch
    {
        WespApplyProcessStatus.Applied => "Applied",
        WespApplyProcessStatus.AlreadyApplied => "Already applied",
        _ => "Failed"
    };

    public string Message => Status switch
    {
        WespApplyProcessStatus.Applied =>
            "The process is now using this WESP Blocking session.",
        WespApplyProcessStatus.AlreadyApplied =>
            "The process was already using this WESP Blocking session.",
        _ => ErrorMessage
    };
}
