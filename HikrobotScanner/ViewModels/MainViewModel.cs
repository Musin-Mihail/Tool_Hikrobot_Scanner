using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HikrobotScanner.Interfaces;
using HikrobotScanner.Properties;
using System.Windows;

namespace HikrobotScanner.ViewModels
{
    /// <summary>
    /// Основная ViewModel. Теперь использует CommunityToolkit.Mvvm (ObservableObject)
    /// и получает все зависимости (сервисы) через конструктор (DI).
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        #region Сервисы (получены через DI)
        private readonly ICameraService _cameraService;
        private readonly IServerService _serverService;
        private readonly IBarcodeService _barcodeService;
        private readonly IDataService _dataService;
        private readonly ISettingsService _settingsService;
        private readonly IAppLogger _logger;
        private readonly IDispatcherService _dispatcher;
        #endregion

        #region Свойства состояния (State)

        [ObservableProperty]
        private Settings _settings = null!;

        [ObservableProperty]
        private string _logText = string.Empty;

        [ObservableProperty]
        private string _statusText = "Сервер не запущен.";

        [ObservableProperty]
        private bool _isErrorState;

        [ObservableProperty]
        private int _barcodeQuantity = 10;

        private long _barcodeCounter;
        public long BarcodeCounter
        {
            get => _barcodeCounter;
            set
            {
                if (SetProperty(ref _barcodeCounter, value))
                {
                    OnPropertyChanged(nameof(BarcodeCounterDisplay));
                }
            }
        }

