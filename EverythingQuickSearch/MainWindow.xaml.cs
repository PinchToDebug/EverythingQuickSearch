using EverythingQuickSearch.Util;
using Gma.System.MouseKeyHook;
using IWshRuntimeLibrary;
using Microsoft.WindowsAPICodePack.Shell;
using StringMath;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using File = System.IO.File;
using MenuItem = Wpf.Ui.Controls.MenuItem;
using Point = System.Windows.Point;
using Registry = Microsoft.Win32.Registry;
using Task = System.Threading.Tasks.Task;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace EverythingQuickSearch
{
    public partial class MainWindow : FluentWindow
    {
        #region import
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0;

        private const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;

        [SecurityCritical]
        [DllImport("dwmapi.dll", SetLastError = false, ExactSpelling = true)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref int pvAttribute, int cbAttribute);


        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType,
            IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [Flags]
        public enum KeyboardEventFlags : uint
        {
            KeyDown = 0x0000,
            KeyUp = 0x0002
        }

        public const byte VK_ESCAPE = 0x1B;

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, KeyboardEventFlags dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        const int WM_CLOSE = 0x0010;

        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);


        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(int uiAction, int uiParam, out bool pvParam, int fWinIni);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(int uiAction, int uiParam, bool pvParam, int fWinIni);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(int uiAction, int uiParam, ref ANIMATIONINFO pvParam, int fWinIni);

        private const int SPI_GETCLIENTAREAANIMATION = 0x1042;
        private const int SPI_SETCLIENTAREAANIMATION = 0x1043;
        private const int SPI_GETANIMATION = 0x0048;
        private const int SPI_SETANIMATION = 0x0049;

        private const int SPIF_UPDATEINIFILE = 0x01;
        private const int SPIF_SENDCHANGE = 0x02;

        [StructLayout(LayoutKind.Sequential)]
        public struct ANIMATIONINFO
        {
            public int cbSize;
            public int iMinAnimate;
        }

        #endregion

        private static readonly HashSet<System.Windows.Forms.Keys> NonCharacterKeys = new()
        {
            System.Windows.Forms.Keys.Escape,
            System.Windows.Forms.Keys.LWin,
            System.Windows.Forms.Keys.RWin,
            System.Windows.Forms.Keys.LMenu,
            System.Windows.Forms.Keys.RMenu,
            System.Windows.Forms.Keys.LControlKey,
            System.Windows.Forms.Keys.RControlKey,
            System.Windows.Forms.Keys.LShiftKey,
            System.Windows.Forms.Keys.RShiftKey,
            System.Windows.Forms.Keys.Tab,
            System.Windows.Forms.Keys.Back,
            System.Windows.Forms.Keys.CapsLock,
        };
        private RegistryHelper reg = new RegistryHelper("EverythingQuickSearch");
        private string url = "https://api.github.com/repos/PinchToDebug/EverythingQuickSearch/releases/latest";

        private uint _taskbarRestartMessage;

        public ObservableCollection<FileItem> FileItems { get; set; }
        public ObservableCollection<FileItem> AppItems { get; set; }
        private EverythingService? _everything;

        readonly ObservableCollection<FileItem> _results = new ObservableCollection<FileItem>();

        private readonly Dictionary<string, FileItem> _appItemMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FileItem> _fileItemMap = new(StringComparer.OrdinalIgnoreCase);

        private List<string> allowedExtensions = new List<string> { ".jpg", ".png", ".bmp", ".mov", ".mp4" };

        private ThumbnailGenerator thumbnailGenerator;

        private FileItem? _selectedItem;
        private bool _selectedItemIsFile;
        private CancellationTokenSource? _thumbnailCts;

        private RECT _searchHostRect;
        IntPtr _searchHwnd = IntPtr.Zero;

        private bool _lookForKeyDown = false;
        private bool _isVisible = true;
        private bool _isShowing;
        private bool originalAnimationState;
        private bool _originalMinAnimationState;

        private IntPtr _hookForeground = IntPtr.Zero;
        private WinEventDelegate _winEventDelegate;
        private IKeyboardMouseEvents m_GlobalHook;
        static BitmapSource? _defaultFolderIcon;
        private bool _darkModeApplication = true;
        private bool _darkModeSearchBar = true;
        private bool didMath = false;

        bool _isStartCentered = false;

        CancellationTokenSource? _searchCts;

        private string _currentQuery = string.Empty;
        private const int PageSize = 30;
        string forwardText = "";

        // int _currentOffset = 0;
        // bool _hasMoreResults = true;
        private int _currentFileOffset = 0;
        private int _currentAppOffset = 0;
        private bool _hasMoreAppResults = true;
        private bool _hasMoreFileResults = true;
        private bool enableRegex = false;

        private int _setSort = 1;
        private bool _setSortAscending = true;

        private HashSet<Key> _keysDown = new();

        private bool _isAppLoading = false;
        private bool _isFileLoading = false;
        private string shortcutFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Everything Quick Search");

        private readonly SemaphoreSlim _thumbnailSemaphore = new SemaphoreSlim(Math.Max(Environment.ProcessorCount - 1, 1));

        private string _categoryFilter = string.Empty;
        private Button? _selectedCategoryButton;

        SolidColorBrush SelectedItemBarBrush = new SolidColorBrush(Colors.Red);

        public MainWindow()
        {
            SystemThemeWatcher.Watch(this); // in 4.2.0 not working https://github.com/lepoco/wpfui/issues/1656

            InitializeComponent();

            if (!reg.KeyExistsRoot("AutoUpdate"))
            {
                reg.WriteToRegistryRoot("AutoUpdate", "True");
            }

            AutoUpdateToggle.IsChecked = reg.KeyExistsRoot("AutoUpdate") && (bool)reg.ReadKeyValueRoot("AutoUpdate");
            AutorunToggle.IsChecked = reg.KeyExistsRoot("startOnLogin") && (bool)reg.ReadKeyValueRoot("startOnLogin");

            versionHeader.Header += " " + Process.GetCurrentProcess().MainModule!.FileVersionInfo.FileVersion!.ToString();
            LoadUwpApps();
            Application.Current.Resources["SelectedItemBarBrush"] = SelectedItemBarBrush;

            _darkModeApplication = (int?)Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 1) == 0;
            _darkModeSearchBar = (int?)Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) == 0;
            UpdateWPFUITheme();
            _defaultFolderIcon = GetDefaultFolderIcon(16);
            FileItems = new ObservableCollection<FileItem>();
            AppItems = new ObservableCollection<FileItem>();
            thumbnailGenerator = new ThumbnailGenerator();

            this.DataContext = this;

            this.IsVisibleChanged += (s, e) =>
            {
                if (IsVisible)
                {
                    Debug.WriteLine("can hide");
                    _isVisible = true;
                }
                else
                {
                    _isVisible = false;
                }
            };

            m_GlobalHook = Hook.GlobalEvents();
            m_GlobalHook.KeyPress += GlobalHookKeyPress;
            m_GlobalHook.KeyDown += M_GlobalHook_KeyDown;
            _winEventDelegate = new WinEventDelegate(WinEventProc);
            _hookForeground = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
            _winEventDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
            Populate_Sortby_DropDownButton_ContextMenu();

        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            this.Visibility = Visibility.Hidden;
            this.Top = 10000;

            int disable = 1;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle,
                DWMWA_TRANSITIONS_FORCEDISABLED,
                ref disable,
                sizeof(int));

            _everything = new EverythingService(this);
            LoadSearchIcon();
        }

        private void UpdateWPFUITheme()
        {
            _darkModeApplication = (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                                                            "SystemUsesLightTheme", 1) == 0;
            _darkModeSearchBar = (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                                                          "AppsUseLightTheme", 1) == 0;

            ApplicationTheme theme = _darkModeApplication ? ApplicationTheme.Dark : ApplicationTheme.Light;
            ApplicationThemeManager.Apply(theme);
            WindowBackgroundManager.UpdateBackground(UiApplication.Current.MainWindow, theme, WindowBackdropType.Acrylic);
            this.SetResourceReference(Button.BackgroundProperty, "ControlOnImageFillColorDefaultBrush");
        }

        private void M_GlobalHook_KeyDown(object? sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (NonCharacterKeys.Contains(e.KeyCode) || e.KeyData.ToString() == "Tab, Alt")
            {
                return;
            }
            if (_lookForKeyDown)
            {
                PostMessage(_searchHwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                e.SuppressKeyPress = true;
                GlobalHookKeyPress(sender, null);
            }
        }

        private void GlobalHookKeyPress(object? sender, System.Windows.Forms.KeyPressEventArgs? e)
        {
            if (!_lookForKeyDown)
            {
                return;
            }
            if (_lookForKeyDown
                && e != null
                && e.KeyChar.ToString().Length > 0
                && e.KeyChar != '\t'
                && e.KeyChar.ToString() != "\u001b") // backspace char
            {

                Task.Run(() =>
                {
                    Dispatcher.Invoke(async () =>
                    {
                        //TODO: When EQS is window too small
                        //the category bar's buttons might not be visible
                        if (!IsVisible)
                        {
                            double scaling = GetDpiForWindow(_searchHwnd) / 96.0;
                            var source = PresentationSource.FromVisual(this);
                            var transform = source.CompositionTarget.TransformFromDevice;

                            Point dip = transform.Transform(new Point(_searchHostRect.left, _searchHostRect.top));
                            this.Left = dip.X;
                            this.Top = dip.Y + 12;
                            this.Width = (_searchHostRect.right - _searchHostRect.left) / scaling;
                            this.Height = ((_searchHostRect.bottom - _searchHostRect.top) / scaling) - 24;
                            if (_isStartCentered)
                            {
                                this.Left += 12;
                            }
                            Debug.WriteLine("show");
                            this.Show();
                        }

                        if (e.KeyChar.ToString() == "\u0016") // paste (ctrl+v)
                        {
                            forwardText = Clipboard.GetText();
                        }
                        else
                        {
                            forwardText += e.KeyChar.ToString();
                        }

                        await Dispatcher.BeginInvoke(new Action(() => { }), DispatcherPriority.Render);
                        await Dispatcher.BeginInvoke(new Action(() =>
                        {
                            var hwnd = new WindowInteropHelper(this).Handle;

                            uint appThread = GetCurrentThreadId();
                            uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);

                            if (foregroundThread != appThread)
                            {
                                AttachThreadInput(appThread, foregroundThread, true);
                                BringWindowToTop(hwnd);
                                SetForegroundWindow(hwnd);
                                AttachThreadInput(appThread, foregroundThread, false);
                            }
                            else
                            {
                                BringWindowToTop(hwnd);
                                SetForegroundWindow(hwnd);
                            }

                            this.Activate();


                            SearchBarTextBox.Text += forwardText;
                            SearchBarTextBox.CaretIndex = SearchBarTextBox.Text.Length;
                            SearchBarTextBox.Focus();

                            _isShowing = true;
                            // set animations to before state
                            Task.Run(() =>
                            {
                                SystemParametersInfo(SPI_SETCLIENTAREAANIMATION, 0, originalAnimationState, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                                SetMinimizeAnimation(_originalMinAnimationState);
                            });

                        }), DispatcherPriority.ApplicationIdle);


                    });
                });
                return;
            }
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (hwnd == IntPtr.Zero) return;
            GetWindowThreadProcessId(hwnd, out uint pid);
            string processName = Process.GetProcessById((int)pid).ProcessName;
            if (processName == null) return;
            if (processName == "SearchHost")
            {
                // get animation before
                Task.Run(() =>
                {
                    SystemParametersInfo(SPI_SETCLIENTAREAANIMATION, 0, false, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                    SetMinimizeAnimation(false);
                });

                GetWindowRect(hwnd, out _searchHostRect);
                _ = Task.Run(async () =>
                {

                    await Task.Delay(550); // animation speed, Windows doesn't let get the rect until anima
                    GetWindowRect(hwnd, out _searchHostRect);
                });

                _searchHwnd = hwnd;
                _lookForKeyDown = true;
                ChangeSelectedButton(AllFilterButton);
                changeRegexButtonColor();
                Debug.WriteLine("look for keys..");
                _isStartCentered = (int?)Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    "TaskbarAl", 1) == 0;
            }
            else if (_lookForKeyDown)
            {
                _ = Task.Run(() =>
                {
                    Thread.Sleep(150); // prevent not forwarding keys when loosing focus on launch
                    _lookForKeyDown = false;
                });
            }
        }

        protected override void OnDeactivated(EventArgs e)
        {
            if (!_isShowing)
            {
                Debug.WriteLine("bad OnDeactivated");
                return;
            }
            base.OnDeactivated(e);
            this.Hide();
            _ = Task.Run(() =>
            {
                SystemParametersInfo(SPI_SETCLIENTAREAANIMATION, 0, originalAnimationState, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                SetMinimizeAnimation(_originalMinAnimationState);
            });
            ChangeSelectedButton(AllFilterButton);
            SearchBarTextBox.Clear();
            if (_selectedItem != null)
            {
                _selectedItem.IsSelected = false;
            }
            SelectedItemPreviewImage.Source = null;
            Debug.WriteLine("OnDeactivated hiding");

            enableRegex = true;
            RegexButton_Click(null!, null!);
            _isShowing = false;
            LoadUwpApps();

        }
        public static bool GetMinimizeAnimation()
        {
            var info = new ANIMATIONINFO();
            info.cbSize = Marshal.SizeOf<ANIMATIONINFO>();
            SystemParametersInfo(SPI_GETANIMATION, info.cbSize, ref info, 0);

            return info.iMinAnimate != 0;
        }
        public static void SetMinimizeAnimation(bool enabled)
        {
            var info = new ANIMATIONINFO();
            info.cbSize = Marshal.SizeOf<ANIMATIONINFO>();
            info.iMinAnimate = enabled ? 1 : 0;
            SystemParametersInfo(SPI_SETANIMATION, info.cbSize, ref info, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }

        private static BitmapSource GetDefaultFolderIcon(int size)
        {
            if (_defaultFolderIcon != null)
            {
                return _defaultFolderIcon;
            }
            try
            {
                var shellObj = ShellObject.FromParsingName(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

                var thumbnail = shellObj.Thumbnail;
                thumbnail.CurrentSize = new System.Windows.Size(size, size);

                _defaultFolderIcon = thumbnail.BitmapSource;
                _defaultFolderIcon.Freeze();

                shellObj.Dispose();
            }
            catch
            {
                _defaultFolderIcon = null;
            }

            return _defaultFolderIcon!;
        }
        private async void SearchBarTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsVisible)
            {
                return;
            }
            string searchText = ((TextBox)sender).Text;
            forwardText = string.Empty;
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            CancellationToken token = _searchCts.Token;

            _currentFileOffset = 0;
            _currentAppOffset = 0;
            _hasMoreAppResults = true;

            App_ScrollViewer.ScrollToTop();
            scrollViewer.ScrollToTop();

            try
            {
                _isAppLoading = false;
                _isFileLoading = false;
                await LoadNextAppPageAsync(searchText, token);
                if (AppItems.Count > 0)
                {
                    NoSearchResultsGrid.Visibility = Visibility.Collapsed;
                    FilePreviewGrid.Visibility = Visibility.Collapsed;
                    FileDetails_DynamicScrollViewer.Visibility = Visibility.Visible;

                    didMath = false;
                    await SetPreviewItem(AppItems[0], false);
                }
                await LoadNextFilePageAsync(_categoryFilter + searchText, token, false);

                NoSearchResultsIcon.Foreground = new SolidColorBrush(_darkModeApplication ? Colors.White : Colors.Black);

                if (FileItems.Count > 0 && AppItems.Count == 0)
                {
                    NoSearchResultsGrid.Visibility = Visibility.Collapsed;
                    FilePreviewGrid.Visibility = Visibility.Visible;
                    FileDetails_DynamicScrollViewer.Visibility = Visibility.Visible;

                    didMath = false;
                    await SetPreviewItem(FileItems[0], true);
                }
                else if (FileItems.Count == 0 && AppItems.Count == 0)
                {
                    string savedText = searchText;
                    FileDetails_DynamicScrollViewer.Visibility = Visibility.Collapsed;
                    try
                    {
                        try
                        {
                            NoSearchResultsTextBlock.Text = "= " + searchText.Eval().ToString();
                        }
                        catch
                        {
                            searchText = searchText.Substring(0, searchText.Length - 1);
                            NoSearchResultsTextBlock.Text = "= " + searchText.Eval().ToString();
                        }
                        NoSearchResultsTextBlock.FontSize = 18;
                        NoSearchResultsIcon.Symbol = SymbolRegular.Calculator20;
                        NoSearchResultsIcon.Foreground = new SolidColorBrush(_darkModeApplication ? Colors.White : Colors.Black);
                        didMath = true;
                    }
                    catch
                    {
                        if (!didMath)
                        {
                            NoSearchResultsIcon.Symbol = SymbolRegular.Search24;
                            NoSearchResultsTextBlock.FontSize = 16;
                            NoSearchResultsTextBlock.Text = $"No results found for \"{savedText}\"";
                            NoSearchResultsIcon.Foreground = new SolidColorBrush(_darkModeApplication ? Colors.White : Colors.Black);
                        }
                        else
                        {
                            NoSearchResultsIcon.Foreground = new SolidColorBrush(Colors.IndianRed);
                        }
                    }
                    NoSearchResultsGrid.Visibility = Visibility.Visible;
                }
            }
            catch { }

            _currentQuery = searchText;
        }

        private async void LoadSearchIcon() // get windows searh icon
        {
            var tempList = await _everything!.SearchAsync("SearchIconOnDark.scale-200.png", 1, 0, 1, false);
            foreach (var item in tempList)
            {
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    SearchIconImage.Source = await thumbnailGenerator.GetThumbnailAsync(item.FullPath, 26);
                });
            }

        }

        private async Task LoadNextAppPageAsync(string searchText, CancellationToken token)
        {
            if (_isAppLoading || _everything == null)
            {
                return;
            }
            _isAppLoading = true;

            try
            {
                List<FileItem> tempList;
                try
                {
                    string searchText2 = $"ext:lnk;url;exe {searchText} file: " +
                     $"\"C:\\Users\\{Environment.UserName}\\AppData\\Roaming\\Microsoft\\Windows\\Start Menu\\Programs\" | " +
                     @"""C:\ProgramData\Microsoft\Windows\Start Menu\Programs\"" | "
                + "\"" + shortcutFolder + "\"";

                    tempList = await _everything.SearchAsync(searchText2, 1, 0, 5, false);
                    foreach (var item in tempList)
                    {
                        item.Name = Path.GetFileNameWithoutExtension(item.Name);
                    }

                }
                catch (TaskCanceledException)
                {
                    _isAppLoading = false;
                    return;
                }

                if (token.IsCancellationRequested)
                {
                    _isAppLoading = false;
                    return;
                }

                _hasMoreAppResults = tempList.Count >= PageSize;
                _currentAppOffset += tempList.Count;

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var newPaths = new HashSet<string>(tempList.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);

                    for (int i = AppItems.Count - 1; i >= 0; i--)
                    {
                        var path = AppItems[i].FullPath;
                        if (!newPaths.Contains(path))
                        {
                            AppItems.RemoveAt(i);
                            _appItemMap.Remove(path);
                        }
                    }
                    foreach (var item in tempList)
                    {
                        if (!_appItemMap.ContainsKey(item.FullPath))
                        {
                            AppItems.Add(item);
                            _appItemMap[item.FullPath] = item;
                        }
                    }

                    var sorted = AppItems
                        .OrderBy(a => a.Name.Length)
                        .Take(3)
                        .ToList();

                    for (int i = 0; i < sorted.Count; i++)
                    {
                        var currentIndex = AppItems.IndexOf(sorted[i]);
                        if (currentIndex != i)
                            AppItems.Move(currentIndex, i);
                    }
                    App_ScrollViewer.Height = Math.Min(AppItems.Count, 3) * 40;
                });

                _ = Parallel.ForEachAsync(tempList, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
                    CancellationToken = token
                },
                async (item, ct) =>
                {
                    if (!_appItemMap.TryGetValue(item.FullPath, out FileItem? target))
                    {
                        return;
                    }
                    var thumb = Directory.Exists(item.FullPath)
                        ? _defaultFolderIcon
                        : await thumbnailGenerator.GetThumbnailAsync(item.FullPath, 32);

                    await Application.Current.Dispatcher.InvokeAsync(() => target.Thumbnail = thumb);
                });
            }
            finally
            {
                _isAppLoading = false;
            }
        }


        private async Task LoadNextFilePageAsync(string searchText, CancellationToken token, bool categoryChanged)
        {
            if (_isFileLoading || _everything == null)
            {
                return;
            }
            _isFileLoading = true;

            try
            {
                List<FileItem> tempList;
                try
                {
                    tempList = await _everything!.SearchAsync(searchText, _setSort, _currentFileOffset, PageSize, enableRegex);
                }
                catch (TaskCanceledException)
                {
                    _isFileLoading = false;
                    return;
                }

                if (token.IsCancellationRequested)
                {
                    _isFileLoading = false;
                    return;
                }

                bool queryChanged = _categoryFilter + _currentQuery != searchText;

                if (queryChanged || categoryChanged)
                {
                    _fileItemMap.Clear();
                    _currentFileOffset = 0;
                }
                else
                {
                    _currentFileOffset += tempList.Count;
                }

                _hasMoreFileResults = tempList.Count >= PageSize;

                var existingPaths = new HashSet<string>(FileItems.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (queryChanged || categoryChanged)
                    {
                        var newPaths = new HashSet<string>(tempList.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);

                        for (int i = FileItems.Count - 1; i >= 0; i--)
                        {
                            var path = FileItems[i].FullPath;
                            if (!newPaths.Contains(path))
                            {
                                FileItems.RemoveAt(i);
                                _fileItemMap.Remove(path);
                            }
                        }
                    }

                    foreach (var item in tempList)
                    {
                        if (existingPaths.Add(item.FullPath))
                        {
                            FileItems.Add(item);
                            _fileItemMap[item.FullPath] = item;
                        }
                    }
                });

                _ = Parallel.ForEachAsync(tempList, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
                    CancellationToken = token
                },
                async (item, ct) =>
                {
                    if (!_fileItemMap.TryGetValue(item.FullPath, out FileItem? target))
                    {
                        return;
                    }
                    var thumb = Directory.Exists(item.FullPath)
                        ? _defaultFolderIcon
                        : await thumbnailGenerator.GetThumbnailAsync(item.FullPath, 16);
                    await Application.Current.Dispatcher.InvokeAsync(() => target.Thumbnail = thumb);
                });
            }
            finally
            {
                _isFileLoading = false;
            }
        }

        private async void scrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isFileLoading || _isAppLoading || _searchCts?.IsCancellationRequested == true)
            {
                return;
            }

            scrollViewer.UpdateLayout();

            if (e.ExtentHeight - e.VerticalOffset - e.ViewportHeight <= 170 && _hasMoreFileResults)
            {
                if (_searchCts != null && !_searchCts.IsCancellationRequested)
                {
                    await LoadNextFilePageAsync(_categoryFilter + _currentQuery, _searchCts.Token, false);
                }
            }
            else
            {
            }
        }
        private void ScrollSelectedIntoView()
        {
            if (FileItems_ItemsControl.ItemContainerGenerator.ContainerFromItem(_selectedItem) is not FrameworkElement container)
            {
                return;
            }
            var position = container.TransformToAncestor(scrollViewer)
                                    .Transform(new System.Windows.Point(0, 0));

            double itemTop = position.Y;
            double itemBottom = itemTop + container.ActualHeight;

            if (itemTop < 0)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + itemTop);

            }
            else if (itemBottom > scrollViewer.ViewportHeight)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + (itemBottom - scrollViewer.ViewportHeight));
            }
        }

        private void ItemTemplate_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border border && border.DataContext is FileItem item)
            {
                if (_darkModeApplication)
                {
                    if (item.IsSelected)
                    {
                        item.Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"];
                    }
                    else
                    {
                        item.Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"];
                    }
                }
                else
                {
                    item.Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"];
                }
            }
        }

        private void ItemTemplate_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border border && border.DataContext is FileItem item)
            {
                if (!item.IsSelected)
                {
                    item.Background = new SolidColorBrush(Colors.Transparent);
                }
                else
                {
                    item.Background = _darkModeApplication
                        ? (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"]
                        : (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
                }
            }
        }

        private void AppItemTemplate_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is FileItem item)
            {
                if (e.ChangedButton == MouseButton.Right)
                {
                    ContextMenu contextMenu = new ContextMenu();
                    MenuItem open = new MenuItem { Header = "Open", Height = 34, Icon = new SymbolIcon(SymbolRegular.Open24, 16) };
                    contextMenu.Items.Add(open);
                    contextMenu.IsOpen = true;
                }
            }
        }

        private void Item_MouseUp(object sender, MouseButtonEventArgs e)
        {
            FileItem item = new FileItem();
            if (sender is Border border && border.DataContext is FileItem fileitem)
            {
                item = fileitem;
            }
            else if (_selectedItem != null)
            {
                item = _selectedItem;
            }
            if (e.ChangedButton == MouseButton.Right)
            {
                ContextMenu contextMenu = new ContextMenu();
                int fontSize = 13;
                int height = 31;
                Thickness padding = new Thickness(4, 0, 4, 0);

                MenuItem open = new MenuItem
                {
                    Header = new System.Windows.Controls.TextBlock
                    {
                        Text = "Open",
                        FontSize = fontSize,
                        Padding = padding
                    },
                    Height = height,
                    Icon = new SymbolIcon(SymbolRegular.Open20, 16)
                };
                Debug.WriteLine(item.FullPath);

                open.Click += (_, _) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
                    }
                    catch { }
                };

                MenuItem openPath = new MenuItem
                {
                    Header = new System.Windows.Controls.TextBlock
                    {
                        Text = "Open Path",
                        FontSize = fontSize,
                        Padding = padding
                    },
                    Height = height,
                    Icon = new SymbolIcon(SymbolRegular.FolderOpen20, 16)
                };
                openPath.Click += (_, _) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(Path.GetDirectoryName(item.FullPath)!) { UseShellExecute = true });
                    }
                    catch { }
                };

                MenuItem copyPath = new MenuItem
                {
                    Header = new System.Windows.Controls.TextBlock
                    {
                        Text = "Copy as path",
                        FontSize = fontSize,
                        Padding = padding
                    },
                    Height = height,
                    Icon = new SymbolIcon(SymbolRegular.Share20, 16)
                };
                copyPath.Click += (_, _) =>
                {
                    Clipboard.SetText(item.FullPath);
                };

                MenuItem copyFolderPath = new MenuItem
                {
                    Header = new System.Windows.Controls.TextBlock
                    {
                        Text = "Copy folder path",
                        FontSize = fontSize,
                        Padding = padding
                    },
                    Height = height,
                    Icon = new SymbolIcon(SymbolRegular.Copy20, 16)
                };
                copyFolderPath.Click += (_, _) =>
                {
                    Clipboard.SetText(Path.GetDirectoryName(item.FullPath));
                };
                contextMenu.Items.Add(open);
                contextMenu.Items.Add(openPath);
                contextMenu.Items.Add(new Separator());
                contextMenu.Items.Add(copyPath);
                contextMenu.Items.Add(copyFolderPath);
                contextMenu.IsOpen = true;
            }
        }

        private string HumanizeSize(long bytes)
        {
            if (bytes < 0) return "-" + HumanizeSize(-bytes);

            string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };

            double size = bytes;
            int powerIndex = 0;

            while (size >= 1024 && powerIndex < units.Length - 1)
            {
                size /= 1024;
                powerIndex++;
            }
            return $"{size:0.##} {units[powerIndex]}";
        }


        private async Task SetPreviewItem(FileItem item, bool showDetails)
        {
            _thumbnailCts?.Cancel();
            _thumbnailCts = new CancellationTokenSource();
            var token = _thumbnailCts.Token;
            if (_selectedItem != null)
            {
                _selectedItem.IsSelected = false;
            }
            _selectedItem = item;
            _selectedItem.IsSelected = true;

            ScrollSelectedIntoView();

            var extension = Path.GetExtension(item.FullPath);
            bool isMedia = false;
            _selectedItemIsFile = showDetails;

            SelectedItemPreviewName.Text = item.Name;

            if (allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                SelectedItemPreviewImage.Width = 250;
                SelectedItemPreviewImage.Height = double.NaN;
                SelectedItemPreviewImage.MaxHeight = 280;
                isMedia = true;
            }
            else
            {
                SelectedItemPreviewImage.Clip = null;
                SelectedItemPreviewImage.Width = 64;
                SelectedItemPreviewImage.Height = 64;
            }

            int thumbnailWidth = (int)(SelectedItemPreviewImage.Width * 1.2);

            try
            {
                var thumb = await Task.Run(async () =>
                {
                    return await thumbnailGenerator.GetThumbnailAsync(item.FullPath, thumbnailWidth);
                }, token);

                if (!token.IsCancellationRequested)
                {
                    SelectedItemPreviewImage.Source = thumb;
                }
            }
            catch (OperationCanceledException) { }

            if (Directory.Exists(item.FullPath)) // hide option for folders
            {
                RunasAdminBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                RunasAdminBorder.Visibility = Visibility.Visible;
            }

            if (showDetails)
            {
                ActionSeparator.Visibility = Visibility.Visible;
            }
            else
            {
                ActionSeparator.Visibility = Visibility.Collapsed;
            }

            if (showDetails)
            {
                FilePreviewGrid.Visibility = Visibility.Visible;
                FilePreviewMPath.Text = Path.GetDirectoryName(item.FullPath);
                if (isMedia)
                {
                    RunasAdminBorder.Visibility = Visibility.Collapsed;
                    uint width = 0;
                    uint height = 0;

                    using (var shellFile = ShellFile.FromFilePath(item.FullPath))
                    {
                        if (shellFile.Properties.System.Image.HorizontalSize.Value.HasValue)
                        {
                            width = shellFile.Properties.System.Image.HorizontalSize.Value!.Value;
                            var h = shellFile.Properties.System.Image.VerticalSize.Value;
                            if (h.HasValue) height = h.Value;
                        }
                        else
                        {
                            var w = shellFile.Properties.System.Video.FrameWidth.Value;
                            var h = shellFile.Properties.System.Video.FrameWidth.Value;
                            if (w.HasValue && h.HasValue)
                            {
                                width = w.Value;
                                height = h.Value;
                            }
                        }

                    }
                    if (width != 0 && height != 0)
                    {
                        FilePreviewResolution.Text = $"{width} x {height}";
                        FilePreviewResolutionTextBlock.Visibility = Visibility.Visible;
                        FilePreviewResolution.Visibility = Visibility.Visible;

                    }
                    else
                    {
                        FilePreviewResolutionTextBlock.Visibility = Visibility.Collapsed;
                        FilePreviewResolution.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    FilePreviewResolutionTextBlock.Visibility = Visibility.Collapsed;
                    FilePreviewResolution.Visibility = Visibility.Collapsed;
                }
                FilePreviewModDate.Text = item.ModificationDate;
                FilePreviewSize.Text = HumanizeSize(item.Size);
            }
            else
            {
                RunasAdminBorder.Visibility = Visibility.Visible;
                FilePreviewGrid.Visibility = Visibility.Collapsed;
            }

        }
        private void LoadUwpApps()
        {
            using (var appsFolder = (ShellContainer)ShellObject.FromParsingName("shell:AppsFolder"))
            {
                List<string> existingUwps = new List<string>();
                string shortcutFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Everything Quick Search");
                Directory.CreateDirectory(shortcutFolder);

                // get uwp apps and create shortcuts for them if not exist, so they can be searched by everything
                foreach (var item in appsFolder)
                {
                    try
                    {
                        if (item.ParsingName.EndsWith("!App"))
                        {
                            existingUwps.Add(item.Name);
                        }
                        if (item.ParsingName.EndsWith("!App") && !File.Exists($@"{shortcutFolder}\{item.Name}.lnk"))
                        {
                            Debug.WriteLine("add new" + item.Name);
                            string shortcutPath = $@"{shortcutFolder}\{item.Name}.lnk";
                            string appUserModelId = item.ParsingName;

                            var shortcut = (IWshShortcut)new WshShell().CreateShortcut(shortcutPath);

                            shortcut.TargetPath = $"shell:AppsFolder\\{item.ParsingName}";
                            shortcut.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                            shortcut.Save();
                        }
                    }
                    catch { }
                }

                foreach (var file in Directory.GetFiles(shortcutFolder, "*.lnk"))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (!existingUwps.Contains(name))
                    {
                        try
                        {
                            File.Delete(file);
                            Debug.WriteLine("delete name: " + name);
                        }
                        catch
                        {
                        }
                    }
                }

            }
        }

        private async void FileItemTemplate_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is FileItem clickedItem)
            {
                if (e.ClickCount == 1)
                {
                    await SetPreviewItem(clickedItem, true);
                }
                else if (e.ClickCount == 2)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(clickedItem.FullPath) { UseShellExecute = true });
                    }
                    catch { }
                }
            }
        }
        private async void AppItemTemplate_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is FileItem clickedItem)
            {
                if (e.ClickCount == 1)
                {
                    await SetPreviewItem(clickedItem, false);
                }
                else if (e.ClickCount == 2)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(clickedItem.FullPath) { UseShellExecute = true });
                    }
                    catch { }
                }
            }
        }
        private void FluentWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                // get animation
                SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, out originalAnimationState, 0);
                _originalMinAnimationState = GetMinimizeAnimation();
            });
            // Remove harsh window shadow
            var hwnd = new WindowInteropHelper(this).Handle;
            int attr = 4;
            DwmSetWindowAttribute(hwnd, 33, ref attr, sizeof(int));

            _taskbarRestartMessage = RegisterWindowMessage("TaskbarCreated");
            var hwndSource = HwndSource.FromHwnd(hwnd);
            hwndSource.AddHook(WndProc);

            ChangeSelectedButton(AllFilterButton);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == _taskbarRestartMessage)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    TrayIcon.Register();
                });
            }
            return IntPtr.Zero;
        }

        private void SelectedItemPreviewImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (_selectedItem != null && e.ChangedButton == MouseButton.Left)
                {
                    Process.Start(new ProcessStartInfo(_selectedItem.FullPath) { UseShellExecute = true });
                }
            }
            catch { }
        }
        private void ChangeSelectedButton(Button newButton)
        {
            UpdateWPFUITheme();
            if (_selectedCategoryButton != null)
            {
                _selectedCategoryButton.ClearValue(Button.BackgroundProperty);
                _selectedCategoryButton.SetResourceReference(Button.ForegroundProperty, "TextFillColorPrimaryBrush");
            }
            _selectedCategoryButton = newButton;
            if (_darkModeApplication)
            {

                _selectedCategoryButton.SetResourceReference(Button.BackgroundProperty, "InfoBadgeAttentionSeverityBackgroundBrush");
            }
            else
            {
                _selectedCategoryButton.SetResourceReference(Button.BackgroundProperty, "SystemAccentColorPrimaryBrush");

            }
            var newBrush = new SolidColorBrush(Colors.Transparent);

            if (_selectedCategoryButton.Background is SolidColorBrush btnBrush)
            {
                newBrush.Color = btnBrush.Color;
                Application.Current.Resources["SelectedItemBarBrush"] = newBrush;

            }
            _selectedCategoryButton.Foreground = new SolidColorBrush(_darkModeApplication ? Colors.Black : Colors.White);
        }
        private void changeRegexButtonColor()
        {
            if (enableRegex)
            {
                if (_darkModeApplication)
                {
                    RegexButton.SetResourceReference(Button.BackgroundProperty, "InfoBadgeAttentionSeverityBackgroundBrush");
                }
                else
                {
                    RegexButton.SetResourceReference(Button.BackgroundProperty, "SystemAccentColorPrimaryBrush");
                }
            }
            else
            {
                RegexButton.ClearValue(Button.BackgroundProperty);
                RegexButton.SetResourceReference(Button.ForegroundProperty, "TextFillColorPrimaryBrush");
            }
            RegexButton.Foreground = new SolidColorBrush(
                _darkModeApplication
                    ? (enableRegex ? Colors.Black : Colors.White)
                    : (enableRegex ? Colors.White : Colors.Black)
                );
        }
        private void RegexButton_Click(object sender, RoutedEventArgs e)
        {
            enableRegex = !enableRegex;
            if (enableRegex)
            {
                ChangeSelectedButton(AllFilterButton);
                _categoryFilter = SearchCategory.GetExtensions(Category.All);
            }
            _currentQuery = string.Empty;
            _currentFileOffset = 0;
            FileItems.Clear();
            _fileItemMap.Clear();
            SearchBarTextBox_TextChanged(SearchBarTextBox, null!);
            changeRegexButtonColor();
        }
        private async void FluentWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            //TODO: add left right
            if (_selectedItem == null) return;

            SearchBarTextBox.Focus();

            Key actualKey = e.Key == Key.System ? e.SystemKey : e.Key;

            // prevent altgr
            bool altDown = (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
                      && !((Keyboard.IsKeyDown(Key.LeftCtrl) || (Keyboard.IsKeyDown(Key.RightCtrl))));

            Key[] motionUp = { Key.W, Key.K };
            Key[] motionDown = { Key.S, Key.J };
            Key[] motionLeft = { Key.A, Key.H };
            Key[] motionRight = { Key.D, Key.L };
            int index;
            if (actualKey == Key.Escape)
            {
                this.Hide();
            }
            if (altDown)
            {
                switch (actualKey)
                {
                    case Key.R:
                        RegexButton_Click(null!, null!);
                        break;

                    case Key.D1:
                    case Key.NumPad1:
                        AllFilterButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;

                    case Key.D2:
                    case Key.NumPad2:
                        FilesFilterButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;

                    case Key.D3:
                    case Key.NumPad3:
                        FoldersFilterButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;

                    case Key.D4:
                    case Key.NumPad4:
                        AppsFilterButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;

                    case Key.D5:
                    case Key.NumPad5:
                        FilterButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;

                    case Key.D6:
                    case Key.NumPad6:
                        ImagesFilterButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;

                    case Key.D7:
                    case Key.NumPad7:
                        VideoFilterButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;

                    case Key.D8:
                    case Key.NumPad8:
                        CompressedFilterButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;
                }
            }
            if (_selectedItemIsFile)
            {
                index = FileItems.IndexOf(_selectedItem);
            }
            else
            {
                index = AppItems.IndexOf(_selectedItem);
            }
            if (actualKey == Key.PageDown)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + scrollViewer.ViewportHeight);
            }
            else if (actualKey == Key.PageUp)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - scrollViewer.ViewportHeight);
            }
            else if (actualKey == Key.End)
            {
                scrollViewer.ScrollToBottom();
                if (FileItems.Count > 0)
                {
                    await SetPreviewItem(FileItems[FileItems.Count - 1], true);
                }
                else if (FileItems.Count == 0 && AppItems.Count > 0)
                {
                    await SetPreviewItem(AppItems[AppItems.Count - 1], false);
                }
            }
            else if (actualKey == Key.Home)
            {
                scrollViewer.ScrollToTop();
                if (AppItems.Count > 0)
                {
                    await SetPreviewItem(AppItems[0], false);
                }
                if (FileItems.Count > 0 && AppItems.Count == 0)
                {
                    await SetPreviewItem(FileItems[0], true);
                }
            }
            else if (actualKey == Key.Enter)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(_selectedItem.FullPath) { UseShellExecute = true });
                }
                catch { }
            }
            else if (actualKey == Key.Up || (altDown && motionUp.Contains(actualKey)))
            {
                if (_selectedItemIsFile)
                {
                    if (index != 0)
                    {
                        await SetPreviewItem(FileItems[index - 1], true);
                    }
                    else
                    {
                        if (AppItems.Count > 0)
                        {
                            await SetPreviewItem(AppItems[AppItems.Count - 1], false);
                        }
                    }
                }
                else
                {
                    if (index > 0)
                    {
                        await SetPreviewItem(AppItems[index - 1], false);
                    }
                }
            }
            else if (e.Key == Key.Down || (altDown && motionDown.Contains(actualKey)))
            {
                if (_selectedItemIsFile)
                {
                    if (index < FileItems.Count - 1)
                    {
                        await SetPreviewItem(FileItems[index + 1], true);
                    }
                }
                else
                {
                    if (index < AppItems.Count - 1)
                    {
                        await SetPreviewItem(AppItems[index + 1], false);
                    }
                    else
                    {
                        if (FileItems.Count > 0)
                        {
                            await SetPreviewItem(FileItems[0], true);
                        }
                    }
                }
            }
        }

        private void FluentWindow_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            _keysDown.Remove(e.Key);
        }

        private void Border_MouseEnter(object sender, MouseEventArgs e)
        {
            ((Border)sender).Background = _darkModeApplication
                ? (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"]
                : (Brush)Application.Current.Resources["ControlAltFillColorTertiaryBrush"];
        }

        private void Border_MouseLeave(object sender, MouseEventArgs e)
        {
            ((Border)sender).Background = new SolidColorBrush(Colors.Transparent);
        }

        private void RunasAdminBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (_selectedItem != null)
                {
                    Process.Start(new ProcessStartInfo(_selectedItem.FullPath)
                    {
                        UseShellExecute = true,
                        Verb = "runas"
                    });
                }

            }
            catch { }
        }

        private void OpenFileLocationBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (_selectedItem != null)
                {
                    Process.Start(new ProcessStartInfo(Path.GetDirectoryName(_selectedItem.FullPath)!) { UseShellExecute = true });
                }
            }
            catch { }
        }

        private void OpenFileBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (_selectedItem != null)
                {
                    Process.Start(new ProcessStartInfo(_selectedItem.FullPath) { UseShellExecute = true });
                }
            }
            catch { }
        }

        private async void CategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (enableRegex) return;

            if (sender is Button btn && Enum.TryParse<Category>(btn.Tag?.ToString(), out var category))
            {
                ChangeSelectedButton(btn);
                _categoryFilter = SearchCategory.GetExtensions(category);
                _currentQuery = string.Empty;
                _currentFileOffset = 0;
                FileItems.Clear();
                _fileItemMap.Clear();
                SearchBarTextBox_TextChanged(SearchBarTextBox, null!);
            }
        }
        private void Populate_Sortby_DropDownButton_ContextMenu()
        {
            MenuItem sortByMenuItem = new MenuItem
            {
                Header = "Sort by",
                Height = 28,
                StaysOpenOnClick = true,
                Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowSort20 }
            };

            HashSet<String> sortValues = new HashSet<string> {"Name","Path","Size","Extension","Type","Date created",
                                                              "Date modified","Attributes","File list filename","Run count",
                                                              "Date recently changed","Date Accessed","Date run"};
            int sortId = 1;
            int fontSize = 13;
            int height = 31;
            Thickness padding = new Thickness(0, 0, 8, 0);

            foreach (var item in sortValues)
            {
                MenuItem menuitem = new MenuItem
                {
                    Header = new System.Windows.Controls.TextBlock
                    {
                        Text = item,
                        FontSize = fontSize,
                        Padding = padding
                    },
                    Height = height,
                    StaysOpenOnClick = true,
                    Tag = sortId,
                    Icon = new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Foreground = Brushes.Transparent, Filled = true }
                };

                if ((int)menuitem.Tag * 2 - (_setSortAscending ? 1 : 0) == _setSort)
                {
                    menuitem.Icon.SetResourceReference(Button.ForegroundProperty, "TextFillColorPrimaryBrush");
                }
                menuitem.Click += (_, _) =>
                {
                    foreach (var item in SortByContextMenu.Items)
                    {
                        if (item is MenuItem menui && menui.Header is System.Windows.Controls.TextBlock tb && sortValues.Contains(tb.Text))
                        {
                            if (menui.Icon is SymbolIcon icon)
                            {
                                icon.Foreground = Brushes.Transparent;
                            }
                        }
                    }
                    menuitem.Icon.SetResourceReference(Button.ForegroundProperty, "TextFillColorPrimaryBrush");
                    _setSort = (int)menuitem.Tag * 2 - (_setSortAscending ? 1 : 0);
                    _currentQuery = string.Empty;
                    SearchBarTextBox_TextChanged(SearchBarTextBox, null!);

                };
                SortByContextMenu.Items.Add(menuitem);

                sortId++;
            }
            MenuItem descendingMenuItem = new MenuItem();
            MenuItem ascendingMenuItem = new MenuItem
            {
                Header = new System.Windows.Controls.TextBlock
                {
                    Text = "Ascending",
                    FontSize = fontSize,
                    Padding = padding
                },
                Height = height,
                StaysOpenOnClick = true,
                Icon = new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Foreground = Brushes.Transparent, Filled = true },
            };
            ascendingMenuItem.Click += (_, _) =>
            {
                if (!_setSortAscending)
                {
                    _setSortAscending = true;
                    _currentQuery = string.Empty;
                    _currentFileOffset = 0;
                    FileItems.Clear();
                    _fileItemMap.Clear();
                    SearchBarTextBox_TextChanged(SearchBarTextBox, null!);

                    _setSort--;
                    ascendingMenuItem.Icon.SetResourceReference(Button.ForegroundProperty, "TextFillColorPrimaryBrush");
                    descendingMenuItem.Icon.Foreground = Brushes.Transparent;
                }
            };

            descendingMenuItem = new MenuItem
            {
                Header = new System.Windows.Controls.TextBlock
                {
                    Text = "Descending",
                    FontSize = fontSize,
                    Padding = padding
                },
                Height = height,
                StaysOpenOnClick = true,
                Icon = new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Foreground = Brushes.Transparent, Filled = true },
            };
            descendingMenuItem.Click += (_, _) =>
            {
                if (_setSortAscending)
                {
                    _setSortAscending = false;
                    _currentQuery = string.Empty;
                    _currentFileOffset = 0;
                    FileItems.Clear();
                    _fileItemMap.Clear();
                    SearchBarTextBox_TextChanged(SearchBarTextBox, null!);

                    _setSort++;
                    descendingMenuItem.Icon.SetResourceReference(Button.ForegroundProperty, "TextFillColorPrimaryBrush");
                    ascendingMenuItem.Icon.Foreground = Brushes.Transparent;
                }
            };

            if (_setSortAscending)
            {
                ascendingMenuItem.Icon.SetResourceReference(Button.ForegroundProperty, "TextFillColorPrimaryBrush");
                descendingMenuItem.Icon.Foreground = Brushes.Transparent;
            }
            else
            {
                descendingMenuItem.SetResourceReference(Button.BackgroundProperty, "TextFillColorPrimaryBrush");
                ascendingMenuItem.Icon.Foreground = Brushes.Transparent;
            }

            SortByContextMenu.Items.Add(new Separator());
            SortByContextMenu.Items.Add(ascendingMenuItem);
            SortByContextMenu.Items.Add(descendingMenuItem);
        }

        private void FilterButton_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Button btn)
            {
                if (enableRegex)
                {
                    btn.ToolTip = "Can't change category when\nregex filtering is enabled";
                }
                else
                {
                    btn.ToolTip = null;
                }
            }
        }
        private void FluentWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_everything != null)
            {
                _everything.Dispose();
            }
        }

        private void FileItemTemplate_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && sender is Border border)
            {
                if (border.DataContext is FileItem fileItem)
                {
                    DataObject data = new DataObject(DataFormats.FileDrop, new string[] { fileItem.FullPath! });
                    DragDrop.DoDragDrop(border, data, DragDropEffects.Copy | DragDropEffects.Move);
                }
            }
        }
        private void RoundedDropDownButtonButton_MouseEnter(object sender, MouseEventArgs e)
        {
            ((DropDownButton)sender).Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        }

        private void RoundedDropDownButtonButton_MouseLeave(object sender, MouseEventArgs e)
        {
            ((DropDownButton)sender).Background = Brushes.Transparent;
        }


        #region notifyicon
        private void AutorunToggle_CheckChanged(object sender, RoutedEventArgs e)
        {
            if ((bool)AutorunToggle.IsChecked!)
            {
                reg.AddToAutoRun("EverythingQuickSearch", Process.GetCurrentProcess().MainModule!.FileName);
            }
            else
            {
                reg.RemoveFromAutoRun("EverythingQuickSearch");
            }
            reg.WriteToRegistryRoot("startOnLogin", AutorunToggle.IsChecked);
        }

        private async void AutoUpdateToggle_CheckChanged(object sender, RoutedEventArgs e)
        {
            reg.WriteToRegistryRoot("AutoUpdate", AutoUpdateToggle.IsChecked!);
            if ((bool)reg.ReadKeyValueRoot("AutoUpdate"))
            {
                await Updater.CheckUpdateAsync(url, false);
            }
        }

        private async void Update_Button_Click(object sender, RoutedEventArgs e)
        {
            await Updater.CheckUpdateAsync(url, true);
        }
        private void visitGithub_Buton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ProcessStartInfo sInfo = new ProcessStartInfo($"https://github.com/PinchToDebug/EverythingQuickSearch") { UseShellExecute = true };
                _ = Process.Start(sInfo);
            }
            catch { }
        }
        private void ExitApp(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        #endregion


    }
}