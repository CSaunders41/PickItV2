using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.PoEMemory.Components;
using ExileCore.Shared;
using ExileCore.Shared.Helpers;
using ItemFilterLibrary;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace PickIt.Services;

public interface IPickItService : IDisposable
{
    Task<bool> PickupItemAsync(Entity item, Element label, RectangleF? customRect = null);
    IEnumerable<PickItItemData> GetItemsToPickup(bool filterAttempts = true);
    bool ShouldPickupItem(PickItItemData item);
    bool IsPickingUp { get; }
    SyncTask<bool> RunPickupIterationAsync();
    void StartPickupTask();
    void StopPickupTask();
}

public interface IInventoryService : IDisposable
{
    bool CanFitInventory(ItemData item);
    bool CanFitInventory(int height, int width);
    Vector2? FindInventorySlot(ItemData item);
    Vector2? FindInventorySlot(int height, int width);
    int[,] GetInventorySlots();
    void RefreshInventory();
}

public interface IItemFilterService : IDisposable
{
    bool ShouldPickupItem(PickItItemData item);
    void LoadFilters();
    void ReloadFilters();
    IReadOnlyList<ItemFilter> ActiveFilters { get; }
    Regex GetCachedRegex(string pattern);
}

public interface IInputService : IDisposable
{
    void RegisterHotkeys();
    WorkMode GetCurrentWorkMode();
    Task<bool> ClickItemAsync(Entity item, Element label, RectangleF? customRect = null);
    bool IsLabelClickable(Element element, RectangleF? customRect = null);
    void UpdateLazyLootingState();
    void HandleMouseStateOnRender();
}

public interface IPortalService : IDisposable
{
    Task<bool> CheckPortalInterferenceAsync(Element label);
    bool IsPortalNearby(Element element);
    bool IsPortalTargeted();
}

public interface IChestService : IDisposable
{
    IEnumerable<LabelOnGround> GetAvailableChests();
    bool ShouldTargetChest(LabelOnGround chest, PickItItemData? nearestItem = null);
    Task<bool> InteractWithChestAsync(LabelOnGround chest);
}

public enum WorkMode
{
    Stop,
    Lazy,
    Manual
} 