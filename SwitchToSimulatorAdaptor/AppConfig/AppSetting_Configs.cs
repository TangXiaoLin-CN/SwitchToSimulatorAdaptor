namespace SwitchToSimulatorAdaptor;

public partial class AppSetting
{
    // ---------------   公共   --------------
    private const string EdenRoomNicknamePrefix = "RealSwitchUser_";
    public static string EdenRoomNickname = $"{EdenRoomNicknamePrefix}{DateTime.Now.Ticks % 10000}";
    
    // --------------- 配置文件 ---------------
    public const string ConfigFileName = "config.json";
    public static string ConfigFilePath { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
    
    // --------------- 日志配置 ---------------
    public const string LogFileName = "SwitchToSimulatorAdaptor.log";
    public const string OldLogFileName = "SwitchToSimulatorAdaptor_Old.log";
    public static string LogFileDic { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs"); // 日志文件所在文件夹：应用程序目录下的 logs 文件夹
    public static string LogFilePath { get; } = Path.Combine(LogFileDic, LogFileName);
    public static string OldLogFilePath { get; } = Path.Combine(LogFileDic, OldLogFileName);
    
    // 批量写入配置
    public const int BatchSize = 50;           // 每批最多写入条数
    public const int FlushIntervalMs = 100;    // 强制刷新间隔（毫秒）
    
    // --------------- EdenRoom ---------------
    
    // Native ENet
    public const string ENetLibrary = "enet";
    public const string ENetDllName = "enet.dll";
    public static string ENetDllPath { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ENetDllName);
    
    // EdenRoom Member
    public const ushort DefaultRoomPort = 24872;
    public const int ConnectionTimeoutMs = 5000;
    public const int NumChannels = 1;
    
    
}