using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Ul8ziz.FittingApp.App.Models;
using Ul8ziz.FittingApp.App.ViewModels;

namespace Ul8ziz.FittingApp.App.Views.Controls
{
    /// <summary>Lightweight WPF-native plot: axes, grid, multiple series as Polylines.</summary>
    public partial class PlotControl : UserControl
    {
        private const double MarginLeft = 36;
        private const double MarginRight = 12;
        private const double MarginTop = 12;
        private const double MarginBottom = 28;

        public PlotControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            SizeChanged += OnSizeChanged;
            DataContextChanged += (_, _) => SubscribeAndRedraw();
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => SubscribeAndRedraw();

        private void SubscribeAndRedraw()
        {
            if (DataContext is PlotControlViewModel vm)
            {
                vm.PropertyChanged -= Vm_PropertyChanged;
                vm.PropertyChanged += Vm_PropertyChanged;
                Redraw();
            }
        }

        private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(PlotControlViewModel.Series))
                Redraw();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

        public void Redraw()
        {
            if (DataContext is not PlotControlViewModel vm) return;

            PlotCanvas.Children.Clear();
            var series = vm.Series?.ToList() ?? new List<GraphSeries>();
            bool hasData = series.Count > 0 && series.Any(s => s.Points != null && s.Points.Count > 0);

            if (!hasData)
            {
                NoDataText.Visibility = Visibility.Visible;
                NoDataText.Text = vm.NoDataMessage ?? "No data";
                return;
            }
            NoDataText.Visibility = Visibility.Collapsed;

            double hostW = ChartHost.ActualWidth;
            double hostH = ChartHost.ActualHeight;
            if (hostW <= 0 || hostH <= 0) return;
            double plotWidth = Math.Max(1, hostW - MarginLeft - MarginRight);
            double plotHeight = Math.Max(1, hostH - MarginTop - MarginBottom);
            double xMin = vm.XMin, xMax = vm.XMax, yMin = vm.YMin, yMax = vm.YMax;
            if (xMax <= xMin) xMax = xMin + 1;
            if (yMax <= yMin) yMax = yMin + 1;

            // Grid lines
            foreach (var line in BuildGridLines(plotWidth, plotHeight, xMin, xMax, yMin, yMax))
                PlotCanvas.Children.Add(line);

            // Axis lines
            var axisBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
            PlotCanvas.Children.Add(new Line
            {
                X1 = MarginLeft,
                Y1 = MarginTop,
                X2 = MarginLeft,
                Y2 = MarginTop + plotHeight,
                Stroke = axisBrush,
                StrokeThickness = 1
            });
            PlotCanvas.Children.Add(new Line
            {
                X1 = MarginLeft,
                Y1 = MarginTop + plotHeight,
                X2 = MarginLeft + plotWidth,
                Y2 = MarginTop + plotHeight,
                Stroke = axisBrush,
                StrokeThickness = 1
            });

            // Tick labels (simplified: 5 X ticks, 5 Y ticks)
            AddTickLabels(plotWidth, plotHeight, xMin, xMax, yMin, yMax, axisBrush);

            // Series as Polylines
            foreach (var s in series)
            {
                if (s.Points == null || s.Points.Count == 0) continue;
                var pts = new PointCollection();
                foreach (var p in s.Points)
                {
                    double x = MarginLeft + (p.X - xMin) / (xMax - xMin) * plotWidth;
                    double y = MarginTop + plotHeight - (p.Y - yMin) / (yMax - yMin) * plotHeight;
                    pts.Add(new System.Windows.Point(x, y));
                }
                var poly = new Polyline
                {
                    Points = pts,
                    Stroke = new SolidColorBrush(s.Color),
                    StrokeThickness = 2,
                    StrokeLineJoin = PenLineJoin.Round
                };
                PlotCanvas.Children.Add(poly);
            }
        }

        private static List<Line> BuildGridLines(double w, double h, double xMin, double xMax, double yMin, double yMax)
        {
            var list = new List<Line>();
            var gridBrush = new SolidColorBrush(Color.FromArgb(0x20, 0x6B, 0x72, 0x80));
            int nx = 5, ny = 5;
            for (int i = 1; i < nx; i++)
            {
                double x = MarginLeft + (double)i / nx * w;
                list.Add(new Line { X1 = x, Y1 = MarginTop, X2 = x, Y2 = MarginTop + h, Stroke = gridBrush, StrokeThickness = 1 });
            }
            for (int i = 1; i < ny; i++)
            {
                double y = MarginTop + (double)i / ny * h;
                list.Add(new Line { X1 = MarginLeft, Y1 = y, X2 = MarginLeft + w, Y2 = y, Stroke = gridBrush, StrokeThickness = 1 });
            }
            return list;
        }

        private void AddTickLabels(double plotWidth, double plotHeight, double xMin, double xMax, double yMin, double yMax, SolidColorBrush brush)
        {
            // Optional: add TextBlocks for axis ticks. Skip to keep control simple; axes are labeled at bottom/left.
        }
    }
}
