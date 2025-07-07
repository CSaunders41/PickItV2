using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.Shared.Cache;

namespace PickIt.Services;

public class ChestService : IChestService, IDisposable
{
    private readonly GameController _gameController;
    private readonly PickItSettings _settings;
    private readonly IItemFilterService _itemFilterService;
    private readonly IInputService _inputService;
    private readonly CachedValue<List<LabelOnGround>> _availableChests;
    private volatile bool _disposed = false;

    public ChestService(
        GameController gameController,
        PickItSettings settings,
        IItemFilterService itemFilterService,
        IInputService inputService)
    {
        _gameController = gameController ?? throw new ArgumentNullException(nameof(gameController));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _itemFilterService = itemFilterService ?? throw new ArgumentNullException(nameof(itemFilterService));
        _inputService = inputService ?? throw new ArgumentNullException(nameof(inputService));
        
        _availableChests = new TimeCache<List<LabelOnGround>>(GetAvailableChestsInternal, 200);
    }

    public IEnumerable<LabelOnGround> GetAvailableChests()
    {
        ThrowIfDisposed();
        
        try
        {
            return _availableChests.Value ?? Enumerable.Empty<LabelOnGround>();
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[ChestService] Error getting available chests: {ex.Message}");
            return Enumerable.Empty<LabelOnGround>();
        }
    }

    public bool ShouldTargetChest(LabelOnGround chest, PickItItemData nearestItem = null)
    {
        ThrowIfDisposed();
        
        if (chest == null) return false;
        
        try
        {
            // Always target chest if no items to pick up
            if (nearestItem == null) return true;
            
            var chestDistance = chest.ItemOnGround.DistancePlayer;
            
            // Check if we should target nearby chests first
            if (_settings.ChestSettings.TargetNearbyChestsFirst && 
                chestDistance < _settings.ChestSettings.TargetNearbyChestsFirstRadius)
            {
                return true;
            }
            
            // Otherwise, target chest if it's closer or equal distance to nearest item
            return chestDistance <= nearestItem.Distance;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[ChestService] Error checking if should target chest: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> InteractWithChestAsync(LabelOnGround chest)
    {
        ThrowIfDisposed();
        
        if (chest == null) return false;
        
        try
        {
            var success = await _inputService.ClickItemAsync(chest.ItemOnGround, chest.Label, null);
            if (success)
            {
                // Force update chest list after interaction
                _availableChests.ForceUpdate();
            }
            return success;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[ChestService] Error interacting with chest: {ex.Message}");
            return false;
        }
    }

    private List<LabelOnGround> GetAvailableChestsInternal()
    {
        try
        {
            var chestPatterns = _settings.ChestSettings.ChestList.Content;
            if (chestPatterns == null || !chestPatterns.Any()) return new List<LabelOnGround>();

            // Find entities that match chest patterns
            var matchingEntities = _gameController.EntityListWrapper.OnlyValidEntities
                .Where(entity => IsChestEntity(entity, chestPatterns))
                .ToList();

            if (!matchingEntities.Any()) return new List<LabelOnGround>();

            // Get labels for matching entities
            var labels = _gameController?.Game?.IngameState?.IngameUi?.ItemsOnGroundLabelsVisible;
            if (labels == null) return new List<LabelOnGround>();

            var availableChests = new List<LabelOnGround>();
            
            foreach (var label in labels)
            {
                if (label.Address == 0 || !label.IsVisible) continue;
                
                if (matchingEntities.Any(entity => entity.Address == label.ItemOnGround.Address))
                {
                    availableChests.Add(label);
                }
            }

            return availableChests.OrderBy(chest => chest.ItemOnGround.DistancePlayer).ToList();
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[ChestService] Error getting available chests: {ex.Message}");
            return new List<LabelOnGround>();
        }
    }

    private bool IsChestEntity(Entity entity, IEnumerable<ChestPattern> chestPatterns)
    {
        if (entity == null || string.IsNullOrEmpty(entity.Metadata)) return false;
        
        try
        {
            // Entity must have a Chest component
            if (!entity.HasComponent<Chest>()) return false;
            
            // Check if entity matches any enabled chest patterns
            return chestPatterns.Any(pattern => 
                pattern.Enabled?.Value == true &&
                !string.IsNullOrEmpty(pattern.MetadataRegex?.Value) &&
                IsMetadataMatch(entity.Metadata, pattern.MetadataRegex.Value));
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[ChestService] Error checking if entity is chest: {ex.Message}");
            return false;
        }
    }

    private bool IsMetadataMatch(string metadata, string pattern)
    {
        try
        {
            var regex = _itemFilterService.GetCachedRegex(pattern);
            return regex?.IsMatch(metadata) == true;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[ChestService] Error matching metadata pattern '{pattern}': {ex.Message}");
            return false;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ChestService));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        _availableChests?.Dispose();
    }
} 