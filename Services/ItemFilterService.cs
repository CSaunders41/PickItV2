using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.Shared;
using ItemFilterLibrary;

namespace PickIt.Services;

public class ItemFilterService : IItemFilterService, IDisposable
{
    private readonly GameController _gameController;
    private readonly PickItSettings _settings;
    private readonly ConcurrentDictionary<string, Regex> _regexCache = new();
    private readonly ReaderWriterLockSlim _filtersLock = new();
    private List<ItemFilter> _activeFilters = new();
    private volatile bool _disposed = false;
    private readonly SemaphoreSlim _loadingSemaphore = new(1, 1);

    public ItemFilterService(GameController gameController, PickItSettings settings)
    {
        _gameController = gameController ?? throw new ArgumentNullException(nameof(gameController));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public IReadOnlyList<ItemFilter> ActiveFilters
    {
        get
        {
            ThrowIfDisposed();
            
            _filtersLock.EnterReadLock();
            try
            {
                return _activeFilters.AsReadOnly();
            }
            finally
            {
                _filtersLock.ExitReadLock();
            }
        }
    }

    public bool ShouldPickupItem(PickItItemData item)
    {
        ThrowIfDisposed();
        
        if (item == null) return false;
        
        try
        {
            // Check if picking up everything
            if (_settings.PickUpEverything) return true;
            
            _filtersLock.EnterReadLock();
            try
            {
                return _activeFilters.Any(filter => filter.Matches(item));
            }
            finally
            {
                _filtersLock.ExitReadLock();
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[ItemFilterService] Error checking if item should be picked up: {ex.Message}");
            return false;
        }
    }

    public void LoadFilters()
    {
        ThrowIfDisposed();
        
        Task.Run(async () =>
        {
            try
            {
                await LoadFiltersAsync();
            }
            catch (Exception ex)
            {
                DebugWindow.LogError($"[ItemFilterService] Error loading filters: {ex.Message}");
            }
        });
    }

    public void ReloadFilters()
    {
        ThrowIfDisposed();
        LoadFilters();
    }

    public Regex GetCachedRegex(string pattern)
    {
        ThrowIfDisposed();
        
        if (string.IsNullOrEmpty(pattern)) return null;
        
        try
        {
            return _regexCache.GetOrAdd(pattern, p => new Regex(p, RegexOptions.Compiled));
        }
        catch (ArgumentException ex)
        {
            DebugWindow.LogError($"[ItemFilterService] Invalid regex pattern '{pattern}': {ex.Message}");
            return null;
        }
    }

    private async Task LoadFiltersAsync()
    {
        if (!await _loadingSemaphore.WaitAsync(TimeSpan.FromSeconds(30)))
        {
            DebugWindow.LogError("[ItemFilterService] Timeout waiting for filter loading semaphore");
            return;
        }

        try
        {
            var configDirectory = GetConfigDirectory();
            if (!Directory.Exists(configDirectory))
            {
                DebugWindow.LogError($"[ItemFilterService] Config directory does not exist: {configDirectory}");
                return;
            }

            var newFilters = await LoadFiltersFromDirectoryAsync(configDirectory);
            
            _filtersLock.EnterWriteLock();
            try
            {
                _activeFilters = newFilters;
            }
            finally
            {
                _filtersLock.ExitWriteLock();
            }
            
            DebugWindow.LogMsg($"[ItemFilterService] Loaded {newFilters.Count} filters");
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[ItemFilterService] Error in LoadFiltersAsync: {ex.Message}");
        }
        finally
        {
            _loadingSemaphore.Release();
        }
    }

    private async Task<List<ItemFilter>> LoadFiltersFromDirectoryAsync(string configDirectory)
    {
        var existingRules = _settings.PickitRules ?? new List<PickitRule>();
        var newFilters = new List<ItemFilter>();

        try
        {
            var diskFiles = Directory.GetFiles(configDirectory, "*.ifl", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .ToList();

            // Add new rules for files not in settings
            var newRules = diskFiles
                .Select(fileInfo => new PickitRule(
                    fileInfo.Name,
                    Path.GetRelativePath(configDirectory, fileInfo.FullName),
                    false))
                .Where(rule => !existingRules.Any(existing => existing.Location == rule.Location))
                .ToList();

            // Combine existing and new rules
            var allRules = existingRules.Concat(newRules).ToList();

            // Load filters for enabled rules
            var enabledRules = allRules.Where(rule => rule.Enabled).ToList();
            
            var loadTasks = enabledRules.Select(rule => LoadFilterAsync(configDirectory, rule));
            var loadedFilters = await Task.WhenAll(loadTasks);
            
            newFilters.AddRange(loadedFilters.Where(filter => filter != null));
            
            // Update settings with all rules (including new ones)
            _settings.PickitRules = allRules;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[ItemFilterService] Error loading filters from directory: {ex.Message}");
        }

        return newFilters;
    }

    private async Task<ItemFilter> LoadFilterAsync(string configDirectory, PickitRule rule)
    {
        var rulePath = Path.Combine(configDirectory, rule.Location);
        
        if (!File.Exists(rulePath))
        {
            DebugWindow.LogError($"[ItemFilterService] Filter file not found: {rulePath}");
            return null;
        }

        try
        {
            return await LoadItemFilterWithRetryAsync(rulePath);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[ItemFilterService] Error loading filter '{rule.Name}': {ex.Message}");
            return null;
        }
    }

    private async Task<ItemFilter> LoadItemFilterWithRetryAsync(string rulePath)
    {
        const int maxRetries = 3;
        const int baseDelayMs = 100;
        
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // Use async file operations to avoid blocking
                var content = await File.ReadAllTextAsync(rulePath);
                return ItemFilter.LoadFromString(content);
            }
            catch (IOException ex) when (attempt < maxRetries - 1)
            {
                var delay = baseDelayMs * (int)Math.Pow(2, attempt); // Exponential backoff
                DebugWindow.LogMsg($"[ItemFilterService] Retry {attempt + 1} for {rulePath} in {delay}ms: {ex.Message}");
                await Task.Delay(delay);
            }
        }
        
        throw new IOException($"Failed to load filter after {maxRetries} attempts: {rulePath}");
    }

    private string GetConfigDirectory()
    {
        var serviceManager = PickItServiceManager.Instance;
        var pickItPlugin = serviceManager.GetService<PickIt>();
        
        if (pickItPlugin?.ConfigDirectory == null)
        {
            throw new InvalidOperationException("Cannot get config directory - PickIt plugin not registered");
        }
        
        var configDirectory = pickItPlugin.ConfigDirectory;
        
        // Check for custom config directory
        if (!string.IsNullOrEmpty(_settings.CustomConfigDir))
        {
            var customConfigDirectory = Path.Combine(
                Path.GetDirectoryName(configDirectory) ?? string.Empty,
                _settings.CustomConfigDir);
                
            if (Directory.Exists(customConfigDirectory))
            {
                configDirectory = customConfigDirectory;
            }
            else
            {
                DebugWindow.LogError($"[ItemFilterService] Custom config directory does not exist: {customConfigDirectory}");
            }
        }

        return configDirectory;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ItemFilterService));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        
        try
        {
            _filtersLock.EnterWriteLock();
            try
            {
                _activeFilters.Clear();
            }
            finally
            {
                _filtersLock.ExitWriteLock();
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[ItemFilterService] Error during disposal: {ex.Message}");
        }
        finally
        {
            _filtersLock?.Dispose();
            _loadingSemaphore?.Dispose();
        }
    }
} 