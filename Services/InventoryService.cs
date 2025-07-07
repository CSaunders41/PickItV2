using System;
using System.Linq;
using System.Numerics;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Cache;
using ItemFilterLibrary;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace PickIt.Services;

public class InventoryService : IInventoryService, IDisposable
{
    private readonly GameController _gameController;
    private readonly CachedValue<int[,]> _inventorySlots;
    private ServerInventory _currentInventory;
    private volatile bool _disposed = false;

    public InventoryService(GameController gameController)
    {
        _gameController = gameController ?? throw new ArgumentNullException(nameof(gameController));
        _inventorySlots = new FrameCache<int[,]>(BuildInventorySlotMap);
    }

    public bool CanFitInventory(ItemData item)
    {
        ThrowIfDisposed();
        
        if (item == null) return false;
        
        try
        {
            return FindInventorySlot(item) != null;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InventoryService] Error checking if item fits: {ex.Message}");
            return false;
        }
    }

    public bool CanFitInventory(int height, int width)
    {
        ThrowIfDisposed();
        
        if (height <= 0 || width <= 0) return false;
        
        try
        {
            return FindInventorySlot(height, width) != null;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InventoryService] Error checking if item fits: {ex.Message}");
            return false;
        }
    }

    public Vector2? FindInventorySlot(ItemData item)
    {
        ThrowIfDisposed();
        
        if (item == null) return null;
        
        try
        {
            // First check if item can be stacked with existing items
            var stackableSlot = FindStackableSlot(item);
            if (stackableSlot != null)
            {
                return stackableSlot;
            }

            // Find empty slot for the item
            return FindInventorySlot(item.Height, item.Width);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InventoryService] Error finding inventory slot: {ex.Message}");
            return null;
        }
    }

    public Vector2? FindInventorySlot(int itemHeight, int itemWidth)
    {
        ThrowIfDisposed();
        
        if (itemHeight <= 0 || itemWidth <= 0) return null;
        
        try
        {
            var inventorySlots = _inventorySlots?.Value;
            if (inventorySlots == null) 
            {
                DebugWindow.LogMsg("[InventoryService] Inventory slots cache is null");
                return null;
            }

            var inventoryHeight = inventorySlots.GetLength(0);
            var inventoryWidth = inventorySlots.GetLength(1);

            // Check bounds
            if (itemHeight > inventoryHeight || itemWidth > inventoryWidth)
            {
                return null;
            }

            // Use optimized search algorithm
            for (var row = 0; row <= inventoryHeight - itemHeight; row++)
            {
                for (var col = 0; col <= inventoryWidth - itemWidth; col++)
                {
                    if (IsSlotAvailable(inventorySlots, row, col, itemHeight, itemWidth))
                    {
                        return new Vector2(col, row);
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InventoryService] Error finding inventory slot: {ex.Message}");
            return null;
        }
    }

    public int[,] GetInventorySlots()
    {
        ThrowIfDisposed();
        
        try
        {
            var inventorySlots = _inventorySlots?.Value;
            if (inventorySlots == null)
            {
                DebugWindow.LogMsg("[InventoryService] Inventory slots cache is null");
                return new int[5, 12]; // Return default size
            }
            
            return inventorySlots;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InventoryService] Error getting inventory slots: {ex.Message}");
            return new int[5, 12]; // Return default size on error
        }
    }

    public void RefreshInventory()
    {
        ThrowIfDisposed();
        
        try
        {
            var playerInventories = _gameController?.Game?.IngameState?.Data?.ServerData?.PlayerInventories;
            if (playerInventories?.Count > 0)
            {
                _currentInventory = playerInventories[0].Inventory;
            }
            
            _inventorySlots.ForceUpdate();
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InventoryService] Error refreshing inventory: {ex.Message}");
        }
    }

    private Vector2? FindStackableSlot(ItemData item)
    {
        if (_currentInventory?.InventorySlotItems == null) return null;

        try
        {
            var stackableItem = _currentInventory.InventorySlotItems.FirstOrDefault(inventoryItem => 
                CanItemBeStacked(item, inventoryItem));
            
            return stackableItem != null ? new Vector2(stackableItem.PosX, stackableItem.PosY) : null;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InventoryService] Error finding stackable slot: {ex.Message}");
            return null;
        }
    }

    private static bool CanItemBeStacked(ItemData item, ServerInventory.InventSlotItem inventoryItem)
    {
        if (item?.Entity?.Path != inventoryItem?.Item?.Path) return false;
        
        if (!item.Entity.HasComponent<Stack>() || !inventoryItem.Item.HasComponent<Stack>())
            return false;

        try
        {
            var itemStackComp = item.Entity.GetComponent<Stack>();
            var inventoryItemStackComp = inventoryItem.Item.GetComponent<Stack>();
            
            if (itemStackComp?.Info == null || inventoryItemStackComp?.Info == null) return false;

            var itemSize = itemStackComp.Size;
            var inventorySize = inventoryItemStackComp.Size;
            var maxStackSize = inventoryItemStackComp.Info.MaxStackSize;

            return inventorySize + itemSize <= maxStackSize;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InventoryService] Error checking stackability: {ex.Message}");
            return false;
        }
    }

    private bool IsSlotAvailable(int[,] inventorySlots, int startRow, int startCol, int height, int width)
    {
        // Optimized check - fail fast on first occupied slot
        for (var row = startRow; row < startRow + height; row++)
        {
            for (var col = startCol; col < startCol + width; col++)
            {
                if (inventorySlots[row, col] > 0)
                {
                    return false;
                }
            }
        }
        return true;
    }

    private int[,] BuildInventorySlotMap()
    {
        if (_currentInventory == null)
        {
            RefreshInventory();
        }

        if (_currentInventory == null)
        {
            // Return default inventory size if we can't get current inventory
            return new int[5, 12]; // Standard PoE inventory size
        }

        try
        {
            var containerCells = new int[_currentInventory.Rows, _currentInventory.Columns];
            
            if (_currentInventory.InventorySlotItems == null) return containerCells;

            var itemId = 1;
            foreach (var item in _currentInventory.InventorySlotItems)
            {
                if (item == null) continue;

                var itemSizeX = Math.Max(1, item.SizeX);
                var itemSizeY = Math.Max(1, item.SizeY);
                var inventPosX = Math.Max(0, item.PosX);
                var inventPosY = Math.Max(0, item.PosY);
                
                var endX = Math.Min(_currentInventory.Columns, inventPosX + itemSizeX);
                var endY = Math.Min(_currentInventory.Rows, inventPosY + itemSizeY);
                
                for (var y = inventPosY; y < endY; y++)
                {
                    for (var x = inventPosX; x < endX; x++)
                    {
                        if (y >= 0 && y < containerCells.GetLength(0) && 
                            x >= 0 && x < containerCells.GetLength(1))
                        {
                            containerCells[y, x] = itemId;
                        }
                    }
                }
                itemId++;
            }

            return containerCells;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InventoryService] Error building inventory slot map: {ex.Message}");
            return new int[5, 12]; // Return default on error
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(InventoryService));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        // Note: CachedValue doesn't implement IDisposable, so we don't dispose it
    }
} 