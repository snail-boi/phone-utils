using System;
using System.Globalization;
using System.Windows.Data;

namespace phone_utils
{
    public class SelectedItemDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;

            // If it has a Name property, use it
            var type = value.GetType();
            var nameProp = type.GetProperty("Name");
            if (nameProp != null)
            {
                var nameVal = nameProp.GetValue(value);
                if (nameVal != null) return nameVal.ToString();
            }

            // Fallback to ToString()
            return value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}