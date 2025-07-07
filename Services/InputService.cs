using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using SharpDX;
using System.Numerics;
using Vector2 = System.Numerics.Vector2;

namespace PickIt.Services;

public class InputService : IInputService, IDisposable
{
    private readonly GameController _gameController;
    private readonly PickItSettings _settings;
    private readonly Stopwatch _sinceLastClick = Stopwatch.StartNew();
    private volatile bool _disposed = false;
    private volatile bool _unclickedMouse = false;
    private DateTime _disableLazyLootingTill = DateTime.MinValue;

    public InputService(GameController gameController, PickItSettings settings)
    {
        _gameController = gameController ?? throw new ArgumentNullException(nameof(gameController));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void RegisterHotkeys()
    {
        ThrowIfDisposed();
        
        try
        {
            _settings.PickUpKey.OnValueChanged += () => RegisterKey(_settings.PickUpKey);
            _settings.ProfilerHotkey.OnValueChanged += () => RegisterKey(_settings.ProfilerHotkey);

            RegisterKey(_settings.PickUpKey);
            RegisterKey(_settings.ProfilerHotkey);
            RegisterKey(Keys.Escape);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error registering hotkeys: {ex.Message}");
        }
    }

    public WorkMode GetCurrentWorkMode()
    {
        ThrowIfDisposed();
        
        try
        {
            if (_gameController?.Window?.IsForeground() != true ||
                _settings?.Enable?.Value != true)
            {
                return WorkMode.Stop;
            }

            // Check for escape key
            if (GetKeyState(Keys.Escape))
            {
                return WorkMode.Stop;
            }

            // Check for manual pickup key
            var pickupKey = _settings?.PickUpKey?.Value;
            if (pickupKey != null && GetKeyState(pickupKey.Value))
            {
                return WorkMode.Manual;
            }

            // Check for lazy loot mode
            if (CanLazyLoot())
            {
                return WorkMode.Lazy;
            }

            return WorkMode.Stop;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error getting work mode: {ex.Message}");
            return WorkMode.Stop;
        }
    }

    public async Task<bool> ClickItemAsync(Entity item, Element label, RectangleF? customRect = null)
    {
        ThrowIfDisposed();
        
        if (item == null || label == null) return false;

        try
        {
            const int maxRetries = 3;
            var tryCount = 0;
            
            while (tryCount < maxRetries)
            {
                if (!IsLabelClickable(label, customRect))
                {
                    return true; // Return true like original - let caller handle
                }

                var ignoreMoving = _settings?.IgnoreMoving == true;
                var itemDistanceToIgnoreMoving = _settings?.ItemDistanceToIgnoreMoving?.Value ?? 0;
                
                if (!ignoreMoving && IsPlayerMoving())
                {
                    if (item.DistancePlayer > itemDistanceToIgnoreMoving)
                    {
                        await TaskUtils.NextFrame();
                        continue;
                    }
                }

                var useMagicInput = _settings?.UseMagicInput == true;
                
                if (useMagicInput)
                {
                    DebugWindow.LogMsg($"[InputService] Attempting MagicInput click on item at distance {item.DistancePlayer}");
                    await TryMagicInputClick(item);
                }
                else
                {
                    DebugWindow.LogMsg($"[InputService] Attempting regular click on item at distance {item.DistancePlayer}");
                    await TryRegularClick(item, label, customRect);
                }

                tryCount++; // Always increment, like original
                await TaskUtils.NextFrame();
            }

            return true; // Always return true like original
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error clicking item: {ex.Message}");
            return true; // Return true even on error, like original
        }
    }

    public bool IsLabelClickable(Element element, RectangleF? customRect = null)
    {
        ThrowIfDisposed();
        
        if (element is not { IsValid: true, IsVisible: true, IndexInParent: not null })
        {
            return false;
        }

        try
        {
            var center = (customRect ?? element.GetClientRect()).Center;
            var gameWindowRect = _gameController.Window.GetWindowRectangleTimeCache with { Location = SharpDX.Vector2.Zero };
            gameWindowRect.Inflate(-36, -36);
            return gameWindowRect.Contains(center.X, center.Y);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error checking if label is clickable: {ex.Message}");
            return false;
        }
    }

    public void UpdateLazyLootingState()
    {
        ThrowIfDisposed();
        
        try
        {
            var lazyLootingPauseKey = _settings?.LazyLootingPauseKey?.Value;
            if (lazyLootingPauseKey != null && GetKeyState(lazyLootingPauseKey.Value))
            {
                _disableLazyLootingTill = DateTime.Now.AddSeconds(2);
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error updating lazy looting state: {ex.Message}");
        }
    }

    public void HandleMouseStateOnRender()
    {
        ThrowIfDisposed();
        
        try
        {
            if (_unclickedMouse)
            {
                _unclickedMouse = false;
                if (!IsKeyDown(Keys.LButton))
                {
                    LeftDown();
                }
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error handling mouse state: {ex.Message}");
        }
    }

    private async Task TryMagicInputClick(Entity item)
    {
        var unclickLeftMouseButton = _settings?.UnclickLeftMouseButton == true;
        var pauseBetweenClicks = _settings?.PauseBetweenClicks?.Value ?? 0;
        
        if (unclickLeftMouseButton && IsKeyDown(Keys.LButton))
        {
            _unclickedMouse = true;
            LeftUp();
        }

        if (_sinceLastClick.ElapsedMilliseconds > pauseBetweenClicks)
        {
            try
            {
                // Direct call like original - no error handling that could break flow
                var magicInputMethod = _gameController.PluginBridge.GetMethod<Action<Entity, uint>>("MagicInput.CastSkillWithTarget");
                magicInputMethod(item, 0x400);
                _sinceLastClick.Restart();
            }
            catch (Exception ex)
            {
                DebugWindow.LogError($"[InputService] MagicInput error: {ex.Message}");
            }
        }
    }

    private async Task TryRegularClick(Entity item, Element label, RectangleF? customRect)
    {
        try
        {
            // Calculate click position
            var position = CalculateClickPosition(label, customRect);
            DebugWindow.LogMsg($"[InputService] Calculated click position: {position}");

            // Check timing delay
            var pauseBetweenClicks = _settings?.PauseBetweenClicks?.Value ?? 0;
            if (_sinceLastClick.ElapsedMilliseconds <= pauseBetweenClicks)
            {
                DebugWindow.LogMsg($"[InputService] Waiting for click delay ({_sinceLastClick.ElapsedMilliseconds}ms < {pauseBetweenClicks}ms)");
                return;
            }

            // Simple approach: Set cursor position and click immediately
            DebugWindow.LogMsg($"[InputService] Setting cursor position to {position}");
            SetCursorPos(position);
            
            // Small delay to ensure cursor position is set
            await Task.Delay(25);
            
            // Click the item
            DebugWindow.LogMsg($"[InputService] Executing left click at {position}");
            LeftClick();
            
            // Reset timing
            _sinceLastClick.Restart();
            
            DebugWindow.LogMsg($"[InputService] Click completed successfully for item at distance {item.DistancePlayer}");
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error in TryRegularClick: {ex.Message}");
        }
    }



    private Vector2 CalculateClickPosition(Element label, RectangleF? customRect)
    {
        var labelRect = customRect ?? label.GetClientRect();
        var randomOffset = labelRect.ClickRandomNum(5, 3);
        var windowOffset = _gameController.Window.GetWindowRectangleTimeCache.TopLeft.ToVector2Num();
        return randomOffset + windowOffset;
    }

    private async Task<bool> CheckPortalInterference(Element label)
    {
        try
        {
            var portalService = PickItServiceManager.Instance.GetService<IPortalService>();
            if (portalService != null)
            {
                return await portalService.CheckPortalInterferenceAsync(label);
            }
            return false;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error checking portal interference: {ex.Message}");
            return false;
        }
    }

    private bool CanLazyLoot()
    {
        var lazyLooting = _settings?.LazyLooting == true;
        if (!lazyLooting) return false;
        
        if (_disableLazyLootingTill > DateTime.Now) return false;
        
        try
        {
            var noLazyLootingWhileEnemyClose = _settings?.NoLazyLootingWhileEnemyClose == true;
            var pickupRange = _settings?.PickupRange?.Value ?? 100;
            
            if (noLazyLootingWhileEnemyClose)
            {
                var entityListWrapper = _gameController?.EntityListWrapper;
                if (entityListWrapper == null) return true; // If we can't check, allow lazy loot

                // Use TryGetValue instead of GetValueOrDefault for older .NET compatibility
                var validEntitiesByType = entityListWrapper.ValidEntitiesByType;
                if (validEntitiesByType == null || !validEntitiesByType.TryGetValue(EntityType.Monster, out var monsters))
                {
                    return true; // If no monsters collection, allow lazy loot
                }
                
                foreach (var monster in monsters)
                {
                    if (monster?.GetComponent<Monster>() == null || 
                        !monster.IsValid || 
                        !monster.IsHostile || 
                        !monster.IsAlive ||
                        monster.IsHidden || 
                        monster.Path?.Contains("ElementalSummoned") == true)
                    {
                        continue;
                    }

                    var renderComponent = monster.GetComponent<Render>();
                    if (renderComponent == null) continue;

                    var playerPos = _gameController?.Player?.PosNum;
                    if (playerPos == null) continue;

                    var distance = System.Numerics.Vector3.Distance(
                        playerPos.Value,
                        renderComponent.PosNum);
                        
                    if (distance < pickupRange)
                    {
                        return false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error checking lazy loot conditions: {ex.Message}");
            return false;
        }

        return true;
    }

    private bool IsPlayerMoving()
    {
        try
        {
            var player = _gameController?.Player;
            if (player == null) return false;

            var actorComponent = player.GetComponent<Actor>();
            if (actorComponent == null) return false;

            return actorComponent.isMoving;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error checking if player is moving: {ex.Message}");
            return false;
        }
    }

    // Wrapper methods for Input class to centralize input handling
    private void RegisterKey(Keys key)
    {
        try
        {
            Input.RegisterKey(key);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error registering key {key}: {ex.Message}");
        }
    }

    private bool GetKeyState(Keys key)
    {
        try
        {
            return Input.GetKeyState(key);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error getting key state for {key}: {ex.Message}");
            return false;
        }
    }

    private bool IsKeyDown(Keys key)
    {
        try
        {
            return Input.IsKeyDown(key);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error checking if key is down {key}: {ex.Message}");
            return false;
        }
    }

    private void LeftDown()
    {
        try
        {
            Input.LeftDown();
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error sending left down: {ex.Message}");
        }
    }

    private void LeftUp()
    {
        try
        {
            Input.LeftUp();
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error sending left up: {ex.Message}");
        }
    }

    private void LeftClick()
    {
        try
        {
            Input.Click(MouseButtons.Left);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error sending left click: {ex.Message}");
        }
    }

    private void SetCursorPos(Vector2 position)
    {
        try
        {
            Input.SetCursorPos(position);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error setting cursor position: {ex.Message}");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(InputService));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
} 