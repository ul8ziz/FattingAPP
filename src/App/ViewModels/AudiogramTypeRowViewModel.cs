using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ul8ziz.FittingApp.App.Models.Audiogram;

namespace Ul8ziz.FittingApp.App.ViewModels
{
    /// <summary>
    /// One row in the per-ear audiogram numeric table (AC, BC, or UCL).
    /// Each of the 8 standard frequency columns is a separate editable string property.
    /// Standard frequencies: 250, 500, 1000, 2000, 3000, 4000, 6000, 8000 Hz.
    /// </summary>
    public sealed class AudiogramTypeRowViewModel : INotifyPropertyChanged
    {
        private string? _val250;
        private string? _val500;
        private string? _val1k;
        private string? _val2k;
        private string? _val3k;
        private string? _val4k;
        private string? _val6k;
        private string? _val8k;

        public AudiogramTypeRowViewModel(AudiogramPointType pointType)
        {
            PointType = pointType;
            RowLabel = pointType switch
            {
                AudiogramPointType.AC => "AC",
                AudiogramPointType.BC => "BC",
                AudiogramPointType.UCL => "UCL",
                _ => "AC"
            };
        }

        public string RowLabel { get; }
        public AudiogramPointType PointType { get; }

        public string? Val250
        {
            get => _val250;
            set { _val250 = value; OnPropertyChanged(); }
        }

        public string? Val500
        {
            get => _val500;
            set { _val500 = value; OnPropertyChanged(); }
        }

        public string? Val1k
        {
            get => _val1k;
            set { _val1k = value; OnPropertyChanged(); }
        }

        public string? Val2k
        {
            get => _val2k;
            set { _val2k = value; OnPropertyChanged(); }
        }

        public string? Val3k
        {
            get => _val3k;
            set { _val3k = value; OnPropertyChanged(); }
        }

        public string? Val4k
        {
            get => _val4k;
            set { _val4k = value; OnPropertyChanged(); }
        }

        public string? Val6k
        {
            get => _val6k;
            set { _val6k = value; OnPropertyChanged(); }
        }

        public string? Val8k
        {
            get => _val8k;
            set { _val8k = value; OnPropertyChanged(); }
        }

        /// <summary>Gets or sets the value for a given frequency index (0=250Hz … 7=8000Hz).</summary>
        public string? GetValueForIndex(int index) => index switch
        {
            0 => Val250,
            1 => Val500,
            2 => Val1k,
            3 => Val2k,
            4 => Val3k,
            5 => Val4k,
            6 => Val6k,
            7 => Val8k,
            _ => null
        };

        public void SetValueForIndex(int index, string? value)
        {
            switch (index)
            {
                case 0: Val250 = value; break;
                case 1: Val500 = value; break;
                case 2: Val1k  = value; break;
                case 3: Val2k  = value; break;
                case 4: Val3k  = value; break;
                case 5: Val4k  = value; break;
                case 6: Val6k  = value; break;
                case 7: Val8k  = value; break;
            }
        }

        /// <summary>Clears all frequency values.</summary>
        public void Clear()
        {
            Val250 = Val500 = Val1k = Val2k = Val3k = Val4k = Val6k = Val8k = null;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
