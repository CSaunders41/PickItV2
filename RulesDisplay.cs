using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using ExileCore;
using ImGuiNET;
using PickIt.Services;

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