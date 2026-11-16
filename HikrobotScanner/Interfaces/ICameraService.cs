namespace HikrobotScanner.Interfaces
{
    /// <summary>
    /// Интерфейс сервиса для работы с камерой Hikrobot
    /// </summary>
    public interface ICameraService
    {
        void Initialize(string cameraIp, string userSet);
        void Cleanup();
    }
}
