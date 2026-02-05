

using SharpPcap;
using SharpPcap.LibPcap;
using SwitchToSimulatorAdaptor;
using SwitchToSimulatorAdaptor.EdenRoom;
using SwitchToSimulatorAdaptor.Utils;

void PrintDevicesList()
{
    //获取所有网络设备
    var devices = CaptureDeviceList.Instance;

    Console.WriteLine($"找到 {devices.Count} 个网络接口 \n");

    for (int i = 0; i < devices.Count; i++)
    {
        var device = devices[i];

        Console.WriteLine($"设备[{i}]：{device.Name}");
        Console.WriteLine($"     描述：{device.Description}");

        if (device is LibPcapLiveDevice libPcapDevice)
        {
            Console.WriteLine($"     友好名称：{libPcapDevice.Interface?.FriendlyName}");

            //打印 Ip 地址
            foreach (var addr in libPcapDevice.Interface?.Addresses ?? [])
            {
                if (addr.Addr?.ipAddress != null)
                {
                    Console.WriteLine($"     IP:{addr.Addr?.ipAddress}");
                }
            }
        }
    }

    Console.WriteLine();
}

//PrintDevicesList(); // 查看所有接口

var entry = new AdaptorEntry();
entry.Start();

Console.ReadLine();

// 程序退出时正确释放资源
entry.Stop();

// 清理 ENet 全局资源
NativeENet.enet_deinitialize();

// 确保日志写入完成
Logger.Instance?.Dispose();