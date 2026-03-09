using Ul8ziz.FittingApp.App.Models.Audiogram;

namespace Ul8ziz.FittingApp.App.ViewModels
{
    /// <summary>A single plotted point on the audiogram chart (display model).</summary>
    public sealed class AudiogramChartPoint
    {
        public double FrequencyHz { get; set; }
        public int DbHL { get; set; }
        public AudiogramPointType PointType { get; set; }
        public bool IsNoResponse { get; set; }
        public bool IsMasked { get; set; }
    }
}
