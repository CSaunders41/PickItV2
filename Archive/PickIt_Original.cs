using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Cache;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using ItemFilterLibrary;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore.PoEMemory;
using SharpDX;
using SDxVector2 = SharpDX.Vector2;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace PickIt;

public partial class PickIt : BaseSettingsPlugin<PickItSettings>
{
    private readonly CachedValue<List<LabelOnGround>> _chestLabels;
    private readonly CachedValue<LabelOnGround> _portalLabel;
    private readonly CachedValue<int[,]> _inventorySlotsWithItemIds;
    private ServerInventory _inventoryItems;
    private SyncTask<bool> _pickUpTask;
    public List<ItemFilter> ItemFilters;
    private bool _pluginBridgeModeOverride;
    public static PickIt Main;
    private readonly Stopwatch _sinceLastClick = Stopwatch.StartNew();
    private readonly ConditionalWeakTable<string, Regex> _regexes = [];

    public PickIt()
    {
        Name = "PickIt With Linq";
        _inventorySlotsWithItemIds = new FrameCache<int[,]>(() => GetContainer2DArrayWithItemIds(_inventoryItems));
        _chestLabels = new TimeCache<List<LabelOnGround>>(UpdateChestList, 200);
        _portalLabel = new TimeCache<LabelOnGround>(() => GetLabel(@"^Metadata/(MiscellaneousObjects|Effects/Microtransactions)/.*Portal"), 200);
    }

    public override bool Initialise()
    {
        Main = this;

        #region Register keys

        Settings.PickUpKey.OnValueChanged += () => Input.RegisterKey(Settings.PickUpKey);
        Settings.ProfilerHotkey.OnValueChanged += () => Input.RegisterKey(Settings.ProfilerHotkey);

        Input.RegisterKey(Settings.PickUpKey);
        Input.RegisterKey(Settings.ProfilerHotkey);
        Input.RegisterKey(Keys.Escape);

        #endregion
        
        Task.Run(RulesDisplay.LoadAndApplyRules);
        GameController.PluginBridge.SaveMethod("PickIt.ListItems", () => GetItemsToPickup(false).Select(x => x.QueriedItem).ToList());
        GameController.PluginBridge.SaveMethod("PickIt.IsActive", () => _pickUpTask?.GetAwaiter().IsCompleted == false);
        GameController.PluginBridge.SaveMethod("PickIt.SetWorkMode", (bool running) => { _pluginBridgeModeOverride = running; });
        return true;
    }

    private enum WorkMode
    {
        Stop,
        Lazy,
        Manual
    }

    private WorkMode GetWorkMode()
    {
        if (!GameController.Window.IsForeground() ||
            !Settings.Enable ||
            Input.GetKeyState(Keys.Escape))
        {
            _pluginBridgeModeOverride = false;
            return WorkMode.Stop;
        }

        if (Input.GetKeyState(Settings.ProfilerHotkey.Value))
        {
            var sw = Stopwatch.StartNew();
            var looseVar2 = GetItemsToPickup(false).FirstOrDefault();
            sw.Stop();
            LogMessage($"GetItemsToPickup Elapsed Time: {sw.ElapsedTicks} Item: {looseVar2?.BaseName} Distance: {looseVar2?.Distance}");
        }

        if (Input.GetKeyState(Settings.PickUpKey.Value) || _pluginBridgeModeOverride)
        {
            return WorkMode.Manual;
        }

        if (CanLazyLoot())
        {
            return WorkMode.Lazy;
        }

        return WorkMode.Stop;
    }

    private DateTime DisableLazyLootingTill { get; set; }

    public override Job Tick()
    {
        var playerInvCount = GameController?.Game?.IngameState?.Data?.ServerData?.PlayerInventories?.Count;
        if (playerInvCount is null or 0)
            return null;

        _inventoryItems = GameController.Game.IngameState.Data.ServerData.PlayerInventories[0].Inventory;
        if (Input.GetKeyState(Settings.LazyLootingPauseKey)) DisableLazyLootingTill = DateTime.Now.AddSeconds(2);

        return null;
    }

