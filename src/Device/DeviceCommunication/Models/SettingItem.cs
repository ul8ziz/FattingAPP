using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication.Models
{
    /// <summary>Single device parameter: name, value, unit, constraints, datatype, read-only. Populated from SDK Parameter objects.</summary>
    public class SettingItem : INotifyPropertyChanged
    {
        public enum DataType
        {
            Unknown,
            Bool,
            Int,
            Double,
            String,
            Enum
        }

        private string _id = string.Empty;
        private string _name = string.Empty;
        private string _displayName = string.Empty;
        private string _parameterId = string.Empty;
        private string _description = string.Empty;
        private string _moduleName = string.Empty;
        private string _memoryName = string.Empty;
        private object? _value;
        private string _unit = string.Empty;
        private double _min = double.NaN;
        private double _max = double.NaN;
        private double _step = double.NaN;
        private DataType _dataType = DataType.Unknown;
        private bool _readOnly;
        private string[] _enumValues = Array.Empty<string>();
        /// <summary>Reference to the SDK Parameter object for direct read/write.</summary>
        private object? _sdkParameterRef;

        public string Id { get => _id; set { _id = value ?? string.Empty; OnPropertyChanged(); } }
        public string Name { get => _name; set { _name = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); } }
        /// <summary>Display label; defaults to Name if not set.</summary>
        public string DisplayName { get => string.IsNullOrEmpty(_displayName) ? _name : _displayName; set { _displayName = value ?? string.Empty; OnPropertyChanged(); } }
        /// <summary>SDK identifier for read/write (parameterId or path).</summary>
        public string ParameterId { get => _parameterId; set { _parameterId = value ?? string.Empty; OnPropertyChanged(); } }
        /// <summary>Parameter description from SDK (Parameter.Description).</summary>
        public string Description { get => _description; set { _description = value ?? string.Empty; OnPropertyChanged(); } }
        /// <summary>Module name from SDK (Parameter.LongModuleName). Used for tab grouping.</summary>
        public string ModuleName { get => _moduleName; set { _moduleName = value ?? string.Empty; OnPropertyChanged(); } }
        /// <summary>Which memory this parameter came from (System, Active, etc.).</summary>
        public string MemoryName { get => _memoryName; set { _memoryName = value ?? string.Empty; OnPropertyChanged(); } }
        public object? Value { get => _value; set { _value = value; OnPropertyChanged(); OnPropertyChanged(nameof(ValueString)); } }
        public string Unit { get => _unit; set { _unit = value ?? string.Empty; OnPropertyChanged(); } }
        public double Min { get => _min; set { _min = value; OnPropertyChanged(); } }
        public double Max { get => _max; set { _max = value; OnPropertyChanged(); } }
        public double Step { get => _step; set { _step = value; OnPropertyChanged(); } }
        public DataType SettingDataType { get => _dataType; set { _dataType = value; OnPropertyChanged(); } }
        public bool ReadOnly { get => _readOnly; set { _readOnly = value; OnPropertyChanged(); } }
        public string[] EnumValues { get => _enumValues; set { _enumValues = value ?? Array.Empty<string>(); OnPropertyChanged(); } }
        /// <summary>Reference to the underlying SDK Parameter object (for live write-back).</summary>
        public object? SdkParameterRef { get => _sdkParameterRef; set => _sdkParameterRef = value; }

        /// <summary>For binding and search; string representation of current value.</summary>
        public string ValueString => Value?.ToString() ?? string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
