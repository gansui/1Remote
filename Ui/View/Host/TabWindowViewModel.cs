using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Timers;
using Dragablz;
using _1RM.Model;
using _1RM.Model.ProtocolRunner;
using _1RM.Service;
using _1RM.Utils;
using _1RM.Utils.WindowsApi;
using _1RM.View.Host.ProtocolHosts;
using _1RM.View.Settings;
using _1RM.View.Utils;
using Shawn.Utils.Wpf;
using Stylet;

namespace _1RM.View.Host
{
    public class TabWindowViewModel : MaskLayerContainerScreenBase, IDisposable
    {
        public readonly string Token;
        public new TabWindowView View { get; private set; }
        public SettingsPageViewModel SettingsPage => IoC.Get<SettingsPageViewModel>();

        private IntPtr _hWndTabContent = IntPtr.Zero;
        private readonly Timer _timer_ObserveTabSwitching = new Timer(5);
        private int _timer_Count = 0;

        private void InitTabSwitchingTimer()
        {
            _timer_ObserveTabSwitching.AutoReset = false;
            _timer_ObserveTabSwitching.Elapsed += (sender, args) => AwaitTabSwitching();
        }

        public TabWindowViewModel(TabWindowView windowView)
        {
            View = windowView;
            Token = DateTime.Now.Ticks.ToString();
            Items.CollectionChanged += ItemsOnCollectionChanged;
            InitTabSwitchingTimer();
        }

        private void ItemsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RaisePropertyChanged(nameof(BtnCloseAllVisibility));
            if (Items.Count == 0)
            {
                View.Hide();
            }
        }

        public void Dispose()
        {
            _timer_ObserveTabSwitching.Stop();
            Execute.OnUIThread(() =>
            {
                SelectedItem = null;
                foreach (var item in Items.ToArray())
                {
                    if (item.Content is IDisposable dp)
                    {
                        dp.Dispose();
                    }
                }
                Items.CollectionChanged -= ItemsOnCollectionChanged;
                Items.Clear();
            });
        }

        private string _title = "";
        public string Title
        {
            get => _title;
            set => SetAndNotifyIfChanged(ref _title, value);
        }

        public ResizeMode WindowResizeMode
        {
            get
            {
                if (SelectedItem?.Content == null
                    || SelectedItem.Content.CanResizeNow() == false)
                    return ResizeMode.NoResize;
                return ResizeMode.CanResize;
            }
        }

        public ObservableCollection<TabItemViewModel> Items { get; } = new ObservableCollection<TabItemViewModel>();

        public Visibility BtnCloseAllVisibility => Items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        private TabItemViewModel? _selectedItem = null;
        public TabItemViewModel? SelectedItem
        {
            get => _selectedItem;
            set
            {
                _timer_ObserveTabSwitching.Stop();

                // Get effective window of the current tab content.
                _hWndTabContent = GetCurrentTabContentWindow();

                if (_selectedItem?.Content != null)
                {
                    _selectedItem.Content.OnCanResizeNowChanged -= OnCanResizeNowChanged;
                }

                var old = _selectedItem;
                if (SetAndNotifyIfChanged(ref _selectedItem, value))
                {
                    if(old != null)
                        old.PropertyChanged -= SelectedItemOnPropertyChanged;
                    if (_selectedItem != null)
                        _selectedItem.PropertyChanged += SelectedItemOnPropertyChanged;
                    if (_selectedItem != null)
                    {
                        SetTitle();
                        _selectedItem.Content?.OnCanResizeNowChanged += OnCanResizeNowChanged;

                        // Here, the SelectedItem property has merely been assigned a new value;
                        // the tab switching process is not yet complete. Therefore, it is still
                        // not possible to give focus to the tab that will become active.

                        // Since there is no event notification when a new tab becomes active,
                        // we use a timer to detect this change and then give focus to the newly
                        // active tab.
                        _timer_Count = 80;  // 5ms interval, total 400ms
                        _timer_ObserveTabSwitching.Start();
                    }
                    foreach (var item in Items)
                    {
                        if (item is TabItemViewModel { Content: IntegrateHost ih })
                        {
                            ih.ShowWindow(item == value);
                        }
                    }
                    RaisePropertyChanged(nameof(WindowResizeMode));
                }
            }
        }

