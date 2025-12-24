namespace Catga.Hosting;

/// <summary>
/// 支持停止接受新请求的接口
/// </summary>
public interface IStoppable
{
    /// <summary>
    /// 停止接受新消息或请求
    /// </summary>
    void StopAcceptingMessages();
    
    /// <summary>
    /// 指示是否正在接受新消息
    /// </summary>
    bool IsAcceptingMessages { get; }
}
