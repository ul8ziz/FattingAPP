using System;
using System.Windows.Controls;

namespace Ul8ziz.FittingApp.App.Views
{
    public partial class AudiogramView : UserControl
    {
        /// <param name="requestNavigate">
        /// Optional navigation callback injected by MainView.
        /// Called with "Fitting" when the user clicks Open Fitting.
        /// </param>
        public AudiogramView(Action<string>? requestNavigate = null)
        {
            InitializeComponent();
            DataContext = new ViewModels.AudiogramViewModel(requestNavigate);
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                (DataContext as ViewModels.AudiogramViewModel)?.PopulateFromSession();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AudiogramView] Loaded error: {ex.Message}");
            }
        }
    }
}
