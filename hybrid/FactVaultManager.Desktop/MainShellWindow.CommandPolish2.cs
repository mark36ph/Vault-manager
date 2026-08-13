using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private void ApplyFinalCommandPolish()
    {
        foreach (var button in FindButtons(this))
        {
            var label = button.Content?.ToString();
            button.FontFamily = new FontFamily("Segoe UI");
            if (label == "Check for Updates") button.Content = "↻  Check for updates";
            else if (label == "Open Production") button.Content = "▶  Production";
            else if (label == "Open Production Workspace") button.Content = "▶  Open production workspace";
            else if (label == "New Project") button.Content = "+  New project";
            else if (label == "Refresh Dashboard" || label == "Refresh Media" || label == "Refresh Review") button.Content = "↻  Refresh";
        }

        foreach (var grid in new[] { ProjectsGrid, MediaGrid, AssetReviewGrid })
        {
            grid.SelectionMode = DataGridSelectionMode.Single;
            grid.SelectionUnit = DataGridSelectionUnit.FullRow;
            grid.RowHeight = 36;
            grid.ColumnHeaderHeight = 34;
        }
    }

    private static System.Collections.Generic.IEnumerable<Button> FindButtons(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button button) yield return button;
            foreach (var nested in FindButtons(child)) yield return nested;
        }
    }
}
