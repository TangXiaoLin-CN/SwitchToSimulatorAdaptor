using System.Text.Json;

namespace SwitchToSimulatorAdaptor;

public class AppSettingData
{
    public bool? RecordDebug { get; set; }
    public bool? RecordInfo { get; set; }
    public bool? RecordWarning { get; set; }
    public bool? RecordError { get; set; }
    public bool? DisplayDebug { get; set; }
    public bool? DisplayInfo { get; set; }
    public bool? DisplayWarning { get; set; }
    public bool? DisplayError { get; set; }
}

public partial class AppSetting
{
    private static AppSetting? _instance;
    private static readonly object _lock = new();

    public static AppSetting Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new AppSetting();
                    _instance.Load();
                }
            }

            return _instance;
        }
    }
    
    // 日志记录级别设置（控制是否写入文件）
    public bool RecordDebug { get; set; } = true;
    public bool RecordInfo { get; set; } = true;
    public bool RecordWarning { get; set; } = true;
    public bool RecordError { get; set; } = true;
    
    // 日志显示级别设置 （控制是否在 UI 显示）
    public bool DisplayDebug { get; set; } = false; // 默认关闭 Debug 显示
    public bool DisplayInfo { get; set; } = true;
    public bool DisplayWarning { get; set; } = true;
    public bool DisplayError { get; set; } = true;

    /// <summary>
    /// 加载配置
    /// </summary>
    public void Load()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                string json = File.ReadAllText(ConfigFilePath);
                System.Diagnostics.Debug.WriteLine($"[AppConfig] 读取配置文件内容：{json}"); // Q: 这个 System.Diagnostics.Debug.WriteLine 到底是干什么的？

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true // 忽略大小写
                };
                var config = JsonSerializer.Deserialize<AppSettingData>(json, options);

                if (config != null)
                {
                    RecordDebug = config.RecordDebug ?? true;
                    RecordInfo = config.RecordInfo ?? true;
                    RecordWarning = config.RecordWarning ?? true;
                    RecordError = config.RecordError ?? true;
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[AppConfig] 配置文件不存在，使用默认配置");
            }
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"[AppConfig] 读取配置文件错误: {e.Message}");
        }
    }

}