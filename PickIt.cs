using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Cache;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using ItemFilterLibrary;
using PickIt.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore.PoEMemory;
using SharpDX;
using SDxVector2 = SharpDX.Vector2;
using SDxVector3 = SharpDX.Vector3;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace PickIt;

public partial class PickIt : BaseSettingsPlugin<PickItSettings>
{
    // Services
    private IPickItService _pickItService;
    private IInventoryService _inventoryService;
    private IItemFilterService _itemFilterService;
    private IInputService _inputService;
    private IRenderService _renderService;
    private IChestService _chestService;
    private IPortalService _portalService;
    private IValidationService _validationService;
    private IDeathAwarenessService _deathAwarenessService;

    // Service manager
    private PickItServiceManager _serviceManager;
    private bool _servicesInitialized = false;
    private volatile bool _disposed = false;

    public PickIt()
    {
        Name = "PickIt With Linq";
    }

    public override bool Initialise()
    {
        try
        {
            // Initialize service manager
            _serviceManager = PickItServiceManager.Instance;
            
            // Initialize all services
            InitializeServices();
            
            // Register services with service manager
            RegisterServices();
            
            // Register hotkeys
            _inputService.RegisterHotkeys();
            
            // Load item filters
            _itemFilterService.LoadFilters();
            
            // Register plugin bridge methods
            RegisterPluginBridgeMethods();
            
            _servicesInitialized = true;
            
            DebugWindow.LogMsg("[PickIt] Successfully initialized with service architecture and death awareness");
            return true;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickIt] Failed to initialize: {ex.Message}");
            return false;
        }
    }

    public override Job Tick()
    {
        if (!_servicesInitialized || _disposed) return null;
        
        try
        {
            // Update death awareness
            _deathAwarenessService.CheckDeathStatus();
            
            // Update inventory
            _inventoryService.RefreshInventory();
            
            // Update input state
            _inputService.UpdateLazyLootingState();
            
            // Handle profiling hotkey
            HandleProfilingHotkey();
            
            return null;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickIt] Error in Tick: {ex.Message}");
            return null;
        }
    }

    public override void Render()
    {
        if (!_servicesInitialized || _disposed) return;
        
        try
        {
            // Handle mouse state
            _inputService.HandleMouseStateOnRender();
            
            // Render all UI elements
            _renderService.RenderAll();
            
            // Render pickup range and death status
            RenderPickupRange();
            RenderDeathStatus();
            
            // Handle pickup task
            HandlePickupTask();
            
            // Handle debug filter testing
            HandleDebugFilterTesting();
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickIt] Error in Render: {ex.Message}");
        }
    }

    private void InitializeServices()
    {
        // Validation service
        _validationService = new ValidationService();
        
        // Validate and clamp settings
        _validationService.ClampConfigurationValues(Settings);
        
        // Death awareness service (initialized early)
        _deathAwarenessService = new DeathAwarenessService(GameController, Settings);
        
        // Core services
        _inventoryService = new InventoryService(GameController);
        _itemFilterService = new ItemFilterService(GameController, Settings);
        _inputService = new InputService(GameController, Settings);
        _portalService = new PortalService(GameController);
        
        // Dependent services
        _chestService = new ChestService(GameController, Settings, _itemFilterService, _inputService);
        _renderService = new RenderService(GameController, Settings, _inventoryService, _itemFilterService, Graphics);
        
        // Main orchestrator service (now includes death awareness)
        _pickItService = new PickItService(
            GameController,
            Settings,
            _inventoryService,
            _itemFilterService,
            _inputService,
            _chestService,
            _deathAwarenessService);
    }

    private void RegisterServices()
    {
        _serviceManager.RegisterService<PickIt>(this);
        _serviceManager.RegisterService<IPickItService>(_pickItService);
        _serviceManager.RegisterService<IInventoryService>(_inventoryService);
        _serviceManager.RegisterService<IItemFilterService>(_itemFilterService);
        _serviceManager.RegisterService<IInputService>(_inputService);
        _serviceManager.RegisterService<IRenderService>(_renderService);
        _serviceManager.RegisterService<IChestService>(_chestService);
        _serviceManager.RegisterService<IPortalService>(_portalService);
        _serviceManager.RegisterService<IValidationService>(_validationService);
        _serviceManager.RegisterService<IDeathAwarenessService>(_deathAwarenessService);
    }

    private void RegisterPluginBridgeMethods()
    {
        try
        {
            // Register plugin bridge methods for external plugin communication
            GameController.PluginBridge.SaveMethod("PickIt.ListItems", 
                () => _pickItService.GetItemsToPickup(false).Select(x => x.QueriedItem).ToList());
            
            GameController.PluginBridge.SaveMethod("PickIt.IsActive", 
                () => _pickItService.IsPickingUp);
            
            GameController.PluginBridge.SaveMethod("PickIt.SetWorkMode", 
                (bool running) => { /* This will be handled by the InputService */ });
                
            GameController.PluginBridge.SaveMethod("PickIt.GetActiveFilters",
                () => _itemFilterService.ActiveFilters);
                
            GameController.PluginBridge.SaveMethod("PickIt.ReloadFilters",
                () => _itemFilterService.ReloadFilters());
                
            // New death awareness bridge methods
            GameController.PluginBridge.SaveMethod("PickIt.IsPlayerDead",
                () => _deathAwarenessService.IsPlayerDead);
                
            GameController.PluginBridge.SaveMethod("PickIt.GetPickupStatistics",
                () => _deathAwarenessService.GetPickupStatistics());
                
            GameController.PluginBridge.SaveMethod("PickIt.ResetDeathState",
                () => _deathAwarenessService.ResetDeathState());
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickIt] Error registering plugin bridge methods: {ex.Message}");
        }
    }

    private void HandleProfilingHotkey()
    {
        try
        {
            if (Input.GetKeyState(Settings.ProfilerHotkey.Value))
            {
                var sw = Stopwatch.StartNew();
                var nearestItem = _pickItService.GetItemsToPickup(false).FirstOrDefault();
                sw.Stop();
                
                var itemInfo = nearestItem != null ? 
                    $"Item: {nearestItem.BaseName} Distance: {nearestItem.Distance}" : 
                    "No items found";
                    
                DebugWindow.LogMsg($"[PickIt] GetItemsToPickup Elapsed: {sw.ElapsedTicks} ticks. {itemInfo}");
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickIt] Error in profiling: {ex.Message}");
        }
    }

    private void HandlePickupTask()
    {
        try
        {
            var workMode = _inputService.GetCurrentWorkMode();
            
            // Check if death awareness allows pickup
            if (workMode != WorkMode.Stop && _deathAwarenessService.ShouldAllowPickup())
            {
                _pickItService.StartPickupTask();
            }
            else
            {
                _pickItService.StopPickupTask();
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickIt] Error handling pickup task: {ex.Message}");
        }
    }

    private void RenderPickupRange()
    {
        try
        {
            if (!Settings.SmartPickupSettings.ShowPickupRange) return;
            
            var player = GameController.Player;
            if (player == null) return;
            
            var playerPos = new SDxVector3(player.Pos.X, player.Pos.Y, player.Pos.Z);
            var range = Settings.PickupRange;
            
            // Fixed green color with good visibility
            var visibleColor = new Color(0, 255, 0, 200);
            
            DrawEllipseToWorld(playerPos, range, 25, 2, visibleColor);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickIt] Error rendering pickup range: {ex.Message}");
        }
    }

    private void RenderDeathStatus()
    {
        try
        {
            if (!Settings.SmartPickupSettings.ShowDebugInfo) return;
            
            // Fixed position in top-left corner
            var screenPos = new Vector2(10f, 200f);
            
            // Get death status
            var deathStatus = _deathAwarenessService.GetStatusString();
            var statistics = _deathAwarenessService.GetPickupStatistics();
            
            // Create simplified status text
            var statusText = $"PickIt Status: {deathStatus}\n" +
                           $"Success Rate: {statistics.SuccessRate:F1}%\n" +
                           $"Attempts: {statistics.TotalAttempts} | Successful: {statistics.SuccessfulPickups}";
            
            // Choose color based on death state
            var textColor = _deathAwarenessService.IsPlayerDead ? Color.Red : 
                           _deathAwarenessService.IsWaitingForResurrection ? Color.Yellow : Color.LightGreen;
            
            Graphics.DrawText(statusText, screenPos, textColor, 14);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickIt] Error rendering debug info: {ex.Message}");
        }
    }

    private void DrawEllipseToWorld(SDxVector3 vector3Pos, int radius, int points, int lineWidth, Color color)
    {
        try
        {
            var camera = GameController.Game.IngameState.Camera;
            var playerPos = GameController.Player.Pos;
            
            if (Math.Abs(vector3Pos.Z - playerPos.Z) > 50) return;
            
            var step = 2.0f * Math.PI / points;
            var prevPoint = SDxVector2.Zero;
            
            for (int i = 0; i <= points; i++)
            {
                var angle = i * step;
                var x = (float)(vector3Pos.X + radius * Math.Cos(angle));
                var y = (float)(vector3Pos.Y + radius * Math.Sin(angle));
                var z = vector3Pos.Z;
                
                var worldPoint = new SDxVector3(x, y, z);
                var screenPoint = camera.WorldToScreen(worldPoint);
                
                if (i > 0 && prevPoint != SDxVector2.Zero)
                {
                    Graphics.DrawLine(prevPoint, screenPoint, lineWidth, color);
                }
                
                prevPoint = screenPoint;
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickIt] Error drawing range ellipse: {ex.Message}");
        }
    }

    private void HandleDebugFilterTesting()
    {
        try
        {
            if (Settings.FilterTest.Value is { Length: > 0 } &&
                GameController.IngameState.UIHover is { Address: not 0 } hovered &&
                hovered.Entity.IsValid)
            {
                var testFilter = ItemFilter.LoadFromString(Settings.FilterTest);
                var itemData = new ItemData(hovered.Entity, GameController);
                var matched = testFilter.Matches(itemData);
                DebugWindow.LogMsg($"[PickIt] Debug filter test: {matched}");
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickIt] Error in debug filter testing: {ex.Message}");
        }
    }

    public override void OnClose()
    {
        try
        {
            Dispose();
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickIt] Error during close: {ex.Message}");
        }
        finally
        {
            base.OnClose();
        }
    }

    public override void Dispose()
    {
        if (_disposed) return;
        
        try
        {
            // Stop any running tasks
            _pickItService?.StopPickupTask();
            
            // Dispose services
            _pickItService?.Dispose();
            _inventoryService?.Dispose();
            _itemFilterService?.Dispose();
            _inputService?.Dispose();
            _renderService?.Dispose();
            _chestService?.Dispose();
            _portalService?.Dispose();
            _deathAwarenessService?.Dispose();
            
            // Clear service manager
            _serviceManager?.Clear();
            
            _servicesInitialized = false;
            
            DebugWindow.LogMsg("[PickIt] Successfully disposed all services including death awareness");
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[PickIt] Error during disposal: {ex.Message}");
        }
        finally
        {
            _disposed = true;
            base.Dispose();
        }
    }

    // Public API for external access
    public IPickItService PickItService => _pickItService;
    public IInventoryService InventoryService => _inventoryService;
    public IItemFilterService ItemFilterService => _itemFilterService;
    public IInputService InputService => _inputService;
    public IRenderService RenderService => _renderService;
    public IChestService ChestService => _chestService;
    public IPortalService PortalService => _portalService;
    public IValidationService ValidationService => _validationService;
    public IDeathAwarenessService DeathAwarenessService => _deathAwarenessService;
} 