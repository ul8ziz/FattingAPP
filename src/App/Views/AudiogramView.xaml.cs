using System;
using System.Windows.Controls;

namespace Ul8ziz.FittingApp.App.Views
{
    public partial class AudiogramView : UserControl
    {
        public AudiogramView()
        {
            InitializeComponent();
            DataContext = new ViewModels.AudiogramViewModel();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                (DataContext as ViewModels.AudiogramViewModel)?.SetDataSourceFromSession();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AudiogramView] Loaded error: {ex.Message}");
            }
        }
    }
}
