using HikrobotScanner.Interfaces;
using System.IO;

namespace HikrobotScanner.Services
{
    public class DataService : IDataService
    {
        private readonly IAppLogger _logger;
        private const string SaveDirectory = "codes";
        private string _currentSessionFilePath;

        public DataService(IAppLogger logger)
        {
            _logger = logger;
        }

        public void StartNewSession()
        {
            try
            {
                var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SaveDirectory);
                Directory.CreateDirectory(directory);

                var fileName = $"Session_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                _currentSessionFilePath = Path.Combine(directory, fileName);

                File.WriteAllText(_currentSessionFilePath, string.Empty);
                _logger.Log($"Создан новый файл для записи сессии: {_currentSessionFilePath}");
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка создания файла сессии: {ex.Message}");
            }
        }

        public void AppendData(string combinedData)
        {
            if (string.IsNullOrEmpty(_currentSessionFilePath))
            {
                _logger.Log("Ошибка: Попытка сохранить данные, но файл сессии не инициализирован.");
                return;
            }

            try
            {
                File.AppendAllText(_currentSessionFilePath, combinedData + Environment.NewLine);
                _logger.Log($"Данные успешно добавлены в текущий файл сессии.");
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка добавления данных в файл: {ex.Message}");
            }
        }

        public void SaveReceivedCodesToFile(List<string> receivedCodes)
        {
            if (receivedCodes.Count == 0)
            {
                _logger.Log("Нет полученных кодов для сохранения.");
                return;
            }

            try
            {
                var directory = AppDomain.CurrentDomain.BaseDirectory;
                var fileName = $"ReceivedCodes_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                var filePath = Path.Combine(directory, fileName);

                File.WriteAllLines(filePath, receivedCodes);
                _logger.Log($"Сохранено {receivedCodes.Count} кодов в файл: {filePath}");
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка сохранения файла: {ex.Message}");
            }
        }
    }
}