        public string BarcodeCounterDisplay => _barcodeCounter.ToString("D7");

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartServerCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopServerCommand))]
        private bool _isServerRunning;

        public bool IsServerStopped => !IsServerRunning;

        private readonly List<string> _receivedCodes = new List<string>();

        #endregion

        #region Конструктор (Внедрение зависимостей)
        public MainViewModel(
            ICameraService cameraService,
            IServerService serverService,
            IBarcodeService barcodeService,
            IDataService dataService,
            ISettingsService settingsService,
            IAppLogger logger,
            IDispatcherService dispatcher)
        {
            _cameraService = cameraService;
            _serverService = serverService;
            _barcodeService = barcodeService;
            _dataService = dataService;
            _settingsService = settingsService;
            _logger = logger;
            _dispatcher = dispatcher;

            _serverService.DataReceivedCallback = OnDataReceivedFromService;
            _logger.LogUpdated += (logText) => _dispatcher.InvokeOnUIThread(() => LogText = logText);

            LoadApplicationState();

        }
        #endregion

        #region Команды (Commands)

        [RelayCommand(CanExecute = nameof(CanStartServer))]
        private void StartServer()
        {
            _logger.Log("Запуск сервера и инициализация камеры...");

            int userSetNumber = Math.Clamp(Settings.UserSetIndex, 0, 2) + 1;
            string userSet = $"UserSet{userSetNumber}";

            if (!int.TryParse(Settings.ListenPort, out int port) || port < 1 || port > 65535)
            {
                string errorMsg = $"Ошибка: Неверный формат порта в настройках ('{Settings.ListenPort}'). Сервер не запущен.";
                _logger.Log(errorMsg);
                ShowError("Порт для прослушивания указан неверно. Проверьте настройки.", "Ошибка настроек");
                return;
            }

            _dataService.StartNewSession();

            _cameraService.Initialize(Settings.CameraIp, userSet);
            _serverService.Start(port);

            IsServerRunning = true;
            StatusText = $"Сервер слушает порт {port}";
        }
        private bool CanStartServer() => IsServerStopped;

        [RelayCommand(CanExecute = nameof(CanStopServer))]
        private void StopServer()
        {
            _logger.Log("Остановка сервера...");
            ShutdownServer();
        }
        private bool CanStopServer() => IsServerRunning;

        [RelayCommand]
        private void ClearDatabase()
        {
            _receivedCodes.Clear();
            _logger.Log("База данных (в памяти) очищена.");
        }

        [RelayCommand]
        private void GenerateBarcodes()
        {
            if (BarcodeQuantity <= 0)
            {
                ShowError("Введите корректное количество кодов (положительное число).", "Ошибка ввода");
                return;
            }

            _logger.Log($"Генерация {BarcodeQuantity} штрих-кодов...");
            try
            {
                var (success, nextCounter) = _barcodeService.GenerateAndPrintBarcodes(BarcodeCounter, BarcodeQuantity);

                if (success)
                {
                    BarcodeCounter = nextCounter;
                    _barcodeService.SaveCounter(BarcodeCounter);
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"Критическая ошибка печати: {ex.Message}");
                ShowError($"Не удалось выполнить печать. Убедитесь, что принтер подключен и готов.\n\nОшибка: {ex.Message}", "Ошибка печати");
            }
        }

        #endregion

        #region Обработка данных

        /// <summary>
        /// Этот метод вызывается из ServerService (из другого потока)
        /// </summary>
        private void OnDataReceivedFromService(string data)
        {
            _dispatcher.InvokeOnUIThread(() =>
            {
                _logger.Log("Получены данные с камеры.");
                _logger.Log(data);

                ProcessData(data);
            });
        }

        /// <summary>
        /// Вся бизнес-логика проверки кодов
        /// </summary>
        private void ProcessData(string data)
        {
            var allParts = data.Split('#', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var linearCodes = new List<string>();
            var qrCodes = new List<string>();

            foreach (var part in allParts)
            {
                if (part.Length == 20 && long.TryParse(part, out _))
                {
                    linearCodes.Add(part);
                }
                else
                {
                    qrCodes.Add(part);
                }
            }

            var uniqueLinearCodes = linearCodes.Distinct().ToList();
            if (uniqueLinearCodes.Count == 0)
            {
                _logger.Log("Ошибка: Линейный штрих-код не найден.");
                ShowError("Линейный штрих-код не найден.", "Ошибка сканирования");
                return;
            }

            if (uniqueLinearCodes.Count > 1)
            {
                _logger.Log("Ошибка: Найдено несколько разных линейных штрих-кодов.");
                ShowError("Найдено несколько разных линейных штрих-кодов.", "Ошибка сканирования");
                return;
            }

            var finalLinearCode = uniqueLinearCodes.Single();
            if (_receivedCodes.Any(c => c.StartsWith(finalLinearCode + "#")))
            {
                _logger.Log($"Штрих-код {finalLinearCode} уже сохранен.");
                return;
            }

            int expectedPartsCount = Settings.ExpectedPartsIndex == 0 ? 6 : 8;

            var uniqueQrCodes = qrCodes.Distinct().ToList();
            if (uniqueQrCodes.Count < expectedPartsCount)
            {
                _logger.Log($"Ошибка: Количество QR-кодов ({uniqueQrCodes.Count}) меньше ожидаемого ({expectedPartsCount}).");
                ShowError($"Недостаточно QR-кодов (Найдено: {uniqueQrCodes.Count}, Ожидалось: {expectedPartsCount}).", "Ошибка сканирования");
                return;
            }

            var codesToSave = new List<string> { finalLinearCode };
            codesToSave.AddRange(uniqueQrCodes);
            var dataToSave = string.Join("#", codesToSave);

            _receivedCodes.Add(dataToSave);
            _dataService.AppendData(dataToSave);
            _logger.Log($"Код успешно обработан и сохранен: {finalLinearCode}");
        }

        #endregion

        #region Вспомогательные методы (Ошибки)

        /// <summary>
        /// Отображение окна с ошибкой, со звуком и миганием.
        /// </summary>
        private void ShowError(string message, string title)
        {
            _dispatcher.InvokeOnUIThread(() =>
            {
                Task.Run(() =>
                {
                    try
                    {
                        Console.Beep(1500, 500);
                        Console.Beep(1500, 500);
                        Console.Beep(1500, 500);
                    }
                    catch (Exception)
                    {
                        // Игнорируем ошибку, если Beep не поддерживается 
                        // (например, в некоторых средах)
                    }
                });
                IsErrorState = true;
                Task.Delay(500).ContinueWith(_ =>
                {
                    _dispatcher.InvokeOnUIThread(() => IsErrorState = false);
                });
                MessageBox.Show(Application.Current.MainWindow,
                    message,
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });
        }

        #endregion

        #region Управление жизненным циклом

        private void LoadApplicationState()
        {
            Settings = _settingsService.LoadSettings();
            _logger.Log("Настройки загружены.");
            BarcodeCounter = _barcodeService.LoadCounter();
            _logger.Log($"Счетчик штрих-кодов загружен: {BarcodeCounter}");
        }

        private void ShutdownServer()
        {
            _serverService.Stop();
            _cameraService.Cleanup();

            _receivedCodes.Clear();

            IsServerRunning = false;
            StatusText = "Сервер остановлен.";
            _logger.Log("Сервер остановлен, ресурсы освобождены.");
        }

        /// <summary>
        /// Вызывается из Code-Behind при закрытии окна.
        /// </summary>
        public void OnWindowClosing()
        {
            _logger.Log("Приложение закрывается...");
            if (IsServerRunning)
            {
                ShutdownServer();
            }
            _settingsService.SaveSettings(Settings);
            _barcodeService.SaveCounter(BarcodeCounter);
            _logger.Log("Настройки и счетчик сохранены. Выход.");
        }

        #endregion
    }
}
