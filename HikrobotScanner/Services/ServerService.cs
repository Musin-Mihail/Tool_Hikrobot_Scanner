using HikrobotScanner.Interfaces;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HikrobotScanner.Services
{
    /// <summary>
    /// Сервис, инкапсулирующий логику TCP-сервера для приема данных.
    /// </summary>
    public class ServerService : IServerService
    {
        private readonly IAppLogger _logger;

        public Action<string>? DataReceivedCallback { get; set; }

        private TcpListener? _tcpServer;
        private CancellationTokenSource? _cancellationTokenSource;

        public ServerService(IAppLogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Запускает TCP-сервер для прослушивания входящих данных от камеры.
        /// </summary>
        public void Start(int port)
        {
            Stop();

            var cts = new CancellationTokenSource();
            _cancellationTokenSource = cts;

            var server = new TcpListener(IPAddress.Any, port);
            _tcpServer = server;

            _ = Task.Run(() => ListenForClients(server, cts));
            _logger.Log($"Сервер запущен. Ожидание данных от камеры на порту {port}...");
        }

        /// <summary>
        /// Останавливает TCP-сервер.
        /// </summary>
        public void Stop()
        {
            var cts = _cancellationTokenSource;
            _cancellationTokenSource = null;

            var server = _tcpServer;
            _tcpServer = null;

            try
            {
                cts?.Cancel();
            }
            catch (ObjectDisposedException) { }

            server?.Stop();
        }

        /// <summary>
        /// Асинхронный метод, который слушает входящие подключения в цикле.
        /// </summary>
        private async Task ListenForClients(TcpListener server, CancellationTokenSource cts)
        {
            var token = cts.Token;
            try
            {
                server.Start();
                while (!token.IsCancellationRequested)
                {
                    var client = await server.AcceptTcpClientAsync(token);
                    _logger.Log("Камера подключилась для отправки данных.");
                    _ = HandleClientTask(client, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted) { }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    _logger.Log($"Критическая ошибка сервера: {ex.Message}");
                }
            }
            finally
            {
                server.Stop();
                cts.Dispose();
                _logger.Log("TCP-сервер остановлен.");
            }
        }

        /// <summary>
        /// Обрабатывает входящие данные от подключенного клиента (камеры) в фоновом потоке.
        /// </summary>
        private async Task HandleClientTask(TcpClient client, CancellationToken token)
        {
            try
            {
                using (var stream = client.GetStream())
                {
                    var buffer = new byte[8192];
                    int bytesRead;
                    while (!token.IsCancellationRequested && (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token)) != 0)
                    {
                        var receivedData = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                        if (!string.IsNullOrEmpty(receivedData))
                        {
                            DataReceivedCallback?.Invoke(receivedData);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException ex) when (ex.InnerException is SocketException)
            {
                _logger.Log("Соединение с камерой было принудительно разорвано.");
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка при чтении данных от камеры: {ex.Message}");
            }
            finally
            {
                client.Close();
                _logger.Log("Камера отключилась.");
            }
        }
    }
}
