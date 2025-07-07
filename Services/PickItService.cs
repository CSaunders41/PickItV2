using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Cache;
using ExileCore.Shared.Helpers;
using ItemFilterLibrary;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace PickIt.Services;

public class PickItService : IPickItService, IDisposable
{
    private readonly GameController _gameController;
    private readonly PickItSettings _settings;
    private readonly IInventoryService _inventoryService;
    private readonly IItemFilterService _itemFilterService;
    private readonly IInputService _inputService;
    private readonly IChestService _chestService;
    
    private readonly CachedValue<List<PickItItemData>> _availableItems;
    private volatile bool _isPickingUp = false;
    private volatile bool _disposed = false;
    private SyncTask<bool> _pickupTask;

    public PickItService(
        GameController gameController,
        PickItSettings settings,
        IInventoryService inventoryService,
        IItemFilterService itemFilterService,
        IInputService inputService,
        IChestService chestService)
    {
        _gameController = gameController ?? throw new ArgumentNullException(nameof(gameController));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
        _itemFilterService = itemFilterService ?? throw new ArgumentNullException(nameof(itemFilterService));
        _inputService = inputService ?? throw new ArgumentNullException(nameof(inputService));
        _chestService = chestService ?? throw new ArgumentNullException(nameof(chestService));
        
        _availableItems = new FrameCache<List<PickItItemData>>(GetAvailableItemsInternal);
    }

    public bool IsPickingUp => _isPickingUp;

    public async Task<bool> PickupItemAsync(Entity item, Element label, RectangleF? customRect = null)
    {
        ThrowIfDisposed();
        
        if (item == null || label == null) return false;

        var wasPickingUp = _isPickingUp;
        _isPickingUp = true;
        
        try
        {
            return await _inputService.ClickItemAsync(item, label, customRect);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickItService] Error picking up item: {ex.Message}");
            return false;
        }
        finally
        {
            _isPickingUp = wasPickingUp;
        }
    }

    public IEnumerable<PickItItemData> GetItemsToPickup(bool filterAttempts = true)
    {
        ThrowIfDisposed();
        
        try
        {
            var items = _availableItems.Value;
            if (items == null) return Enumerable.Empty<PickItItemData>();

            return filterAttempts 
                ? items.Where(item => item.AttemptedPickups == 0)
                : items;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickItService] Error getting items to pickup: {ex.Message}");
            return Enumerable.Empty<PickItItemData>();
        }
    }

    public bool ShouldPickupItem(PickItItemData item)
    {
        ThrowIfDisposed();
        
        if (item == null) return false;
        
        try
        {
            return _itemFilterService.ShouldPickupItem(item);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickItService] Error checking if item should be picked up: {ex.Message}");
            return false;
        }
    }

    public async SyncTask<bool> RunPickupIterationAsync()
    {
        ThrowIfDisposed();
        
        try
        {
            if (!_gameController.Window.IsForeground()) return true;

            var workMode = _inputService.GetCurrentWorkMode();
            if (workMode == WorkMode.Stop) return true;

            var itemsToPickup = GetItemsToPickup(true);
            var nearestItem = itemsToPickup.FirstOrDefault();

            // Check if we should process chests first
            if (_settings.ChestSettings.ClickChests)
            {
                var availableChests = _chestService.GetAvailableChests();
                var nearestChest = availableChests.FirstOrDefault(chest => 
                    chest.ItemOnGround.DistancePlayer < _settings.PickupRange &&
                    _inputService.IsLabelClickable(chest.Label, null));

                if (nearestChest != null && _chestService.ShouldTargetChest(nearestChest, nearestItem))
                {
                    return await _chestService.InteractWithChestAsync(nearestChest);
                }
            }

            // Process items
            if (nearestItem != null)
            {
                var shouldPickup = workMode == WorkMode.Manual || 
                    (workMode == WorkMode.Lazy && ShouldLazyLoot(nearestItem));

                if (shouldPickup)
                {
                    nearestItem.AttemptedPickups++;
                    return await PickupItemAsync(
                        nearestItem.QueriedItem.Entity,
                        nearestItem.QueriedItem.Label,
                        nearestItem.QueriedItem.ClientRect);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickItService] Error in pickup iteration: {ex.Message}");
            return false;
        }
    }

    public void StartPickupTask()
    {
        ThrowIfDisposed();
        
        try
        {
            TaskUtils.RunOrRestart(ref _pickupTask, RunPickupIterationAsync);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickItService] Error starting pickup task: {ex.Message}");
        }
    }

    public void StopPickupTask()
    {
        ThrowIfDisposed();
        
        try
        {
            _pickupTask = null;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickItService] Error stopping pickup task: {ex.Message}");
        }
    }

    private bool ShouldLazyLoot(PickItItemData item)
    {
        if (item == null) return false;
        
        try
        {
            var itemPos = item.QueriedItem.Entity.PosNum;
            var playerPos = _gameController.Player.PosNum;
            
            return Math.Abs(itemPos.Z - playerPos.Z) <= 50 &&
                   itemPos.Xy().DistanceSquared(playerPos.Xy()) <= 275 * 275;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickItService] Error checking if should lazy loot: {ex.Message}");
            return false;
        }
    }

    private List<PickItItemData> GetAvailableItemsInternal()
    {
        try
        {
            var labels = _gameController.Game.IngameState.IngameUi.ItemsOnGroundLabelElement.VisibleGroundItemLabels;
            if (labels == null) return new List<PickItItemData>();

            var availableItems = new List<PickItItemData>();
            
            foreach (var label in labels)
            {
                if (label.Entity?.DistancePlayer is not { } distance || 
                    distance >= _settings.PickupRange ||
                    string.IsNullOrEmpty(label.Entity?.Path))
                {
                    continue;
                }

                if (!_inputService.IsLabelClickable(label.Label, label.ClientRect))
                {
                    continue;
                }

                try
                {
                    var itemData = new PickItItemData(label, _gameController);
                    if (itemData.Entity == null) continue;

                    // Check if item should be picked up
                    if (!ShouldPickupItem(itemData)) continue;

                    // Check if item fits in inventory (unless setting allows full inventory pickup)
                    if (!_settings.PickUpWhenInventoryIsFull && !_inventoryService.CanFitInventory(itemData))
                    {
                        continue;
                    }

                    availableItems.Add(itemData);
                }
                catch (Exception ex)
                {
                    DebugWindow.LogError($"[PickItService] Error processing item: {ex.Message}");
                }
            }

            // Sort by distance for optimal pickup order
            return availableItems.OrderBy(item => item.Distance).ToList();
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickItService] Error getting available items: {ex.Message}");
            return new List<PickItItemData>();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PickItService));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        
        try
        {
            _pickupTask = null;
            // Note: CachedValue doesn't implement IDisposable, so we don't dispose it
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickItService] Error during disposal: {ex.Message}");
        }
    }
} 