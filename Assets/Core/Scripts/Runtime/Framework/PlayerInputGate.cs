namespace Blocks.Gameplay.Core
{
    /// <summary>
    /// 玩家输入闸门接口。
    ///
    /// 由持有 InputActionMap 的组件实现（例如 CoreInputHandler），
    /// 供外部系统（自由相机观察模式、过场演出、断开连接等）统一开关玩家输入。
    ///
    /// 设计目的：外部系统不需要知道玩家输入分散在几个组件里、各自用的是哪张 Action Map，
    /// 只需要面向这个接口即可，新增输入处理组件时外部代码零改动。
    ///
    /// 约定：
    /// - 关闭后应停止向游戏逻辑派发输入（角色不再响应操作）；
    /// - 开启时只对本地拥有者生效，避免影响其它玩家的对象。
    /// </summary>
    public interface IPlayerInputGate
    {
        /// <summary>开启或关闭该组件负责的玩家输入。</summary>
        /// <param name="enabled">true 恢复输入，false 屏蔽输入。</param>
        void SetPlayerInputEnabled(bool enabled);
    }
}
