using System.Net.Mail;
using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

internal sealed class WebsiteUserEditDialog : Window
{
    private readonly TextBox _usernameBox;
    private readonly TextBox _emailBox;
    private readonly PasswordBox _passwordBox;
    private readonly PasswordBox _confirmPasswordBox;

    public WebsiteUserEditDialog(Window owner, string username, string email)
    {
        Owner = owner;
        Title = "Edit Website Account";
        Width = 500;
        Height = 430;
        MinWidth = 500;
        MinHeight = 430;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(24) };
        for (var index = 0; index < 10; index++)
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "Edit Factburst website account",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        });

        var help = new TextBlock
        {
            Text = "Change the username or email, or enter a new password. Leave the password fields blank to keep the current password.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        };
        Grid.SetRow(help, 1);
        root.Children.Add(help);

        AddLabel(root, 2, "Username");
        _usernameBox = AddTextBox(root, 3, username);
        AddLabel(root, 4, "Email address");
        _emailBox = AddTextBox(root, 5, email);
        AddLabel(root, 6, "New password");
        _passwordBox = AddPasswordBox(root, 7);
        AddLabel(root, 8, "Confirm new password");
        _confirmPasswordBox = AddPasswordBox(root, 9);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, MinHeight = 34, IsCancel = true };
        var save = new Button { Content = "Save changes", MinWidth = 118, MinHeight = 34, Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        save.Click += (_, _) => Accept();
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        Grid.SetRow(buttons, 11);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) => _usernameBox.Focus();
    }

    public string Username => _usernameBox.Text.Trim();
    public string Email => _emailBox.Text.Trim();
    public string Password => _passwordBox.Password;

    private void Accept()
    {
        if (Username.Length < 3 || Username.Length > 24 || !System.Text.RegularExpressions.Regex.IsMatch(Username, "^[A-Za-z0-9][A-Za-z0-9 _.-]*[A-Za-z0-9]$"))
        {
            MessageBox.Show(this, "Username must be 3–24 characters and may contain letters, numbers, spaces, dots, dashes or underscores.", "Edit Website Account", MessageBoxButton.OK, MessageBoxImage.Information);
            _usernameBox.Focus();
            return;
        }

        try
        {
            _ = new MailAddress(Email);
        }
        catch
        {
            MessageBox.Show(this, "Enter a valid email address.", "Edit Website Account", MessageBoxButton.OK, MessageBoxImage.Information);
            _emailBox.Focus();
            return;
        }

        if (Password.Length > 0 && (Password.Length < 10 || Password.Length > 128))
        {
            MessageBox.Show(this, "A new password must be between 10 and 128 characters.", "Edit Website Account", MessageBoxButton.OK, MessageBoxImage.Information);
            _passwordBox.Focus();
            return;
        }

        if (!string.Equals(Password, _confirmPasswordBox.Password, StringComparison.Ordinal))
        {
            MessageBox.Show(this, "The new passwords do not match.", "Edit Website Account", MessageBoxButton.OK, MessageBoxImage.Information);
            _confirmPasswordBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private static void AddLabel(Grid root, int row, string text)
    {
        var label = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 0) };
        Grid.SetRow(label, row);
        root.Children.Add(label);
    }

    private static TextBox AddTextBox(Grid root, int row, string value)
    {
        var box = new TextBox { Text = value, MinHeight = 34, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 3, 0, 10) };
        Grid.SetRow(box, row);
        root.Children.Add(box);
        return box;
    }

    private static PasswordBox AddPasswordBox(Grid root, int row)
    {
        var box = new PasswordBox { MinHeight = 34, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 3, 0, 10) };
        Grid.SetRow(box, row);
        root.Children.Add(box);
        return box;
    }
}
