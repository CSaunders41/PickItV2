using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.Shared.Cache;
using SharpDX;

namespace PickIt.Services;

public class PortalService : IPortalService, IDisposable
{
    private readonly GameController _gameController;
    private readonly CachedValue<LabelOnGround> _portalLabel;
    private volatile bool _disposed = false;
    private static readonly Regex PortalRegex = new(@"^Metadata/(MiscellaneousObjects|Effects/Microtransactions)/.*Portal", RegexOptions.Compiled);

    public PortalService(GameController gameController)
    {
        _gameController = gameController ?? throw new ArgumentNullException(nameof(gameController));
        _portalLabel = new TimeCache<LabelOnGround>(GetPortalLabel, 200);
    }

    public async Task<bool> CheckPortalInterferenceAsync(Element label)
    {
        ThrowIfDisposed();
        
        if (label == null) return false;
        
        try
        {
            if (!IsPortalNearby(label)) return false;
            
            // Check if portal is currently targeted
            if (IsPortalTargeted())
            {
                return true;
            }

            // Wait a bit and check again for portal targeting
            await Task.Delay(25);
            return IsPortalTargeted();
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PortalService] Error checking portal interference: {ex.Message}");
            return false;
        }
    }

    public bool IsPortalNearby(Element element)
    {
        ThrowIfDisposed();
        
        if (element == null) return false;
        
        try
        {
            var portalLabel = _portalLabel.Value;
            if (portalLabel == null) return false;
            
            var portalRect = portalLabel.Label.GetClientRectCache;
            var elementRect = element.GetClientRectCache;
            
            portalRect.Inflate(100, 100);
            elementRect.Inflate(100, 100);
            
            return portalRect.Intersects(elementRect);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PortalService] Error checking if portal is nearby: {ex.Message}");
            return false;
        }
    }

    public bool IsPortalTargeted()
    {
        ThrowIfDisposed();
        
        try
        {
            var portalLabel = _portalLabel.Value;
            if (portalLabel == null) return false;
            
            var ingameState = _gameController.IngameState;
            if (ingameState == null) return false;

            // Check multiple ways portal can be targeted
            return CheckPortalHover(ingameState, portalLabel) || 
                   CheckPortalTargetable(portalLabel);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PortalService] Error checking if portal is targeted: {ex.Message}");
            return false;
        }
    }

    private bool CheckPortalHover(IngameState ingameState, LabelOnGround portalLabel)
    {
        try
        {
            return ingameState.UIHover.Address == portalLabel.Address ||
                   ingameState.UIHover.Address == portalLabel.ItemOnGround.Address ||
                   ingameState.UIHover.Address == portalLabel.Label.Address ||
                   ingameState.UIHoverElement.Address == portalLabel.Address ||
                   ingameState.UIHoverElement.Address == portalLabel.ItemOnGround.Address ||
                   ingameState.UIHoverElement.Address == portalLabel.Label.Address ||
                   ingameState.UIHoverTooltip.Address == portalLabel.Address ||
                   ingameState.UIHoverTooltip.Address == portalLabel.ItemOnGround.Address ||
                   ingameState.UIHoverTooltip.Address == portalLabel.Label.Address;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PortalService] Error checking portal hover: {ex.Message}");
            return false;
        }
    }

    private bool CheckPortalTargetable(LabelOnGround portalLabel)
    {
        try
        {
            return portalLabel.ItemOnGround?.HasComponent<Targetable>() == true &&
                   portalLabel.ItemOnGround?.GetComponent<Targetable>()?.isTargeted == true;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PortalService] Error checking portal targetable: {ex.Message}");
            return false;
        }
    }

    private LabelOnGround GetPortalLabel()
    {
        try
        {
            var labels = _gameController?.Game?.IngameState?.IngameUi?.ItemsOnGroundLabels;
            if (labels == null) return null;

            foreach (var labelOnGround in labels)
            {
                if (labelOnGround?.Label is not { IsValid: true, Address: > 0, IsVisible: true })
                    continue;

                var itemOnGround = labelOnGround.ItemOnGround;
                if (itemOnGround?.Metadata == null) continue;

                if (PortalRegex.IsMatch(itemOnGround.Metadata))
                {
                    return labelOnGround;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PortalService] Error getting portal label: {ex.Message}");
            return null;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PortalService));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        _portalLabel?.Dispose();
    }
} 