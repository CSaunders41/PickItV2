using System;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.Shared.Enums;
using ItemFilterLibrary;

namespace PickIt;

public class PickItItemData : ItemData
{
    public PickItItemData(ItemsOnGroundLabelElement.VisibleGroundItemDescription queriedItem, GameController gc)
        : base(queriedItem.Entity?.GetComponent<WorldItem>()?.ItemEntity, queriedItem.Entity, gc)
    {
        QueriedItem = queriedItem;
        FirstSeenTime = DateTime.Now;
        LastAttemptTime = DateTime.MinValue;
        IsPriorityItem = DetermineIfPriorityItem();
    }

    public ItemsOnGroundLabelElement.VisibleGroundItemDescription QueriedItem { get; }
    public int AttemptedPickups { get; set; }
    public DateTime FirstSeenTime { get; }
    public DateTime LastAttemptTime { get; set; }
    public bool IsPriorityItem { get; }
    public bool IsMaxAttemptsReached { get; set; }
    
    /// <summary>
    /// Time since the item was first seen
    /// </summary>
    public TimeSpan TimeSinceFirstSeen => DateTime.Now - FirstSeenTime;
    
    /// <summary>
    /// Time since the last pickup attempt
    /// </summary>
    public TimeSpan TimeSinceLastAttempt => LastAttemptTime == DateTime.MinValue ? TimeSpan.Zero : DateTime.Now - LastAttemptTime;
    
    /// <summary>
    /// Records a pickup attempt
    /// </summary>
    public void RecordAttempt()
    {
        AttemptedPickups++;
        LastAttemptTime = DateTime.Now;
    }
    
    /// <summary>
    /// Resets pickup attempts (used when attempts should be cleared)
    /// </summary>
    public void ResetAttempts()
    {
        AttemptedPickups = 0;
        LastAttemptTime = DateTime.MinValue;
        IsMaxAttemptsReached = false;
    }
    
    /// <summary>
    /// Checks if this item should be skipped based on attempt limits
    /// </summary>
    public bool ShouldSkipDueToAttempts(PickItSettings settings)
    {
        if (!settings.PickupAttemptSettings.EnablePickupAttemptLimiting)
            return false;
            
        // Priority items ignore attempt limits if setting is enabled
        if (IsPriorityItem && settings.PickupAttemptSettings.PriorityItemsIgnoreLimit)
            return false;
            
        // Check if max attempts reached
        if (AttemptedPickups >= settings.PickupAttemptSettings.MaxPickupAttempts)
        {
            IsMaxAttemptsReached = true;
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Checks if attempts should be reset based on time
    /// </summary>
    public bool ShouldResetAttempts(PickItSettings settings)
    {
        if (!settings.PickupAttemptSettings.EnablePickupAttemptLimiting)
            return false;
            
        if (LastAttemptTime == DateTime.MinValue)
            return false;
            
        var resetTime = TimeSpan.FromMilliseconds(settings.PickupAttemptSettings.AttemptResetTime);
        return TimeSinceLastAttempt >= resetTime;
    }
    
    /// <summary>
    /// Determines if this item should be considered priority (currency, maps, etc.)
    /// </summary>
    private bool DetermineIfPriorityItem()
    {
        try
        {
            if (Entity?.GetComponent<WorldItem>()?.ItemEntity == null)
                return false;
                
            var itemEntity = Entity.GetComponent<WorldItem>().ItemEntity;
            var itemPath = itemEntity.Path;
            
            // Check for currency items
            if (itemPath.Contains("Currency") || itemPath.Contains("Stackable"))
                return true;
                
            // Check for maps
            if (itemPath.Contains("Maps/"))
                return true;
                
            // Check for divination cards
            if (itemPath.Contains("DivinationCards"))
                return true;
                
            // Check for fragments
            if (itemPath.Contains("MapFragments") || itemPath.Contains("LabyrinthMapFragments"))
                return true;
                
            // Check for essences
            if (itemPath.Contains("Essence"))
                return true;
                
            // Check for fossils
            if (itemPath.Contains("Fossil"))
                return true;
                
            // Check for gems with quality
            if (itemPath.Contains("SkillGems"))
                return true;
                
            // Check for rare and unique items (simplified - any rare/unique is priority)
            if (itemPath.Contains("Rare") || itemPath.Contains("Unique"))
                return true;
                
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
    
    /// <summary>
    /// Gets a string representation of the item's attempt status
    /// </summary>
    public string GetAttemptStatusString()
    {
        if (AttemptedPickups == 0)
            return "Not attempted";
            
        var timeSinceLastAttempt = TimeSinceLastAttempt.TotalSeconds;
        var priorityText = IsPriorityItem ? " (Priority)" : "";
        var maxAttemptsText = IsMaxAttemptsReached ? " (Max reached)" : "";
        
        return $"Attempts: {AttemptedPickups} ({timeSinceLastAttempt:F1}s ago){priorityText}{maxAttemptsText}";
    }
}