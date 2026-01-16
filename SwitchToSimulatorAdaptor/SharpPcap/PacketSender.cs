using SharpPcap.LibPcap;
using System.Collections.Concurrent;


namespace SwitchToSimulatorAdaptor.SharpPcap;

// ● 根据之前的讨论，问题出在 ARP 欺骗和 TCP 服务器同时使用同一个设备发送数据包时发生冲突。这是一个典型的线程安全问题。让我提供一个解决方案：
//
// 解决方案：统一的数据包发送器
//
// 创建一个线程安全的统一发送器，所有模块都通过它来发送数据包：

/// <summary>
/// 线程安全的统一数据包发送器
/// </summary>
public class PacketSender
{
    private readonly LibPcapLiveDevice? _device;
    private readonly object _sendLock = new();
    private readonly BlockingCollection<byte[]>? _sendQueue;
    private readonly Thread _sendThread;
    private volatile  bool _running; // volatile 是什么关键字？  这是一个关键字，用于告诉编译器，这个变量可能会被多个线程访问，因此需要使用原子操作来保证变量的访问安全。
    
    // 发送统计
    public long PacketsSent { get; set; }
    public long sendErrors { get; set; }
    
    // 事件
    public event Action<string> OnLog;
    public event Action<Exception> OnError;

    public PacketSender(LibPcapLiveDevice? device, bool useQueue = false)
    {
        _device = device;

        if (useQueue)
        {
            // 使用队列模式：异步发送，不阻塞调用者
            _sendQueue = new BlockingCollection<byte[]>(1000);
            _sendThread = new Thread(SendLoop) { IsBackground = true };
            _sendThread.Start();
        }
    }

    /// <summary>
    /// 同步发送数据包（线程安全）
    /// </summary>
    /// <param name="packet"></param>
    /// <returns></returns>
    public bool Send(byte[]? packet)
    {
        if (packet == null || packet.Length == 0) return false;

        lock (_sendLock)
        {
            try
            {
                _device.SendPacket(packet);
                PacketsSent++;
                return true;
            }
            catch (Exception e)
            {
                sendErrors++;
                OnError?.Invoke(e);
                OnLog?.Invoke($"[PacketSender] 发送失败：{e.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// 异步发送数据包 （放入队列）
    /// </summary>
    /// <param name="packet"></param>
    /// <returns></returns>
    public bool Enqueue(byte[] packet)
    {
        if (_sendQueue == null)
        {
            OnLog?.Invoke("[PacketSender] 队列模式未启用，使用同步发送");
            return Send(packet);
        }

        try
        {
            _sendQueue.Add(packet);
            return true;
        }
        catch (Exception e)
        {
            sendErrors++;
            OnError?.Invoke(e);
            OnLog?.Invoke($"[PacketSender] 队列已关闭：{e.Message}");
            return false;
        }
    }

    private void SendLoop()
    {
        OnLog?.Invoke("[PacketSender] 发送线程已启动");

        while (_running)
        {
            try
            {
                if (_sendQueue != null && _sendQueue.TryTake(out var packet, 100))
                {
                    Send(packet);
                }
            }
            catch (Exception e)
            {
                OnLog?.Invoke($"[PacketSender] 发送线程异常：{e.Message}");
            }
        }
        
        OnLog?.Invoke("[PacketSender] 发送线程已停止");
    }

    public void Dispose()
    {
        _running = false;
        _sendQueue?.CompleteAdding();
        _sendThread?.Join(1000);
        _sendQueue?.Dispose();
    }
}