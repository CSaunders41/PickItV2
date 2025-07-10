using System;

namespace PickIt.Services;

public interface IDeathAwarenessService : IDisposable
{
    /// <summary>
    /// Gets whether the player is currently dead
    /// </summary>
    bool IsPlayerDead { get; }
    
    /// <summary>
    /// Gets whether the service is waiting for resurrection
    /// </summary>
    bool IsWaitingForResurrection { get; }
    
    /// <summary>
    /// Gets the time when death was detected
    /// </summary>
    DateTime? DeathDetectedTime { get; }
    
    /// <summary>
    /// Gets the time since death was detected
    /// </summary>
    TimeSpan TimeSinceDeath { get; }
    
    /// <summary>
    /// Checks death status and updates internal state
    /// </summary>
    void CheckDeathStatus();
    
    /// <summary>
    /// Resets death state (useful for testing or manual resets)
    /// </summary>
    void ResetDeathState();
    
    /// <summary>
    /// Gets pickup statistics
    /// </summary>
    PickupStatistics GetPickupStatistics();
    
    /// <summary>
    /// Clears pickup statistics
    /// </summary>
    void ClearPickupStatistics();
    
    /// <summary>
    /// Records a pickup attempt
    /// </summary>
    void RecordPickupAttempt(string itemName, bool success);
    
    /// <summary>
    /// Checks if pickup should be allowed based on death state
    /// </summary>
    bool ShouldAllowPickup();
    
    /// <summary>
    /// Gets a status string for display purposes
    /// </summary>
    string GetStatusString();
    
    /// <summary>
    /// Event raised when player dies
    /// </summary>
    event Action OnPlayerDeath;
    
    /// <summary>
    /// Event raised when player resurrects
    /// </summary>
    event Action OnPlayerResurrection;
}

public class PickupStatistics
{
    public int TotalAttempts { get; set; }
    public int SuccessfulPickups { get; set; }
    public int FailedPickups { get; set; }
    public DateTime LastPickupTime { get; set; }
    public DateTime SessionStartTime { get; set; }
    
    public double SuccessRate => TotalAttempts > 0 ? (double)SuccessfulPickups / TotalAttempts * 100 : 0;
    public TimeSpan SessionDuration => DateTime.Now - SessionStartTime;
} 