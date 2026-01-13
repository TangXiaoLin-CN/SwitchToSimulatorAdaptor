using System.Collections.Concurrent;
using System.Text;

namespace SwitchToSimulatorAdaptor.Utils
{
    /// <summary>
    /// 高性能异步日志管理器
    /// 特性：
    ///     - 异步队列写入，不阻塞调用线程
    ///     - 分离日志记录级别和日志显示级别
    ///     - 批量写入减少 I/O 次数
    /// </summary>
    public class Logger : IDisposable // 一般什么时候会继承自 IDisposable ？  
    {
        private static Logger? _instance;
        private static readonly object _lock = new object();
        
        // private readonly string _logFilePath;
        private readonly StreamWriter? _logWriter;
        private readonly BlockingCollection<LogEntry> _logQueue;
        private readonly Thread _writeThread;
        private readonly CancellationTokenSource _cts; // 这个类是用于干什么呢？  
        private bool _disposed = false;
        
        // 日志记录级别 （控制是否写入文件）
        private bool _recordDebug = true;
        private bool _recordInfo = true;
        private bool _recordWarning = true;
        private bool _recordError = true;
        
        // 日志显示级别 （控制是否在 UI 显示）
        private bool _displayDebug = false;
        private bool _displayInfo = true;
        private bool _displayWarning = true;
        private bool _displayError = true;
        
        // UI 显示事件
        public event Action<string>? LogMessage;
        public event Action<string, LogLevel>? LogMessageWithLevel;
        
        // 批量写入配置
        // private const int BatchSize = 50;           // 每批最多写入条数
        // private const int FlushIntervalMs = 100;    // 强制刷新间隔（毫秒）
        
