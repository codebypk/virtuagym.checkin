using Hardware.Models;
using System.Collections.Generic;

namespace Hardware.Interfaces
{

    public interface IHidDeviceService
    {
        IReadOnlyList<HidDeviceInfo> GetDevices();

        HidDeviceInfo? FindByDevicePath(string devicePath);

        bool DeviceExists(string devicePath);

        bool SendCommand(string devicePath, byte[] command);
    }
}
