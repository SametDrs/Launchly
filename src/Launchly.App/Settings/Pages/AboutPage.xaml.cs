using System.Windows;
using System.Windows.Controls;

namespace Launchly.App.SettingsUi.Pages;

/// <summary>Logo, sürüm ve depo bağlantısı.</summary>
public partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
    }

    private void OnOpenRepository(object sender, RoutedEventArgs e) => (DataContext as SettingsViewModel)?.OpenRepository();
}
