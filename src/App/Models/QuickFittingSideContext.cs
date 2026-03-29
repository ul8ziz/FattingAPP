using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ul8ziz.FittingApp.App.ViewModels;

namespace Ul8ziz.FittingApp.App.Models
{
    /// <summary>
    /// Lightweight context for one side (Left or Right) of the Quick Fitting panel.
    /// Exposes the curated parameter VMs and the NR master depth slider value.
    /// </summary>
    public class QuickFittingSideContext : INotifyPropertyChanged
    {
        public SettingItemViewModel? FbcEnable { get; set; }
        public SettingItemViewModel? FbcGainMgmtEnable { get; set; }
        public SettingItemViewModel? FbcGainMgmtLimit { get; set; }
        public SettingItemViewModel? NrEnable { get; set; }
        public SettingItemViewModel? MmiEnable { get; set; }
        public SettingItemViewModel? PowerOnDelay { get; set; }

        /// <summary>NR master slider range (from first X_NR_MaxDepth sample in snapshot).</summary>
        public double NrSliderMin { get; set; }
        public double NrSliderMax { get; set; } = 15;
        public double NrSliderStep { get; set; } = 1;

        private double _nrMasterDepth;
        public double NrMasterDepth
        {
            get => _nrMasterDepth;
            set
            {
                if (System.Math.Abs(_nrMasterDepth - value) < 0.001) return;
                _nrMasterDepth = value;
                OnPropertyChanged();
                NrMasterDepthChanged?.Invoke(value);
            }
        }

        public delegate void NrMasterDepthChangedHandler(double newValue);
        public event NrMasterDepthChangedHandler? NrMasterDepthChanged;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
