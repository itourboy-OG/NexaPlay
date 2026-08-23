using System.Windows;

namespace NexaPlay.Owner;

public partial class ReportsWindow : Window
{
    public ReportsWindow(IReadOnlyList<CommunityReport> reports)
    {
        InitializeComponent();
        ReportsList.ItemsSource = reports;
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
