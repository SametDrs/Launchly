using System.Windows;
using System.Windows.Controls;

namespace Launchly.App.SettingsUi.Pages;

/// <summary>Web arama kaynaklarının listesi (ekle/sil/düzenle). Değişiklikler Uygula'ya kadar taslakta kalır.</summary>
public partial class SearchSourcesPage : UserControl
{
    public SearchSourcesPage()
    {
        InitializeComponent();
    }

    private void OnAddSource(object sender, RoutedEventArgs e) => (DataContext as SettingsViewModel)?.AddSource();

    private void OnRemoveSource(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel && sender is FrameworkElement { Tag: SearchSourceItem item })
        {
            viewModel.RemoveSource(item);
        }
    }
}
