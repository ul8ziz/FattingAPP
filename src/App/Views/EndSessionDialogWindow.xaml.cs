using System;
using System.Windows;
using System.Windows.Media.Imaging;
using Ul8ziz.FittingApp.App.ViewModels;

namespace Ul8ziz.FittingApp.App.Views
{
    /// <summary>Modal dialog for End Session. No text input; three actions: Save &amp; End, End Without Saving, Cancel.</summary>
    public partial class EndSessionDialogWindow : Window
    {
        public EndSessionDialogResult Result { get; private set; } = EndSessionDialogResult.Cancel;

        public EndSessionDialogWindow()
        {
            InitializeComponent();
            var vm = new EndSessionDialogViewModel();
            vm.CloseRequested += OnCloseRequested;
            DataContext = vm;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var uri = new Uri("pack://application:,,,/Ul8ziz.FittingApp.App;component/Resources/Images/AppIcon.png", UriKind.Absolute);
                Icon = BitmapFrame.Create(uri);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not load dialog icon: {ex.Message}");
            }
        }

        private void OnCloseRequested(EndSessionDialogResult result)
        {
            Result = result;
            Close();
        }
    }
}
