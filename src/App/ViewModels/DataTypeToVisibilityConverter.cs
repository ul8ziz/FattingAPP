using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.ViewModels
{
    /// <summary>Converts SettingItem.DataType to Visibility when it matches ConverterParameter (Bool, Int, Double, Enum, String).</summary>
    public sealed class DataTypeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not SettingItem.DataType dt || parameter == null) return Visibility.Collapsed;
            var param = parameter.ToString()?.Trim() ?? "";
            var match = param.Equals("Bool", StringComparison.OrdinalIgnoreCase) && dt == SettingItem.DataType.Bool
                || param.Equals("Int", StringComparison.OrdinalIgnoreCase) && dt == SettingItem.DataType.Int
                || param.Equals("Double", StringComparison.OrdinalIgnoreCase) && dt == SettingItem.DataType.Double
                || param.Equals("Enum", StringComparison.OrdinalIgnoreCase) && dt == SettingItem.DataType.Enum
                || param.Equals("String", StringComparison.OrdinalIgnoreCase) && dt == SettingItem.DataType.String;
            return match ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public sealed class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return b ? Visibility.Collapsed : Visibility.Visible;
            return Visibility.Visible;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public sealed class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return true;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return true;
        }
    }

    /// <summary>MultiValue: (string tabId, string selectedTabId) -> True when equal (for tab selected style).</summary>
    public sealed class TabIdEqualsConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values?.Length >= 2 && values[0] is string id && values[1] is string sel)
                return string.Equals(id, sel, StringComparison.OrdinalIgnoreCase);
            return false;
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public sealed class ObjectToDoubleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return 0.0;
            if (value is double d) return d;
            if (value is int i) return (double)i;
            if (value is float f) return (double)f;
            return double.TryParse(value.ToString(), out var v) ? v : 0.0;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d) return d;
            return 0.0;
        }
    }

    public sealed class ObjectToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return b;
            if (value is bool?) return (bool?)value == true;
            return false;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is true;
        }
    }

    /// <summary>String null or empty -> Collapsed; otherwise Visible.</summary>
    public sealed class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    /// <summary>Null -> Collapsed; non-null -> Visible. Used e.g. for ResetCommand visibility.</summary>
    public sealed class ObjectNullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == null ? Visibility.Collapsed : Visibility.Visible;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    /// <summary>Converts double.NaN to a fallback (ConverterParameter: "0" or "100") so Slider Minimum/Maximum never receive NaN.</summary>
    public sealed class DoubleNaNToFallbackConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d && !double.IsNaN(d)) return d;
            if (parameter != null && double.TryParse(parameter.ToString(), out var fallback)) return fallback;
            return 0.0;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is double d ? d : 0.0;
        }
    }
}
