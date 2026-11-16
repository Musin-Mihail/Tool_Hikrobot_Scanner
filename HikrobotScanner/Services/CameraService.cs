using HikrobotScanner.Interfaces;
using MvCamCtrl.NET;
using System.Net;
using System.Runtime.InteropServices;

namespace HikrobotScanner.Services
{
    /// <summary>
    /// Сервис, инкапсулирующий логику работы с SDK камеры Hikrobot.
    /// </summary>
    public class CameraService : ICameraService
    {
        private readonly IAppLogger _logger;
        private MyCamera? _camera;

        private sealed record DiscoveredCamera(MyCamera.MV_CC_DEVICE_INFO DeviceInfo, string Name, string? IpAddress);

        public CameraService(IAppLogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Инициализация и подключение к камере (по IP из настроек, либо к единственной найденной).
        /// </summary>
        public void Initialize(string cameraIp, string userSet)
        {
            if (_camera != null)
            {
                _logger.Log("Камера уже подключена.");
                return;
            }

            Cleanup();

            MyCamera.MV_CC_DEVICE_INFO_LIST stDevList = new MyCamera.MV_CC_DEVICE_INFO_LIST();
            int nRet = MyCamera.MV_CC_EnumDevices_NET(MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE, ref stDevList);
            if (nRet != MyCamera.MV_OK)
            {
                _logger.Log($"Ошибка: Не удалось перечислить устройства. Код: {nRet:X}");
                return;
            }

            if (stDevList.nDeviceNum == 0)
            {
                _logger.Log("Ошибка: Камеры не найдены.");
                return;
            }

            var devices = GetDiscoveredCameras(stDevList);
            if (devices.Count == 0)
            {
                _logger.Log("Ошибка: Не удалось прочитать параметры обнаруженных камер.");
                return;
            }

            _logger.Log($"Найдено камер: {devices.Count}.");
            foreach (var dev in devices)
            {
                string ipLabel = dev.IpAddress != null ? $", IP: {dev.IpAddress}" : " (USB)";
                _logger.Log($"  - {dev.Name}{ipLabel}");
            }

            var selectedDevice = SelectCamera(devices, cameraIp);
            if (selectedDevice == null)
            {
                return;
            }

            _camera = ConnectAndStartCamera(selectedDevice, userSet);
        }

        /// <summary>
        /// Освобождение ресурсов камеры.
        /// </summary>
        public void Cleanup()
        {
            if (_camera == null) return;

            _logger.Log("Освобождение ресурсов камеры...");
            _camera.MV_CC_StopGrabbing_NET();
            _camera.MV_CC_CloseDevice_NET();
            _camera.MV_CC_DestroyDevice_NET();
            _camera = null;
            _logger.Log("Камера отключена и ресурсы освобождены.");
        }

        private List<DiscoveredCamera> GetDiscoveredCameras(MyCamera.MV_CC_DEVICE_INFO_LIST stDevList)
        {
            var result = new List<DiscoveredCamera>();
            int count = (int)Math.Min(stDevList.nDeviceNum, (uint)stDevList.pDeviceInfo.Length);

            for (int i = 0; i < count; i++)
            {
                if (stDevList.pDeviceInfo[i] == IntPtr.Zero)
                {
                    continue;
                }

                var stDevInfo = Marshal.PtrToStructure<MyCamera.MV_CC_DEVICE_INFO>(stDevList.pDeviceInfo[i]);
                var (name, ipAddress) = GetCameraInfo(stDevInfo, i);
                result.Add(new DiscoveredCamera(stDevInfo, name, ipAddress));
            }

            return result;
        }

        private DiscoveredCamera? SelectCamera(List<DiscoveredCamera> devices, string cameraIp)
        {
            string targetIp = NormalizeIp(cameraIp);

            if (!string.IsNullOrEmpty(targetIp))
            {
                var matched = devices.FirstOrDefault(d =>
                    d.IpAddress != null &&
                    string.Equals(d.IpAddress, targetIp, StringComparison.OrdinalIgnoreCase));

                if (matched != null)
                {
                    _logger.Log($"Выбрана камера по IP {targetIp}: {matched.Name}");
                    return matched;
                }
            }

            if (devices.Count == 1)
            {
                var single = devices[0];
                if (string.IsNullOrEmpty(targetIp))
                {
                    _logger.Log($"Подключение к единственной найденной камере: {single.Name}");
                }
                else
                {
                    string foundIpText = single.IpAddress != null ? $" (IP: {single.IpAddress})" : string.Empty;
                    _logger.Log($"Предупреждение: Камера с IP {targetIp} не найдена, но обнаружена единственная камера {single.Name}{foundIpText}. Подключение к ней.");
                }
                return single;
            }

            if (string.IsNullOrEmpty(targetIp))
            {
                _logger.Log($"Ошибка: Найдено {devices.Count} камер, но не указан IP нужной камеры. Укажите IP камеры в настройках.");
            }
            else
            {
                _logger.Log($"Ошибка: Найдено {devices.Count} камер, но камера с IP '{targetIp}' не обнаружена. Проверьте IP камеры в настройках.");
            }

            return null;
        }

        private MyCamera? ConnectAndStartCamera(DiscoveredCamera selected, string userSet)
        {
            var camera = new MyCamera();
            var stDevInfo = selected.DeviceInfo;

            _logger.Log($"Попытка подключения к камере: {selected.Name}");

            int nRet = camera.MV_CC_CreateDevice_NET(ref stDevInfo);
            if (nRet != MyCamera.MV_OK)
            {
                _logger.Log($"Ошибка: Не удалось создать экземпляр для камеры {selected.Name}. Код: {nRet:X}");
                return null;
            }

            nRet = camera.MV_CC_OpenDevice_NET();
            if (nRet != MyCamera.MV_OK)
            {
                _logger.Log($"Ошибка: Не удалось открыть камеру {selected.Name}. Код: {nRet:X}");
                camera.MV_CC_DestroyDevice_NET();
                return null;
            }

            _logger.Log($"Загрузка настроек из {userSet} для камеры {selected.Name}...");

            nRet = camera.MV_CC_SetEnumValueByString_NET("UserSetSelector", userSet);
            if (nRet != MyCamera.MV_OK)
            {
                _logger.Log($"Ошибка: Не удалось выбрать {userSet} для камеры {selected.Name}. Код: {nRet:X}");
                camera.MV_CC_CloseDevice_NET();
                camera.MV_CC_DestroyDevice_NET();
                return null;
            }

            nRet = camera.MV_CC_SetCommandValue_NET("UserSetLoad");
            if (nRet != MyCamera.MV_OK)
            {
                _logger.Log($"Ошибка: Не удалось загрузить настройки из {userSet} для камеры {selected.Name}. Код: {nRet:X}");
                camera.MV_CC_CloseDevice_NET();
                camera.MV_CC_DestroyDevice_NET();
                return null;
            }
            _logger.Log($"Настройки из {userSet} успешно загружены для камеры {selected.Name}.");

            nRet = camera.MV_CC_StartGrabbing_NET();
            if (nRet != MyCamera.MV_OK)
            {
                _logger.Log($"Ошибка: Не удалось начать захват изображений для камеры {selected.Name}. Код: {nRet:X}");
                camera.MV_CC_CloseDevice_NET();
                camera.MV_CC_DestroyDevice_NET();
                return null;
            }

            _logger.Log($"Захват изображений запущен для камеры {selected.Name}. Успешно подключена через SDK.");
            return camera;
        }

        private (string Name, string? IpAddress) GetCameraInfo(MyCamera.MV_CC_DEVICE_INFO stDevInfo, int index)
        {
            string? cameraName = null;
            string? ipAddress = null;

            try
            {
                if (stDevInfo.nTLayerType == MyCamera.MV_GIGE_DEVICE)
                {
                    byte[] gigeInfoBytes = stDevInfo.SpecialInfo.stGigEInfo;
                    var gigeInfo = (MyCamera.MV_GIGE_DEVICE_INFO)MyCamera.ByteToStruct(gigeInfoBytes, typeof(MyCamera.MV_GIGE_DEVICE_INFO));
                    ipAddress = FormatGigEIp(gigeInfo.nCurrentIp);
                    cameraName = CleanSdkString(gigeInfo.chUserDefinedName);
                    if (string.IsNullOrEmpty(cameraName))
                    {
                        cameraName = CleanSdkString(gigeInfo.chModelName);
                    }
                }
                else if (stDevInfo.nTLayerType == MyCamera.MV_USB_DEVICE)
                {
                    byte[] usbInfoBytes = stDevInfo.SpecialInfo.stUsb3VInfo;
                    var usbInfo = (MyCamera.MV_USB3_DEVICE_INFO)MyCamera.ByteToStruct(usbInfoBytes, typeof(MyCamera.MV_USB3_DEVICE_INFO));
                    cameraName = CleanSdkString(usbInfo.chUserDefinedName);
                    if (string.IsNullOrEmpty(cameraName))
                    {
                        cameraName = CleanSdkString(usbInfo.chModelName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"Не удалось прочитать параметры камеры {index + 1}: {ex.Message}");
            }

            if (string.IsNullOrEmpty(cameraName))
            {
                cameraName = $"Камера {index + 1} (без имени)";
            }

            return (cameraName, ipAddress);
        }

        private static string FormatGigEIp(uint ip)
        {
            uint nIp1 = (ip & 0xFF000000) >> 24;
            uint nIp2 = (ip & 0x00FF0000) >> 16;
            uint nIp3 = (ip & 0x0000FF00) >> 8;
            uint nIp4 = ip & 0x000000FF;
            return $"{nIp1}.{nIp2}.{nIp3}.{nIp4}";
        }

        private static string NormalizeIp(string? rawIp)
        {
            string trimmed = (rawIp ?? string.Empty).Trim();
            if (IPAddress.TryParse(trimmed, out var parsed))
            {
                return parsed.ToString();
            }
            return trimmed;
        }

        private static string? CleanSdkString(string? value)
        {
            return value?.TrimEnd('\0').Trim();
        }
    }
}
