using System.Windows;
using System.Windows.Controls;

namespace Launchly.App.SettingsUi.Pages;

/// <summary>Eklentileri açıp kapatma; Programs eklentisinin altında gizlenen uygulamalar.</summary>
public partial class PluginsPage : UserControl
{
    public PluginsPage()
    {
        InitializeComponent();
    }

    private void OnUnhideProgram(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel && sender is FrameworkElement { Tag: HiddenProgramItem item })
        {
            viewModel.Unhide(item);
        }
    }
}
