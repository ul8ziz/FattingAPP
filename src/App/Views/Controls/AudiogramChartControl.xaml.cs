using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Ul8ziz.FittingApp.App.Models.Audiogram;
using Ul8ziz.FittingApp.App.ViewModels;

namespace Ul8ziz.FittingApp.App.Views.Controls
{
    /// <summary>
    /// Clinical-style audiogram chart drawn on a WPF Canvas.
    /// Frequency (Hz) on X-axis (log scale, 250–8000 Hz).
    /// Hearing level (dB HL) on Y-axis (linear, −10 to 120 dB, inverted — 0 at top).
    /// Click interaction converts pixel coordinates to the nearest standard frequency and snaps dB to 5 dB steps.
    /// </summary>
    public partial class AudiogramChartControl : UserControl
    {
        // ── Chart layout constants ──────────────────────────────────────────────
        private const double LeftMargin   = 44;
        private const double RightMargin  = 36;  // room for severity bar + padding
        private const double TopMargin    = 28;
        private const double BottomMargin = 32;

        private const double DbMin = -10;
        private const double DbMax = 120;

        private static readonly int[] StandardFrequencies = { 250, 500, 1000, 2000, 3000, 4000, 6000, 8000 };
        private static readonly int[] DbGridLines         = { 0, 20, 40, 60, 80, 100, 120 };
        private static readonly int[] DbFineGridLines     = { -10, 10, 30, 50, 70, 90, 110 };

        // ── Brushes ──────────────────────────────────────────────────────────────
        private static readonly Pen GridPen           = CreatePen(Color.FromRgb(210, 215, 220), 0.5);
        private static readonly Pen FineGridPen       = CreatePen(Color.FromRgb(230, 233, 236), 0.5);
        private static readonly Pen AxisPen           = CreatePen(Color.FromRgb(100, 110, 120), 1.0);
        private static readonly Pen RightEarPointPen  = CreatePen(Color.FromRgb(190, 30, 30), 1.8);
        private static readonly Pen LeftEarPointPen   = CreatePen(Color.FromRgb(20, 60, 170), 1.8);
        private static readonly Pen ConnectPen        = CreatePen(Color.FromArgb(160, 190, 30, 30), 1.2);
        private static readonly Pen ConnectPenLeft    = CreatePen(Color.FromArgb(160, 20, 60, 170), 1.2);

        private static Pen CreatePen(Color c, double thickness)
        {
            var p = new Pen(new SolidColorBrush(c), thickness);
            p.Freeze();
            return p;
        }

        // ── Dependency Properties ──────────────────────────────────────────────
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(AudiogramChartControl),
                new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

        public static readonly DependencyProperty EarSideProperty =
            DependencyProperty.Register(nameof(EarSide), typeof(string), typeof(AudiogramChartControl),
                new PropertyMetadata("Right", OnVisualPropertyChanged));

        public static readonly DependencyProperty PointsProperty =
            DependencyProperty.Register(nameof(Points), typeof(ObservableCollection<AudiogramChartPoint>),
                typeof(AudiogramChartControl),
                new PropertyMetadata(null, OnPointsPropertyChanged));

        public static readonly DependencyProperty ChartClickCommandProperty =
            DependencyProperty.Register(nameof(ChartClickCommand), typeof(ICommand),
                typeof(AudiogramChartControl), new PropertyMetadata(null));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        /// <summary>"Right" or "Left" — controls symbol color and style.</summary>
        public string EarSide
        {
            get => (string)GetValue(EarSideProperty);
            set => SetValue(EarSideProperty, value);
        }

        public ObservableCollection<AudiogramChartPoint>? Points
        {
            get => (ObservableCollection<AudiogramChartPoint>?)GetValue(PointsProperty);
            set => SetValue(PointsProperty, value);
        }

        /// <summary>Command invoked when user clicks the chart. Parameter is (double freqHz, int dbHL).</summary>
        public ICommand? ChartClickCommand
        {
            get => (ICommand?)GetValue(ChartClickCommandProperty);
            set => SetValue(ChartClickCommandProperty, value);
        }

