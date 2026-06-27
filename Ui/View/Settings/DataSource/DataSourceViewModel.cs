using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using _1RM.Model;
using _1RM.Model.Protocol.Base;
using _1RM.Service;
using _1RM.Service.DataSource;
using _1RM.Service.DataSource.DAO;
using _1RM.Service.DataSource.Model;
using _1RM.Utils;
using _1RM.View.ServerView;
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

            // 检测目标目录是否已有数据
            bool targetHasData = File.Exists(targetPaths.ProfileJsonPath);
            if (targetHasData)
            {
                if (false == MessageBoxHelper.Confirm(
                    "目标目录已存在配置数据，是否用当前数据覆盖？\n\n选择[取消]将只切换模式，保留目标目录的现有数据。"))
                {
                    // 只更新标志文件，不复制数据
                    UpdateFlagFile(toAppData);
                    return;
                }
            }

            // 确保目标目录存在
            AppPathHelper.CreateDirIfNotExist(targetPaths.BaseDirPath, false);
            AppPathHelper.CreateDirIfNotExist(targetPaths.BaseDirPathForLocality, false);

            // 复制配置文件
            CopyFileIfNotExist(sourcePaths.ProfileJsonPath, targetPaths.ProfileJsonPath, overwrite: true);
            CopyFileIfNotExist(sourcePaths.ProfileAdditionalDataSourceJsonPath, targetPaths.ProfileAdditionalDataSourceJsonPath, overwrite: true);

            // 复制数据库文件
            var sourceDbPath = GetDbPath(sourcePaths);
            var targetDbPath = GetDbPath(targetPaths);
            if (!string.IsNullOrEmpty(sourceDbPath) && File.Exists(sourceDbPath))
            {
                CopyFileIfNotExist(sourceDbPath, targetDbPath, overwrite: true);
            }

            // 复制PuTTY配置目录
            CopyDirectoryIfExists(sourcePaths.PuttyDirPath, targetPaths.PuttyDirPath, overwrite: true);

            // 更新标志文件
            UpdateFlagFile(toAppData);
        }

        private static void UpdateFlagFile(bool toAppData)
        {
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

        private static void CopyFileIfNotExist(string source, string target, bool overwrite = false)
        {
            if (string.IsNullOrEmpty(source) || !File.Exists(source)) return;
            if (!overwrite && File.Exists(target)) return;

            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.Copy(source, target, overwrite);
        }

        private static void CopyDirectoryIfExists(string source, string target, bool overwrite = false)
        {
            if (string.IsNullOrEmpty(source) || !Directory.Exists(source)) return;

            if (!Directory.Exists(target))
                Directory.CreateDirectory(target);

            foreach (var file in Directory.GetFiles(source))
            {
                var targetFile = Path.Combine(target, Path.GetFileName(file));
                if (overwrite || !File.Exists(targetFile))
                    File.Copy(file, targetFile, overwrite);
            }

            foreach (var dir in Directory.GetDirectories(source))
            {
                var targetDir = Path.Combine(target, Path.GetFileName(dir));
                CopyDirectoryIfExists(dir, targetDir, overwrite);
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


        private RelayCommand? _cmdImportFromJson;
        public RelayCommand CmdImportFromJson
        {
            get
            {
                return _cmdImportFromJson ??= new RelayCommand((o) =>
                {
                    var path = SelectFileHelper.OpenFile("json|*.json");
                    if (string.IsNullOrEmpty(path)) return;

                    MaskLayerController.ShowProcessingRing("正在导入...", IoC.Get<MainWindowViewModel>());
                    Task.Factory.StartNew(() =>
                    {
                        try
                        {
                            var list = new List<ProtocolBase>();
                            var deserializeObject = JsonConvert.DeserializeObject<List<object>>(File.ReadAllText(path, System.Text.Encoding.UTF8)) ?? new List<object>();
                            foreach (var server in deserializeObject.Select(json => ItemCreateHelper.CreateFromJsonString(json.ToString() ?? "")))
                            {
                                if (server == null) continue;
                                server.Id = string.Empty;
                                server.DecryptToConnectLevel();
                                list.Add(server);
                            }

                            if (list.Count == 0)
                            {
                                MessageBoxHelper.Info("未找到可导入的服务器配置。");
                                return;
                            }

                            // 删除旧数据库文件，重新创建
                            var dbPath = LocalSource.Path;
                            if (File.Exists(dbPath))
                            {
                                LocalSource.Database_CloseConnection();
                                File.Delete(dbPath);
                                SimpleLogHelper.Debug($"Deleted old database: {dbPath}");
                            }

                            var ret = LocalSource.Database_InsertServer(list);
                            if (ret.IsSuccess)
                            {
                                IoC.Get<GlobalData>().ReloadAll(true);
                                MessageBoxHelper.Info($"导入完成，共添加 {list.Count} 个服务器配置。\n\n请重启软件以加载新数据。");
                            }
                            else
                            {
                                MessageBoxHelper.ErrorAlert($"导入失败：{ret.ErrorInfo}");
                            }
                        }
                        catch (Exception e)
                        {
                            SimpleLogHelper.Warning(e);
                            MessageBoxHelper.ErrorAlert("导入失败：数据格式错误 - " + e.Message);
                        }
                        finally
                        {
                            MaskLayerController.HideMask(IoC.Get<MainWindowViewModel>());
                        }
                    });
                });
            }
        }

    }
}
