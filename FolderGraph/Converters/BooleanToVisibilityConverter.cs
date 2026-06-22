using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FolderGraph.Converters
{
    /// <summary>
    /// bool → Visibility 변환기.
    /// true = Visible, false = Collapsed. (WPF 기본 제공판과 동일 동작이나
    /// 명시적으로 두어 의존성을 분명히 한다.)
    /// </summary>
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = value is bool && (bool)value;
            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility && (Visibility)value == Visibility.Visible;
        }
    }
}
