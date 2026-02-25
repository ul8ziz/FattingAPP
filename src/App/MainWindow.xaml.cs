using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Ul8ziz.FittingApp.App
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
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
                System.Diagnostics.Debug.WriteLine($"Could not load window icon: {ex.Message}");
            }
        }
    }
}
