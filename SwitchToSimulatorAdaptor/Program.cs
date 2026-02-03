

using SwitchToSimulatorAdaptor;
using SwitchToSimulatorAdaptor.SharpPcap;

// SharpPcapManager.PrintDevicesList(); // 查看所有接口

var entry = new AdaptorEntry();
entry.Start();

Console.ReadLine();