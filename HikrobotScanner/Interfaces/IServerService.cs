namespace HikrobotScanner.Interfaces
{
    /// <summary>
    /// Интерфейс TCP-сервера для приема данных
    /// </summary>
    public interface IServerService
    {
        /// <summary>
        /// Колбэк, вызываемый при получении данных.
        /// </summary>
        Action<string>? DataReceivedCallback { get; set; }

        void Start(int port);
        void Stop();
    }
}
