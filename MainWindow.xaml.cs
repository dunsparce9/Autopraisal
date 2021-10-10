using Autopraisal.Models;
using OreCalc.Models;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
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
            Start();
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
        Regex itemName = new Regex(@"[^\t]*");
        Regex itemQty = new Regex(@"\t\d{1,3}(\.\d{1,3})?(\.\d{1,3})?");
        Storyboard sb = new Storyboard();
        ThicknessAnimation slide = new ThicknessAnimation();
        bool SlidingDown = false;
        NameValueCollection outgoingQueryString = HttpUtility.ParseQueryString(String.Empty);
        private string resultPrice;
        ContextMenuStrip contextMenu = new ContextMenuStrip();
        ToolStripMenuItem enabled = new ToolStripMenuItem("Monitoring enabled");
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

            slide.BeginTime = new TimeSpan(0);
            slide.SetValue(Storyboard.TargetProperty, mainGrid);
            Storyboard.SetTargetProperty(slide, new PropertyPath(MarginProperty));

            slide.From = new Thickness(0, 25, 0, 0);
            slide.To = new Thickness(0, 0, 0, 0);
            slide.Duration = new Duration(TimeSpan.FromSeconds(0.2));

            sb.Children.Add(slide);
            sb.Completed += Sb_Completed;

        }

        private void Settings_Click(object sender, EventArgs e)
        {
            throw new NotImplementedException();
        }

        private void Enabled_Click(object sender, EventArgs e)
        {
            enabled.Checked = !enabled.Checked;
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
            string clipboardText = System.Windows.Clipboard.GetText();
            if (clipboardText == resultPrice) return;
            Process p = GetActiveProcess();
            if (p.ProcessName == "exefile" && p.MainWindowTitle.StartsWith("EVE - "))
            {
                items.Clear();
                using (StringReader reader = new StringReader(clipboardText))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        items.Add(new EveItem(itemName.Match(line).Value, itemQty.Match(line).Value.Trim()));
                    }
                }
                if (items.Count > 0)
                {
                    tbItem.Text = items.Count > 1 ? "(multiple items)" : items[0].Quantity.ToString() + " x " + items[0].Name;
                    var result = Appraise(clipboardText);
                    resultPrice = result.appraisal.totals.buy.ToString("N");
                    tbPrice.Text = resultPrice;
                    System.Windows.Clipboard.SetDataObject(resultPrice);
                    Visibility = Visibility.Visible;
                    UpdateLayout();
                    Rect desktopWorkingArea = SystemParameters.WorkArea;
                    Left = desktopWorkingArea.Right - Width;
                    Top = desktopWorkingArea.Bottom - Height;
                    Slide(true);
                    Task.Delay(4000).ContinueWith(_ =>
                    {
                        Slide(false);
                    });
                }
            }

            Result Appraise(string clipboardText)
            {
                HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create("http://evepraisal.com/appraisal.json?persist=no");
                httpWebRequest.ContentType = "application/x-www-form-urlencoded";
                httpWebRequest.Method = "POST";
                httpWebRequest.UserAgent = "Autopraisal/0.0.1a (github.com/dunsparce9/autopraisal)";
                outgoingQueryString.Add("market", "jita");
                outgoingQueryString.Add("price_percentage", "90");
                outgoingQueryString.Set("raw_textarea", clipboardText);
                string postdata = outgoingQueryString.ToString();
                using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
                {
                    streamWriter.Write(postdata);
                }
                var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                {
                    string response = streamReader.ReadToEnd();
                    return JsonSerializer.Deserialize<Result>(response);
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

        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            notifyIcon.Visible = true;
        }
    }
}
