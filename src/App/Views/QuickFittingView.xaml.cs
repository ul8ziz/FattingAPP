using System.Windows.Controls;
using Ul8ziz.FittingApp.App.ViewModels;

namespace Ul8ziz.FittingApp.App.Views
{
    /// <summary>
    /// Standalone navigation screen for abbreviated fitting controls (FBC, NR master, MMI, power-on delay).
    /// Shares the same <see cref="FittingViewModel"/> instance as <see cref="FittingView"/> when created from MainView.
    /// </summary>
    public partial class QuickFittingView : UserControl
    {
        public QuickFittingView()
        {
            InitializeComponent();
            var vm = new FittingViewModel();
            DataContext = vm;
        }

        public QuickFittingView(FittingViewModel sharedViewModel)
        {
            InitializeComponent();
            DataContext = sharedViewModel;
        }
    }
}
