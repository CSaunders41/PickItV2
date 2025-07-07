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
        
        if (item == null) 
        {
            DebugWindow.LogMsg("[ItemFilterService] Item is null, not picking up");
            return false;
        }
        
        try
        {
            // Check if picking up everything
            if (_settings.PickUpEverything) 
            {
                DebugWindow.LogMsg($"[ItemFilterService] PickUpEverything enabled, picking up {item.BaseName}");
                return true;
            }
            
            _filtersLock.EnterReadLock();
            try
            {
                var activeFiltersCount = _activeFilters.Count;
                DebugWindow.LogMsg($"[ItemFilterService] Checking {item.BaseName} against {activeFiltersCount} active filters");
                
                if (activeFiltersCount == 0)
                {
                    DebugWindow.LogMsg("[ItemFilterService] No active filters loaded! Item will not be picked up.");
                    return false;
                }
                
                foreach (var filter in _activeFilters)
                {
                    try
                    {
                        if (filter.Matches(item))
                        {
                            DebugWindow.LogMsg($"[ItemFilterService] {item.BaseName} matched filter, will pickup");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugWindow.LogError($"[ItemFilterService] Error checking filter match for {item.BaseName}: {ex.Message}");
                    }
                }
                
                DebugWindow.LogMsg($"[ItemFilterService] {item.BaseName} did not match any filters, not picking up");
                return false;
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
            DebugWindow.LogMsg($"[ItemFilterService] Loading filters from directory: {configDirectory}");
            
            if (!Directory.Exists(configDirectory))
            {
                DebugWindow.LogError($"[ItemFilterService] Config directory does not exist: {configDirectory}");
                return;
            }

            // Check if config directory is empty and copy template files if needed
            await EnsureTemplateFilesExist(configDirectory);

            var newFilters = await LoadFiltersFromDirectoryAsync(configDirectory);
            
            _filtersLock.EnterWriteLock();
            try
            {
                _activeFilters = newFilters;
                DebugWindow.LogMsg($"[ItemFilterService] Successfully loaded {_activeFilters.Count} active filters");
                
                // Log which filters are active
                foreach (var filter in _activeFilters)
                {
                    DebugWindow.LogMsg($"[ItemFilterService] Active filter loaded: {filter.GetType().Name}");
                }
            }
            finally
            {
                _filtersLock.ExitWriteLock();
            }
            
            DebugWindow.LogMsg($"[ItemFilterService] Filter loading complete. Total active filters: {newFilters.Count}");
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

    private async Task EnsureTemplateFilesExist(string configDirectory)
    {
        try
        {
            // Check if any .ifl files exist in config directory
            var existingFiles = Directory.GetFiles(configDirectory, "*.ifl", SearchOption.AllDirectories);
            if (existingFiles.Length > 0)
            {
                DebugWindow.LogMsg($"[ItemFilterService] Found {existingFiles.Length} existing filter files");
                return; // Files already exist, no need to copy templates
            }

            // Try to find template files in common locations relative to the plugin
            var templateSources = GetPossibleTemplateLocations();
            
            foreach (var templateSource in templateSources)
            {
                if (Directory.Exists(templateSource))
                {
                    await CopyTemplateFiles(templateSource, configDirectory);
                    DebugWindow.LogMsg($"[ItemFilterService] Copied template files from: {templateSource}");
                    return;
                }
            }
            
            // If no template files found, create basic default filters
            DebugWindow.LogMsg("[ItemFilterService] No template files found. Creating basic default filters.");
            await CreateDefaultFilterFiles(configDirectory);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[ItemFilterService] Error ensuring template files exist: {ex.Message}");
        }
    }

    private async Task CreateDefaultFilterFiles(string configDirectory)
    {
        try
        {
            var defaultFilters = new Dictionary<string, string>
            {
                ["Currency.ifl"] = @"//----------------------------------------------
// Currency
//----------------------------------------------

ClassName.EndsWith(""Currency"")

//----------------------------------------------
// Main Currency (Uncomment to enable specific currencies)
//----------------------------------------------

//BaseName == ""Chaos Orb""
//BaseName == ""Divine Orb""
//BaseName == ""Exalted Orb""
//BaseName == ""Mirror of Kalandra""
//BaseName == ""Chromatic Orb""
//BaseName == ""Orb of Alchemy""
//BaseName == ""Orb of Fusing""
//BaseName == ""Vaal Orb""",

                ["Questing.ifl"] = @"//----------------------------------------------
// Quest Items
//----------------------------------------------

ClassName == ""QuestItem""",

                ["Uniques.ifl"] = @"//----------------------------------------------
// Uniques
//----------------------------------------------

Rarity == ItemRarity.Unique // All Uniques",

                ["Gems.ifl"] = @"//----------------------------------------------
// Skill Gems
//----------------------------------------------

//ClassName == ""Active Skill Gem""
//ClassName == ""Support Skill Gem""

// Specific valuable gems (uncomment to enable)
//BaseName == ""Empower Support""
//BaseName == ""Enlighten Support""
//BaseName == ""Enhance Support""",

                ["Basic.ifl"] = @"//----------------------------------------------
// Basic Starter Filter
//----------------------------------------------

// Pick up all currency
ClassName.EndsWith(""Currency"")

// Pick up all uniques
Rarity == ItemRarity.Unique

// Pick up quest items
ClassName == ""QuestItem""

// Pick up maps
ClassName == ""Map""

// Pick up divination cards
ClassName == ""DivinationCard"""
            };

            foreach (var (fileName, content) in defaultFilters)
            {
                var filePath = Path.Combine(configDirectory, fileName);
                if (!File.Exists(filePath))
                {
                    await File.WriteAllTextAsync(filePath, content);
                    DebugWindow.LogMsg($"[ItemFilterService] Created default filter: {fileName}");
                }
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[ItemFilterService] Error creating default filter files: {ex.Message}");
        }
    }

    private string[] GetPossibleTemplateLocations()
    {
        var serviceManager = PickItServiceManager.Instance;
        var pickItPlugin = serviceManager.GetService<PickIt>();
        
        if (pickItPlugin?.ConfigDirectory == null) return new string[0];
        
        var pluginConfigDir = pickItPlugin.ConfigDirectory;
        
        // Common locations where template files might be found
        return new string[]
        {
            // Relative to plugin config directory
            Path.Combine(Path.GetDirectoryName(pluginConfigDir) ?? "", "Pickit Rules"),
            Path.Combine(Path.GetDirectoryName(pluginConfigDir) ?? "", "PickitRules"),
            Path.Combine(Path.GetDirectoryName(pluginConfigDir) ?? "", "Templates"),
            
            // In plugin directory itself
            Path.Combine(pluginConfigDir, "Pickit Rules"),
            Path.Combine(pluginConfigDir, "PickitRules"),
            Path.Combine(pluginConfigDir, "Templates"),
            
            // Look for embedded resources or alongside plugin DLL
            Path.Combine(Path.GetDirectoryName(typeof(ItemFilterService).Assembly.Location) ?? "", "Pickit Rules"),
            Path.Combine(Path.GetDirectoryName(typeof(ItemFilterService).Assembly.Location) ?? "", "PickitRules"),
        };
    }

    private async Task CopyTemplateFiles(string sourceDirectory, string targetDirectory)
    {
        try
        {
            var templateFiles = Directory.GetFiles(sourceDirectory, "*.ifl", SearchOption.AllDirectories);
            
            foreach (var sourceFile in templateFiles)
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
                var targetFile = Path.Combine(targetDirectory, relativePath);
                
                // Create target directory if it doesn't exist
                var targetDir = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }
                
                // Copy file if it doesn't already exist
                if (!File.Exists(targetFile))
                {
                    await File.WriteAllTextAsync(targetFile, await File.ReadAllTextAsync(sourceFile));
                    DebugWindow.LogMsg($"[ItemFilterService] Copied template: {relativePath}");
                }
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[ItemFilterService] Error copying template files: {ex.Message}");
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
                    true)) // Enable new rules by default
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