using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using ExileCore;
using ImGuiNET;
using PickIt.Services;
using System.Collections.Generic;
using System.Linq;

namespace PickIt;

public static class RulesDisplay
{
    public static void DrawSettings()
    {
        try
        {
            var serviceManager = PickItServiceManager.Instance;
            var plugin = serviceManager.GetService<PickIt>();
            var itemFilterService = serviceManager.GetService<IItemFilterService>();
            
            if (plugin == null || itemFilterService == null)
            {
                ImGui.Text("Services not available");
                return;
            }

            ImGui.Separator();
            
            if (ImGui.Button("Open Filter Folder"))
            {
                OpenFilterFolder(plugin);
            }

            if (ImGui.Button("Reload Rules"))
            {
                ReloadRules(itemFilterService);
            }

            if (ImGui.Button("Create Default Filters"))
            {
                CreateDefaultFilters(plugin, itemFilterService);
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Test Pickup"))
            {
                TestPickupSettings(plugin, itemFilterService);
            }

            ImGui.Separator();
            
            // Show important settings status
            ShowSettingsStatus(plugin);
            
            ImGui.Separator();
            ImGui.Text("Rule Files\nFiles are loaded in order, so easier to process (common item queries hit more often that others) rule sets should be loaded first.");
            ImGui.Separator();

            if (ImGui.BeginTable("RulesTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("Drag", ImGuiTableColumnFlags.WidthFixed, 40);
                ImGui.TableSetupColumn("Toggle", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("File", ImGuiTableColumnFlags.None);
                ImGui.TableHeadersRow();

                DrawRulesTable(plugin.Settings, itemFilterService);

                ImGui.EndTable();
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[RulesDisplay] Error in DrawSettings: {ex.Message}");
            ImGui.Text($"Error: {ex.Message}");
        }
    }

    private static void OpenFilterFolder(PickIt plugin)
    {
        try
        {
            var configDirectory = GetPickitConfigFileDirectory(plugin);
            Process.Start("explorer.exe", configDirectory);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[RulesDisplay] Failed to open filter folder: {ex.Message}");
        }
    }

    private static void ReloadRules(IItemFilterService itemFilterService)
    {
        try
        {
            itemFilterService.ReloadFilters();
            DebugWindow.LogMsg("[RulesDisplay] Rules reloaded");
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[RulesDisplay] Failed to reload rules: {ex.Message}");
        }
    }

    private static void CreateDefaultFilters(PickIt plugin, IItemFilterService itemFilterService)
    {
        try
        {
            var configDirectory = GetPickitConfigFileDirectory(plugin);
            var existingFiles = Directory.GetFiles(configDirectory, "*.ifl", SearchOption.AllDirectories);
            
            if (existingFiles.Length > 0)
            {
                DebugWindow.LogMsg($"[RulesDisplay] {existingFiles.Length} filter files already exist. Use 'Reload Rules' to refresh.");
                return;
            }

            // This will trigger the ItemFilterService to create default files
            itemFilterService.ReloadFilters();
            DebugWindow.LogMsg("[RulesDisplay] Default filter files created. Check the filter folder!");
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[RulesDisplay] Failed to create default filters: {ex.Message}");
        }
    }

    private static void TestPickupSettings(PickIt plugin, IItemFilterService itemFilterService)
    {
        try
        {
            DebugWindow.LogMsg("=== PICKUP TEST STARTED ===");
            
            // Check main plugin enable state
            DebugWindow.LogMsg($"Plugin Enabled: {plugin.Settings.Enable.Value}");
            
            // Check pickup key
            DebugWindow.LogMsg($"Pickup Key: {plugin.Settings.PickUpKey.Value}");
            
            // Check pickup range
            DebugWindow.LogMsg($"Pickup Range: {plugin.Settings.PickupRange.Value}");
            
            // Check active filters
            var activeFilters = itemFilterService.ActiveFilters;
            DebugWindow.LogMsg($"Active Filters Count: {activeFilters.Count}");
            
            if (activeFilters.Count == 0)
            {
                DebugWindow.LogError("NO ACTIVE FILTERS! This is likely why nothing is being picked up.");
                DebugWindow.LogError("Solutions: 1) Enable some filter files in the table below, 2) Enable 'Pick Up Everything' setting, or 3) Click 'Create Default Filters'");
            }
            
            // Check enabled rules
            var enabledRules = plugin.Settings.PickitRules?.Where(r => r.Enabled).ToList() ?? new List<PickitRule>();
            DebugWindow.LogMsg($"Enabled Rules Count: {enabledRules.Count}");
            
            foreach (var rule in enabledRules)
            {
                DebugWindow.LogMsg($"  - {rule.Name} ({rule.Location})");
            }
            
            if (enabledRules.Count == 0)
            {
                DebugWindow.LogError("NO ENABLED RULES! Enable some filter files in the table below.");
            }
            
            // Check pick up everything setting
            DebugWindow.LogMsg($"Pick Up Everything: {plugin.Settings.PickUpEverything.Value}");
            
            if (plugin.Settings.PickUpEverything.Value)
            {
                DebugWindow.LogMsg("Pick Up Everything is enabled - plugin should pick up all items regardless of filters!");
            }
            
            DebugWindow.LogMsg("=== PICKUP TEST COMPLETE - Check above for issues ===");
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[RulesDisplay] Error in pickup test: {ex.Message}");
        }
    }

    private static void ShowSettingsStatus(PickIt plugin)
    {
        try
        {
            // Plugin enabled status
            var pluginEnabled = plugin.Settings.Enable.Value;
            var color = pluginEnabled ? new Vector4(0.0f, 1.0f, 0.0f, 1.0f) : new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
            ImGui.TextColored(color, $"Plugin Enabled: {pluginEnabled}");
            
            // Active filters count
            var enabledRulesCount = plugin.Settings.PickitRules?.Count(r => r.Enabled) ?? 0;
            var filtersColor = enabledRulesCount > 0 ? new Vector4(0.0f, 1.0f, 0.0f, 1.0f) : new Vector4(1.0f, 0.6f, 0.0f, 1.0f);
            ImGui.TextColored(filtersColor, $"Enabled Filter Files: {enabledRulesCount}");
            
            // Pick up everything status
            var pickupEverything = plugin.Settings.PickUpEverything.Value;
            if (pickupEverything)
            {
                ImGui.TextColored(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), "PICK UP EVERYTHING: ON");
            }
            
            // Pickup key
            ImGui.Text($"Pickup Key: {plugin.Settings.PickUpKey.Value}");
            
            // Quick warnings
            if (!pluginEnabled)
            {
                ImGui.TextColored(new Vector4(1.0f, 0.0f, 0.0f, 1.0f), "⚠️ Plugin is DISABLED - enable it in the main settings!");
            }
            else if (enabledRulesCount == 0 && !pickupEverything)
            {
                ImGui.TextColored(new Vector4(1.0f, 0.6f, 0.0f, 1.0f), "⚠️ No filters enabled - nothing will be picked up!");
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[RulesDisplay] Error showing settings status: {ex.Message}");
        }
    }

    private static void DrawRulesTable(PickItSettings settings, IItemFilterService itemFilterService)
    {
        try
        {
            var rules = settings.PickitRules ?? new System.Collections.Generic.List<PickitRule>();
            
            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                ImGui.TableNextRow();

                // Drag column
                ImGui.TableSetColumnIndex(0);
                ImGui.PushID($"drag_{rule.Location}");

                var dropTargetStart = ImGui.GetCursorScreenPos();

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
                ImGui.Button("=", new Vector2(30, 20));
                ImGui.PopStyleColor();

                if (ImGui.BeginDragDropSource())
                {
                    ImGuiHelpers.SetDragDropPayload("RuleIndex", i);
                    ImGui.Text(rule.Name);
                    ImGui.EndDragDropSource();
                }
                else if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Drag me to reorder");
                }

                ImGui.SetCursorScreenPos(dropTargetStart);
                ImGui.InvisibleButton($"dropTarget_{rule.Location}", new Vector2(30, 20));

                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGuiHelpers.AcceptDragDropPayload<int>("RuleIndex");
                    if (payload != null && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    {
                        var movedRule = rules[payload.Value];
                        rules.RemoveAt(payload.Value);
                        rules.Insert(i, movedRule);
                        itemFilterService.ReloadFilters();
                    }

                    ImGui.EndDragDropTarget();
                }

                ImGui.PopID();

                // Toggle column
                ImGui.TableSetColumnIndex(1);
                ImGui.PushID($"toggle_{rule.Location}");
                var enabled = rule.Enabled;
                if (ImGui.Checkbox("", ref enabled))
                {
                    rule.Enabled = enabled;
                    itemFilterService.ReloadFilters();
                }

                ImGui.PopID();

                // File column
                ImGui.TableSetColumnIndex(2);
                ImGui.PushID(rule.Location);

                DrawFileColumn(rule);

                ImGui.PopID();
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[RulesDisplay] Error drawing rules table: {ex.Message}");
        }
    }

    private static void DrawFileColumn(PickitRule rule)
    {
        try
        {
            var directoryPart = Path.GetDirectoryName(rule.Location)?.Replace("\\", "/") ?? "";
            var fileName = Path.GetFileName(rule.Location);
            var serviceManager = PickItServiceManager.Instance;
            var plugin = serviceManager.GetService<PickIt>();
            
            if (plugin == null) return;
            
            var fileFullPath = Path.Combine(GetPickitConfigFileDirectory(plugin), rule.Location);

            var cellWidth = ImGui.GetContentRegionAvail().X;

            ImGui.InvisibleButton($"FileCell_{rule.Location}", new Vector2(cellWidth, ImGui.GetFrameHeight()));

            ImGui.SameLine();

            StartContextMenu(fileName, fileFullPath, $"FileCell_{rule.Location}");

            var textPos = ImGui.GetItemRectMin();
            ImGui.SetCursorScreenPos(textPos);

            if (!string.IsNullOrEmpty(directoryPart))
            {
                ImGui.TextColored(new Vector4(0.4f, 0.7f, 1.0f, 1.0f), directoryPart + "/");
                ImGui.SameLine(0, 0);
                ImGui.Text(fileName);
            }
            else
            {
                ImGui.Text(fileName);
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[RulesDisplay] Error drawing file column: {ex.Message}");
        }
    }

    private static void StartContextMenu(string fileName, string fileFullPath, string contextMenuId)
    {
        try
        {
            if (ImGui.BeginPopupContextItem(contextMenuId))
            {
                if (ImGui.MenuItem("Open"))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = fileFullPath,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        DebugWindow.LogError($"[RulesDisplay] Failed to open file: {ex.Message}");
                    }
                }

                ImGui.EndPopup();
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[RulesDisplay] Error in context menu: {ex.Message}");
        }
    }

    private static string GetPickitConfigFileDirectory(PickIt plugin)
    {
        try
        {
            var pickitConfigFileDirectory = plugin.ConfigDirectory;
            if (!string.IsNullOrEmpty(plugin.Settings.CustomConfigDir))
            {
                var customConfigFileDirectory = Path.Combine(
                    Path.GetDirectoryName(plugin.ConfigDirectory) ?? string.Empty,
                    plugin.Settings.CustomConfigDir);
                    
                if (Directory.Exists(customConfigFileDirectory))
                {
                    pickitConfigFileDirectory = customConfigFileDirectory;
                }
                else
                {
                    DebugWindow.LogError("[RulesDisplay] Custom config folder does not exist.");
                }
            }

            return pickitConfigFileDirectory;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[RulesDisplay] Error getting config directory: {ex.Message}");
            return plugin.ConfigDirectory;
        }
    }
} 