    public override void Render()
    {
        DrawInventoryCells();

        if (Settings.DebugHighlight)
        {
            foreach (var item in GetItemsToPickup(false))
            {
                Graphics.DrawFrame(item.QueriedItem.ClientRect, Color.Violet, 5);
            }
        }

        if ((!_isPickingUp || _pickUpTask == null) && _unclickedMouse)
        {
            _unclickedMouse = false;
            if (!Input.IsKeyDown(Keys.LButton))
            {
                Input.LeftDown();
            }
        }

        if (GetWorkMode() != WorkMode.Stop)
        {
            TaskUtils.RunOrRestart(ref _pickUpTask, RunPickerIterationAsync);
        }
        else
        {
            _pickUpTask = null;
        }

        if (Settings.FilterTest.Value is { Length: > 0 } &&
            GameController.IngameState.UIHover is { Address: not 0 } h &&
            h.Entity.IsValid)
        {
            var f = ItemFilter.LoadFromString(Settings.FilterTest);
            var matched = f.Matches(new ItemData(h.Entity, GameController));
            DebugWindow.LogMsg($"Debug item match: {matched}");
        }
    }

    private void DrawInventoryCells()
    {
        var settings = Settings.InventoryRender;
        if (!settings.ShowInventoryView.Value)
            return;

        var ingameUi = GameController.Game.IngameState.IngameUi;
        if (!settings.IgnoreFullscreenPanels && ingameUi.FullscreenPanels.Any(x => x.IsVisible))
            return;

        if (!settings.IgnoreLargePanels && ingameUi.LargePanels.Any(x => x.IsVisible))
            return;

        if (!settings.IgnoreChatPanel && ingameUi.ChatTitlePanel.IsVisible)
            return;

        if (!settings.IgnoreLeftPanel && ingameUi.OpenLeftPanel.IsVisible)
            return;

        if (!settings.IgnoreRightPanel && ingameUi.OpenRightPanel.IsVisible)
            return;

        var windowSize = GameController.Window.GetWindowRectangleTimeCache;
        var inventoryItemIds = _inventorySlotsWithItemIds.Value;
        if (inventoryItemIds == null)
            return;

        var viewTopLeftX = (int)(windowSize.Width * (settings.Position.Value.X / 100f));
        var viewTopLeftY = (int)(windowSize.Height * (settings.Position.Value.Y / 100f));

        var cellSize = settings.CellSize;
        var cellSpacing = settings.CellSpacing;
        var outlineWidth = settings.ItemOutlineWidth;
        var backerPadding = settings.BackdropPadding;

        var inventoryRows = inventoryItemIds.GetLength(0);
        var inventoryCols = inventoryItemIds.GetLength(1);
        var gridWidth = inventoryCols * (cellSize + cellSpacing) - cellSpacing;
        var gridHeight = inventoryRows * (cellSize + cellSpacing) - cellSpacing;
        var backerRect = new RectangleF(
            viewTopLeftX - backerPadding, viewTopLeftY - backerPadding, gridWidth + backerPadding * 2, gridHeight + backerPadding * 2);
        Graphics.DrawBox(backerRect, settings.BackgroundColor.Value);

        var itemBounds = new Dictionary<int, (int MinX, int MinY, int MaxX, int MaxY)>();
        for (var y = 0; y < inventoryRows; y++)
        for (var x = 0; x < inventoryCols; x++)
        {
            var isOccupied = inventoryItemIds[y, x] > 0;
            var cellColor = isOccupied ? settings.OccupiedSlotColor.Value : settings.UnoccupiedSlotColor.Value;
            var cellX = viewTopLeftX + x * (cellSize + cellSpacing);
            var cellY = viewTopLeftY + y * (cellSize + cellSpacing);
            var cellRect = new RectangleF(cellX, cellY, cellSize, cellSize);
            Graphics.DrawBox(cellRect, cellColor);

            var itemId = inventoryItemIds[y, x];
            if (itemId == 0) continue;

            if (itemBounds.TryGetValue(itemId, out var bounds))
            {
                bounds.MinX = Math.Min(bounds.MinX, x);
                bounds.MinY = Math.Min(bounds.MinY, y);
                bounds.MaxX = Math.Max(bounds.MaxX, x);
                bounds.MaxY = Math.Max(bounds.MaxY, y);
                itemBounds[itemId] = bounds;
            }
            else
            {
                itemBounds[itemId] = (x, y, x, y);
            }
        }

        foreach (var (_, (minX, minY, maxX, maxY)) in itemBounds)
        {
            var itemAreaX = viewTopLeftX + minX * (cellSize + cellSpacing);
            var itemAreaY = viewTopLeftY + minY * (cellSize + cellSpacing);
            var itemAreaWidth = (maxX - minX + 1) * (cellSize + cellSpacing) - cellSpacing;
            var itemAreaHeight = (maxY - minY + 1) * (cellSize + cellSpacing) - cellSpacing;

            var outerRect = new RectangleF(itemAreaX, itemAreaY, itemAreaWidth, itemAreaHeight);
            DrawFrameInside(outerRect, outlineWidth, settings.ItemOutlineColor.Value);
        }

        return;

        void DrawFrameInside(RectangleF outerRect, int thickness, Color color)
        {
            // A horrible workaround to the uneven values set by users resulting in not pixel perfect drawing
            if (thickness <= 0) return;
            // Top
            Graphics.DrawBox(new RectangleF(outerRect.Left, outerRect.Top, outerRect.Width, thickness), color);
            // Bottom
            Graphics.DrawBox(new RectangleF(outerRect.Left, outerRect.Bottom - thickness, outerRect.Width, thickness), color);
            // Left
            Graphics.DrawBox(new RectangleF(outerRect.Left, outerRect.Top + thickness, thickness, outerRect.Height - thickness * 2), color);
            // Right
            Graphics.DrawBox(new RectangleF(outerRect.Right - thickness, outerRect.Top + thickness, thickness, outerRect.Height - thickness * 2), color);
        }
    }

