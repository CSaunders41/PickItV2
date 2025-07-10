using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using Newtonsoft.Json;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace PickIt;

public class PickItSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new ToggleNode(false);
    public InventoryRender InventoryRender { get; set; } = new InventoryRender();
    public HotkeyNode ProfilerHotkey { get; set; } = Keys.None;
    public HotkeyNode PickUpKey { get; set; } = Keys.F;
    public ToggleNode PickUpWhenInventoryIsFull { get; set; } = new ToggleNode(false);
    public RangeNode<int> PickupRange { get; set; } = new RangeNode<int>(600, 1, 1000);
    public ToggleNode IgnoreMoving { get; set; } = new ToggleNode(false);
    public RangeNode<int> ItemDistanceToIgnoreMoving { get; set; } = new RangeNode<int>(20, 0, 1000);
    public RangeNode<int> PauseBetweenClicks { get; set; } = new RangeNode<int>(100, 0, 500);
    public ToggleNode LazyLooting { get; set; } = new ToggleNode(false);
    public ToggleNode NoLazyLootingWhileEnemyClose { get; set; } = new ToggleNode(false);
    
    [Menu("Lazy Looting Pause Key", "Primary key to pause lazy looting for 2 seconds. Fallback keys: Escape (5s pause + stops plugin), Pause key, or Ctrl+Space")]
    public HotkeyNode LazyLootingPauseKey { get; set; } = new HotkeyNode(Keys.Space);
    
    [Menu("Restore Mouse State After Lazy Loot", "When enabled, the mouse cursor will return to its original position and left/right button states after picking up items during lazy looting")]
    public ToggleNode RestoreMousePositionAfterLazyLoot { get; set; } = new ToggleNode(false);
    public ToggleNode PickUpEverything { get; set; } = new ToggleNode(false);
    public ChestSettings ChestSettings { get; set; } = new();
    public ToggleNode UseMagicInput { get; set; } = new ToggleNode(false);
    public ToggleNode UnclickLeftMouseButton { get; set; } = new ToggleNode(true);

    [JsonIgnore]
    public TextNode FilterTest { get; set; } = new TextNode();

    [Menu("Use a Custom \"\\config\\custom_folder\" folder ")]
    public TextNode CustomConfigDir { get; set; } = new TextNode();

    public List<PickitRule> PickitRules = new List<PickitRule>();

    [JsonIgnore]
    public ToggleNode DebugHighlight { get; set; } = new ToggleNode(false);

    [JsonIgnore]
    public FilterNode Filters { get; } = new FilterNode();

    // Death Awareness & Pickup Attempt Settings
    public DeathAwarenessSettings DeathAwarenessSettings { get; set; } = new();
    public PickupAttemptSettings PickupAttemptSettings { get; set; } = new();
    
    // Range & Visualization Settings
    public RangeVisualizationSettings RangeVisualizationSettings { get; set; } = new();
}

[Submenu(CollapsedByDefault = false)]
public class DeathAwarenessSettings
{
    [Menu("Enable Death Awareness", "Prevents getting stuck in pickup loops when player dies")]
    public ToggleNode EnableDeathAwareness { get; set; } = new ToggleNode(true);
    
    [ConditionalDisplay(nameof(EnableDeathAwareness))]
    [Menu("Death Detection Check Interval (ms)", "How often to check if player is dead")]
    public RangeNode<int> DeathCheckInterval { get; set; } = new RangeNode<int>(500, 100, 2000);
    
    [ConditionalDisplay(nameof(EnableDeathAwareness))]
    [Menu("Auto Resume After Death", "Automatically resume pickup when player resurrects")]
    public ToggleNode AutoResumeAfterDeath { get; set; } = new ToggleNode(true);
    
    [ConditionalDisplay(nameof(EnableDeathAwareness))]
    [Menu("Clear Failed Items On Death", "Reset pickup attempts for all items when player dies")]
    public ToggleNode ClearFailedItemsOnDeath { get; set; } = new ToggleNode(true);
    
    [ConditionalDisplay(nameof(EnableDeathAwareness))]
    [Menu("Resurrection Timeout (ms)", "How long to wait for resurrection before giving up")]
    public RangeNode<int> ResurrectionTimeout { get; set; } = new RangeNode<int>(30000, 5000, 120000);
}

[Submenu(CollapsedByDefault = false)]
public class PickupAttemptSettings
{
    [Menu("Enable Pickup Attempt Limiting", "Limit how many times to try picking up each item")]
    public ToggleNode EnablePickupAttemptLimiting { get; set; } = new ToggleNode(true);
    
    [ConditionalDisplay(nameof(EnablePickupAttemptLimiting))]
    [Menu("Max Pickup Attempts", "Maximum attempts before giving up on an item")]
    public RangeNode<int> MaxPickupAttempts { get; set; } = new RangeNode<int>(3, 1, 20);
    
    [ConditionalDisplay(nameof(EnablePickupAttemptLimiting))]
    [Menu("Attempt Reset Time (ms)", "Time after which pickup attempts are reset")]
    public RangeNode<int> AttemptResetTime { get; set; } = new RangeNode<int>(30000, 5000, 300000);
    
