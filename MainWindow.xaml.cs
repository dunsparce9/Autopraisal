using Autopraisal.Models;
using OreCalc.Models;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;

namespace Autopraisal
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region clipboard monitoring
        private const int WM_CLIPBOARDUPDATE = 0x031D;

        private IntPtr windowHandle;

        public event EventHandler ClipboardUpdate;

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            windowHandle = new WindowInteropHelper(this).EnsureHandle();
            HwndSource.FromHwnd(windowHandle)?.AddHook(HwndHandler);
            if (Properties.Settings.Default.MonitoringEnabled) Start();
        }

        public static readonly DependencyProperty ClipboardUpdateCommandProperty =
            DependencyProperty.Register("ClipboardUpdateCommand", typeof(ICommand), typeof(MainWindow), new FrameworkPropertyMetadata(null));

        public ICommand ClipboardUpdateCommand
        {
            get { return (ICommand)GetValue(ClipboardUpdateCommandProperty); }
            set { SetValue(ClipboardUpdateCommandProperty, value); }
        }

        public void Start()
        {
            NativeMethods.AddClipboardFormatListener(windowHandle);
        }

        public void Stop()
        {
            NativeMethods.RemoveClipboardFormatListener(windowHandle);
        }

        private IntPtr HwndHandler(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE)
            {
                // fire event
                this.ClipboardUpdate?.Invoke(this, new EventArgs());
                // execute command
                if (this.ClipboardUpdateCommand?.CanExecute(null) ?? false)
                {
                    this.ClipboardUpdateCommand?.Execute(null);
                }
                // call virtual method
                OnClipboardUpdate();
            }
            handled = false;
            return IntPtr.Zero;
        }


        private static class NativeMethods
        {
            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool AddClipboardFormatListener(IntPtr hwnd);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        }
        #endregion
        #region get active window
        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowThreadProcessId(IntPtr hWnd, out uint ProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        Process GetActiveProcess()
        {
            IntPtr hwnd = GetForegroundWindow();
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            return Process.GetProcessById((int)pid);
        }
        #endregion
        private System.Windows.Forms.NotifyIcon notifyIcon = null;
        List<EveItem> items = new List<EveItem>();
        string LastAppraisal;
        string itemText;
        bool ValueOutdated = false;
        DispatcherTimer slideDownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4)
        };
        List<string> ores = new List<string>{
            "Arkonor","Bezdacine","Bistot","Crokite","Dark Ochre","Gneiss",
            "Hedbergite","Hemorphite","Jaspet", "Kernite", "Mercoxit", "Omber",
            "Plagioclase", "Pyroxeres", "Rakovene","Scordite","Spodumain","Talassonite","Veldspar",
        };
        List<string> ice = new List<string>
        {
            "Blue Ice","Clear Icicle","Dark Glitter","Gelidus","Glacial Mass","Glare Crust","Krystallos","White Glaze"
        };
        List<string> markets = new List<string> { "Jita", "Perimeter", "Universe", "Amarr", "Dodixie", "Hek", "Rens" };
        Regex itemName = new Regex(@"[^\t]*");
        Regex itemQty = new Regex(@"\t\d{1,3}(\.\d{1,3})?(\.\d{1,3})?");
        Storyboard sb = new Storyboard();
        ThicknessAnimation slide = new ThicknessAnimation();
        bool SlidingDown = false;
        NameValueCollection outgoingQueryString = HttpUtility.ParseQueryString(String.Empty);
        private string resultPrice;
        ContextMenuStrip contextMenu = new ContextMenuStrip();
        ToolStripMenuItem enabled = new ToolStripMenuItem("Auto mode");
        ToolStripMenuItem settings = new ToolStripMenuItem("Settings");
        ToolStripMenuItem exit = new ToolStripMenuItem("Exit");

        public MainWindow()
        {
            InitializeComponent();

            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");

            enabled.Checked = true;
            enabled.Click += Enabled_Click;
            settings.Click += Settings_Click;
            exit.Click += Exit_Click;
            contextMenu.Items.Add(enabled);
            contextMenu.Items.Add(settings);
            contextMenu.Items.Add(exit);
            notifyIcon = new System.Windows.Forms.NotifyIcon();
            notifyIcon.ContextMenuStrip = contextMenu;
            notifyIcon.Icon = Autopraisal.Properties.Resources.Wallet;
            notifyIcon.MouseClick += NotifyIcon_MouseClick;
            enabled.Checked = Properties.Settings.Default.MonitoringEnabled;

            slide.BeginTime = new TimeSpan(0);
            slide.SetValue(Storyboard.TargetProperty, mainGrid);
            Storyboard.SetTargetProperty(slide, new PropertyPath(MarginProperty));
            slideDownTimer.Tick += SlideDownTimer_Tick;

            slide.From = new Thickness(0, 25, 0, 0);
            slide.To = new Thickness(0, 0, 0, 0);
            slide.Duration = new Duration(TimeSpan.FromSeconds(0.2));

            sb.Children.Add(slide);
            sb.Completed += Sb_Completed;

        }

        private void SlideDownTimer_Tick(object sender, EventArgs e)
        {
            Slide(false);
            slideDownTimer.Stop();
        }

        private void NotifyIcon_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            string clipboardText = System.Windows.Clipboard.GetText();
            if (clipboardText == resultPrice) return;
            Appraise(clipboardText);
        }

        private void Settings_Click(object sender, EventArgs e)
        {
            new Settings().ShowDialog();
            ValueOutdated = true;
            enabled.Checked = Properties.Settings.Default.MonitoringEnabled;
            UpdateMonitoring();
        }

        private void Enabled_Click(object sender, EventArgs e)
        {
            enabled.Checked = !enabled.Checked;
            Properties.Settings.Default.MonitoringEnabled = enabled.Checked;
            UpdateMonitoring();
        }

        private void UpdateMonitoring()
        {
            if (enabled.Checked)
            {
                Start();
            }
            else
            {
                Stop();
            }
        }

        private void Exit_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void Sb_Completed(object sender, EventArgs e)
        {
            if (SlidingDown)
            {
                Visibility = Visibility.Collapsed;
            }
        }

        private void Rectangle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        void OnClipboardUpdate()
        {
            string clipboardText = "";
            try
            {
                clipboardText = System.Windows.Clipboard.GetText();
            }
            catch
            {
                Notify("?", "Failed to fetch clipboard");
                return;
            }
            if (clipboardText == resultPrice || clipboardText == "") return;
            Process p = GetActiveProcess();
            bool isEve = p.ProcessName == "exefile" && p.MainWindowTitle.StartsWith("EVE - ");
            if (!Properties.Settings.Default.CheckForEve | isEve)
            {
                Appraise(clipboardText);
            }
        }

        void Appraise(string text)
        {
            items.Clear();
            string AppraisalCache = "";
            try
            {
                using (StringReader reader = new StringReader(text))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string name = itemName.Match(line).Value;
                        int qty = int.Parse(itemQty.Match(line).Value.Trim().Replace(".",""));
                        if (Properties.Settings.Default.CompressOres && !name.StartsWith("Compressed "))
                        {
                            if (ores.Any(s => name.EndsWith(s)) && qty / 100 >= 1)
                            {
                                Regex r = new Regex(name);
                                name = "Compressed " + name;
                                qty /= 100;
                            }
                            if (ice.Any(s => name.EndsWith(s)))
                            {
                                Regex r = new Regex(name);
                                name = "Compressed " + name;
                            }
                        }
                        items.Add(new EveItem(name, qty.ToString()));
                        AppraisalCache += name + "\t" + qty + "\n";
                    }
                }
            }
            catch (Exception e)
            {
                Notify("?", "Failed to parse");
                return;
            }
            if (!string.IsNullOrEmpty(AppraisalCache) && LastAppraisal == AppraisalCache && !ValueOutdated)
            {
                System.Windows.Clipboard.SetDataObject(resultPrice);
                Notify(itemText, resultPrice, "Last value copied");
                ValueOutdated = false;
                return;
            }
            if (items.Count > 0)
            {
                itemText = items.Count > 1 ? "(multiple items)" : items[0].Quantity.ToString() + " x " + items[0].Name;
                try
                {
                    var result = GetAppraisal(AppraisalCache);
                    if (Properties.Settings.Default.Price == 0)
                    {
                        resultPrice = result.appraisal.totals.buy.ToString("N");
                    }
                    else
                    {
                        resultPrice = result.appraisal.totals.sell.ToString("N");
                    }
                    System.Windows.Clipboard.SetDataObject(resultPrice);
                    Notify(itemText, resultPrice, "Value copied");
                    ValueOutdated = false;
                    LastAppraisal = AppraisalCache;
                }
                catch (Exception e)
                {
                    Notify(itemText, "Appraisal failed");
                    return;
                }
            }
        }

        void Notify(string item, string message)
        {
            tbItem.Text = item;
            tbMessage.Text = message;
            tbMessage.Foreground = new SolidColorBrush(Colors.Gold);
            wpValue.Visibility = Visibility.Collapsed;
            SlideUp();
        }

        void Notify(string item, string price, string message)
        {
            tbItem.Text = item;
            tbPrice.Text = price;
            tbMessage.Text = message;
            tbMessage.Foreground = new SolidColorBrush(Colors.Turquoise);
            wpValue.Visibility = Visibility.Visible;
            SlideUp();
        }

        void SlideUp()
        {
            Visibility = Visibility.Visible;
            UpdateLayout();
            Rect desktopWorkingArea = SystemParameters.WorkArea;
            Left = desktopWorkingArea.Right - Width;
            Top = desktopWorkingArea.Bottom - Height;
            if (slideDownTimer.IsEnabled)
            {
                slideDownTimer.Stop();
                slideDownTimer.Start();
            }
            else
            {
                Slide(true);
                slideDownTimer.Start();
            }
        }

        Result GetAppraisal(string text)
        {
            HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create("http://evepraisal.com/appraisal.json?persist=no");
            httpWebRequest.ContentType = "application/x-www-form-urlencoded";
            httpWebRequest.Method = "POST";
            httpWebRequest.UserAgent = "Autopraisal/0.1a (github.com/dunsparce9/autopraisal)";
            outgoingQueryString.Clear();
            outgoingQueryString.Add("market", markets[Properties.Settings.Default.Market].ToLowerInvariant());
            outgoingQueryString.Add("price_percentage", Properties.Settings.Default.Percentage.ToString());
            outgoingQueryString.Set("raw_textarea", text);
            string postdata = outgoingQueryString.ToString();
            using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
            {
                streamWriter.Write(postdata);
            }
            var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
            using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
            {
                string response = streamReader.ReadToEnd();
                return JsonConvert.DeserializeObject<Result>(response);
            }
        }
        void Slide(bool up)
        {
            SlidingDown = !up;
            if (up)
            {
                App.Current.Dispatcher.Invoke((Action)delegate
                {
                    slide.From = new Thickness(0, 25, 0, 0);
                    slide.To = new Thickness(0, 0, 0, 0);
                    sb.Begin();
                });
            }
            else
            {
                App.Current.Dispatcher.Invoke((Action)delegate
                {
                    slide.From = new Thickness(0, 0, 0, 0);
                    slide.To = new Thickness(0, 25, 0, 0);
                    sb.Begin();
                });
            }
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            notifyIcon.Visible = true;
        }
    }
}
