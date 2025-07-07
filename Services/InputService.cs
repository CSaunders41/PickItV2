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
            if (!_gameController.Window.IsForeground() ||
                !_settings.Enable ||
                GetKeyState(Keys.Escape))
            {
                return WorkMode.Stop;
            }

            if (GetKeyState(_settings.PickUpKey.Value))
            {
                return WorkMode.Manual;
            }

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
                    return false;
                }

                if (!_settings.IgnoreMoving && IsPlayerMoving())
                {
                    if (item.DistancePlayer > _settings.ItemDistanceToIgnoreMoving.Value)
                    {
                        await TaskUtils.NextFrame();
                        continue;
                    }
                }

                if (_settings.UseMagicInput)
                {
                    if (await TryMagicInputClick(item))
                    {
                        tryCount++;
                    }
                }
                else
                {
                    if (await TryRegularClick(item, label, customRect))
                    {
                        tryCount++;
                    }
                }

                await TaskUtils.NextFrame();
            }

            return true;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error clicking item: {ex.Message}");
            return false;
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
            if (GetKeyState(_settings.LazyLootingPauseKey))
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

    private async Task<bool> TryMagicInputClick(Entity item)
    {
        if (_settings.UnclickLeftMouseButton && IsKeyDown(Keys.LButton))
        {
            _unclickedMouse = true;
            LeftUp();
        }

        if (_sinceLastClick.ElapsedMilliseconds > _settings.PauseBetweenClicks)
        {
            if (await TryInvokeMagicInput(item))
            {
                _sinceLastClick.Restart();
                return true;
            }
        }

        return false;
    }

    private async Task<bool> TryRegularClick(Entity item, Element label, RectangleF? customRect)
    {
        if (_sinceLastClick.ElapsedMilliseconds <= _settings.PauseBetweenClicks)
        {
            return false;
        }

        var position = CalculateClickPosition(label, customRect);
        
        if (!IsTargeted(item, label))
        {
            return await SetCursorPositionAsync(position, item, label);
        }

        if (await CheckPortalInterference(label))
        {
            return true;
        }

        if (!IsTargeted(item, label))
        {
            await TaskUtils.NextFrame();
            return false;
        }

        LeftClick();
        _sinceLastClick.Restart();
        return true;
    }

    private async Task<bool> TryInvokeMagicInput(Entity item)
    {
        try
        {
            var magicInputMethod = _gameController.PluginBridge.GetMethod<Action<Entity, uint>>("MagicInput.CastSkillWithTarget");
            if (magicInputMethod != null)
            {
                magicInputMethod(item, 0x400);
                return true;
            }
            else
            {
                DebugWindow.LogError("[InputService] MagicInput plugin not available");
                return false;
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error invoking MagicInput: {ex.Message}");
            return false;
        }
    }

    private Vector2 CalculateClickPosition(Element label, RectangleF? customRect)
    {
        var labelRect = customRect ?? label.GetClientRect();
        var randomOffset = labelRect.ClickRandomNum(5, 3);
        var windowOffset = _gameController.Window.GetWindowRectangleTimeCache.TopLeft.ToVector2Num();
        return randomOffset + windowOffset;
    }

    private async Task<bool> SetCursorPositionAsync(Vector2 position, Entity item, Element label)
    {
        try
        {
            SetCursorPos(position);
            using var cancellationToken = new CancellationTokenSource(TimeSpan.FromMilliseconds(60));
            return await TaskUtils.CheckEveryFrame(() => IsTargeted(item, label), cancellationToken.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error setting cursor position: {ex.Message}");
            return false;
        }
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
        if (!_settings.LazyLooting) return false;
        if (_disableLazyLootingTill > DateTime.Now) return false;
        
        try
        {
            if (_settings.NoLazyLootingWhileEnemyClose)
            {
                var monsters = _gameController.EntityListWrapper.ValidEntitiesByType[EntityType.Monster];
                
                foreach (var monster in monsters)
                {
                    if (monster?.GetComponent<Monster>() == null || 
                        !monster.IsValid || 
                        !monster.IsHostile || 
                        !monster.IsAlive ||
                        monster.IsHidden || 
                        monster.Path.Contains("ElementalSummoned"))
                    {
                        continue;
                    }

                    var distance = System.Numerics.Vector3.Distance(
                        _gameController.Player.PosNum,
                        monster.GetComponent<Render>().PosNum);
                        
                    if (distance < _settings.PickupRange)
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
            return _gameController.Player.GetComponent<Actor>().isMoving;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error checking if player is moving: {ex.Message}");
            return false;
        }
    }

    private static bool IsTargeted(Entity item, Element label)
    {
        if (item == null) return false;
        
        try
        {
            var targetable = item.GetComponent<Targetable>();
            if (targetable?.isTargeted is { } isTargeted)
            {
                return isTargeted;
            }

            return label is { HasShinyHighlight: true };
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[InputService] Error checking if targeted: {ex.Message}");
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