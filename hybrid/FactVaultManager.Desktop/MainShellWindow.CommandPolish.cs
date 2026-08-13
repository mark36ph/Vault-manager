using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _commandPolishApplied;

    private void ApplyCommandPolish()
    {
        if (_commandPolishApplied)
        {
            return;
        }
        _commandPolishApplied = true;

        StyleGridSelection(ProjectsGrid);
        StyleGridSelection(MediaGrid);
        StyleGridSelection(AssetReviewGrid);
    }

    private void StyleGridSelection(DataGrid grid)
    {
        grid.SelectionUnit = DataGridSelectionUnit.FullRow;
        grid.SelectionMode = DataGridSelectionMode.Single;
        grid.RowHeight = 36;
        grid.ColumnHeaderHeight = 34;
        grid.BorderThickness = new Thickness(1);
        grid.BorderBrush = MakeBrush("#343434");

        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, MakeBrush("#1B1B1B")));
        rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, MakeBrush("#F2F2F2")));
        rowStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));

        var selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, MakeBrush("#153E75")));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        rowStyle.Triggers.Add(selected);

        grid.RowStyle = rowStyle;
    }
}
