using System.Windows;

namespace NexaPlay.Owner;

public partial class OwnerSetupWindow : Window
{
    public string CommunityUrl => UrlBox.Text.Trim();
    public string AdminKey => KeyBox.Password.Trim();
    public OwnerSetupWindow(string url) { InitializeComponent(); UrlBox.Text = url; }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(CommunityUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback) || AdminKey.Length < 16) { MessageBox.Show("Enter an HTTPS server URL and an owner key with at least 16 characters.", "Owner connection", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        DialogResult = true; Close();
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