    private bool DoWePickThis(PickItItemData item)
    {
        return Settings.PickUpEverything || (ItemFilters?.Any(filter => filter.Matches(item)) ?? false);
    }

    private List<LabelOnGround> UpdateChestList()
    {
        bool IsFittingEntity(Entity entity)
        {
            return Settings.ChestSettings.ChestList.Content.Any(
                    x => x.Enabled?.Value == true &&
                        !string.IsNullOrEmpty(x.MetadataRegex?.Value) &&
                        !string.IsNullOrEmpty(entity.Metadata) &&
                        _regexes.GetValue(x.MetadataRegex.Value, p => new Regex(p))!.IsMatch(entity.Metadata))
                   && entity.HasComponent<Chest>();
        }

        if (GameController.EntityListWrapper.OnlyValidEntities.Any(IsFittingEntity))
        {
            return GameController?.Game?.IngameState?.IngameUi?.ItemsOnGroundLabelsVisible
                .Where(x => x.Address != 0 &&
                            x.IsVisible &&
                            IsFittingEntity(x.ItemOnGround))
                .OrderBy(x => x.ItemOnGround.DistancePlayer)
                .ToList() ?? [];
        }

        return [];
    }

    private bool CanLazyLoot()
    {
        if (!Settings.LazyLooting) return false;
        if (DisableLazyLootingTill > DateTime.Now) return false;
        try
        {
            if (Settings.NoLazyLootingWhileEnemyClose && GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Monster]
                    .Any(x => x?.GetComponent<Monster>() != null && x.IsValid && x.IsHostile && x.IsAlive
                              && !x.IsHidden && !x.Path.Contains("ElementalSummoned")
                              && Vector3.Distance(GameController.Player.PosNum, x.GetComponent<Render>().PosNum) < Settings.PickupRange)) return false;
        }
        catch (NullReferenceException)
        {
        }