        // ── Constructor ────────────────────────────────────────────────────────
        public AudiogramChartControl()
        {
            InitializeComponent();
        }

        // ── Property change callbacks ──────────────────────────────────────────
        private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((AudiogramChartControl)d).Redraw();

        private static void OnPointsPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = (AudiogramChartControl)d;
            if (e.OldValue is ObservableCollection<AudiogramChartPoint> oldCol)
                oldCol.CollectionChanged -= ctrl.OnPointsCollectionChanged;
            if (e.NewValue is ObservableCollection<AudiogramChartPoint> newCol)
                newCol.CollectionChanged += ctrl.OnPointsCollectionChanged;
            ctrl.Redraw();
        }

        private void OnPointsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => Redraw();

        // ── Layout events ──────────────────────────────────────────────────────
        private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
            => Redraw();

        // ── Coordinate helpers ─────────────────────────────────────────────────
        private double ChartWidth  => Math.Max(1, ChartCanvas.ActualWidth  - LeftMargin - RightMargin);
        private double ChartHeight => Math.Max(1, ChartCanvas.ActualHeight - TopMargin  - BottomMargin);

        private double FreqToX(double hz)
        {
            double t = (Math.Log10(hz) - Math.Log10(250)) /
                       (Math.Log10(8000) - Math.Log10(250));
            return LeftMargin + t * ChartWidth;
        }

        private double DbToY(double db)
        {
            double t = (db - DbMin) / (DbMax - DbMin);
            return TopMargin + t * ChartHeight;
        }

        private double XToFreq(double x)
        {
            double t = (x - LeftMargin) / ChartWidth;
            return Math.Pow(10, t * (Math.Log10(8000) - Math.Log10(250)) + Math.Log10(250));
        }

        private double YToDb(double y)
        {
            double t = (y - TopMargin) / ChartHeight;
            return DbMin + t * (DbMax - DbMin);
        }

        private static int SnapToStandardFreq(double hz)
        {
            return StandardFrequencies.OrderBy(f => Math.Abs(f - hz)).First();
        }

        private static int SnapDb(double db)
        {
            int clamped = (int)Math.Round(Math.Max(DbMin, Math.Min(DbMax, db)));
            return (int)(Math.Round(clamped / 5.0) * 5);
        }

        // ── Redraw ─────────────────────────────────────────────────────────────
        private void Redraw()
        {
            if (ChartCanvas == null || ChartCanvas.ActualWidth < 1 || ChartCanvas.ActualHeight < 1)
                return;

            ChartCanvas.Children.Clear();

            DrawSeverityBar();
            DrawFineGridLines();
            DrawGridLines();
            DrawAxes();
            DrawFrequencyLabels();
            DrawDbLabels();
            DrawTitle();
            DrawPoints();
        }

