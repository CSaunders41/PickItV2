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
using System.Runtime.InteropServices;
using Vector2 = System.Numerics.Vector2;
using System.Collections.Generic;

namespace PickIt.Services;

public class InputService : IInputService, IDisposable
{
    private readonly GameController _gameController;
    private readonly PickItSettings _settings;
    private readonly Stopwatch _sinceLastClick = Stopwatch.StartNew();
    private volatile bool _disposed = false;
    private volatile bool _unclickedMouse = false;
    private DateTime _disableLazyLootingTill = DateTime.MinValue;

    // Windows API for getting cursor position
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    // Windows API for getting key/mouse button states
    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    // Virtual key codes for mouse buttons
    private const int VK_LBUTTON = 0x01;
    private const int VK_RBUTTON = 0x02;
    private const int VK_MBUTTON = 0x04;
    private const int VK_XBUTTON1 = 0x05;
    private const int VK_XBUTTON2 = 0x06;

    /// <summary>
    /// Captures the current state of mouse buttons for restoration later
    /// </summary>
    private struct MouseButtonState
    {
        public bool LeftButton;
        public bool RightButton;
        public bool MiddleButton;
        public bool XButton1;
        public bool XButton2;

        public static MouseButtonState Capture()
        {
            return new MouseButtonState
            {
                LeftButton = (GetKeyState(VK_LBUTTON) & 0x8000) != 0,
                RightButton = (GetKeyState(VK_RBUTTON) & 0x8000) != 0,
                MiddleButton = (GetKeyState(VK_MBUTTON) & 0x8000) != 0,
                XButton1 = (GetKeyState(VK_XBUTTON1) & 0x8000) != 0,
                XButton2 = (GetKeyState(VK_XBUTTON2) & 0x8000) != 0
            };
        }

        public override string ToString()
        {
            var pressed = new List<string>();
            if (LeftButton) pressed.Add("Left");
            if (RightButton) pressed.Add("Right");
            if (MiddleButton) pressed.Add("Middle");
            if (XButton1) pressed.Add("X1");
            if (XButton2) pressed.Add("X2");
            return pressed.Count > 0 ? string.Join(", ", pressed) : "None";
        }
    }

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

            // Mouse state restoration feature for lazy looting
            // Captures both mouse position and button states before clicking and restores them after
            // This prevents the mouse from being left at the item location with different button states
            Vector2? originalMousePosition = null;
            MouseButtonState originalButtonState = default;
            var shouldRestoreMouseState = ShouldRestoreMousePosition();
            
            if (shouldRestoreMouseState)
            {
                originalMousePosition = GetCursorPos();
                originalButtonState = MouseButtonState.Capture();
                
                if (originalMousePosition.HasValue)
                {
                    DebugWindow.LogMsg($"[InputService] Captured original mouse state - Position: {originalMousePosition.Value}, Buttons: [{originalButtonState}]");
                }
            }

            // Set cursor position and click
            DebugWindow.LogMsg($"[InputService] Setting cursor position to {position}");
            SetCursorPos(position);
            
            // Small delay to ensure cursor position is set
            await Task.Delay(25);
            
            // Click the item
            DebugWindow.LogMsg($"[InputService] Executing left click at {position}");
            LeftClick();
            
            // Reset timing
            _sinceLastClick.Restart();
            
            // Restore original mouse state (position and buttons) if feature is enabled
            if (shouldRestoreMouseState && originalMousePosition.HasValue)
            {
                // Small delay to ensure click is processed before restoring mouse state
                await Task.Delay(50);
                
                // Restore mouse position first
                SetCursorPos(originalMousePosition.Value);
                
                // Small delay between position and button restoration
                await Task.Delay(25);
                
                // Restore mouse button states
                RestoreMouseButtonState(originalButtonState);
                
                DebugWindow.LogMsg($"[InputService] Restored complete mouse state - Position: {originalMousePosition.Value}, Buttons: [{originalButtonState}]");
            }
            
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

    private void RightDown()
    {
        try
        {
            Input.RightDown();
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error sending right down: {ex.Message}");
        }
    }

    private void RightUp()
    {
        try
        {
            Input.RightUp();
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error sending right up: {ex.Message}");
        }
    }



    /// <summary>
    /// Restores the mouse button states to their previously captured state
    /// Only restores Left and Right buttons as these are the only ones supported by ExileCore Input class
    /// </summary>
    private void RestoreMouseButtonState(MouseButtonState originalState)
    {
        try
        {
            // Get current state to compare
            var currentState = MouseButtonState.Capture();
            
            // Restore left button state
            if (originalState.LeftButton && !currentState.LeftButton)
            {
                LeftDown();
                DebugWindow.LogMsg($"[InputService] Restored left button to DOWN state");
            }
            else if (!originalState.LeftButton && currentState.LeftButton)
            {
                LeftUp();
                DebugWindow.LogMsg($"[InputService] Restored left button to UP state");
            }
            
            // Restore right button state
            if (originalState.RightButton && !currentState.RightButton)
            {
                RightDown();
                DebugWindow.LogMsg($"[InputService] Restored right button to DOWN state");
            }
            else if (!originalState.RightButton && currentState.RightButton)
            {
                RightUp();
                DebugWindow.LogMsg($"[InputService] Restored right button to UP state");
            }
            
            // Note: Middle, X1, and X2 buttons are captured for completeness but not restored
            // as ExileCore Input class doesn't provide methods for these buttons
            
            DebugWindow.LogMsg($"[InputService] Mouse button state restoration completed - Original: [{originalState}], Current: [{currentState}]");
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error restoring mouse button state: {ex.Message}");
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

    private Vector2? GetCursorPos()
    {
        try
        {
            if (GetCursorPos(out POINT point))
            {
                return new Vector2(point.X, point.Y);
            }
            return null;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error getting cursor position: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Determines if mouse state (position and button states) should be restored after clicking.
    /// This feature only applies when lazy looting is enabled and the user has enabled the setting.
    /// </summary>
    private bool ShouldRestoreMousePosition()
    {
        try
        {
            var workMode = GetCurrentWorkMode();
            return workMode == WorkMode.Lazy && 
                   _settings?.RestoreMousePositionAfterLazyLoot?.Value == true;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error checking if mouse state should be restored: {ex.Message}");
            return false;
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