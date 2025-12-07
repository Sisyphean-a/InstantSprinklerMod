using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace InstantSprinklerMod;

/// <summary>
/// 模组入口类
/// </summary>
public class ModEntry : Mod
{
    private ModConfig _config = null!;
    private IGenericModConfigMenuApi? _configMenu;
    private readonly HashSet<Vector2> _wateredToday = new();

    /// <summary>
    /// 模组入口点
    /// </summary>
    public override void Entry(IModHelper helper)
    {
        // 加载配置
        _config = helper.ReadConfig<ModConfig>();

        // 注册事件
        helper.Events.Input.ButtonPressed += OnButtonPressed;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;

        Monitor.Log("Instant Sprinkler Mod loaded!", LogLevel.Info);
    }

    /// <summary>
    /// 游戏启动时的事件处理
    /// </summary>
    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        // 集成 Generic Mod Config Menu
        SetupConfigMenu();
    }

    /// <summary>
    /// 每天开始时清空已浇水记录
    /// </summary>
    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        _wateredToday.Clear();
        if (_config.DebugMode)
        {
            Monitor.Log("New day started, cleared watered sprinklers list.", LogLevel.Debug);
        }
    }

    /// <summary>
    /// 按键按下事件处理
    /// </summary>
    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        // 过滤条件检查
        if (!Context.IsWorldReady) return;
        if (Game1.activeClickableMenu != null) return;
        if (!e.Button.IsActionButton()) return;

        // 获取点击的地块
        Vector2 tile = e.Cursor.GrabTile;
        GameLocation location = Game1.currentLocation;

        // 获取该地块的对象
        if (!location.objects.TryGetValue(tile, out SObject? obj)) return;

        // 检查是否为洒水器
        if (!IsSprinkler(obj)) return;

        // 检查是否已经浇过水（如果启用了每天一次限制）
        if (_config.OncePerDay && _wateredToday.Contains(tile))
        {
            Game1.showRedMessage(Helper.Translation.Get("message.already-watered"));
            return;
        }

        // 检查体力
        if (_config.RequireStamina)
        {
            if (Game1.player.Stamina < _config.StaminaCost)
            {
                Game1.showRedMessage(Helper.Translation.Get("message.not-enough-stamina"));
                return;
            }
            Game1.player.Stamina -= _config.StaminaCost;
        }

        // 执行洒水逻辑
        WaterSprinkler(obj, tile, location);

        // 记录已浇水
        if (_config.OncePerDay)
        {
            _wateredToday.Add(tile);
        }

        // 阻止默认行为（防止打开洒水器界面）
        Helper.Input.Suppress(e.Button);

        if (_config.DebugMode)
        {
            Monitor.Log($"Manually triggered sprinkler at {tile}", LogLevel.Debug);
        }
    }

    /// <summary>
    /// 检查对象是否为洒水器
    /// </summary>
    private bool IsSprinkler(SObject obj)
    {
        // 1.6 版本新 API
        if (obj.IsSprinkler())
            return true;

        // 兼容不同的 ItemId 格式
        string itemId = obj.ItemId;
        string normalizedId = itemId.Replace("(BC)", "").Replace("(O)", "");
        return normalizedId is "599" or "621" or "645";
    }

    /// <summary>
    /// 执行洒水器的洒水逻辑
    /// </summary>
    private void WaterSprinkler(SObject sprinkler, Vector2 sprinklerTile, GameLocation location)
    {
        // 获取洒水器覆盖范围
        List<Vector2> coverage = GetSprinklerCoverage(sprinkler, sprinklerTile);

        if (_config.DebugMode)
        {
            Monitor.Log($"Sprinkler at {sprinklerTile}, ItemId: {sprinkler.ItemId}, Coverage count: {coverage.Count}", LogLevel.Debug);
            Monitor.Log($"Coverage tiles: {string.Join(", ", coverage)}", LogLevel.Debug);
        }

        int wateredCount = 0;

        // 遍历覆盖范围并浇水
        foreach (Vector2 tile in coverage)
        {
            if (WaterTile(tile, location))
            {
                wateredCount++;

                // 播放动画
                if (_config.EnableAnimation)
                {
                    PlayWaterAnimation(tile, location);
                }
            }
        }

        // 播放音效
        if (_config.EnableSound && wateredCount > 0)
        {
            location.playSound("wateringCan");
        }

        if (_config.DebugMode)
        {
            Monitor.Log($"Watered {wateredCount} tiles", LogLevel.Debug);
        }
    }

    /// <summary>
    /// 获取洒水器的覆盖范围
    /// </summary>
    private List<Vector2> GetSprinklerCoverage(SObject sprinkler, Vector2 origin)
    {
        List<Vector2> tiles = new();
        string itemId = sprinkler.ItemId;

        // 兼容不同的 ItemId 格式
        // 1.6 版本可能是 "599", "621", "645"
        // 也可能是 "(BC)599", "(BC)621", "(BC)645"
        string normalizedId = itemId.Replace("(BC)", "").Replace("(O)", "");

        // 检查是否有加压喷头
        bool hasPressureNozzle = sprinkler.heldObject.Value?.QualifiedItemId == "(O)915"
                                  || sprinkler.heldObject.Value?.ItemId == "915";

        int radius = 0;
        bool isCross = false;

        if (_config.DebugMode)
        {
            Monitor.Log($"ItemId: {itemId}, NormalizedId: {normalizedId}, HasPressureNozzle: {hasPressureNozzle}", LogLevel.Debug);
        }

        // 判断洒水器类型
        if (normalizedId is "599") // 普通洒水器
        {
            if (hasPressureNozzle)
            {
                radius = 1; // 3x3
            }
            else
            {
                isCross = true; // 十字形
            }
        }
        else if (normalizedId is "621") // 优质洒水器
        {
            radius = hasPressureNozzle ? 2 : 1; // 5x5 或 3x3
        }
        else if (normalizedId is "645") // 铱制洒水器
        {
            radius = hasPressureNozzle ? 3 : 2; // 7x7 或 5x5
        }

        // 生成覆盖地块列表
        if (isCross)
        {
            // 十字形（普通洒水器无加压喷头）
            tiles.Add(origin + new Vector2(0, -1)); // 上
            tiles.Add(origin + new Vector2(0, 1));  // 下
            tiles.Add(origin + new Vector2(-1, 0)); // 左
            tiles.Add(origin + new Vector2(1, 0));  // 右
        }
        else if (radius > 0)
        {
            // 方形范围
            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    if (x == 0 && y == 0) continue; // 跳过中心（洒水器自身位置）
                    tiles.Add(origin + new Vector2(x, y));
                }
            }
        }

        return tiles;
    }

    /// <summary>
    /// 浇水指定地块
    /// </summary>
    private bool WaterTile(Vector2 tile, GameLocation location)
    {
        // 检查地块是否在地图内
        if (!location.isTileOnMap(tile))
        {
            if (_config.DebugMode)
                Monitor.Log($"Tile {tile} is not on map", LogLevel.Trace);
            return false;
        }

        bool watered = false;

        // 检查 TerrainFeatures 层（普通土壤）
        if (location.terrainFeatures.TryGetValue(tile, out TerrainFeature? feature))
        {
            if (_config.DebugMode)
                Monitor.Log($"Tile {tile} has TerrainFeature: {feature.GetType().Name}", LogLevel.Trace);

            if (feature is HoeDirt dirt)
            {
                dirt.state.Value = HoeDirt.watered;
                watered = true;
                if (_config.DebugMode)
                    Monitor.Log($"Watered HoeDirt at {tile}", LogLevel.Trace);
            }
        }
        else if (_config.DebugMode)
        {
            Monitor.Log($"Tile {tile} has no TerrainFeature", LogLevel.Trace);
        }

        // 检查 Objects 层（花盆）
        if (location.objects.TryGetValue(tile, out SObject? obj))
        {
            if (_config.DebugMode)
                Monitor.Log($"Tile {tile} has Object: {obj.Name}", LogLevel.Trace);

            if (obj is IndoorPot pot && pot.hoeDirt.Value != null)
            {
                pot.hoeDirt.Value.state.Value = HoeDirt.watered;
                watered = true;
                if (_config.DebugMode)
                    Monitor.Log($"Watered IndoorPot at {tile}", LogLevel.Trace);
            }
        }

        return watered;
    }

    /// <summary>
    /// 播放喷水动画
    /// </summary>
    private void PlayWaterAnimation(Vector2 tile, GameLocation location)
    {
        // 创建临时动画精灵
        location.temporarySprites.Add(new TemporaryAnimatedSprite(
            textureName: "TileSheets\\animations",
            sourceRect: new Rectangle(0, 1984, 64, 64),
            animationInterval: 100f,
            animationLength: 4,
            numberOfLoops: 1,
            position: tile * 64f,
            flicker: false,
            flipped: false,
            layerDepth: (tile.Y * 64f + 32f) / 10000f,
            alphaFade: 0.01f,
            color: Color.White,
            scale: 1f,
            scaleChange: 0f,
            rotation: 0f,
            rotationChange: 0f
        ));
    }

    /// <summary>
    /// 设置 Generic Mod Config Menu
    /// </summary>
    private void SetupConfigMenu()
    {
        _configMenu = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (_configMenu == null) return;

        // 注册模组配置
        _configMenu.Register(
            mod: ModManifest,
            reset: () => _config = new ModConfig(),
            save: () => Helper.WriteConfig(_config)
        );

        // 添加配置选项
        _configMenu.AddBoolOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.enable-animation.name"),
            tooltip: () => Helper.Translation.Get("config.enable-animation.tooltip"),
            getValue: () => _config.EnableAnimation,
            setValue: value => _config.EnableAnimation = value
        );

        _configMenu.AddBoolOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.enable-sound.name"),
            tooltip: () => Helper.Translation.Get("config.enable-sound.tooltip"),
            getValue: () => _config.EnableSound,
            setValue: value => _config.EnableSound = value
        );

        _configMenu.AddBoolOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.require-stamina.name"),
            tooltip: () => Helper.Translation.Get("config.require-stamina.tooltip"),
            getValue: () => _config.RequireStamina,
            setValue: value => _config.RequireStamina = value
        );

        _configMenu.AddNumberOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.stamina-cost.name"),
            tooltip: () => Helper.Translation.Get("config.stamina-cost.tooltip"),
            getValue: () => _config.StaminaCost,
            setValue: value => _config.StaminaCost = value,
            min: 0f,
            max: 10f,
            interval: 0.5f
        );

        _configMenu.AddBoolOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.once-per-day.name"),
            tooltip: () => Helper.Translation.Get("config.once-per-day.tooltip"),
            getValue: () => _config.OncePerDay,
            setValue: value => _config.OncePerDay = value
        );

        _configMenu.AddBoolOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.debug-mode.name"),
            tooltip: () => Helper.Translation.Get("config.debug-mode.tooltip"),
            getValue: () => _config.DebugMode,
            setValue: value => _config.DebugMode = value
        );
    }
}

/// <summary>
/// Generic Mod Config Menu API 接口
/// </summary>
public interface IGenericModConfigMenuApi
{
    void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
    void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
    void AddNumberOption(IManifest mod, Func<float> getValue, Action<float> setValue, Func<string> name, Func<string>? tooltip = null, float? min = null, float? max = null, float? interval = null, string? fieldId = null);
}
