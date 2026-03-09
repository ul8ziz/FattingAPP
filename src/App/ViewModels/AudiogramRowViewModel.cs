using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Ul8ziz.FittingApp.App.ViewModels
{
    /// <summary>One row in the audiogram threshold table: frequency and L/R thresholds.</summary>
    public class AudiogramRowViewModel : INotifyPropertyChanged
    {
        private string? _leftThreshold;
        private string? _rightThreshold;

        public int FrequencyHz { get; set; }
        public string FrequencyLabel => FrequencyHz + " Hz";

        /// <summary>Left ear threshold (dB HL) as editable string. Empty or null = no value.</summary>
        public string? LeftThreshold
        {
            get => _leftThreshold;
            set { _leftThreshold = value; OnPropertyChanged(); }
        }

        /// <summary>Right ear threshold (dB HL) as editable string.</summary>
        public string? RightThreshold
        {
            get => _rightThreshold;
            set { _rightThreshold = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
