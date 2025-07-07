using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore;
using ExileCore.PoEMemory.Elements;
using SharpDX;

namespace PickIt.Services;

public class RenderService : IRenderService, IDisposable
{
    private readonly GameController _gameController;
    private readonly PickItSettings _settings;
    private readonly IInventoryService _inventoryService;
    private readonly IItemFilterService _itemFilterService;
    private readonly Graphics _graphics;
    private volatile bool _disposed = false;

    public RenderService(
        GameController gameController,
        PickItSettings settings,
        IInventoryService inventoryService,
        IItemFilterService itemFilterService,
        Graphics graphics)
    {
        _gameController = gameController ?? throw new ArgumentNullException(nameof(gameController));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
        _itemFilterService = itemFilterService ?? throw new ArgumentNullException(nameof(itemFilterService));
        _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
    }

    public void RenderAll()
    {
        ThrowIfDisposed();
        
        try
        {
            RenderInventoryOverlay();
            RenderDebugHighlights();
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[RenderService] Error in RenderAll: {ex.Message}");
        }
    }

    public void RenderInventoryOverlay()
    {
        ThrowIfDisposed();
        
        if (!_settings.InventoryRender.ShowInventoryView.Value) return;
        
        try
        {
            if (!ShouldRenderInventory()) return;
            
            var inventorySlots = _inventoryService.GetInventorySlots();
            if (inventorySlots == null) return;
            
            var windowSize = _gameController.Window.GetWindowRectangleTimeCache;
            var renderSettings = _settings.InventoryRender;
            
            var viewTopLeftX = (int)(windowSize.Width * (renderSettings.Position.Value.X / 100f));
            var viewTopLeftY = (int)(windowSize.Height * (renderSettings.Position.Value.Y / 100f));
            
            var cellSize = renderSettings.CellSize.Value;
            var cellSpacing = renderSettings.CellSpacing.Value;
            var outlineWidth = renderSettings.ItemOutlineWidth.Value;
            var backdropPadding = renderSettings.BackdropPadding.Value;
            
            var inventoryRows = inventorySlots.GetLength(0);
            var inventoryCols = inventorySlots.GetLength(1);
            
            // Calculate grid dimensions
            var gridWidth = inventoryCols * (cellSize + cellSpacing) - cellSpacing;
            var gridHeight = inventoryRows * (cellSize + cellSpacing) - cellSpacing;
            
            // Draw background
            var backgroundRect = new RectangleF(
                viewTopLeftX - backdropPadding,
                viewTopLeftY - backdropPadding,
                gridWidth + backdropPadding * 2,
                gridHeight + backdropPadding * 2);
            _graphics.DrawBox(backgroundRect, renderSettings.BackgroundColor.Value);
            
            // Track item bounds for outline drawing
            var itemBounds = new Dictionary<int, (int MinX, int MinY, int MaxX, int MaxY)>();
            
            // Draw individual cells
            for (var row = 0; row < inventoryRows; row++)
            {
                for (var col = 0; col < inventoryCols; col++)
                {
                    var isOccupied = inventorySlots[row, col] > 0;
                    var cellColor = isOccupied 
                        ? renderSettings.OccupiedSlotColor.Value 
                        : renderSettings.UnoccupiedSlotColor.Value;
                    
                    var cellX = viewTopLeftX + col * (cellSize + cellSpacing);
                    var cellY = viewTopLeftY + row * (cellSize + cellSpacing);
                    var cellRect = new RectangleF(cellX, cellY, cellSize, cellSize);
                    
                    _graphics.DrawBox(cellRect, cellColor);
                    
                    // Track item bounds for outline
                    var itemId = inventorySlots[row, col];
                    if (itemId > 0)
                    {
                        UpdateItemBounds(itemBounds, itemId, col, row);
                    }
                }
            }
            
            // Draw item outlines
            DrawItemOutlines(itemBounds, viewTopLeftX, viewTopLeftY, cellSize, cellSpacing, outlineWidth, renderSettings.ItemOutlineColor.Value);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[RenderService] Error rendering inventory overlay: {ex.Message}");
        }
    }