        return true;
    }

    private bool ShouldLazyLoot(PickItItemData item)
    {
        if (item == null)
        {
            return false;
        }

        var itemPos = item.QueriedItem.Entity.PosNum;
        var playerPos = GameController.Player.PosNum;
        return Math.Abs(itemPos.Z - playerPos.Z) <= 50 &&
               itemPos.Xy().DistanceSquared(playerPos.Xy()) <= 275 * 275;
    }

    private bool IsLabelClickable(Element element, RectangleF? customRect)
    {
        if (element is not { IsValid: true, IsVisible: true, IndexInParent: not null })
        {
            return false;
        }

        var center = (customRect ?? element.GetClientRect()).Center;

        var gameWindowRect = GameController.Window.GetWindowRectangleTimeCache with { Location = SDxVector2.Zero };
        gameWindowRect.Inflate(-36, -36);
        return gameWindowRect.Contains(center.X, center.Y);
    }

    private bool IsPortalTargeted(LabelOnGround portalLabel)
    {
        if (portalLabel == null)
        {
            return false;
        }

        // extra checks in case of HUD/game update. They are easy on CPU
        return
            GameController.IngameState.UIHover.Address == portalLabel.Address ||
            GameController.IngameState.UIHover.Address == portalLabel.ItemOnGround.Address ||
            GameController.IngameState.UIHover.Address == portalLabel.Label.Address ||
            GameController.IngameState.UIHoverElement.Address == portalLabel.Address ||
            GameController.IngameState.UIHoverElement.Address == portalLabel.ItemOnGround.Address ||
            GameController.IngameState.UIHoverElement.Address ==
            portalLabel.Label.Address || // this is the right one
            GameController.IngameState.UIHoverTooltip.Address == portalLabel.Address ||
            GameController.IngameState.UIHoverTooltip.Address == portalLabel.ItemOnGround.Address ||
            GameController.IngameState.UIHoverTooltip.Address == portalLabel.Label.Address ||
            portalLabel.ItemOnGround?.HasComponent<Targetable>() == true &&
            portalLabel.ItemOnGround?.GetComponent<Targetable>()?.isTargeted == true;
    }

    private static bool IsPortalNearby(LabelOnGround portalLabel, Element element)
    {
        if (portalLabel == null) return false;
        var rect1 = portalLabel.Label.GetClientRectCache;
        var rect2 = element.GetClientRectCache;
        rect1.Inflate(100, 100);
        rect2.Inflate(100, 100);
        return rect1.Intersects(rect2);
    }

    private LabelOnGround GetLabel(string id)
    {
        var labels = GameController?.Game?.IngameState?.IngameUi?.ItemsOnGroundLabels;
        if (labels == null)
        {
            return null;
        }

        var regex = new Regex(id);
        var labelQuery =
            from labelOnGround in labels
            where labelOnGround?.Label is { IsValid: true, Address: > 0, IsVisible: true }
            let itemOnGround = labelOnGround.ItemOnGround
            where itemOnGround?.Metadata is { } metadata && regex.IsMatch(metadata)
            let dist = GameController?.Player?.GridPosNum.DistanceSquared(itemOnGround.GridPosNum)
            orderby dist
            select labelOnGround;

        return labelQuery.FirstOrDefault();
    }

    private bool _isPickingUp = false;
    private bool _unclickedMouse = false;
    private async SyncTask<bool> RunPickerIterationAsync()
    {
        _isPickingUp = false;
        try
        {
            if (!GameController.Window.IsForeground()) return true;

            var pickUpThisItem = GetItemsToPickup(true).FirstOrDefault();

            var workMode = GetWorkMode();
            if (workMode == WorkMode.Manual || workMode == WorkMode.Lazy && ShouldLazyLoot(pickUpThisItem))
            {
                if (Settings.ChestSettings.ClickChests)
                {
                    var chestLabel = _chestLabels?.Value.FirstOrDefault(x =>
                        x.ItemOnGround.DistancePlayer < Settings.PickupRange &&
                        IsLabelClickable(x.Label, null));

                    if (chestLabel != null)
                    {
                        var shouldPickChest = pickUpThisItem == null ||
                                              Settings.ChestSettings.TargetNearbyChestsFirst && chestLabel.ItemOnGround.DistancePlayer < Settings.ChestSettings.TargetNearbyChestsFirstRadius || 
                                              pickUpThisItem.Distance >= chestLabel.ItemOnGround.DistancePlayer;

                        if (shouldPickChest)
                        {
                            await PickAsync(chestLabel.ItemOnGround, chestLabel.Label, null, _chestLabels.ForceUpdate);
                            return true;
                        }
                    }
                }

                if (pickUpThisItem == null)
                {
                    return true;
                }

                pickUpThisItem.AttemptedPickups++;
                await PickAsync(pickUpThisItem.QueriedItem.Entity, pickUpThisItem.QueriedItem.Label, pickUpThisItem.QueriedItem.ClientRect, () => { });
            }
        }
        finally
        {
            _isPickingUp = false;
        }

        return true;
    }

    private IEnumerable<PickItItemData> GetItemsToPickup(bool filterAttempts)
    {
        var labels = GameController.Game.IngameState.IngameUi.ItemsOnGroundLabelElement.VisibleGroundItemLabels?
            .Where(x=> x.Entity?.DistancePlayer is {} distance && distance < Settings.PickupRange)
            .OrderBy(x => x.Entity?.DistancePlayer ?? int.MaxValue);

        return labels?
            .Where(x => x.Entity?.Path != null && IsLabelClickable(x.Label, x.ClientRect))
            .Select(x => new PickItItemData(x, GameController))
            .Where(x => x.Entity != null
                        && (!filterAttempts || x.AttemptedPickups == 0)
                        && DoWePickThis(x)
                        && (Settings.PickUpWhenInventoryIsFull || CanFitInventory(x))) ?? [];
    }

    private async SyncTask<bool> PickAsync(Entity item, Element label, RectangleF? customRect, Action onNonClickable)
    {
        _isPickingUp = true;
        var tryCount = 0;
        while (tryCount < 3)
        {
            if (!IsLabelClickable(label, customRect))
            {
                onNonClickable();
                return true;
            }

            if (!Settings.IgnoreMoving && GameController.Player.GetComponent<Actor>().isMoving)
            {
                if (item.DistancePlayer > Settings.ItemDistanceToIgnoreMoving.Value)
                {
                    await TaskUtils.NextFrame();
                    continue;
                }
            }

            if (Settings.UseMagicInput)
            {
                if (Settings.UnclickLeftMouseButton && Input.IsKeyDown(Keys.LButton))
                {
                    _unclickedMouse = true;
                    Input.LeftUp();
                }

                if (_sinceLastClick.ElapsedMilliseconds > Settings.PauseBetweenClicks)
                {
                    GameController.PluginBridge.GetMethod<Action<Entity, uint>>("MagicInput.CastSkillWithTarget")(item, 0x400);
                    _sinceLastClick.Restart();
                    tryCount++;
                }
            }
            else
            {
                var position = label.GetClientRect().ClickRandomNum(5, 3) + GameController.Window.GetWindowRectangleTimeCache.TopLeft.ToVector2Num();
                if (_sinceLastClick.ElapsedMilliseconds > Settings.PauseBetweenClicks)
                {
                    if (!IsTargeted(item, label))
                    {
                        await SetCursorPositionAsync(position, item, label);
                    }
                    else
                    {
                        if (await CheckPortal(label)) return true;
                        if (!IsTargeted(item, label))
                        {
                            await TaskUtils.NextFrame();
                            continue;
                        }

                        Input.Click(MouseButtons.Left);
                        _sinceLastClick.Restart();
                        tryCount++;
                    }
                }
            }

            await TaskUtils.NextFrame();
        }

        return true;
    }

    private async Task<bool> CheckPortal(Element label)
    {
        if (!IsPortalNearby(_portalLabel.Value, label)) return false;
        // in case of portal nearby do extra checks with delays
        if (IsPortalTargeted(_portalLabel.Value))
        {
            return true;
        }

        await Task.Delay(25);
        return IsPortalTargeted(_portalLabel.Value);
    }

    private static bool IsTargeted(Entity item, Element label)
    {
        if (item == null) return false;
        if (item.GetComponent<Targetable>()?.isTargeted is { } isTargeted)
        {
            return isTargeted;
        }

        return label is { HasShinyHighlight: true };
    }

    private static async SyncTask<bool> SetCursorPositionAsync(Vector2 position, Entity item, Element label)
    {
        DebugWindow.LogMsg($"Set cursor pos: {position}");
        Input.SetCursorPos(position);
        return await TaskUtils.CheckEveryFrame(() => IsTargeted(item, label), new CancellationTokenSource(60).Token);
    }
}