using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Autopraisal
{
    /// <summary>
    /// Settings.xaml etkileşim mantığı
    /// </summary>
    public partial class Settings : Window
    {
        public bool IsAuto
        {
            get => Properties.Settings.Default.MonitoringEnabled;
            set => Properties.Settings.Default.MonitoringEnabled = value;
        }
        public bool IsManual
        {
            get => !Properties.Settings.Default.MonitoringEnabled;
            set => Properties.Settings.Default.MonitoringEnabled = !value;
        }
        public List<string> markets { get; set; } = new List<string> { "Jita", "Perimeter", "Universe", "Amarr", "Dodixie", "Hek", "Rens" };
        public Settings()
        {
            InitializeComponent();
            Rect desktopWorkingArea = SystemParameters.WorkArea;
            Left = desktopWorkingArea.Right - Width;
            Top = desktopWorkingArea.Bottom - Height;
            DataContext = this;
            Title = "Settings - Autopraisal " + Properties.Settings.Default.Version;
            tbVersion.Text = "Autopraisal " + Properties.Settings.Default.Version;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.Save();
            Close();
        }
    }
}
