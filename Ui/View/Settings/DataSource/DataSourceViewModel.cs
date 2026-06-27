using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using _1RM.Model;
using _1RM.Service;
using _1RM.Service.DataSource;
using _1RM.Service.DataSource.DAO;
using _1RM.Service.DataSource.Model;
using _1RM.Utils;
using _1RM.View.Utils;
using _1RM.View.Utils.MaskAndPop;
using Shawn.Utils;
using Shawn.Utils.Wpf;
using Shawn.Utils.Wpf.FileSystem;

namespace _1RM.View.Settings.DataSource
{
    public class DataSourceViewModel : NotifyPropertyChangedBase
    {
        private readonly ConfigurationService _configurationService;
        private readonly DataSourceService _dataSourceService;

        public DataSourceViewModel()
        {
            _configurationService = IoC.Get<ConfigurationService>();
            _dataSourceService = IoC.Get<DataSourceService>();

            LocalSource = _configurationService.LocalDataSource;
            _sourceConfigs.Add(_configurationService.LocalDataSource);

            foreach (var config in _configurationService.AdditionalDataSource)
            {
                _sourceConfigs.Add(config);
            }

            CurrentMode = File.Exists(AppPathHelper.FORCE_INTO_APPDATA_MODE) ? "AppData模式" : "便携模式";
        }

        private string _currentMode = "";
        public string CurrentMode
        {
            get => _currentMode;
            set => SetAndNotifyIfChanged(ref _currentMode, value);
        }

        public bool IsPortableMode => CurrentMode == "便携模式";

        private RelayCommand? _cmdSwitchMode;
        public RelayCommand CmdSwitchMode
        {
            get
            {
                return _cmdSwitchMode ??= new RelayCommand((o) =>
                {
                    var targetMode = IsPortableMode ? "AppData" : "便携";
                    var targetModeName = IsPortableMode ? "AppData模式" : "便携模式";

                    if (false == MessageBoxHelper.Confirm(
                        $"确定要切换到{targetModeName}吗？\n\n切换后将复制当前配置数据到目标目录，需要重启软件生效。"))
                        return;

                    try
                    {
                        SwitchMode(!IsPortableMode);
                        CurrentMode = targetModeName;
                        MessageBoxHelper.Info("模式已切换，请重启软件以生效。");
                    }
                    catch (Exception ex)
                    {
                        MessageBoxHelper.ErrorAlert($"切换失败：{ex.Message}");
                    }
                });
            }
        }

        private void SwitchMode(bool toAppData)
        {
            var portablePaths = new AppPathHelper(Environment.CurrentDirectory, Environment.CurrentDirectory);
            var appDataPaths = new AppPathHelper(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Assert.APP_NAME),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Assert.APP_NAME));

            var sourcePaths = toAppData ? portablePaths : appDataPaths;
            var targetPaths = toAppData ? appDataPaths : portablePaths;

            // 确保目标目录存在
            AppPathHelper.CreateDirIfNotExist(targetPaths.BaseDirPath, false);
            AppPathHelper.CreateDirIfNotExist(targetPaths.BaseDirPathForLocality, false);

            // 复制配置文件
            CopyFileIfNotExist(sourcePaths.ProfileJsonPath, targetPaths.ProfileJsonPath);
            CopyFileIfNotExist(sourcePaths.ProfileAdditionalDataSourceJsonPath, targetPaths.ProfileAdditionalDataSourceJsonPath);

            // 复制数据库文件
            var sourceDbPath = GetDbPath(sourcePaths);
            var targetDbPath = GetDbPath(targetPaths);
            if (!string.IsNullOrEmpty(sourceDbPath) && File.Exists(sourceDbPath))
            {
                CopyFileIfNotExist(sourceDbPath, targetDbPath);
            }

            // 复制PuTTY配置目录
            CopyDirectoryIfExists(sourcePaths.PuttyDirPath, targetPaths.PuttyDirPath);

            // 更新标志文件
            if (File.Exists(AppPathHelper.FORCE_INTO_PORTABLE_MODE))
                File.Delete(AppPathHelper.FORCE_INTO_PORTABLE_MODE);
            if (File.Exists(AppPathHelper.FORCE_INTO_APPDATA_MODE))
                File.Delete(AppPathHelper.FORCE_INTO_APPDATA_MODE);