    [ConditionalDisplay(nameof(EnablePickupAttemptLimiting))]
    [Menu("Reset Attempts On Area Change", "Reset pickup attempts when changing areas")]
    public ToggleNode ResetAttemptsOnAreaChange { get; set; } = new ToggleNode(true);
    
    [ConditionalDisplay(nameof(EnablePickupAttemptLimiting))]
    [Menu("Priority Items Ignore Limit", "Currency and valuable items ignore attempt limits")]
    public ToggleNode PriorityItemsIgnoreLimit { get; set; } = new ToggleNode(true);
}

[Submenu(CollapsedByDefault = false)]
public class RangeVisualizationSettings
{
    [Menu("Show Pickup Range", "Display pickup range circle around player")]
    public ToggleNode ShowPickupRange { get; set; } = new ToggleNode(false);
    
    [ConditionalDisplay(nameof(ShowPickupRange))]
    [Menu("Range Circle Color", "Color of the pickup range circle")]
    public ColorNode RangeCircleColor { get; set; } = new Color(0, 255, 0, 128);
    
    [ConditionalDisplay(nameof(ShowPickupRange))]
    [Menu("Range Circle Thickness", "Thickness of the pickup range circle")]
    public RangeNode<int> RangeCircleThickness { get; set; } = new RangeNode<int>(2, 1, 10);
    
    [Menu("Show Death Status", "Display death status and pickup statistics")]
    public ToggleNode ShowDeathStatus { get; set; } = new ToggleNode(true);
    
    [ConditionalDisplay(nameof(ShowDeathStatus))]
    [Menu("Death Status Position", "Position of death status display")]
    public RangeNode<Vector2> DeathStatusPosition { get; set; } = new(new Vector2(10f, 200f), Vector2.Zero, new Vector2(100f, 100f));
}

[Submenu(CollapsedByDefault = false)]
public class ChestPattern
{
    public ToggleNode Enabled { get; set; } = new ToggleNode(true);
    public TextNode MetadataRegex { get; set; } = new("^$");

    public override string ToString()
    {
        return $"{MetadataRegex.Value}###{base.ToString()}";
    }
}

[Submenu(CollapsedByDefault = true)]
public class ChestSettings
{
    public ToggleNode ClickChests { get; set; } = new ToggleNode(true);

    [ConditionalDisplay(nameof(ClickChests))]
    public ToggleNode TargetNearbyChestsFirst { get; set; } = new ToggleNode(true);

    [ConditionalDisplay(nameof(ClickChests))]
    public RangeNode<int> TargetNearbyChestsFirstRadius { get; set; } = new RangeNode<int>(12, 1, 200);

    [ConditionalDisplay(nameof(ClickChests))]
    public ContentNode<ChestPattern> ChestList { get; set; } = new()
    {
        ItemFactory = () => new ChestPattern(), Content = new[]
        {
            "^Metadata/Chests/QuestChests/",
            "^Metadata/Chests/LeaguesExpedition/",
            "^Metadata/Chests/LegionChests/",
            "^Metadata/Chests/Blight",
            "^Metadata/Chests/Breach/",
            "^Metadata/Chests/IncursionChest",
            "^Metadata/Chests/LeagueSanctum/"
        }.Select(x => new ChestPattern() { Enabled = new ToggleNode(true), MetadataRegex = new TextNode(x) }).ToList()
    };
}

[Submenu(RenderMethod = nameof(Render))]
public class FilterNode
{
    public void Render()
    {
                    RulesDisplay.DrawSettings();
    }
}

[Submenu(CollapsedByDefault = false)]
public class InventoryRender
{
    public ToggleNode ShowInventoryView { get; set; } = new(true);
    public ToggleNode IgnoreFullscreenPanels { get; set; } = new ToggleNode(false);
    public ToggleNode IgnoreLargePanels { get; set; } = new ToggleNode(true);
    public ToggleNode IgnoreChatPanel { get; set; } = new ToggleNode(false);
    public ToggleNode IgnoreLeftPanel { get; set; } = new ToggleNode(true);
    public ToggleNode IgnoreRightPanel { get; set; } = new ToggleNode(true);
    public RangeNode<Vector2> Position { get; set; } = new(new Vector2(50f, 50f), Vector2.Zero, new Vector2(100f, 100f));
    public RangeNode<int> BackdropPadding { get; set; } = new(1, 0, 100);
    public RangeNode<int> CellSize { get; set; } = new(20, 1, 100);
    public RangeNode<int> CellSpacing { get; set; } = new(1, 0, 100);
    public RangeNode<int> ItemOutlineWidth { get; set; } = new(1, 0, 100);
    public ColorNode BackgroundColor { get; set; } = new Color(0, 0, 0, 50);
    public ColorNode ItemOutlineColor { get; set; } = new Color(255, 255, 255, 255);
    public ColorNode OccupiedSlotColor { get; set; } = new Color(231, 56, 56, 160);
    public ColorNode UnoccupiedSlotColor { get; set; } = new Color(130, 250, 130, 81);
}

public record PickitRule(string Name, string Location, bool Enabled)
{
    public bool Enabled = Enabled;
}