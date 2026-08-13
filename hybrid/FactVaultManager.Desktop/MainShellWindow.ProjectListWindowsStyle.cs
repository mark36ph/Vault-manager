using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private void ApplyProjectListWindowsStyle()
    {
        ProjectsGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        ProjectsGrid.GridLinesVisibility = DataGridGridLinesVisibility.None;
        ProjectsGrid.Background = MakeBrush("#161616");
        ProjectsGrid.BorderBrush = MakeBrush("#303030");
        ProjectsGrid.BorderThickness = new Thickness(1);
        ProjectsGrid.RowBackground = MakeBrush("#161616");
        ProjectsGrid.AlternatingRowBackground = MakeBrush("#191919");
        ProjectsGrid.RowHeight = 38;
        ProjectsGrid.ColumnHeaderHeight = 32;
        ProjectsGrid.SelectionMode = DataGridSelectionMode.Single;
        ProjectsGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
        ProjectsGrid.CanUserResizeRows = false;
        ProjectsGrid.CanUserReorderColumns = false;
        ProjectsGrid.CanUserSortColumns = true;

        var headerStyle = new Style(typeof(DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, MakeBrush("#1D1D1D")));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, MakeBrush("#BDBDBD")));
        headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, MakeBrush("#303030")));
        headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 0, 8, 0)));
        headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Normal));
        headerStyle.Setters.Add(new Setter(Control.FontSizeProperty, 11.5));
        ProjectsGrid.ColumnHeaderStyle = headerStyle;

        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, MakeBrush("#161616")));
        rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, MakeBrush("#F2F2F2")));
        rowStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        rowStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));

        var hover = new Trigger { Property = DataGridRow.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, MakeBrush("#222222")));
        rowStyle.Triggers.Add(hover);

        var selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, MakeBrush("#153E75")));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        rowStyle.Triggers.Add(selected);
        ProjectsGrid.RowStyle = rowStyle;

        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        cellStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 0, 8, 0)));
        cellStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        cellStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        ProjectsGrid.CellStyle = cellStyle;

        if (ProjectsGrid.Columns.Count >= 2)
        {
            ProjectsGrid.Columns[0].Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            ProjectsGrid.Columns[1].Width = new DataGridLength(96);
        }
    }
}
