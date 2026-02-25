using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Ul8ziz.FittingApp.App.Models;

namespace Ul8ziz.FittingApp.App.ViewModels
{
    /// <summary>ViewModel for PlotControl: series, axis labels, and computed layout (margins, scale).</summary>
    public class PlotControlViewModel : INotifyPropertyChanged
    {
        private string _xAxisLabel = "X";
        private string _yAxisLabel = "Y";
        private double _xMin = 0;
        private double _xMax = 100;
        private double _yMin = 0;
        private double _yMax = 100;
        private string? _noDataMessage;

        public ObservableCollection<GraphSeries> Series { get; } = new ObservableCollection<GraphSeries>();

        public string XAxisLabel { get => _xAxisLabel; set { _xAxisLabel = value ?? ""; OnPropertyChanged(); } }
        public string YAxisLabel { get => _yAxisLabel; set { _yAxisLabel = value ?? ""; OnPropertyChanged(); } }
        public double XMin { get => _xMin; set { _xMin = value; OnPropertyChanged(); } }
        public double XMax { get => _xMax; set { _xMax = value; OnPropertyChanged(); } }
        public double YMin { get => _yMin; set { _yMin = value; OnPropertyChanged(); } }
        public double YMax { get => _yMax; set { _yMax = value; OnPropertyChanged(); } }
        public string? NoDataMessage { get => _noDataMessage; set { _noDataMessage = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public void UpdateBoundsFromSeries()
        {
            double xMin = double.MaxValue, xMax = double.MinValue, yMin = double.MaxValue, yMax = double.MinValue;
            bool any = false;
            foreach (var s in Series)
            {
                foreach (var p in s.Points)
                {
                    any = true;
                    if (p.X < xMin) xMin = p.X;
                    if (p.X > xMax) xMax = p.X;
                    if (p.Y < yMin) yMin = p.Y;
                    if (p.Y > yMax) yMax = p.Y;
                }
            }
            if (any && xMax > xMin && yMax > yMin)
            {
                var padX = (xMax - xMin) * 0.05; if (padX == 0) padX = 1;
                var padY = (yMax - yMin) * 0.05; if (padY == 0) padY = 1;
                XMin = xMin - padX;
                XMax = xMax + padX;
                YMin = yMin - padY;
                YMax = yMax + padY;
            }
        }

        public void SetSeries(IEnumerable<GraphSeries>? series)
        {
            Series.Clear();
            if (series != null)
                foreach (var s in series)
                    Series.Add(s);
            UpdateBoundsFromSeries();
            OnPropertyChanged(nameof(Series));
        }
    }
}
