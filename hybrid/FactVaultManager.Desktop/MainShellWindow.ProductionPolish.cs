using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _productionPolishApplied;
    private bool _productionProjectFilterUpdating;
    private CheckBox? _productionShowCompletedCheckBox;

    private void ApplyProductionPolish()
    {
        if (_productionPolishApplied ||
            _productionProjectComboBox is null ||
            _productionTopicTextBox is null ||
            _productionProgressBar is null ||
            MainTabs.Items.Count <= 2 ||
            MainTabs.Items[2] is not TabItem productionTab ||
            productionTab.Content is not Grid page)
        {
            return;
        }

        _productionPolishApplied = true;

        page.Margin = new Thickness(30, 24, 30, 28);
        if (page.RowDefinitions.Count > 0)
        {
            page.RowDefinitions[0].MinHeight = 64;
        }

        if (page.Children.OfType<StackPanel>().FirstOrDefault() is { } header)
        {
            header.Margin = new Thickness(0, 0, 0, 20);
        }

        var body = page.Children.OfType<Grid>().FirstOrDefault(grid => Grid.GetRow(grid) == 1);
        if (body is not null && body.ColumnDefinitions.Count >= 3)
        {
            body.ColumnDefinitions[0].Width = new GridLength(390);
            body.ColumnDefinitions[0].MinWidth = 360;
            body.ColumnDefinitions[1].Width = new GridLength(18);
            body.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            body.ColumnDefinitions[2].MinWidth = 560;
        }

        _productionProjectComboBox.Height = 36;
        _productionTopicTextBox.Height = 36;
        _productionTopicTextBox.Padding = new Thickness(10, 5, 10, 5);
        _productionTopicTextBox.ToolTip = "Video topic";
        _productionAssetKindComboBox.Height = 36;

        AddProductionCompletedProjectToggle();

        _productionPexelsCheckBox.Margin = new Thickness(2, 3, 22, 3);
        _productionPixabayCheckBox.Margin = new Thickness(0, 3, 0, 3);
        _productionVoiceCheckBox.Margin = new Thickness(2, 12, 0, 2);

        _productionProjectStatusText.Margin = new Thickness(0, 11, 0, 0);
        _productionProjectFolderText.Margin = new Thickness(0, 5, 0, 2);
        _productionCredentialText.LineHeight = 20;

        foreach (var button in new[]
        {
            _productionActionButton,
            _productionResumeButton,
            _productionExportButton,
            _productionCancelButton,
            _productionOpenFolderButton,
            _productionRefreshButton,
        })
        {
            button.Height = ReferenceEquals(button, _productionActionButton) ? 40 : 36;
            button.Margin = new Thickness(0, 0, 0, 7);
            button.Padding = new Thickness(12, 0, 12, 0);
        }

        _productionCancelButton.BorderBrush = new SolidColorBrush(Color.FromRgb(253, 162, 155));
        _productionCancelButton.Background = Brushes.White;
        _productionOpenFolderButton.Margin = new Thickness(0, 4, 0, 7);
        _productionRefreshButton.Margin = new Thickness(0, 2, 0, 0);

        _productionCurrentStageText.FontSize = 19;
        _productionElapsedText.Margin = new Thickness(0, 5, 0, 0);
        _productionProgressBar.Height = 8;
        _productionProgressBar.Margin = new Thickness(0, 14, 0, 16);

        foreach (var pair in _productionStageRows)
        {
            var icon = pair.Value.Icon;
            var detail = pair.Value.Detail;
            icon.FontSize = 15;
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            detail.Margin = new Thickness(14, 0, 4, 0);
            detail.MaxWidth = 310;
            detail.TextTrimming = TextTrimming.CharacterEllipsis;
            if (icon.Parent is Grid row)
            {
                row.Height = 42;
                row.Margin = new Thickness(0, 1, 0, 1);
                row.Background = Brushes.Transparent;
            }
        }

        _productionLogTextBox.Padding = new Thickness(10, 8, 10, 8);
        _productionLogTextBox.BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236));
        _productionLogTextBox.BorderThickness = new Thickness(1);
        _productionLogTextBox.FontSize = 11.5;
    }

    private void AddProductionCompletedProjectToggle()
    {
        if (_productionShowCompletedCheckBox is not null ||
            _productionProjectComboBox.Parent is not StackPanel projectPanel)
        {
            return;
        }

        _productionShowCompletedCheckBox = new CheckBox
        {
            Content = "Show completed projects",
            IsChecked = true,
            Margin = new Thickness(2, 8, 0, 1),
            Foreground = ProductionMutedBrush(),
        };
        _productionShowCompletedCheckBox.Checked += (_, _) => ApplyProductionProjectVisibility();
        _productionShowCompletedCheckBox.Unchecked += (_, _) => ApplyProductionProjectVisibility();

        var comboIndex = projectPanel.Children.IndexOf(_productionProjectComboBox);
        projectPanel.Children.Insert(Math.Max(0, comboIndex + 1), _productionShowCompletedCheckBox);

        var descriptor = DependencyPropertyDescriptor.FromProperty(
            ItemsControl.ItemsSourceProperty,
            typeof(ComboBox));
        descriptor?.AddValueChanged(_productionProjectComboBox, (_, _) =>
        {
            if (_productionProjectFilterUpdating)
            {
                return;
            }
            Dispatcher.BeginInvoke(new Action(ApplyProductionProjectVisibility));
        });

        ApplyProductionProjectVisibility();
    }

    private void ApplyProductionProjectVisibility()
    {
        if (_productionShowCompletedCheckBox is null || _productionProjectComboBox is null || _productionProjectFilterUpdating)
        {
            return;
        }

        _productionProjectFilterUpdating = true;
        try
        {
            var selectedId = EmbeddedSelectedProject?.Id;
            var showCompleted = _productionShowCompletedCheckBox.IsChecked == true;
            var visibleProjects = _productionProjects
                .Where(project => showCompleted || !string.Equals(project.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                .ToList();

            _productionProjectComboBox.ItemsSource = null;
            _productionProjectComboBox.ItemsSource = visibleProjects;
            _productionProjectComboBox.SelectedItem = selectedId is int id
                ? visibleProjects.FirstOrDefault(project => project.Id == id) ?? visibleProjects.FirstOrDefault()
                : visibleProjects.FirstOrDefault();

            if (visibleProjects.Count == 0)
            {
                _productionProjectStatusText.Text = showCompleted
                    ? "No In Progress or Completed projects found."
                    : "No In Progress projects found. Turn on Show completed projects to include completed work.";
                _productionProjectFolderText.Text = "";
            }
            else
            {
                ApplyEmbeddedSelectedProject();
            }
        }
        finally
        {
            _productionProjectFilterUpdating = false;
        }
    }
}
