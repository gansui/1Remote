using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using _1RM.Model.Protocol.Base;
using Shawn.Utils.WpfResources.Theme.Styles;

namespace _1RM.View.Host.ProtocolHosts
{
    public enum SplitDirection
    {
        Horizontal,
        Vertical
    }
    public partial class SplitPaneHost : HostBase
    {
        private readonly List<HostBase> _children = new();
        private HostBase? _activeChild;

        public HostBase? ActiveChild => _activeChild;

        public SplitPaneHost(ProtocolBase protocolServer, HostBase firstChild) : base(protocolServer, false)
        {
            InitializeComponent();
            _children.Add(firstChild);
            _activeChild = firstChild;
            RebuildLayout();
            FocusChild(firstChild);
        }

        public void Split(SplitDirection direction, HostBase newChild)
        {
            _children.Add(newChild);
            RebuildLayout();
            FocusChild(newChild);
        }

        private void RebuildLayout()
        {
            RootGrid.RowDefinitions.Clear();
            RootGrid.ColumnDefinitions.Clear();
            RootGrid.Children.Clear();

            if (_children.Count == 0) return;

            if (_children.Count == 1)
            {
                RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                var child = _children[0];
                child.HorizontalAlignment = HorizontalAlignment.Stretch;
                child.VerticalAlignment = VerticalAlignment.Stretch;
                Grid.SetRow(child, 0);
                Grid.SetColumn(child, 0);
                RootGrid.Children.Add(child);
                return;
            }

            bool isHorizontal = _children.Count == 2;

            if (isHorizontal)
            {
                for (int i = 0; i < _children.Count; i++)
                {
                    RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    if (i < _children.Count - 1)
                        RootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                }
            }
            else
            {
                for (int i = 0; i < _children.Count; i++)
                {
                    RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    if (i < _children.Count - 1)
                        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                }
            }

            for (int i = 0; i < _children.Count; i++)
            {
                var child = _children[i];
                child.HorizontalAlignment = HorizontalAlignment.Stretch;
                child.VerticalAlignment = VerticalAlignment.Stretch;

                if (isHorizontal)
                {
                    int rowIndex = i * 2;
                    Grid.SetRow(child, rowIndex);
                    Grid.SetColumn(child, 0);
                }
                else
                {
                    int colIndex = i * 2;
                    Grid.SetRow(child, 0);
                    Grid.SetColumn(child, colIndex);
                }

                RootGrid.Children.Add(child);
                child.PreviewMouseLeftButtonDown += Child_PreviewMouseLeftButtonDown;

                if (i < _children.Count - 1)
                {
                    var splitter = new GridSplitter
                    {
                        Background = System.Windows.Media.Brushes.LightGray,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        ResizeBehavior = GridResizeBehavior.PreviousAndNext
                    };

                    if (isHorizontal)
                    {
                        splitter.Height = 5;
                        splitter.ResizeDirection = GridResizeDirection.Rows;
                        Grid.SetRow(splitter, i * 2 + 1);
                        Grid.SetColumn(splitter, 0);
                    }
                    else
                    {
                        splitter.Width = 5;
                        splitter.ResizeDirection = GridResizeDirection.Columns;
                        Grid.SetRow(splitter, 0);
                        Grid.SetColumn(splitter, i * 2 + 1);
                    }

                    RootGrid.Children.Add(splitter);
                }
            }
        }

        private void Child_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is HostBase child)
            {
                FocusChild(child);
                e.Handled = false;
            }
        }

        public void FocusChild(HostBase child)
        {
            if (_activeChild != null && _activeChild != child)
                _activeChild.BorderThickness = new Thickness(0);

            _activeChild = child;
            child.BorderThickness = new Thickness(2);
            child.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 215));
            child.FocusOnMe();
        }

        public override void Conn()
        {
            foreach (var child in _children)
            {
                if (child.Status == ProtocolHostStatus.NotInit)
                    child.Conn();
            }
        }

        public override void ReConn()
        {
            foreach (var child in _children)
                child.ReConn();
        }

        public override void Close()
        {
            foreach (var child in _children)
                child.Close();
        }

        public override void FocusOnMe()
        {
            _activeChild?.FocusOnMe();
        }

        public override ProtocolHostType GetProtocolHostType() => ProtocolHostType.Native;

        public override IntPtr GetHostHwnd() => _activeChild?.GetHostHwnd() ?? IntPtr.Zero;

        public override void SetParentWindow(WindowBase? value)
        {
            base.SetParentWindow(value);
            foreach (var child in _children)
                child.SetParentWindow(value);
        }

        public override void ToggleAutoResize(bool isEnable)
        {
            foreach (var child in _children)
                child.ToggleAutoResize(isEnable);
        }

        public override bool CanResizeNow()
        {
            foreach (var child in _children)
                if (!child.CanResizeNow()) return false;
            return true;
        }
    }
}