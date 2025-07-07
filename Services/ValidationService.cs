using System;
using System.Numerics;
using ExileCore;

namespace PickIt.Services;

public class ValidationService : IValidationService
{
    public bool ValidateSettings(PickItSettings settings)
    {
        if (settings == null) return false;
        
        try
        {
            return IsPickupRangeValid(settings.PickupRange.Value) &&
                   IsPositionValid(settings.InventoryRender.Position.Value) &&
                   IsColorValid(settings.InventoryRender.BackgroundColor.Value) &&
                   IsCellSizeValid(settings.InventoryRender.CellSize.Value) &&
                   IsDelayValid(settings.PauseBetweenClicks.Value);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[ValidationService] Error validating settings: {ex.Message}");
            return false;
        }
    }

    public void ClampConfigurationValues(PickItSettings settings)
    {
        if (settings == null) return;
        
        try
        {
            // Clamp pickup range
            var pickupRange = settings.PickupRange.Value;
            if (pickupRange < 1) settings.PickupRange.Value = 1;
            if (pickupRange > 2000) settings.PickupRange.Value = 2000;
            
            // Clamp position values to 0-100%
            var position = settings.InventoryRender.Position.Value;
            var clampedPosition = new Vector2(
                Math.Clamp(position.X, 0f, 100f),
                Math.Clamp(position.Y, 0f, 100f));
            if (position != clampedPosition)
            {
                settings.InventoryRender.Position.Value = clampedPosition;
            }
            
            // Clamp cell size
            var cellSize = settings.InventoryRender.CellSize.Value;
            if (cellSize < 5) settings.InventoryRender.CellSize.Value = 5;
            if (cellSize > 100) settings.InventoryRender.CellSize.Value = 100;
            
            // Clamp cell spacing
            var cellSpacing = settings.InventoryRender.CellSpacing.Value;
            if (cellSpacing < 0) settings.InventoryRender.CellSpacing.Value = 0;
            if (cellSpacing > 20) settings.InventoryRender.CellSpacing.Value = 20;
            
            // Clamp outline width
            var outlineWidth = settings.InventoryRender.ItemOutlineWidth.Value;
            if (outlineWidth < 0) settings.InventoryRender.ItemOutlineWidth.Value = 0;
            if (outlineWidth > 10) settings.InventoryRender.ItemOutlineWidth.Value = 10;
            
            // Clamp backdrop padding
            var backdropPadding = settings.InventoryRender.BackdropPadding.Value;
            if (backdropPadding < 0) settings.InventoryRender.BackdropPadding.Value = 0;
            if (backdropPadding > 50) settings.InventoryRender.BackdropPadding.Value = 50;
            
            // Clamp pause between clicks
            var pauseBetweenClicks = settings.PauseBetweenClicks.Value;
            if (pauseBetweenClicks < 0) settings.PauseBetweenClicks.Value = 0;
            if (pauseBetweenClicks > 2000) settings.PauseBetweenClicks.Value = 2000;
            
            // Clamp item distance to ignore moving
            var itemDistance = settings.ItemDistanceToIgnoreMoving.Value;
            if (itemDistance < 0) settings.ItemDistanceToIgnoreMoving.Value = 0;
            if (itemDistance > 500) settings.ItemDistanceToIgnoreMoving.Value = 500;
            
            // Clamp chest radius
            var chestRadius = settings.ChestSettings.TargetNearbyChestsFirstRadius.Value;
            if (chestRadius < 1) settings.ChestSettings.TargetNearbyChestsFirstRadius.Value = 1;
            if (chestRadius > 300) settings.ChestSettings.TargetNearbyChestsFirstRadius.Value = 300;
            
            DebugWindow.LogMsg("[ValidationService] Configuration values clamped to valid ranges");
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[ValidationService] Error clamping configuration values: {ex.Message}");
        }
    }

    public bool IsConfigurationValid(PickItSettings settings)
    {
        if (settings == null) return false;
        
        try
        {
            // Check for null references
            if (settings.InventoryRender == null ||
                settings.ChestSettings == null ||
                settings.PickitRules == null)
            {
                return false;
            }
            
            // Validate critical settings
            return ValidateSettings(settings);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[ValidationService] Error checking configuration validity: {ex.Message}");
            return false;
        }
    }

    private static bool IsPickupRangeValid(int pickupRange)
    {
        return pickupRange is >= 1 and <= 2000;
    }

    private static bool IsPositionValid(Vector2 position)
    {
        return position.X is >= 0f and <= 100f &&
               position.Y is >= 0f and <= 100f;
    }

    private static bool IsColorValid(SharpDX.Color color)
    {
        // Basic color validation - ensure alpha is reasonable
        return color.A is >= 0 and <= 255;
    }

    private static bool IsCellSizeValid(int cellSize)
    {
        return cellSize is >= 5 and <= 100;
    }

    private static bool IsDelayValid(int delay)
    {
        return delay is >= 0 and <= 2000;
    }
} 