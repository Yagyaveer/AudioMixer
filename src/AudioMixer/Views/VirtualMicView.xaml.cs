using System.Windows.Controls;
using AudioMixer.ViewModels;

namespace AudioMixer.Views;

public partial class VirtualMicView : UserControl
{
    public VirtualMicView()
    {
        InitializeComponent();
    }

    private void AddableCombo_DropDownOpened(object sender, EventArgs e)
    {
        if (DataContext is VirtualMicViewModel vm) vm.RefreshAddableSources();
    }
}
