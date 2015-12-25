﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.WindowsAPICodePack.Dialogs;
using Xceed.Wpf.AvalonDock.Layout;
using Xceed.Wpf.AvalonDock.Layout.Serialization;

namespace Huoyaoyuan.AdmiralRoom
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow
    {
        public MainWindow()
        {
            ThemeService.ChangeRibbonTheme(Config.Current.Theme, this);

            InitializeComponent();

            //Browser button handler
            GameHost.WebBrowser.Navigating += (_, e) =>
            {
                BrowserAddr.Text = e.Uri.AbsoluteUri;
                BrowserBack.IsEnabled = GameHost.WebBrowser.CanGoBack;
                BrowserForward.IsEnabled = GameHost.WebBrowser.CanGoForward;
            };
            BrowserBack.Click += (_, __) => GameHost.WebBrowser.GoBack();
            BrowserForward.Click += (_, __) => GameHost.WebBrowser.GoForward();
            BrowserGoto.Click += (_, __) =>
            {
                if (!BrowserAddr.Text.Contains(":"))
                    BrowserAddr.Text = "http://" + BrowserAddr.Text;
                try
                {
                    GameHost.WebBrowser.Navigate(BrowserAddr.Text);
                }
                catch { }
            };
            BrowserRefresh.Click += (_, __) => GameHost.WebBrowser.Navigate(GameHost.WebBrowser.Source);
            BrowserBackToGame.Click += (_, __) => GameHost.WebBrowser.Navigate(Properties.Settings.Default.GameUrl);
            
            //Theme handler
            Config.Current.NoDwmChanged += v => this.DontUseDwm = v;
            this.DontUseDwm = Config.Current.NoDWM;
            Themes.SelectionChanged += (s, _) =>
            {
                if (DockMan.FloatingWindows.Any())//Can't DestroyWindow
                    MessageBox.Show("有子窗口处于浮动状态，主题切换必须下次启动程序才能生效。", "", MessageBoxButton.OK, MessageBoxImage.Warning);
                else
                {
                    backstage.IsOpen = false;
                    ThemeService.ChangeRibbonTheme(Config.Current.Theme, this);
                }
            };
            Config.Current.AeroChanged += ThemeService.EnableAeroControls;
            ThemeService.EnableAeroControls(Config.Current.Aero);

            //Proxy button handler
            UpdateProxySetting.Click += (_, __) =>
            {
                Config.Current.Proxy.Host = ProxyHost.Text;
                Config.Current.Proxy.Port = int.Parse(ProxyPort.Text);
                Config.Current.EnableProxy = EnableProxy.IsChecked.Value;
                Config.Current.HTTPSProxy.Host = ProxyHostHTTPS.Text;
                Config.Current.HTTPSProxy.Port = int.Parse(ProxyPortHTTPS.Text);
                Config.Current.EnableProxyHTTPS = EnableProxyHTTPS.IsChecked.Value;
            };
            CancelProxySetting.Click += (_, __) =>
            {
                ProxyHost.Text = Config.Current.Proxy.Host;
                ProxyPort.Text = Config.Current.Proxy.Port.ToString();
                EnableProxy.IsChecked = Config.Current.EnableProxy;
                ProxyHostHTTPS.Text = Config.Current.HTTPSProxy.Host;
                ProxyPortHTTPS.Text = Config.Current.HTTPSProxy.Port.ToString();
                EnableProxyHTTPS.IsChecked = Config.Current.EnableProxyHTTPS;
            };

            //Font handler
            FontLarge.Click += (_, __) => Config.Current.FontSize++;
            FontSmall.Click += (_, __) => Config.Current.FontSize--;

            ScreenShotButton.Click += (_, __) => GameHost.TakeScreenShot(Config.Current.GenerateScreenShotFileName());
            DeleteCacheButton.Click += async (sender, __) =>
            {
                var button = sender as Button;
                button.IsEnabled = false;
                if (MessageBox.Show(Properties.Resources.CleanCache_Alert, "", MessageBoxButton.YesNo, MessageBoxImage.Asterisk) == MessageBoxResult.Yes)
                    if (await WinInetHelper.DeleteInternetCacheAsync())
                        MessageBox.Show(Properties.Resources.CleanCache_Success);
                    else MessageBox.Show(Properties.Resources.CleanCache_Fail);
                button.IsEnabled = true;
            };

            this.Loaded += (_, __) => GameHost.Browser.Navigate(Properties.Settings.Default.GameUrl);
            this.Loaded += (_, __) => Win32Helper.GetRestoreWindowPosition(this);
            this.Closing += (_, __) => Win32Helper.SetRestoreWindowPosition(this);

            layoutserializer = new XmlLayoutSerializer(DockMan);
            DockCommands = new Config.CommandSet
            {
                Save = new DelegateCommand(() => TrySaveLayout()),
                Load = new DelegateCommand(() => TryLoadLayout()),
                SaveAs = new DelegateCommand(() =>
                {
                    using (var filedialog = new CommonSaveFileDialog())
                    {
                        filedialog.InitialDirectory = Environment.CurrentDirectory;
                        filedialog.DefaultFileName = "config.xml";
                        filedialog.Filters.Add(new CommonFileDialogFilter("Xml Files", "*.xml"));
                        filedialog.Filters.Add(new CommonFileDialogFilter("All Files", "*"));
                        if (filedialog.ShowDialog() == CommonFileDialogResult.Ok)
                            TrySaveLayout(filedialog.FileName);
                    }
                }),
                LoadFrom = new DelegateCommand(() =>
                {
                    using (var filedialog = new CommonOpenFileDialog())
                    {
                        filedialog.InitialDirectory = Environment.CurrentDirectory;
                        filedialog.Filters.Add(new CommonFileDialogFilter("Xml Files", "*.xml"));
                        filedialog.Filters.Add(new CommonFileDialogFilter("All Files", "*"));
                        if (filedialog.ShowDialog() == CommonFileDialogResult.Ok)
                            TryLoadLayout(filedialog.FileName);
                    }
                })
            };
        }

        private void MakeViewList(ILayoutElement elem)
        {
            if (elem is LayoutAnchorable)
            {
                ViewList.Add((elem as LayoutAnchorable).ContentId, elem as LayoutAnchorable);
                return;
            }
            if (elem is ILayoutContainer)
            {
                foreach (var child in (elem as ILayoutContainer).Children)
                {
                    MakeViewList(child);
                }
            }
        }

        #region Layout
        private XmlLayoutSerializer layoutserializer;
        private void LoadLayout(object sender, RoutedEventArgs e)
        {
            layoutserializer.LayoutSerializationCallback += (_, args) => args.Content = args.Content;
            TryLoadLayout();
            foreach (var view in DockMan.Layout.Hidden.Where(x => x.PreviousContainerIndex == -1).ToArray())
            {
                DockMan.Layout.Hidden.Remove(view);
            }
            MakeViewList(DockMan.Layout);

            BindingOperations.SetBinding(BrowserDocument, LayoutDocument.TitleProperty, new Binding("Browser")
            {
                Source = ResourceService.Current
            });
        }
        private void SaveLayout(object sender, EventArgs e) => TrySaveLayout();
        private void TryLoadLayout(string path = "layout.xml")
        {
            try
            {
                layoutserializer.Deserialize(path);
            }
            catch { }
        }
        private void TrySaveLayout(string path = "layout.xml")
        {
            try
            {
                layoutserializer.Serialize(path);
            }
            catch { }
        }
        #endregion

        private Dictionary<string, LayoutAnchorable> ViewList = new Dictionary<string, LayoutAnchorable>();
        private void SetToggleBinding(object sender, RoutedEventArgs e)
        {
            Binding ToggleBinding = new Binding();
            Control content = (sender as Control).Tag as Control;
            string ViewName = content.GetType().Name;
            LayoutAnchorable TargetView;
            if (!ViewList.TryGetValue(ViewName, out TargetView))
            {
                TargetView = new LayoutAnchorable();
                ViewList.Add(ViewName, TargetView);
                TargetView.AddToLayout(DockMan, AnchorableShowStrategy.Most);
                //TargetView.Float();
                TargetView.Hide();
            }
            if (TargetView.Content == null)
            {
                TargetView.Content = content;
                if (content.DataContext == null)
                    content.DataContext = Officer.Staff.Current;
                TargetView.ContentId = ViewName;
                TargetView.Title = ViewName;
                TargetView.CanAutoHide = false;
                TargetView.FloatingHeight = content.Height;
                TargetView.FloatingWidth = content.Width;
                //TargetView.FloatingTop = this.ActualHeight / 2;
                //TargetView.FloatingWidth = this.ActualWidth / 2;
                Binding titlebinding = new Binding("Resources.ViewTitle_" + ViewName);
                titlebinding.Source = ResourceService.Current;
                BindingOperations.SetBinding(TargetView, LayoutAnchorable.TitleProperty, titlebinding);
                (sender as Fluent.ToggleButton).SetBinding(Fluent.ToggleButton.HeaderProperty, titlebinding);
            }
            ToggleBinding.Source = TargetView;
            ToggleBinding.Path = new PropertyPath("IsVisible");
            ToggleBinding.Mode = BindingMode.TwoWay;
            (sender as Fluent.ToggleButton).SetBinding(Fluent.ToggleButton.IsCheckedProperty, ToggleBinding);
        }

        private void SetUniqueWindowCommand(object sender, RoutedEventArgs e)
        {
            Button control = sender as Button;
            Type windowtype = control.Tag as Type;
            if (windowtype == null) return;

            control.Content = windowtype.Name;
            Binding titlebinding = new Binding("Resources.ViewTitle_" + windowtype.Name);
            titlebinding.Source = ResourceService.Current;
            control.SetBinding(ContentProperty, titlebinding);

            control.Click += UniqueCommandClick;
        }

        private void UniqueCommandClick(object sender, RoutedEventArgs e)
        {
            Button control = sender as Button;
            Window w;
            if (control.Tag is Type)
            {
                w = Activator.CreateInstance(control.Tag as Type) as Window;
                w.Closed += (_, __) => control.Tag = w.GetType();
                Binding titlebinding = new Binding("Resources.ViewTitle_" + w.GetType().Name);
                titlebinding.Source = ResourceService.Current;
                w.SetBinding(TitleProperty, titlebinding);
                control.Tag = w;
            }
            else w = control.Tag as Window;
            w.Show();
            w.Activate();
        }

        private void SetBrowserZoomFactor(object sender, RoutedPropertyChangedEventArgs<double> e)
            => this.GameHost.ApplyZoomFactor(e.NewValue);

        public Config.CommandSet DockCommands { get; }
    }
}
