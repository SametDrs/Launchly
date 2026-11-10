using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Launchly.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private int _selectedIndex;

    public ObservableCollection<ResultItemViewModel> Results { get; } = new();

    public MainViewModel()
    {
        // Sahte veri: gerçek sorgu çekirdeği 03-sorgu-cekirdegi'nde gelecek.
        Results.Add(new("Visual Studio Code", "Application", "", "#2F80ED"));
        Results.Add(new("Google Chrome", "Application", "", "#34A853"));
        Results.Add(new("Notepad", "Application", "", "#6B7280"));
        Results.Add(new("File Explorer", "Application", "", "#D98E1F"));
        Results.Add(new("Windows Terminal", "Application", "", "#374151"));
        Results.Add(new("Calculator", "Application", "", "#8B5CF6"));
        Results.Add(new("Settings", "Application", "", "#0EA5E9"));
        Results.Add(new("Task Manager", "Application", "", "#EF4444"));
    }

    /// <summary>Seçimi <paramref name="delta"/> kadar kaydırır; uçlarda başa/sona sarar.</summary>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        var next = (SelectedIndex + delta) % Results.Count;
        SelectedIndex = next < 0 ? next + Results.Count : next;
    }

    /// <summary>Alt+1..9 hızlı seçim iskeleti: 1 tabanlı sıradaki sonucu seçer. Çalıştırma sonraki görevde.</summary>
    public bool SelectByNumber(int number)
    {
        var index = number - 1;
        if (index < 0 || index >= Results.Count)
        {
            return false;
        }

        SelectedIndex = index;
        return true;
    }

    /// <summary>Pencere her açıldığında çağrılır: kutuyu temizler, seçimi başa alır.</summary>
    public void Reset()
    {
        QueryText = string.Empty;
        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }
}
