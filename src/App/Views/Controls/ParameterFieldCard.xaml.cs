using System.Windows;
using System.Windows.Controls;

namespace Ul8ziz.FittingApp.App.Views.Controls
{
    /// <summary>
    /// Reusable card row for a single parameter: title, description, value control area, unit badge, reset, dirty state.
    /// DataContext is the parameter ViewModel. ValueTemplate is provided by the parent (e.g. FittingView).
    /// </summary>
    public partial class ParameterFieldCard : UserControl
    {
        public static readonly DependencyProperty ValueTemplateProperty =
            DependencyProperty.Register(
                nameof(ValueTemplate),
                typeof(DataTemplate),
                typeof(ParameterFieldCard),
                new PropertyMetadata(null));

        /// <summary>DataTemplate used to render the value control (ComboBox, Slider, Toggle, etc.).</summary>
        public DataTemplate? ValueTemplate
        {
            get => (DataTemplate?)GetValue(ValueTemplateProperty);
            set => SetValue(ValueTemplateProperty, value);
        }

        public ParameterFieldCard()
        {
            InitializeComponent();
        }
    }
}
