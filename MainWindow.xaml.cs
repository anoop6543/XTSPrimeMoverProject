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
                double centerX = 400;
                double centerY = 350;
                double radius = 250;
                
                double angleRadians = (position - 90) * Math.PI / 180.0;
                
                if (parameter?.ToString() == "X")
                {
                    return centerX + radius * Math.Cos(angleRadians) - 10;
                }
                else if (parameter?.ToString() == "Y")
                {
                    return centerY + radius * Math.Sin(angleRadians) - 10;
                }
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
}