            if (toAppData)
                File.WriteAllText(AppPathHelper.FORCE_INTO_APPDATA_MODE, $"rename to '{AppPathHelper.FORCE_INTO_PORTABLE_MODE}' can make it portable");
            else
                File.WriteAllText(AppPathHelper.FORCE_INTO_PORTABLE_MODE, $"rename to '{AppPathHelper.FORCE_INTO_APPDATA_MODE}' can save to AppData");
        }

        private static string GetDbPath(AppPathHelper paths)
        {
            if (File.Exists(paths.ProfileJsonPath))
            {
                try
                {
                    var cfg = Configuration.Load(paths.ProfileJsonPath);
                    if (cfg != null && !string.IsNullOrEmpty(cfg.SqliteDatabasePath))
                    {
                        var dbPath = cfg.SqliteDatabasePath;
                        if (!Path.IsPathRooted(dbPath))
                            dbPath = Path.Combine(paths.BaseDirPath, dbPath.TrimStart('.', '/'));
                        return dbPath;
                    }
                }
                catch { }
            }
            return paths.SqliteDbDefaultPath;
        }

        private static void CopyFileIfNotExist(string source, string target)
        {
            if (string.IsNullOrEmpty(source) || !File.Exists(source)) return;
            if (File.Exists(target)) return;

            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.Copy(source, target);
        }

        private static void CopyDirectoryIfExists(string source, string target)
        {
            if (string.IsNullOrEmpty(source) || !Directory.Exists(source)) return;

            if (!Directory.Exists(target))
                Directory.CreateDirectory(target);

            foreach (var file in Directory.GetFiles(source))
            {
                var targetFile = Path.Combine(target, Path.GetFileName(file));
                if (!File.Exists(targetFile))
                    File.Copy(file, targetFile);
            }

            foreach (var dir in Directory.GetDirectories(source))
            {
                var targetDir = Path.Combine(target, Path.GetFileName(dir));
                CopyDirectoryIfExists(dir, targetDir);
            }
        }

        public int DatabaseCheckPeriod
        {
            get => _configurationService.DatabaseCheckPeriod;
            set
            {
                if (value != _configurationService.DatabaseCheckPeriod)
                {
                    _configurationService.DatabaseCheckPeriod = value;
                    RaisePropertyChanged();
                }
            }
        }


        public SqliteSource LocalSource { get; }

        private ObservableCollection<DataSourceBase> _sourceConfigs = new ObservableCollection<DataSourceBase>();
        public ObservableCollection<DataSourceBase> SourceConfigs
        {
            get => _sourceConfigs;
            set => SetAndNotifyIfChanged(ref _sourceConfigs, value);
        }



        private RelayCommand? _cmdAdd;
        public RelayCommand CmdAdd
        {
            get
            {
                return _cmdAdd ??= new RelayCommand((o) =>
                {
                    if (o is not string type)
                    {
                        return;
                    }

                    DataSourceBase? dataSource = null;
                    switch (type.ToLower())
                    {
                        case "sqlite":
                            {
                                var vm = new SqliteSettingViewModel(this);
                                if (MaskLayerController.ShowDialogWithMask(vm, doNotHideMaskIfReturnTrue: true, ownerViewModel: IoC.Get<MainWindowViewModel>()) != true)
                                    return;
                                dataSource = vm.New;
                                break;
                            }
                        case "mysql":
                            {
                                var vm = new MysqlSettingViewModel(this);
                                if (MaskLayerController.ShowDialogWithMask(vm, doNotHideMaskIfReturnTrue: true, ownerViewModel: IoC.Get<MainWindowViewModel>()) != true)
                                    return;
                                dataSource = vm.New;
                                break;
                            }
                        case "postgresql":
                            {
                                var vm = new PgsqlSettingViewModel(this);
                                if (MaskLayerController.ShowDialogWithMask(vm, doNotHideMaskIfReturnTrue: true, ownerViewModel: IoC.Get<MainWindowViewModel>()) != true)
                                    return;
                                dataSource = vm.New;
                                break;
                            }
                        default:
                            throw new ArgumentOutOfRangeException($"{type} is not a vaild type");
                    }

                    SourceConfigs.Add(dataSource);
                    _configurationService.AdditionalDataSource.Add(dataSource);
                    _configurationService.Save();
                    Task.Factory.StartNew(() =>
                    {
                        try
                        {
                            var ret = _dataSourceService.AddOrUpdateDataSource(dataSource);
                            if (ret.Status != EnumDatabaseStatus.OK)
                            {
                                MessageBoxHelper.ErrorAlert(ret.GetErrorMessage);
                            }
                        }
                        finally
                        {
                            MaskLayerController.HideMask(IoC.Get<MainWindowViewModel>());
                        }
                    });
                }, _ =>
                        IoPermissionHelper.HasWritePermissionOnFile(AppPathHelper.Instance.ProfileAdditionalDataSourceJsonPath)
                    );
            }
        }


        private RelayCommand? _cmdEdit;
        public RelayCommand CmdEdit
        {
            get
            {
                return _cmdEdit ??= new RelayCommand((o) =>
                {
                    if (o is not DataSourceBase dataSource) return;

                    PopupBase? vm = dataSource switch
                    {
                        SqliteSource sqliteConfig => new SqliteSettingViewModel(this, sqliteConfig),
                        MysqlSource mysqlConfig => new MysqlSettingViewModel(this, mysqlConfig),
                        PgsqlSource pgsqlConfig => new PgsqlSettingViewModel(this, pgsqlConfig),
                        _ => throw new NotSupportedException($"{o?.GetType()} is not a supported type")
                    };

                    if (MaskLayerController.ShowDialogWithMask(vm, doNotHideMaskIfReturnTrue: true) != true)
                        return;

                    _configurationService.Save();
                    Task.Factory.StartNew(() =>
                    {
                        try
                        {
                            var ret = _dataSourceService.AddOrUpdateDataSource(dataSource);
                            if (ret.Status != EnumDatabaseStatus.OK)
                            {
                                MessageBoxHelper.ErrorAlert(ret.GetErrorMessage);
                            }
                        }
                        finally
                        {
                            MaskLayerController.HideMask(IoC.Get<MainWindowViewModel>());
                        }
                    });
                });
            }
        }
        

        private RelayCommand? _cmdDelete;
        public RelayCommand CmdDelete
        {
            get
            {
                return _cmdDelete ??= new RelayCommand((o) =>
                {
                    if (o is DataSourceBase configBase && configBase != LocalSource)
                    {
                        if (true == MessageBoxHelper.Confirm(IoC.Translate("confirm_to_delete_selected")))
                        {
                            if (_configurationService.AdditionalDataSource.Contains(configBase))
                            {
                                _configurationService.AdditionalDataSource.Remove(configBase);
                                _configurationService.Save();
                            }
                            SourceConfigs.Remove(configBase);
                            Task.Factory.StartNew(() =>
                            {
                                _dataSourceService.RemoveDataSource(configBase.DataSourceName);
                            });
                        }
                    }
                }, _ =>
                    IoPermissionHelper.HasWritePermissionOnFile(AppPathHelper.Instance.ProfileAdditionalDataSourceJsonPath));
            }
        }
        

        private RelayCommand? _cmdRefreshDataSource;
        public RelayCommand CmdRefreshDataSource
        {
            get
            {
                return _cmdRefreshDataSource ??= new RelayCommand((o) =>
                {
                    if (o is DataSourceBase dataSource)
                    {
                        MaskLayerController.ShowProcessingRing();
                        if (dataSource.Status != EnumDatabaseStatus.OK)
                        {
                            dataSource.ReconnectTime = DateTime.MinValue;
                        }
                        else
                        {
                            IoC.Get<GlobalData>().CheckUpdateTime = DateTime.MinValue;
                        }

                        Task.Factory.StartNew(() =>
                        {
                            try
                            {
                                var ret = _dataSourceService.AddOrUpdateDataSource(dataSource);
                                if (ret.Status != EnumDatabaseStatus.OK)
                                {
                                    MessageBoxHelper.ErrorAlert(ret.GetErrorMessage);
                                }
                            }
                            finally
                            {
                                MaskLayerController.HideMask();
                            }
                        });
                    }
                });
            }
        }

    }
}
