using System.Windows;
using Shackles.App.Models;

namespace Shackles.App.Dialogs;

public partial class AssignmentResultsDialog : Window
{
    internal AssignmentResultsDialog(IReadOnlyList<AssignmentOutcome> outcomes)
    {
        InitializeComponent();
        ResultsList.ItemsSource = outcomes;
        var succeeded = outcomes.Count(item => item.Succeeded);
        var failed = outcomes.Count(item => !item.Succeeded && item.WasAttempted);
        var notAttempted = outcomes.Count(item => !item.WasAttempted);
        SummaryText.Text = failed == 0 && notAttempted == 0
            ? $"All {succeeded} selected process{(succeeded == 1 ? string.Empty : "es")} were assigned."
            : $"{succeeded} assigned · {failed} failed · {notAttempted} not attempted. Unassigned processes remain outside this job.";
    }
}
