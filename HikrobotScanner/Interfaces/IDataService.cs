namespace HikrobotScanner.Interfaces
{
    public interface IDataService
    {
        void SaveReceivedCodesToFile(List<string> receivedCodes);

        /// <summary>
        /// Создает новый файл для записи кодов текущей сессии.
        /// </summary>
        void StartNewSession();

        /// <summary>
        /// Добавляет строку с данными в конец файла текущей сессии.
        /// </summary>
        void AppendData(string combinedData);
    }
}