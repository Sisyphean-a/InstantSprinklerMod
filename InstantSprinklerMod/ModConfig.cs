namespace InstantSprinklerMod;

/// <summary>
/// 模组配置类
/// </summary>
public class ModConfig
{
    /// <summary>
    /// 是否启用喷水动画
    /// </summary>
    public bool EnableAnimation { get; set; } = true;

    /// <summary>
    /// 是否启用音效
    /// </summary>
    public bool EnableSound { get; set; } = true;

    /// <summary>
    /// 是否消耗体力
    /// </summary>
    public bool RequireStamina { get; set; } = false;

    /// <summary>
    /// 每次触发消耗的体力值
    /// </summary>
    public float StaminaCost { get; set; } = 2f;

    /// <summary>
    /// 是否限制每天每个洒水器只能手动触发一次
    /// </summary>
    public bool OncePerDay { get; set; } = false;

    /// <summary>
    /// 是否在控制台显示调试信息
    /// </summary>
    public bool DebugMode { get; set; } = false;
}