    public void RenderDebugHighlights()
    {
        ThrowIfDisposed();
        
        if (!_settings.DebugHighlight) return;
        
        try
        {
            var pickItService = PickItServiceManager.Instance.GetService<IPickItService>();
            if (pickItService == null) return;
            
            var itemsToPickup = pickItService.GetItemsToPickup(false);
            
            foreach (var item in itemsToPickup)
            {
                RenderItemHighlight(item, Color.Violet);
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[RenderService] Error rendering debug highlights: {ex.Message}");
        }
    }

    public void RenderItemHighlight(PickItItemData item, Color color)
    {
        ThrowIfDisposed();
        
        if (item?.QueriedItem?.ClientRect == null) return;
        
        try
        {
            _graphics.DrawFrame(item.QueriedItem.ClientRect, color, 5);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[RenderService] Error rendering item highlight: {ex.Message}");
        }
    }

    private bool ShouldRenderInventory()
    {
        try
        {
            var ingameUi = _gameController.Game.IngameState.IngameUi;
            var renderSettings = _settings.InventoryRender;
            
            if (!renderSettings.IgnoreFullscreenPanels && ingameUi.FullscreenPanels.Any(x => x.IsVisible))
                return false;
            
            if (!renderSettings.IgnoreLargePanels && ingameUi.LargePanels.Any(x => x.IsVisible))
                return false;
            
            if (!renderSettings.IgnoreChatPanel && ingameUi.ChatTitlePanel.IsVisible)
                return false;
            
            if (!renderSettings.IgnoreLeftPanel && ingameUi.OpenLeftPanel.IsVisible)
                return false;
            
            if (!renderSettings.IgnoreRightPanel && ingameUi.OpenRightPanel.IsVisible)
                return false;
            
            return true;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[RenderService] Error checking if should render inventory: {ex.Message}");
            return false;
        }
    }

    private static void UpdateItemBounds(Dictionary<int, (int MinX, int MinY, int MaxX, int MaxY)> itemBounds, int itemId, int col, int row)
    {
        if (itemBounds.TryGetValue(itemId, out var bounds))
        {
            bounds.MinX = Math.Min(bounds.MinX, col);
            bounds.MinY = Math.Min(bounds.MinY, row);
            bounds.MaxX = Math.Max(bounds.MaxX, col);
            bounds.MaxY = Math.Max(bounds.MaxY, row);
            itemBounds[itemId] = bounds;
        }
        else
        {
            itemBounds[itemId] = (col, row, col, row);
        }
    }

    private void DrawItemOutlines(
        Dictionary<int, (int MinX, int MinY, int MaxX, int MaxY)> itemBounds,
        int viewTopLeftX,
        int viewTopLeftY,
        int cellSize,
        int cellSpacing,
        int outlineWidth,
        Color outlineColor)
    {
        if (outlineWidth <= 0) return;
        
        try
        {
            foreach (var (_, (minX, minY, maxX, maxY)) in itemBounds)
            {
                var itemAreaX = viewTopLeftX + minX * (cellSize + cellSpacing);
                var itemAreaY = viewTopLeftY + minY * (cellSize + cellSpacing);
                var itemAreaWidth = (maxX - minX + 1) * (cellSize + cellSpacing) - cellSpacing;
                var itemAreaHeight = (maxY - minY + 1) * (cellSize + cellSpacing) - cellSpacing;
                
                var outerRect = new RectangleF(itemAreaX, itemAreaY, itemAreaWidth, itemAreaHeight);
                DrawFrameInside(outerRect, outlineWidth, outlineColor);
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[RenderService] Error drawing item outlines: {ex.Message}");
        }
    }

    private void DrawFrameInside(RectangleF outerRect, int thickness, Color color)
    {
        try
        {
            var graphics = _graphics;
            
            // Top
            graphics.DrawBox(new RectangleF(outerRect.Left, outerRect.Top, outerRect.Width, thickness), color);
            
            // Bottom
            graphics.DrawBox(new RectangleF(outerRect.Left, outerRect.Bottom - thickness, outerRect.Width, thickness), color);
            
            // Left
            graphics.DrawBox(new RectangleF(outerRect.Left, outerRect.Top + thickness, thickness, outerRect.Height - thickness * 2), color);
            
            // Right
            graphics.DrawBox(new RectangleF(outerRect.Right - thickness, outerRect.Top + thickness, thickness, outerRect.Height - thickness * 2), color);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[RenderService] Error drawing frame inside: {ex.Message}");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RenderService));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
} 