        private IntPtr GetCurrentTabContentWindow()
        {
            IntPtr hWndParent = Win32Api.FindWindow(null, this.Title);
            if (hWndParent == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }
            IntPtr last = IntPtr.Zero;
            do
            {
                IntPtr hWnd = Win32Api.FindWindowEx(hWndParent, last, null, "");
                if (hWnd != IntPtr.Zero && Win32Api.IsWindowVisible(hWnd))
                {
                    return hWnd;
                }
                last = hWnd;
            } while (last != IntPtr.Zero);
            return IntPtr.Zero;
        }

        private void AwaitTabSwitching()
        {
            if (_timer_Count <= 0) return;
            _timer_Count--;
            IntPtr hWnd = GetCurrentTabContentWindow();
            if (hWnd == IntPtr.Zero || hWnd == _hWndTabContent)
            {
                _timer_ObserveTabSwitching.Start();  // continue to observe
            }
            else
            {
                Win32Api.SetForegroundWindow(hWnd);
                _selectedItem?.Content?.FocusOnMe();
            }
        }

        private void SelectedItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TabItemViewModel.DisplayName))
            {
                SetTitle();
            }
        }

        private void OnCanResizeNowChanged()
        {
            RaisePropertyChanged(nameof(WindowResizeMode));
        }


        #region drag drop tab

        public IInterTabClient InterTabClient { get; } = new InterTabClient();

        #endregion drag drop tab

        private void SetTitle()
        {
            if (SelectedItem != null)
            {
                this.Title = SelectedItem.DisplayName + " - " + Assert.APP_DISPLAY_NAME;
            }
        }

        public bool TryRemoveItem(string connectionId)
        {
            var item = Items.FirstOrDefault(x => x.Content.ConnectionId == connectionId);
            if (item != null)
            {
                Execute.OnUIThreadSync(() =>
                {
                    Items.Remove(item);
                    // Dispose the item to release event subscriptions
                    if (item is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                    SelectedItem = Items.FirstOrDefault();
                });
                return true;
            }
            return false;
        }

        public void AddItem(TabItemViewModel newItem)
        {
            if (Items.Any(x => x.Content?.ConnectionId == newItem.Content.ConnectionId))
            {
                SelectedItem = Items.First(x => x.Content.ConnectionId == newItem.Content.ConnectionId);
                return;
            }
            Items.Add(newItem);
            newItem.Content.SetParentWindow(View);
            SelectedItem = newItem;
        }

        public TabItemViewModel? GetItem(string connectionId)
        {
            return Items.FirstOrDefault(x => x.Content.ConnectionId == connectionId);
        }


        #region CMD

        private RelayCommand? _cmdHostGoFullScreen;
        public RelayCommand CmdHostGoFullScreen
        {
            get
            {
                return _cmdHostGoFullScreen ??= new RelayCommand((o) =>
                {
                    if (this.SelectedItem?.Content?.CanResizeNow() ?? false)
                        IoC.Get<SessionControlService>().MoveSessionToFullScreen(SelectedItem.Content.ConnectionId);
                }, o => this.SelectedItem != null && (this.SelectedItem.Content?.CanFullScreen ?? false));
            }
        }

        private RelayCommand? _cmdInvokeLauncher;
        public RelayCommand CmdInvokeLauncher
        {
            get
            {
                return _cmdInvokeLauncher ??= new RelayCommand((o) => { IoC.Get<LauncherWindowViewModel>().ShowMe(); }, o => this.SelectedItem != null);
            }
        }

        private RelayCommand? _cmdShowTabByIndex;
        public RelayCommand CmdShowTabByIndex
        {
            get
            {
                return _cmdShowTabByIndex ??= new RelayCommand((o) =>
                {
                    if (int.TryParse(o?.ToString() ?? "0", out int i))
                    {
                        if (i > 0 && i <= Items.Count)
                        {
                            SelectedItem = Items[i - 1];
                        }
                    }
                }, o => this.SelectedItem != null);
            }
        }

        private RelayCommand? _cmdGoMinimize;
        public RelayCommand CmdGoMinimize
        {
            get
            {
                return _cmdGoMinimize ??= new RelayCommand((_) =>
                {
                    if (this.View is Window window)
                    {
                        window.WindowState = WindowState.Minimized;
                    }
                });
            }
        }

        private RelayCommand? _cmdGoMaximize;
        public RelayCommand CmdGoMaximize
        {
            get
            {
                return _cmdGoMaximize ??= new RelayCommand((o) =>
                {
                    if (View.WindowState != WindowState.Maximized)
                    {
                        View.WindowState = WindowState.Maximized;
                    }
                    else
                    {
                        View.WindowStyle = WindowStyle.SingleBorderWindow;
                        View.WindowState = WindowState.Normal;
                    }
                });
            }
        }


        private RelayCommand? _cmdGoMaximizeF11;
        public RelayCommand CmdGoMaximizeF11
        {
            get
            {
                return _cmdGoMaximizeF11 ??= new RelayCommand((o) =>
                {
                    if (View.WindowState != WindowState.Maximized)
                    {
                        View.WindowStyle = WindowStyle.None;
                        View.WindowState = WindowState.Maximized;
                    }
                    else
                    {
                        View.WindowStyle = WindowStyle.SingleBorderWindow;
                        View.WindowState = WindowState.Normal;
                    }
                });
            }
        }

        public Visibility BtnSplitVisibility => Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        private void DoSplit(SplitDirection direction)
        {
            if (SelectedItem?.Content == null) return;

            var currentHost = SelectedItem.Content;
            var currentProtocol = currentHost.ProtocolServer;

            var runner = RunnerHelper.GetRunner(
                IoC.Get<ProtocolConfigurationService>(),
                currentProtocol,
                currentProtocol.Protocol);

            if (runner == null || runner.IsRunWithoutHosting()) return;

            if (currentHost is SplitPaneHost splitPane)
            {
                var protocolClone = currentProtocol.Clone();
                protocolClone.DecryptToConnectLevel();
                protocolClone.GenerateSessionId();
                var newHost = runner.GetHost(protocolClone, View);
                if (newHost == null) return;
                RegisterHost(newHost);
                splitPane.Split(direction, newHost);
                newHost.Conn();
                return;
            }

            // 创建两个全新的host
            var protocolClone1 = currentProtocol.Clone();
            protocolClone1.DecryptToConnectLevel();
            protocolClone1.GenerateSessionId();
            var host1 = runner.GetHost(protocolClone1, View);
            if (host1 == null) return;

            var protocolClone2 = currentProtocol.Clone();
            protocolClone2.DecryptToConnectLevel();
            protocolClone2.GenerateSessionId();
            var host2 = runner.GetHost(protocolClone2, View);
            if (host2 == null) return;

            RegisterHost(host1);
            RegisterHost(host2);

            var splitHost = new SplitPaneHost(protocolClone1, host1);
            splitHost.SetParentWindow(View);
            splitHost.Split(direction, host2);

            // 作为新tab添加（不移除旧tab，避免触发清理杀PuTTY进程）
            var newItem = new TabItemViewModel(splitHost, currentProtocol.DisplayName + " [Split]");
            Items.Add(newItem);
            SelectedItem = newItem;

            host1.Conn();
            host2.Conn();
        }
        }

        private void RegisterHost(HostBase host)
        {
            host.OnClosed += id =>
            {
                IoC.Get<SessionControlService>().CloseProtocolHostAsync(id);
            };
            host.OnFullScreen2Window += id =>
            {
                IoC.Get<SessionControlService>().MoveSessionToTabWindow(id);
            };
            IoC.Get<SessionControlService>().ConnectionId2Hosts.TryAdd(host.ConnectionId, host);
        }

        private RelayCommand? _cmdSplitHorizontal;
        public RelayCommand CmdSplitHorizontal
        {
            get
            {
                return _cmdSplitHorizontal ??= new RelayCommand((o) => DoSplit(SplitDirection.Horizontal),
                    o => this.SelectedItem != null);
            }
        }

        private RelayCommand? _cmdSplitVertical;
        public RelayCommand CmdSplitVertical
        {
            get
            {
                return _cmdSplitVertical ??= new RelayCommand((o) => DoSplit(SplitDirection.Vertical),
                    o => this.SelectedItem != null);
            }
        }


        private readonly object _canCmdClose = new object();
        private RelayCommand? _cmdCloseAll;
        public RelayCommand CmdCloseAll
        {
            get
            {
                return _cmdCloseAll ??= new RelayCommand((o) =>
                {
                    if (this.Items.Count <= 0 || App.ExitingFlag != false) return;
                    lock (_canCmdClose)
                    {
                        if (this.Items.Count <= 0 || App.ExitingFlag != false) return;
                        if (IoC.Get<ConfigurationService>().General.ConfirmBeforeClosingSession == true
                            && false == MessageBoxHelper.Confirm(IoC.Translate("Are you sure you want to close the connection?"), ownerViewModel: this))
                        {
                        }
                        else
                        {
                            IoC.Get<SessionControlService>().CloseProtocolHostAsync(
                                Items
                                    .Select(x => x.Content.ConnectionId).ToArray());
                        }
                    }
                });
            }
        }

        private RelayCommand? _cmdClose;
        public RelayCommand CmdClose
        {
            get
            {
                return _cmdClose ??= new RelayCommand((o) =>
                {
                    lock (_canCmdClose)
                    {
                        if (IoC.Get<ConfigurationService>().General.ConfirmBeforeClosingSession == true
                            && App.ExitingFlag == false
                            && false == MessageBoxHelper.Confirm(IoC.Translate("Are you sure you want to close the connection?"), ownerViewModel: this))
                        {
                        }
                        else
                        {
                            HostBase? host = null;
                            if (o is string connectionId)
                            {
                                host = Items.FirstOrDefault(x => x.Content.ConnectionId == connectionId)?.Content;
                            }
                            else
                            {
                                host = SelectedItem?.Content;
                            }

                            if (host != null)
                            {
                                IoC.Get<SessionControlService>().CloseProtocolHostAsync(host.ConnectionId);
                            }
                        }
                    }
                }, o => this.SelectedItem != null);
            }
        }

        #endregion CMD
    }

    public class InterTabClient : IInterTabClient
    {
        /// <summary>
        /// split tab window
        /// </summary>
        /// <param name="interTabClient"></param>
        /// <param name="partition"></param>
        /// <param name="source"></param>
        /// <returns></returns>
        public INewTabHost<Window> GetNewHost(IInterTabClient interTabClient, object partition, TabablzControl source)
        {
            var v = new TabWindowView();
            IoC.Get<SessionControlService>().AddTab(v);
            return new NewTabHost<Window>(v, v.TabablzControl);
        }

        /// <summary>
        /// merge tab window
        /// </summary>
        /// <param name="tabControl"></param>
        /// <param name="window"></param>
        /// <returns></returns>
        public TabEmptiedResponse TabEmptiedHandler(TabablzControl tabControl, Window window)
        {
            if (window is TabWindowView tab)
            {
                tab.GetViewModel().Items.Clear();
                IoC.Get<SessionControlService>().CleanupProtocolsAndWindows();
            }
            return TabEmptiedResponse.CloseWindowOrLayoutBranch;
        }
    }
}