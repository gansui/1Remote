using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using _1RM.View.Host.ProtocolHosts;

namespace _1RM.View.Host
{
    public enum SplitDirection
    {
        Horizontal,
        Vertical
    }

    public partial class SplitPaneView : UserControl
    {
        private readonly List<HostBase> _hosts = new();
        private readonly List<UIElement> _splitters = new();
        private SplitDirection _currentDirection = SplitDirection.Horizontal;

        public SplitPaneView()
        {
            InitializeComponent();
        }

        public HostBase? ActiveHost { get; private set; }

        public void AddHost(HostBase host, SplitDirection direction)
        {
            if (host == null) return;

            _hosts.Add(host);
            _currentDirection = direction;

            RebuildLayout();
            SetActiveHost(host);
        }

        public void SetActiveHost(HostBase host)
        {
            if (ActiveHost != null)
            {
                ActiveHost.BorderThickness = new Thickness(0);
            }

            ActiveHost = host;

            if (ActiveHost != null)
            {
                ActiveHost.BorderThickness = new Thickness(2);
                ActiveHost.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 120, 215));
                ActiveHost.FocusOnMe();
            }
        }

        public HostBase? GetActiveHost()
        {
            return ActiveHost;
        }

        public List<HostBase> GetAllHosts()
        {
            return new List<HostBase>(_hosts);
        }

        private void RebuildLayout()
        {
            RootGrid.RowDefinitions.Clear();
            RootGrid.ColumnDefinitions.Clear();
            RootGrid.Children.Clear();
            _splitters.Clear();

            if (_hosts.Count == 0) return;

            if (_hosts.Count == 1)
            {
                RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                var host = _hosts[0];
                host.HorizontalAlignment = HorizontalAlignment.Stretch;
                host.VerticalAlignment = VerticalAlignment.Stretch;
                Grid.SetRow(host, 0);
                Grid.SetColumn(host, 0);
                RootGrid.Children.Add(host);
                return;
            }

            if (_currentDirection == SplitDirection.Horizontal)
            {
                for (int i = 0; i < _hosts.Count; i++)
                {
                    RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    if (i < _hosts.Count - 1)
                    {
                        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
                    }
                }

                for (int i = 0; i < _hosts.Count; i++)
                {
                    var host = _hosts[i];
                    int rowIndex = i * 2;
                    host.HorizontalAlignment = HorizontalAlignment.Stretch;
                    host.VerticalAlignment = VerticalAlignment.Stretch;
                    Grid.SetRow(host, rowIndex);
                    Grid.SetColumn(host, 0);
                    RootGrid.Children.Add(host);

                    host.PreviewMouseLeftButtonDown += Host_PreviewMouseLeftButtonDown;

                    if (i < _hosts.Count - 1)
                    {
                        var splitter = new GridSplitter
                        {
                            Height = 5,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Center,
                            Background = Brushes.LightGray,
                            ResizeDirection = GridResizeDirection.Rows,
                            ResizeBehavior = GridResizeBehavior.PreviousAndNext
                        };
                        Grid.SetRow(splitter, rowIndex + 1);
                        Grid.SetColumn(splitter, 0);
                        RootGrid.Children.Add(splitter);
                        _splitters.Add(splitter);
                    }
                }
            }
            else
            {
                for (int i = 0; i < _hosts.Count; i++)
                {
                    RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    if (i < _hosts.Count - 1)
                    {
                        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
                    }
                }

                for (int i = 0; i < _hosts.Count; i++)
                {
                    var host = _hosts[i];
                    int columnIndex = i * 2;
                    host.HorizontalAlignment = HorizontalAlignment.Stretch;
                    host.VerticalAlignment = VerticalAlignment.Stretch;
                    Grid.SetRow(host, 0);
                    Grid.SetColumn(host, columnIndex);
                    RootGrid.Children.Add(host);

                    host.PreviewMouseLeftButtonDown += Host_PreviewMouseLeftButtonDown;

                    if (i < _hosts.Count - 1)
                    {
                        var splitter = new GridSplitter
                        {
                            Width = 5,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Stretch,
                            Background = Brushes.LightGray,
                            ResizeDirection = GridResizeDirection.Columns,
                            ResizeBehavior = GridResizeBehavior.PreviousAndNext
                        };
                        Grid.SetRow(splitter, 0);
                        Grid.SetColumn(splitter, columnIndex + 1);
                        RootGrid.Children.Add(splitter);
                        _splitters.Add(splitter);
                    }
                }
            }
        }

        private void Host_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is HostBase host)
            {
                SetActiveHost(host);
                e.Handled = false;
            }
        }

        public void RemoveHost(HostBase host)
        {
            if (_hosts.Contains(host))
            {
                _hosts.Remove(host);
                RebuildLayout();

                if (ActiveHost == host)
                {
                    ActiveHost = _hosts.Count > 0 ? _hosts[0] : null;
                    if (ActiveHost != null)
                    {
                        ActiveHost.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 120, 215));
                    }
                }
            }
        }
    }
}