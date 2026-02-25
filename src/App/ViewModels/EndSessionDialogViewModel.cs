using System;
using System.Windows.Input;
using Ul8ziz.FittingApp.App.Services;
using Ul8ziz.FittingApp.App.Views;

namespace Ul8ziz.FittingApp.App.ViewModels
{
    /// <summary>Result of End Session dialog. Cancel = stay on fitting; others = end session.</summary>
    public enum EndSessionDialogResult
    {
        Cancel,
        SaveAndEnd,
        EndWithoutSaving
    }

    /// <summary>ViewModel for End Session modal. No text input; three explicit actions only.</summary>
    public class EndSessionDialogViewModel
    {
        private readonly DeviceSessionService _session = DeviceSessionService.Instance;

        public EndSessionDialogViewModel()
        {
            SaveAndEndCommand = new RelayCommand(_ => CloseWithResult(EndSessionDialogResult.SaveAndEnd));
            EndWithoutSavingCommand = new RelayCommand(_ => CloseWithResult(EndSessionDialogResult.EndWithoutSaving));
            CancelCommand = new RelayCommand(_ => CloseWithResult(EndSessionDialogResult.Cancel));
        }

        public string Title => "End Session";
        public string Message => "What would you like to do with the current fitting settings?";

        /// <summary>When only one device is connected, show that saving applies only to connected device(s).</summary>
        public string ConnectedDeviceNotice
        {
            get
            {
                bool left = _session.LeftConnected;
                bool right = _session.RightConnected;
                if (left && right) return string.Empty;
                if (left) return "Saving will apply only to the connected device (Left).";
                if (right) return "Saving will apply only to the connected device (Right).";
                return string.Empty;
            }
        }

        public bool ShowConnectedDeviceNotice => _session.LeftConnected ^ _session.RightConnected;

        public ICommand SaveAndEndCommand { get; }
        public ICommand EndWithoutSavingCommand { get; }
        public ICommand CancelCommand { get; }

        /// <summary>Raised when user chooses an action. Parameter is the result.</summary>
        public event Action<EndSessionDialogResult>? CloseRequested;

        private void CloseWithResult(EndSessionDialogResult result)
        {
            CloseRequested?.Invoke(result);
        }
    }
}
