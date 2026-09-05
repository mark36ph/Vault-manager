using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static bool TryCaptureSelectedRelink(Window dialog, out QuizArchiveRelinkRequest request)
    {
        request = null!;
        if (dialog.Content is not Grid root)
            return false;
        var tabs = root.Children.OfType<TabControl>().FirstOrDefault();
        var firstTab = tabs?.Items.Cast<object>().FirstOrDefault();
        if (firstTab is not TabItem { Content: DataGrid grid } ||
            grid.SelectedItem is not QuizArchiveFolderAudit selected || !selected.HasSuggestion || !selected.HistoryId.HasValue)
        {
            return false;
        }

        request = new QuizArchiveRelinkRequest(
            selected.HistoryId.GetValueOrDefault(),
            selected.HistoryLabel,
            selected.CurrentFolder,
            selected.ArchiveFolder,
            selected.Confidence);

        return true;
    }
}
