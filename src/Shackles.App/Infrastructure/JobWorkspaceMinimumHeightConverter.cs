using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Shackles.App.Infrastructure;

/// <summary>Keeps the editor usable when workspace chrome needs more vertical room.</summary>
public sealed class JobWorkspaceMinimumHeightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // Include the list's 10px top margin, the 10px details gap, and 2px card border.
        var minimum = 22d;
        for (var index = 0; index < Math.Min(values.Length, 3); index++)
        {
            if (index == 2 && values.Length > 3 && values[3] is Visibility visibility && visibility != Visibility.Visible)
            {
                continue;
            }

            if (values[index] is double height && double.IsFinite(height) && height >= 0)
            {
                minimum += height;
            }
        }

        return Math.Max(650, minimum);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
