using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const string InstagramApprovalButtonTag = "InstagramApprovalButton";

    public void InitializeInstagramPromoApprovalUi()
    {
        if (!_settingsPages.TryGetValue("facebook", out var facebookPage))
            return;

        var resetButton = FindDescendantButton(facebookPage, "Reset approved upload Page");
        if (resetButton?.Parent is not Panel panel)
            return;

        if (panel.Children.OfType<Button>().Any(button =>
                string.Equals(button.Tag?.ToString(), InstagramApprovalButtonTag, StringComparison.Ordinal)))
        {
            if (_data.LoadSettings().ApprovedFacebookPageId.Trim().Length > 0)
                InitializeInstagramPromoFollowup();
            return;
        }

        var status = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 4),
            TextWrapping = TextWrapping.Wrap,
            Foreground = SettingsMutedBrush(),
        };

        var approveButton = new Button
        {
            Content = "Approve Page for Instagram Autopilot",
            Tag = InstagramApprovalButtonTag,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0),
        };
        approveButton.Click += async (_, _) => await ApproveInstagramFacebookPageAsync(status, approveButton);

        var index = panel.Children.IndexOf(resetButton);
        panel.Children.Insert(index + 1, status);
        panel.Children.Insert(index + 2, approveButton);

        var settings = _data.LoadSettings();
        if (settings.ApprovedFacebookPageId.Trim().Length > 0)
        {
            status.Text = $"Instagram Autopilot destination approved: {settings.ApprovedFacebookPageName} ({settings.ApprovedFacebookPageId})";
            approveButton.Content = "Re-check approved Page";
            InitializeInstagramPromoFollowup();
        }
        else
        {
            status.Text = "Instagram Autopilot is waiting for a one-time approval of the connected Facebook Page.";
        }
    }

    private async Task ApproveInstagramFacebookPageAsync(TextBlock status, Button button)
    {
        button.IsEnabled = false;
        try
        {
            var settings = _data.LoadSettings();
            var pageToken = FacebookPageToken();
            if (pageToken.Trim().Length == 0)
                throw new InvalidOperationException("Connect the Facebook Page access token first.");

            var identity = await _facebookAnalytics.GetPageIdentityAsync(pageToken);
            if (identity.PageId.Trim().Length == 0)
                throw new InvalidOperationException("The connected Facebook account did not return a Page ID.");

            settings.ApprovedFacebookPageId = identity.PageId;
            settings.ApprovedFacebookPageName = identity.PageName;
            _data.SaveSettings(settings);
            ClearStaleInstagramApprovalFailures();

            status.Text = $"Instagram Autopilot destination approved: {identity.PageName} ({identity.PageId})";
            button.Content = "Re-check approved Page";
            InitializeInstagramPromoFollowup();
            await RefreshAutopilotHomeAsync();
        }
        catch (Exception error)
        {
            status.Text = error.Message;
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void ClearStaleInstagramApprovalFailures()
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={_data.DatabasePath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE publication_state
                SET state = 'pending',
                    failed_step = '',
                    last_error = '',
                    updated_at = datetime('now')
                WHERE platform = 'Instagram'
                  AND content_kind = 'promo'
                  AND last_error LIKE 'Approve the Facebook Page / linked Instagram destination once%';
                """;
            command.ExecuteNonQuery();
        }
        catch
        {
            // A stale setup failure is cosmetic; normal publication reconciliation can repair it later.
        }
    }

    private static Button? FindDescendantButton(DependencyObject root, string content)
    {
        if (root is Button button && string.Equals(button.Content?.ToString(), content, StringComparison.Ordinal))
            return button;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var found = FindDescendantButton(VisualTreeHelper.GetChild(root, index), content);
            if (found is not null)
                return found;
        }

        return null;
    }
}
