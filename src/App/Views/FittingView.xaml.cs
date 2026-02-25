using System.Windows;
using System.Windows.Controls;
using Ul8ziz.FittingApp.App.Models;
using Ul8ziz.FittingApp.App.ViewModels;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.Views
{
    public partial class FittingView : UserControl
    {
        public FittingView()
        {
            InitializeComponent();
            var vm = new FittingViewModel();
            DataContext = vm;
            Loaded += (_, _) => vm.OnNavigatedTo();
        }

        private void OnLeftGroupExpanded(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Expander exp || exp.DataContext is not GroupDescriptor g)
                return;
            (DataContext as FittingViewModel)?.EnsureGroupLoadedAsync(DeviceSide.Left, g.Id);
        }

        private void OnRightGroupExpanded(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Expander exp || exp.DataContext is not GroupDescriptor g)
                return;
            (DataContext as FittingViewModel)?.EnsureGroupLoadedAsync(DeviceSide.Right, g.Id);
        }
    }
}
