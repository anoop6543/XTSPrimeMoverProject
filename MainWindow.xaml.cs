using System;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using XTSPrimeMoverProject.ViewModels;

namespace XTSPrimeMoverProject
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }
    }

    public class MoverPositionConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values[0] is double position)
            {
                const double left = 130;
                const double top = 140;
                const double width = 740;
                const double height = 300;

                double r = height / 2.0;
                double straight = width - 2.0 * r;
                double arc = Math.PI * r;
                double total = (2.0 * straight) + (2.0 * arc);

                double s = (position / 360.0) * total;

                double x;
                double y;

                if (s < straight)
                {
                    x = left + r + s;
                    y = top;
                }
                else if (s < straight + arc)
                {
                    double t = (s - straight) / r;
                    x = left + width - r + r * Math.Sin(t);
                    y = top + r - r * Math.Cos(t);
                }
                else if (s < (2.0 * straight) + arc)
                {
                    double back = s - (straight + arc);
                    x = left + width - r - back;
                    y = top + height;
                }
                else
                {
                    double t = (s - ((2.0 * straight) + arc)) / r;
                    x = left + r - r * Math.Sin(t);
                    y = top + r + r * Math.Cos(t);
                }

                if (parameter?.ToString() == "X")
                {
                    return x - 10;
                }

                return y - 10;
            }

            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class MoverColorConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2)
            {
                bool hasPart = values[0] is bool hp && hp;
                string partStatus = values[1]?.ToString() ?? "Empty";
                
                if (!hasPart)
                {
                    return Colors.Gray;
                }
                
                return partStatus switch
                {
                    "BaseLayer" => Colors.Yellow,
                    "InProcess" => Colors.Orange,
                    "Assembled" => Colors.Cyan,
                    "Good" => Colors.LimeGreen,
                    "Bad" => Colors.Red,
                    _ => Colors.White
                };
            }
            
            return Colors.Gray;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StationRotaryPositionConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] is not int stationIndex || values[1] is not int stationCount || stationCount <= 0)
            {
                return 0.0;
            }

            const double center = 60;
            const double radius = 42;
            double angleRadians = ((360.0 * stationIndex / stationCount) - 90.0) * Math.PI / 180.0;

            if (string.Equals(parameter?.ToString(), "X", StringComparison.OrdinalIgnoreCase))
            {
                return center + radius * Math.Cos(angleRadians) - 7;
            }

            return center + radius * Math.Sin(angleRadians) - 7;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StationStateBrushConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string status = values.Length > 0 ? values[0]?.ToString() ?? "Idle" : "Idle";
            bool isCurrent = values.Length > 1 && values[1] is bool current && current;
            bool hasPart = values.Length > 2 && values[2] is bool part && part;

            if (isCurrent && hasPart)
            {
                return Colors.DeepSkyBlue;
            }

            return status switch
            {
                "Processing" => Colors.Orange,
                "Complete" => Colors.LimeGreen,
                "Error" => Colors.Red,
                _ => Colors.DimGray
            };
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>Converts bool to Visibility (true=Visible, false=Collapsed).</summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Converts a hex color string such as "#FF6B35" to a SolidColorBrush.</summary>
    public class StringToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string hex)
            {
                try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
                catch { }
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}