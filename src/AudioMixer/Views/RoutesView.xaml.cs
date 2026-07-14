using System.Windows.Controls;
using AudioMixer.ViewModels;

namespace AudioMixer.Views;

public partial class RoutesView : UserControl
{
    public RoutesView()
    {
        InitializeComponent();
    }

    private void SourceCombo_DropDownOpened(object sender, EventArgs e)
    {
        // Refresh the list of audio-playing apps/devices right when the user looks at it.
        if (DataContext is RoutesViewModel vm) vm.RefreshSourceOptions();
    }
}
