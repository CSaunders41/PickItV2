using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
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
    private readonly IDeathAwarenessService _deathAwarenessService;
    
    private readonly CachedValue<List<PickItItemData>> _availableItems;
    private volatile bool _isPickingUp = false;
    private volatile bool _disposed = false;
    private SyncTask<bool> _pickupTask;
    private uint _lastAreaHash = 0;

    public PickItService(
        GameController gameController,
        PickItSettings settings,
        IInventoryService inventoryService,
        IItemFilterService itemFilterService,
        IInputService inputService,
        IChestService chestService,
        IDeathAwarenessService deathAwarenessService)
    {
        _gameController = gameController ?? throw new ArgumentNullException(nameof(gameController));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
        _itemFilterService = itemFilterService ?? throw new ArgumentNullException(nameof(itemFilterService));
        _inputService = inputService ?? throw new ArgumentNullException(nameof(inputService));
        _chestService = chestService ?? throw new ArgumentNullException(nameof(chestService));
        _deathAwarenessService = deathAwarenessService ?? throw new ArgumentNullException(nameof(deathAwarenessService));
        
        _availableItems = new FrameCache<List<PickItItemData>>(GetAvailableItemsInternal);
        
        // Subscribe to death awareness events
        _deathAwarenessService.OnPlayerDeath += OnPlayerDeath;
        _deathAwarenessService.OnPlayerResurrection += OnPlayerResurrection;
        
        // Initialize area hash
        _lastAreaHash = _gameController.Game?.IngameState?.Data?.CurrentAreaHash ?? 0;
    }

    public bool IsPickingUp => _isPickingUp;

    public async Task<bool> PickupItemAsync(Entity item, Element label, RectangleF? customRect = null)
    {
        ThrowIfDisposed();
        
        if (item == null || label == null) return false;

        // Check death state before attempting pickup
        if (!_deathAwarenessService.ShouldAllowPickup())
        {
            DebugWindow.LogMsg("[PickItService] Pickup blocked due to death state");
            return false;
        }

        var wasPickingUp = _isPickingUp;
        _isPickingUp = true;
        
        try
        {
            var result = await _inputService.ClickItemAsync(item, label, customRect);
            
            // Record pickup attempt with death awareness service
            var itemName = GetItemName(item);
            _deathAwarenessService.RecordPickupAttempt(itemName, result);
            
            return result;
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
            // Check for area changes
            CheckAreaChange();
            
            var items = _availableItems?.Value;
            if (items == null) 
            {
                DebugWindow.LogMsg("[PickItService] Available items cache is null");
                return Enumerable.Empty<PickItItemData>();
            }

            // Process items for attempt management
            ProcessItemAttempts(items);

            return filterAttempts 
                ? items.Where(item => ShouldAttemptPickup(item))
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
            // Check death status first
            _deathAwarenessService.CheckDeathStatus();
            
            // Don't proceed if death awareness blocks pickup
            if (!_deathAwarenessService.ShouldAllowPickup())
            {
                DebugWindow.LogMsg("[PickItService] Pickup iteration blocked due to death state");
                return true;
            }

            if (!_gameController.Window.IsForeground()) 
            {
                DebugWindow.LogMsg("[PickItService] Window not in foreground, skipping pickup");
                return true;
            }

            var workMode = _inputService.GetCurrentWorkMode();
            DebugWindow.LogMsg($"[PickItService] Current work mode: {workMode}");
            
            if (workMode == WorkMode.Stop) return true;

            var itemsToPickup = GetItemsToPickup(true);
            var nearestItem = itemsToPickup.FirstOrDefault();
            
            DebugWindow.LogMsg($"[PickItService] Found {itemsToPickup.Count()} items to pickup. Nearest: {nearestItem?.BaseName ?? "None"}");

            // Check if we should process chests first
            if (_settings.ChestSettings.ClickChests)
            {
                var availableChests = _chestService.GetAvailableChests();
                var nearestChest = availableChests.FirstOrDefault(chest => 
                    chest.ItemOnGround.DistancePlayer < _settings.PickupRange &&
                    _inputService.IsLabelClickable(chest.Label, null));

                if (nearestChest != null && _chestService.ShouldTargetChest(nearestChest, nearestItem))
                {
                    DebugWindow.LogMsg($"[PickItService] Targeting chest at distance {nearestChest.ItemOnGround.DistancePlayer}");
                    return await _chestService.InteractWithChestAsync(nearestChest);
                }
            }

            // Process items
            if (nearestItem != null)
            {
                var shouldPickup = workMode == WorkMode.Manual || 
                    (workMode == WorkMode.Lazy && ShouldLazyLoot(nearestItem));

                DebugWindow.LogMsg($"[PickItService] Should pickup {nearestItem.BaseName}? {shouldPickup} (workMode: {workMode})");

                if (shouldPickup)
                {
                    // Record attempt on the item
                    nearestItem.RecordAttempt();
                    
                    DebugWindow.LogMsg($"[PickItService] Attempting to pickup {nearestItem.BaseName} (attempt #{nearestItem.AttemptedPickups})");
                    
                    var result = await PickupItemAsync(
                        nearestItem.QueriedItem.Entity,
                        nearestItem.QueriedItem.Label,
                        nearestItem.QueriedItem.ClientRect);
                        
                    DebugWindow.LogMsg($"[PickItService] Pickup result for {nearestItem.BaseName}: {result}");
                    
                    // Check if item should be marked as max attempts reached
                    if (!result && nearestItem.ShouldSkipDueToAttempts(_settings))
                    {
                        DebugWindow.LogMsg($"[PickItService] Item {nearestItem.BaseName} reached max attempts, will be skipped");
                    }
                    
                    return result;
                }
            }
            else
            {
                DebugWindow.LogMsg("[PickItService] No items found to pickup");
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

    private bool ShouldAttemptPickup(PickItItemData item)
    {
        if (item == null) return false;
        
        // Check if item should be skipped due to max attempts
        if (item.ShouldSkipDueToAttempts(_settings))
        {
            return false;
        }
        
        // Check if item is within pickup range
        if (item.Distance > _settings.PickupRange)
        {
            return false;
        }
        
        return true;
    }

    private void ProcessItemAttempts(List<PickItItemData> items)
    {
        if (!_settings.PickupAttemptSettings.EnablePickupAttemptLimiting)
            return;
            
        foreach (var item in items)
        {
            try
            {
                // Reset attempts if enough time has passed
                if (item.ShouldResetAttempts(_settings))
                {
                    item.ResetAttempts();
                    DebugWindow.LogMsg($"[PickItService] Reset attempts for {item.BaseName} due to timeout");
                }
            }
            catch (Exception ex)
            {
                DebugWindow.LogError($"[PickItService] Error processing item attempts: {ex.Message}");
            }
        }
    }

    private void CheckAreaChange()
    {
        try
        {
            var currentAreaHash = _gameController.Game?.IngameState?.Data?.CurrentAreaHash ?? 0;
            if (currentAreaHash != _lastAreaHash && currentAreaHash != 0)
            {
                _lastAreaHash = currentAreaHash;
                
                if (_settings.PickupAttemptSettings.ResetAttemptsOnAreaChange)
                {
                    DebugWindow.LogMsg("[PickItService] Area change detected, resetting pickup attempts");
                    // Note: Items will be refreshed automatically by the cache, so no need to reset existing items
                }
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickItService] Error checking area change: {ex.Message}");
        }
    }

    private void OnPlayerDeath()
    {
        try
        {
            DebugWindow.LogMsg("[PickItService] Player death detected, stopping pickup operations");
            
            // Stop pickup task
            StopPickupTask();
            
            // Clear failed items if setting is enabled
            if (_settings.DeathAwarenessSettings.ClearFailedItemsOnDeath)
            {
                DebugWindow.LogMsg("[PickItService] Clearing failed item attempts due to death");
                // Items will be refreshed automatically by the cache
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickItService] Error handling player death: {ex.Message}");
        }
    }

    private void OnPlayerResurrection()
    {
        try
        {
            DebugWindow.LogMsg("[PickItService] Player resurrection detected");
            
            if (_settings.DeathAwarenessSettings.AutoResumeAfterDeath)
            {
                DebugWindow.LogMsg("[PickItService] Auto-resuming pickup operations after resurrection");
                // The pickup task will be restarted automatically by the main plugin
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickItService] Error handling player resurrection: {ex.Message}");
        }
    }

    private string GetItemName(Entity item)
    {
        try
        {
            var worldItem = item?.GetComponent<WorldItem>();
            if (worldItem?.ItemEntity != null)
            {
                return worldItem.ItemEntity.GetComponent<Base>()?.Name ?? "Unknown Item";
            }
            return "Unknown Item";
        }
        catch (Exception)
        {
            return "Unknown Item";
        }
    }

    private List<PickItItemData> GetAvailableItemsInternal()
    {
        try
        {
            // Safe access to game state with null checking
            var game = _gameController?.Game;
            if (game == null)
            {
                DebugWindow.LogError("[PickItService] Game controller or game is null");
                return new List<PickItItemData>();
            }

            var ingameState = game.IngameState;
            if (ingameState == null)
            {
                DebugWindow.LogError("[PickItService] IngameState is null");
                return new List<PickItItemData>();
            }

            var ingameUi = ingameState.IngameUi;
            if (ingameUi == null)
            {
                DebugWindow.LogError("[PickItService] IngameUi is null");
                return new List<PickItItemData>();
            }

            var itemsOnGroundLabelElement = ingameUi.ItemsOnGroundLabelElement;
            if (itemsOnGroundLabelElement == null)
            {
                DebugWindow.LogError("[PickItService] ItemsOnGroundLabelElement is null");
                return new List<PickItItemData>();
            }

            var labels = itemsOnGroundLabelElement.VisibleGroundItemLabels;
            if (labels == null)
            {
                DebugWindow.LogError("[PickItService] VisibleGroundItemLabels is null");
                return new List<PickItItemData>();
            }

            var availableItems = new List<PickItItemData>();
            
            foreach (var label in labels)
            {
                if (label?.Entity == null)
                {
                    continue;
                }

                var distance = label.Entity.DistancePlayer;
                if (distance > _settings.PickupRange ||
                    string.IsNullOrEmpty(label.Entity.Path))
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
                    if (itemData?.Entity == null) continue;

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
            // Unsubscribe from events
            if (_deathAwarenessService != null)
            {
                _deathAwarenessService.OnPlayerDeath -= OnPlayerDeath;
                _deathAwarenessService.OnPlayerResurrection -= OnPlayerResurrection;
            }
            
            _pickupTask = null;
            // Note: CachedValue doesn't implement IDisposable, so we don't dispose it
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickItService] Error during disposal: {ex.Message}");
        }
    }
} 