        // 单例
        public static Logger? Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new Logger(); // Q: ??= 是什么语法呢？
                    }
                }

                return _instance;
            }
        }

        private struct LogEntry
        {
            public string Message;
            public LogLevel Level;
            public DateTime Timestamp;
        }
        
        public enum LogLevel
        {
            Debug,
            Info,
            Warning,
            Error
        }

        private Logger()
        {
            _logQueue = new BlockingCollection<LogEntry>(10000); // 队列大小
            _cts = new CancellationTokenSource();
            
            // 从 AppConfigs 中加载日志级别配置
            var config = AppSetting.Instance;
            _recordDebug = config.RecordDebug;
            _recordInfo = config.RecordInfo;
            _recordWarning = config.RecordWarning;
            _recordError = config.RecordError;
            _displayDebug = config.DisplayDebug;
            _displayInfo = config.DisplayInfo;
            _displayWarning = config.DisplayWarning;
            _displayError = config.DisplayError;
            
            // // 日志文件路径：应用程序目录下的 logs 文件夹
            // string appDir = AppDomain.CurrentDomain.BaseDirectory;
            // string logsDir = Path.Combine(appDir, "logs");
            
            if (!Directory.Exists(AppSetting.LogFileDic))
            {
                Directory.CreateDirectory(AppSetting.LogFileDic);
            }
            
            // 轮转日志文件
            // RotateLogFiles(logsDir);
            RotateLogFiles();

            // _logFilePath = Path.Combine(logsDir, "SwitchToSimulatorAdaptor.log");

            try
            {
                // 创建日志文件 （不使用 AutoFlush，由写入线程控制刷新） // 什么是 AutoFlush？ 是一个参数吗？
                // _logWriter = new StreamingWriter(_logFilePath, false, Encoding.UTF8)
                _logWriter = new StreamWriter(AppSetting.LogFilePath, false, Encoding.UTF8)
                {
                    AutoFlush = false
                };
                
                // 写入日志头
                _logWriter.WriteLine("========================================");
                _logWriter.WriteLine($"SwitchToSimulatorAdaptor 日志 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                _logWriter.WriteLine("========================================");
                _logWriter.Flush();
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[Logger] 无法创建日志文件: {e.Message}"); // 这个System.Diagnostics.Debug.WriteLine是什么？ 是系统提供的日志吗？
            }
            
            // 启动后台写入线程
            _writeThread = new Thread(WriterThreadLoop)
            {
                IsBackground = true,
                Name = "LogWriterThread",
                Priority = ThreadPriority.BelowNormal
            };
            _writeThread.Start();
        }

        /// <summary>
        /// 后台写入线程的主循环
        /// </summary>
        private void WriterThreadLoop()
        {
            // var batch = new List<LogEntry>(BatchSize);
            var batch = new List<LogEntry>(AppSetting.BatchSize);
            var lastFlushTime = DateTime.UtcNow;

            while (!_disposed)
            {
                try
                {
                    // 尝试从队列获取日志条目
                    // if (_logQueue.TryTake(out var entry, FlushIntervalMs, _cts.Token))
                    if (_logQueue.TryTake(out var entry, AppSetting.FlushIntervalMs, _cts.Token))
                    {
                        batch.Add(entry);
                        
                        // 继续尝试获取更多条目 （非阻塞）
                        // while (batch.Count < BatchSize && _logQueue.TryTake(out entry))
                        while (batch.Count < AppSetting.BatchSize && _logQueue.TryTake(out entry))
                        {
                            batch.Add(entry);
                        }
                    }
                    
                    // 写入批量日志
                    if (batch.Count > 0 && _logWriter != null)
                    {
                        // foreach (var item in batch)
                        // {
                        //     string levelStr = item.Level switch
                        //     {
                        //         LogLevel.Debug => "DEBUG",
                        //         LogLevel.Info => "INFO",
                        //         LogLevel.Warning => "WARNING",
                        //         LogLevel.Error => "ERROR",
                        //         _ => "INFO"
                        //     };
                        //     string logLine = $"[{item.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{levelStr}] {item.Message}";
                        //     _logWriter.WriteLine(logLine);
                        // }
                        
                        WriteLog(batch);
                        
                        batch.Clear();
                        lastFlushTime = DateTime.UtcNow;
                    }
                    
                    // 定期刷新 （即使没有新日志）
                    // if ((DateTime.UtcNow - lastFlushTime).TotalMilliseconds >= FlushIntervalMs)
                    if ((DateTime.UtcNow - lastFlushTime).TotalMilliseconds >= AppSetting.FlushIntervalMs)
                    {
                        _logWriter?.Flush();
                        lastFlushTime = DateTime.UtcNow;
                    }
                    
                }
                catch (OperationCanceledException)
                {
                    // 忽略取消异常
                    break;
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine($"[Logger] 写入线程错误: {e.Message}");
                }
            }
            
            // 退出前写入剩余日志
            try
            {
                while (_logQueue.TryTake(out var entry))
                {
                    batch.Add(entry);
                }

                if (batch.Count > 0 && _logWriter != null)
                {
                    // foreach (var item in batch)
                    // {
                    //     string levelStr = item.Level switch
                    //     {
                    //         LogLevel.Debug => "DEBUG",
                    //         LogLevel.Info => "INFO",
                    //         LogLevel.Warning => "WARNING",
                    //         LogLevel.Error => "ERROR",
                    //         _ => "INFO"
                    //     };
                    //     string logLine = $"[{item.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{levelStr}] {item.Message}";
                    //     _logWriter.WriteLine(logLine);
                    // }
                    
                    WriteLog(batch);
                }
                
                _logWriter?.WriteLine("========================================");
                _logWriter?.WriteLine($"日志结束 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                _logWriter?.WriteLine("========================================");
                _logWriter?.Flush();
            }
            catch { }
        }

        private void WriteLog(List<LogEntry> batch)
        {
            try
            {
                foreach (var item in batch)
                {
                    string levelStr = item.Level switch
                    {
                        LogLevel.Debug => "DEBUG",
                        LogLevel.Info => "INFO",
                        LogLevel.Warning => "WARNING",
                        LogLevel.Error => "ERROR",
                        _ => "INFO"
                    };
                    string logLine = $"[{item.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{levelStr}] {item.Message}";
                    _logWriter.WriteLine(logLine);
                }
            }
            catch (OperationCanceledException)
            {
                // 忽略取消异常
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[Logger] 写入线程错误: {e.Message}");
            }
        }

        /// <summary>
        /// 轮转日志文件
        /// </summary>
        // private void RotateLogFiles(string logsDir)
        private void RotateLogFiles()
        {
            try
            {
                // string currentLogPath = Path.Combine(logsDir, "edenldnbridge.log");
                // string oldLogPath = Path.Combine(logsDir, "edenldnbridge_old.log");
                //
                // if (File.Exists(currentLogPath))
                // {
                //     if (File.Exists(oldLogPath))
                //     {
                //         try { File.Delete(oldLogPath); } catch {}
                //     }
                //     try { File.Move(currentLogPath, oldLogPath); } catch {}
                // }
                
                if (File.Exists(AppSetting.LogFilePath))
                {
                    if (File.Exists(AppSetting.OldLogFilePath))
                    {
                        try { File.Delete(AppSetting.OldLogFilePath); } catch {}
                    }
                    try { File.Move(AppSetting.LogFilePath, AppSetting.OldLogFilePath); } catch {}
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public void Log(string message, LogLevel level = LogLevel.Info)
        {
            if (_disposed) return;
            
            // 检查是否需要记录
            bool shouldRecord = level switch
            {
                LogLevel.Debug => _recordDebug,
                LogLevel.Info => _recordInfo,
                LogLevel.Warning => _recordWarning,
                LogLevel.Error => _recordError,
                _ => true
            };
            
            // 检查是否需要显示
            bool shouldDisplay = level switch
            {
                LogLevel.Debug => _displayDebug,
                LogLevel.Info => _displayInfo,
                LogLevel.Warning => _displayWarning,
                LogLevel.Error => _displayError,
                _ => true
            };
            
            // 如果既不记录也不显示，直接返回
            if (!shouldRecord && !shouldDisplay) return;

            var timestamp = DateTime.Now;
            
            // 异步写入文件 （入队，不阻塞）
            if (shouldRecord)
            {
                var entry = new LogEntry
                {
                    Message = message,
                    Level = level,
                    Timestamp = timestamp
                };
                
                // TryAdd 不会阻塞，如果队列已满，TryAdd 返回 false
                if (!_logQueue.TryAdd(entry))
                {
                    // 队列满了， 强制同步写入（极端情况）
                    System.Diagnostics.Debug.WriteLine($"[Logger] 队列已满，丢弃日志: {message}");
                }
            }
            
            // 触发 UI 显示事件
            if (shouldDisplay)
            {
                try
                {
                    LogMessage?.Invoke(message);
                    LogMessageWithLevel?.Invoke(message, level);
                }
                catch (InvalidOperationException)
                {
                    // 窗口句柄未创建，忽略
                }
                catch { }
            }
            
            // 输出到调试控制台 （同步，仅用于开发）
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"[{timestamp:HH:mm:ss.fff}] [{level}] {message}");
            #endif
        }

        public void LogDebug(string message) => Log(message, LogLevel.Debug);
        public void LogInfo(string message) => Log(message);
        public void LogWarning(string message) => Log(message, LogLevel.Warning);
        public void LogError(string message) => Log(message, LogLevel.Error);
        public void LogError(string message, Exception ex) => Log(message, LogLevel.Error);
        
        #region 日志级别控制

        /// <summary>
        /// 设置日志记录级别是否启用 （控制是否写入文件）
        /// </summary>
        /// <param name="level"></param>
        /// <param name="enabled"></param>
        public void SetRecordLevelEnabled(LogLevel level, bool enabled)
        {
            switch (level)
            {
                case LogLevel.Debug: _recordDebug = enabled; break;
                case LogLevel.Info: _recordInfo = enabled; break;
                case LogLevel.Warning: _recordWarning = enabled; break;
                case LogLevel.Error: _recordError = enabled; break;
            }
        }

        /// <summary>
        /// 获取日志记录级别是否启用
        /// </summary>
        /// <param name="level"></param>
        /// <returns></returns>
        public bool IsRecordLevelEnabled(LogLevel level)
        {
            return level switch
            {
                LogLevel.Debug => _recordDebug,
                LogLevel.Info => _recordInfo,
                LogLevel.Warning => _recordWarning,
                LogLevel.Error => _recordError,
                _ => true
            };
        }
        
        #endregion
        
        #region 日志显示级别控制
        
        /// <summary>
        /// 设置日志显示级别是否启用 （控制是否在 UI 显示）
        /// </summary>
        /// <param name="level"></param>
        /// <param name="enabled"></param>
        public void SetDisplayLevelEnable(LogLevel level, bool enabled)
        {
            switch (level)
            {
                case LogLevel.Debug: _displayDebug = enabled; break;
                case LogLevel.Info: _displayInfo = enabled; break;
                case LogLevel.Warning: _displayWarning = enabled; break;
                case LogLevel.Error: _displayError = enabled; break;
            }
        }

        /// <summary>
        /// 获取日志显示级别是否启用
        /// </summary>
        /// <param name="level"></param>
        /// <returns></returns>
        public bool IsDisplayLevelEnable(LogLevel level)
        {
            return level switch
            {
                LogLevel.Debug => _displayDebug,
                LogLevel.Info => _displayInfo,
                LogLevel.Warning => _displayWarning,
                LogLevel.Error => _displayError,
                _ => true
            };
        }
        
        #endregion
        
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _cts.Cancel();
                _logQueue.CompleteAdding();
                
                // 等待写入线程完成
                if (_writeThread.IsAlive)
                {
                    _writeThread.Join(2000); // 最多等 2 秒
                }

                try
                {
                    _logWriter?.Dispose();
                }
                catch {}
                
                _cts.Dispose();
                _logQueue.Dispose();
            }
        }
    }
}