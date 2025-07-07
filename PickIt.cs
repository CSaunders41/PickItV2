using ExileCore;
using ExileCore.Shared;
using ExileCore.Shared.Helpers;
using ItemFilterLibrary;
using PickIt.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PickIt;

public class PickIt : BaseSettingsPlugin<PickItSettings>
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
            
            DebugWindow.LogMsg("[PickIt] Successfully initialized with service architecture");
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
        
        // Core services
        _inventoryService = new InventoryService(GameController);
        _itemFilterService = new ItemFilterService(GameController, Settings);
        _inputService = new InputService(GameController, Settings);
        _portalService = new PortalService(GameController);
        
        // Dependent services
        _chestService = new ChestService(GameController, Settings, _itemFilterService, _inputService);
        _renderService = new RenderService(GameController, Settings, _inventoryService, _itemFilterService);
        
        // Main orchestrator service
        _pickItService = new PickItService(
            GameController,
            Settings,
            _inventoryService,
            _itemFilterService,
            _inputService,
            _chestService);
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
            
            if (workMode != WorkMode.Stop)
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

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        
        if (disposing)
        {
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
                
                // Clear service manager
                _serviceManager?.Clear();
                
                _servicesInitialized = false;
                
                DebugWindow.LogMsg("[PickIt] Successfully disposed all services");
            }
            catch (Exception ex)
            {
                DebugWindow.LogError($"[PickIt] Error during disposal: {ex.Message}");
            }
        }
        
        _disposed = true;
        base.Dispose(disposing);
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
} 