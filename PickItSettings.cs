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