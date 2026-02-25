using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Ul8ziz.FittingApp.App.Models
{
    /// <summary>Single curve/series for plotting: label, points, color.</summary>
    public class GraphSeries : INotifyPropertyChanged
    {
        private string _label = string.Empty;
        private IReadOnlyList<GraphPoint> _points = Array.Empty<GraphPoint>();
        private Color _color;

        public string Label { get => _label; set { _label = value ?? ""; OnPropertyChanged(); } }
        public IReadOnlyList<GraphPoint> Points { get => _points; set { _points = value ?? Array.Empty<GraphPoint>(); OnPropertyChanged(); } }
        public Color Color { get => _color; set { _color = value; OnPropertyChanged(); OnPropertyChanged(nameof(Brush)); } }
        /// <summary>Brush for XAML bindings (e.g. Rectangle.Fill).</summary>
        public SolidColorBrush Brush => new SolidColorBrush(_color);

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public struct GraphPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
    }
}
