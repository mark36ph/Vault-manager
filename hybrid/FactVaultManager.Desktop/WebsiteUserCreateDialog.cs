using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

internal sealed class WebsiteUserCreateDialog : Window
{
    private readonly TextBox _usernameBox;
    private readonly TextBox _emailBox;
    private readonly PasswordBox _passwordBox;
    private readonly CheckBox _activateBox;

    public WebsiteUserCreateDialog(Window owner)
    {
        Owner = owner;
        Title = "Create Website Account";
        Width = 460;
        Height = 405;
        MinWidth = 460;
        MinHeight = 405;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(24) };
        for (var index = 0; index < 9; index++)
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = "Create a Factburst website account",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        };
        root.Children.Add(heading);

        var help = new TextBlock
        {
            Text = "Admin-created accounts may use reserved usernames such as Admin, Support or Factburst. The normal public signup restrictions remain unchanged.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        };
        Grid.SetRow(help, 1);
        root.Children.Add(help);

        AddLabel(root, 2, "Username");
        _usernameBox = AddTextBox(root, 3);

        AddLabel(root, 4, "Email address");
        _emailBox = AddTextBox(root, 5);

        AddLabel(root, 6, "Password");
        _passwordBox = new PasswordBox
        {
            MinHeight = 34,
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 3, 0, 10),
        };
        Grid.SetRow(_passwordBox, 7);
        root.Children.Add(_passwordBox);

        _activateBox = new CheckBox
        {
            Content = "Activate immediately and mark the email as verified",
            IsChecked = true,
            Margin = new Thickness(0, 2, 0, 0),
        };
        Grid.SetRow(_activateBox, 8);
        root.Children.Add(_activateBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, MinHeight = 34, IsCancel = true };
        var create = new Button { Content = "Create account", MinWidth = 112, MinHeight = 34, Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        create.Click += (_, _) => Accept();
        buttons.Children.Add(cancel);
        buttons.Children.Add(create);
        Grid.SetRow(buttons, 10);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) => _usernameBox.Focus();
    }

    public string Username => _usernameBox.Text.Trim();
    public string Email => _emailBox.Text.Trim();
    public string Password => _passwordBox.Password;
    public bool ActivateImmediately => _activateBox.IsChecked == true;

    private void Accept()
    {
        if (Username.Length < 3)
        {
            MessageBox.Show(this, "Enter a username with at least 3 characters.", "Create Website Account", MessageBoxButton.OK, MessageBoxImage.Information);
            _usernameBox.Focus();
            return;
        }
        if (Email.Length == 0 || !Email.Contains('@'))
        {
            MessageBox.Show(this, "Enter a valid email address.", "Create Website Account", MessageBoxButton.OK, MessageBoxImage.Information);
            _emailBox.Focus();
            return;
        }
        if (Password.Length < 10)
        {
            MessageBox.Show(this, "Use a password with at least 10 characters.", "Create Website Account", MessageBoxButton.OK, MessageBoxImage.Information);
            _passwordBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private static void AddLabel(Grid root, int row, string text)
    {
        var label = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 3, 0, 0),
        };
        Grid.SetRow(label, row);
        root.Children.Add(label);
    }

    private static TextBox AddTextBox(Grid root, int row)
    {
        var box = new TextBox
        {
            MinHeight = 34,
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 3, 0, 10),
        };
        Grid.SetRow(box, row);
        root.Children.Add(box);
        return box;
    }
}