        private void DrawSeverityBar()
        {
            // Vertical severity color bar on the right edge (matches clinical audiogram convention)
            double barX     = ChartCanvas.ActualWidth - RightMargin + 6;
            double barWidth = 14;

            var stops = new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(85, 180, 90),   0.0),   // normal
                new GradientStop(Color.FromRgb(180, 210, 80),  0.22),  // mild
                new GradientStop(Color.FromRgb(255, 210, 50),  0.44),  // moderate
                new GradientStop(Color.FromRgb(240, 130, 40),  0.66),  // severe
                new GradientStop(Color.FromRgb(210, 50,  50),  1.0),   // profound
            };
            var brush = new LinearGradientBrush(stops, new Point(0, 0), new Point(0, 1));

            double barTop    = DbToY(0);
            double barBottom = DbToY(100);
            if (barBottom <= barTop) return;

            var rect = new Rectangle
            {
                Width  = barWidth,
                Height = barBottom - barTop,
                Fill   = brush,
                RadiusX = 3,
                RadiusY = 3,
                Opacity = 0.85
            };
            Canvas.SetLeft(rect, barX);
            Canvas.SetTop(rect, barTop);
            ChartCanvas.Children.Add(rect);
        }

        private void DrawFineGridLines()
        {
            // Light horizontal grid lines at odd dB multiples
            foreach (int db in DbFineGridLines)
            {
                if (db < DbMin || db > DbMax) continue;
                double y = DbToY(db);
                var line = new Line
                {
                    X1 = LeftMargin, Y1 = y,
                    X2 = LeftMargin + ChartWidth, Y2 = y,
                    StrokeThickness = FineGridPen.Thickness,
                    Stroke = FineGridPen.Brush
                };
                ChartCanvas.Children.Add(line);
            }
        }

        private void DrawGridLines()
        {
            // Vertical lines at each standard frequency
            foreach (int hz in StandardFrequencies)
            {
                double x = FreqToX(hz);
                var vLine = new Line
                {
                    X1 = x, Y1 = TopMargin,
                    X2 = x, Y2 = TopMargin + ChartHeight,
                    StrokeThickness = GridPen.Thickness,
                    Stroke = GridPen.Brush
                };
                ChartCanvas.Children.Add(vLine);
            }

            // Horizontal lines at major dB grid lines
            foreach (int db in DbGridLines)
            {
                double y = DbToY(db);
                var hLine = new Line
                {
                    X1 = LeftMargin, Y1 = y,
                    X2 = LeftMargin + ChartWidth, Y2 = y,
                    StrokeThickness = GridPen.Thickness,
                    Stroke = GridPen.Brush
                };
                ChartCanvas.Children.Add(hLine);

                // Bold 0 dB HL line
                if (db == 0)
                    hLine.StrokeThickness = 1.2;
            }
        }

        private void DrawAxes()
        {
            // Left vertical axis
            var leftAxis = new Line
            {
                X1 = LeftMargin, Y1 = TopMargin,
                X2 = LeftMargin, Y2 = TopMargin + ChartHeight,
                StrokeThickness = AxisPen.Thickness,
                Stroke = AxisPen.Brush
            };
            ChartCanvas.Children.Add(leftAxis);

            // Bottom horizontal axis
            var bottomAxis = new Line
            {
                X1 = LeftMargin, Y1 = TopMargin + ChartHeight,
                X2 = LeftMargin + ChartWidth, Y2 = TopMargin + ChartHeight,
                StrokeThickness = AxisPen.Thickness,
                Stroke = AxisPen.Brush
            };
            ChartCanvas.Children.Add(bottomAxis);
        }

        private void DrawFrequencyLabels()
        {
            foreach (int hz in StandardFrequencies)
            {
                string label = hz >= 1000 ? $"{hz / 1000}k" : hz.ToString();
                double x = FreqToX(hz);

                var tb = new TextBlock
                {
                    Text       = label,
                    FontSize   = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(80, 90, 100)),
                    FontFamily = new FontFamily("Segoe UI")
                };
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(tb, x - tb.DesiredSize.Width / 2);
                Canvas.SetTop(tb, TopMargin + ChartHeight + 5);
                ChartCanvas.Children.Add(tb);
            }

            // X-axis label
            var xLabel = new TextBlock
            {
                Text       = "Frequency (Hz)",
                FontSize   = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 110, 120)),
                FontFamily = new FontFamily("Segoe UI")
            };
            xLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(xLabel, LeftMargin + ChartWidth / 2 - xLabel.DesiredSize.Width / 2);
            Canvas.SetTop(xLabel, TopMargin + ChartHeight + 18);
            ChartCanvas.Children.Add(xLabel);
        }

        private void DrawDbLabels()
        {
            foreach (int db in DbGridLines)
            {
                double y = DbToY(db);
                var tb = new TextBlock
                {
                    Text       = db.ToString(),
                    FontSize   = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(80, 90, 100)),
                    FontFamily = new FontFamily("Segoe UI")
                };
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(tb, LeftMargin - tb.DesiredSize.Width - 5);
                Canvas.SetTop(tb, y - tb.DesiredSize.Height / 2);
                ChartCanvas.Children.Add(tb);
            }
        }

        private void DrawTitle()
        {
            if (string.IsNullOrEmpty(Title)) return;

            var isLeft = EarSide?.Equals("Left", StringComparison.OrdinalIgnoreCase) == true;
            var dot = new Ellipse
            {
                Width = 10, Height = 10,
                Fill = isLeft
                    ? new SolidColorBrush(Color.FromRgb(20, 60, 170))
                    : new SolidColorBrush(Color.FromRgb(190, 30, 30))
            };
            Canvas.SetLeft(dot, LeftMargin);
            Canvas.SetTop(dot, 6);
            ChartCanvas.Children.Add(dot);

            var tb = new TextBlock
            {
                Text       = " " + Title,
                FontSize   = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 40, 55)),
                FontFamily = new FontFamily("Segoe UI")
            };
            Canvas.SetLeft(tb, LeftMargin + 12);
            Canvas.SetTop(tb, 3);
            ChartCanvas.Children.Add(tb);
        }

        // ── Point rendering ────────────────────────────────────────────────────
        private void DrawPoints()
        {
            var pts = Points;
            if (pts == null || pts.Count == 0) return;

            bool isLeft = EarSide?.Equals("Left", StringComparison.OrdinalIgnoreCase) == true;
            var pen = isLeft ? LeftEarPointPen : RightEarPointPen;

            // Draw connection lines first (behind symbols)
            var acPts = pts.Where(p => p.PointType == AudiogramPointType.AC && !p.IsNoResponse)
                          .OrderBy(p => p.FrequencyHz).ToList();
            DrawConnectLine(acPts, isLeft ? ConnectPenLeft : ConnectPen);

            // Draw each symbol on top
            foreach (var pt in pts)
            {
                double cx = FreqToX(pt.FrequencyHz);
                double cy = DbToY(pt.DbHL);

                // Skip if outside visible area
                if (cy < TopMargin - 10 || cy > TopMargin + ChartHeight + 10) continue;

                switch (pt.PointType)
                {
                    case AudiogramPointType.AC:
                        DrawAcSymbol(cx, cy, pen, pt.IsMasked, isLeft, pt.IsNoResponse);
                        break;
                    case AudiogramPointType.BC:
                        DrawBcSymbol(cx, cy, pen, pt.IsMasked, isLeft, pt.IsNoResponse);
                        break;
                    case AudiogramPointType.UCL:
                        DrawUclSymbol(cx, cy, pen);
                        break;
                }
            }
        }

        private void DrawConnectLine(List<AudiogramChartPoint> pts, Pen pen)
        {
            for (int i = 0; i < pts.Count - 1; i++)
            {
                double x1 = FreqToX(pts[i].FrequencyHz);
                double y1 = DbToY(pts[i].DbHL);
                double x2 = FreqToX(pts[i + 1].FrequencyHz);
                double y2 = DbToY(pts[i + 1].DbHL);

                var line = new Line
                {
                    X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                    StrokeThickness = pen.Thickness,
                    Stroke = pen.Brush,
                    StrokeDashArray = new DoubleCollection { 4, 2 }
                };
                ChartCanvas.Children.Add(line);
            }
        }

        private void DrawAcSymbol(double cx, double cy, Pen pen, bool masked, bool isLeft, bool noResponse)
        {
            const double r = 6;
            if (noResponse)
            {
                // Open circle + downward arrow
                var circle = new Ellipse
                {
                    Width = r * 2, Height = r * 2,
                    Stroke = pen.Brush, StrokeThickness = pen.Thickness,
                    Fill = Brushes.Transparent
                };
                Canvas.SetLeft(circle, cx - r);
                Canvas.SetTop(circle, cy - r);
                ChartCanvas.Children.Add(circle);
                DrawArrowDown(cx, cy + r, pen);
            }
            else if (masked)
            {
                // Filled circle for masked
                var circle = new Ellipse
                {
                    Width = r * 2, Height = r * 2,
                    Stroke = pen.Brush, StrokeThickness = pen.Thickness,
                    Fill = pen.Brush
                };
                Canvas.SetLeft(circle, cx - r);
                Canvas.SetTop(circle, cy - r);
                ChartCanvas.Children.Add(circle);
            }
            else
            {
                // Standard open circle (AC unmasked)
                var circle = new Ellipse
                {
                    Width = r * 2, Height = r * 2,
                    Stroke = pen.Brush, StrokeThickness = pen.Thickness,
                    Fill = Brushes.White
                };
                Canvas.SetLeft(circle, cx - r);
                Canvas.SetTop(circle, cy - r);
                ChartCanvas.Children.Add(circle);
            }
        }

        private void DrawBcSymbol(double cx, double cy, Pen pen, bool masked, bool isLeft, bool noResponse)
        {
            const double s = 7;
            // BC: "[" for left ear, "]" for right ear
            if (isLeft)
            {
                // "[" shape
                var top    = MakeLine(cx,     cy - s, cx + s, cy - s, pen);
                var left   = MakeLine(cx,     cy - s, cx,     cy + s, pen);
                var bottom = MakeLine(cx,     cy + s, cx + s, cy + s, pen);
                ChartCanvas.Children.Add(top);
                ChartCanvas.Children.Add(left);
                ChartCanvas.Children.Add(bottom);
            }
            else
            {
                // "]" shape
                var top    = MakeLine(cx - s, cy - s, cx, cy - s, pen);
                var right  = MakeLine(cx,     cy - s, cx, cy + s, pen);
                var bottom = MakeLine(cx - s, cy + s, cx, cy + s, pen);
                ChartCanvas.Children.Add(top);
                ChartCanvas.Children.Add(right);
                ChartCanvas.Children.Add(bottom);
            }

            if (noResponse) DrawArrowDown(cx, cy + s, pen);
        }

        private void DrawUclSymbol(double cx, double cy, Pen pen)
        {
            // UCL: downward-pointing triangle / chevron
            const double s = 6;
            var pg = new PathGeometry();
            var pf = new PathFigure { StartPoint = new Point(cx, cy + s) };
            pf.Segments.Add(new LineSegment(new Point(cx - s, cy - s), true));
            pf.Segments.Add(new LineSegment(new Point(cx + s, cy - s), true));
            pf.IsClosed = true;
            pg.Figures.Add(pf);

            var path = new Path
            {
                Data = pg,
                Stroke = pen.Brush, StrokeThickness = pen.Thickness,
                Fill = Brushes.Transparent
            };
            ChartCanvas.Children.Add(path);
        }

        private void DrawArrowDown(double cx, double startY, Pen pen)
        {
            const double arrowLen = 8;
            const double headSize = 4;

            var shaft = MakeLine(cx, startY, cx, startY + arrowLen, pen);
            ChartCanvas.Children.Add(shaft);

            var leftWing  = MakeLine(cx, startY + arrowLen, cx - headSize, startY + arrowLen - headSize, pen);
            var rightWing = MakeLine(cx, startY + arrowLen, cx + headSize, startY + arrowLen - headSize, pen);
            ChartCanvas.Children.Add(leftWing);
            ChartCanvas.Children.Add(rightWing);
        }

        private static Line MakeLine(double x1, double y1, double x2, double y2, Pen pen) => new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = pen.Brush,
            StrokeThickness = pen.Thickness
        };

        // ── Mouse interaction ──────────────────────────────────────────────────
        private void ClickOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ChartClickCommand == null) return;

            var pos = e.GetPosition(ChartCanvas);

            // Reject clicks outside the chart plot area
            if (pos.X < LeftMargin || pos.X > LeftMargin + ChartWidth ||
                pos.Y < TopMargin  || pos.Y > TopMargin  + ChartHeight)
                return;

            double rawFreq = XToFreq(pos.X);
            double rawDb   = YToDb(pos.Y);

            int snappedFreq = SnapToStandardFreq(rawFreq);
            int snappedDb   = SnapDb(rawDb);

            var parameter = (FrequencyHz: (double)snappedFreq, DbHL: snappedDb);
            if (ChartClickCommand.CanExecute(parameter))
                ChartClickCommand.Execute(parameter);
        }
    }
}
