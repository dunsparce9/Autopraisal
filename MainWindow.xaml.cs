using Autopraisal;
using Autopraisal.Models;
using OreCalc.Models;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
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
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;

namespace OreCalc
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
        List<EveItem> items = new List<EveItem>();
        Regex itemName = new Regex(@"[^\t]*");
        Regex itemQty = new Regex(@"\t\d{1,3}(\.\d{1,3})?(\.\d{1,3})?");
        Storyboard sb = new Storyboard();
        ThicknessAnimation slide = new ThicknessAnimation();
        bool SlidingDown = false;
        NameValueCollection outgoingQueryString = HttpUtility.ParseQueryString(String.Empty);
        public MainWindow()
        {
            InitializeComponent();
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");

            slide.BeginTime = new TimeSpan(0);
            slide.SetValue(Storyboard.TargetProperty, mainGrid);
            Storyboard.SetTargetProperty(slide, new PropertyPath(MarginProperty));

            slide.From = new Thickness(0, 25, 0, 0);
            slide.To = new Thickness(0, 0, 0, 0);
            slide.Duration = new Duration(TimeSpan.FromSeconds(0.2));

            sb.Children.Add(slide);
            sb.Completed += Sb_Completed;

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
            Process p = GetActiveProcess();
            if (p.ProcessName == "exefile" && p.MainWindowTitle.StartsWith("EVE - "))
            {
                items.Clear();
                using (StringReader reader = new StringReader(Clipboard.GetText()))
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
                    var result = Appraise(Clipboard.GetText());
                    tbPrice.Text = String.Format(result.appraisal.totals.buy.ToString("N"));
                    Clipboard.SetText(result.appraisal.totals.buy.ToString("N"));
                    Visibility = Visibility.Hidden;
                    UpdateLayout();
                    Rect desktopWorkingArea = SystemParameters.WorkArea;
                    Left = desktopWorkingArea.Right - Width;
                    Top = desktopWorkingArea.Bottom - Height;
                    Visibility = Visibility.Visible;
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
                outgoingQueryString.Set("raw_textarea", Clipboard.GetText());
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
    }
}
