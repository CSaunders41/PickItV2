using System;
using ExileCore;
using ExileCore.Shared;

namespace PickIt.Services;

public class DeathAwarenessService : IDeathAwarenessService
{
    private readonly GameController _gameController;
    private readonly PickItSettings _settings;
    private volatile bool _disposed = false;
    
    // Death tracking
    private bool _wasPlayerDead = false;
    private DateTime? _deathDetectedTime = null;
    private DateTime _lastDeathCheckTime = DateTime.MinValue;
    private bool _isWaitingForResurrection = false;
    
    // Pickup statistics
    private PickupStatistics _pickupStatistics;
    
    public DeathAwarenessService(GameController gameController, PickItSettings settings)
    {
        _gameController = gameController ?? throw new ArgumentNullException(nameof(gameController));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        
        _pickupStatistics = new PickupStatistics
        {
            SessionStartTime = DateTime.Now
        };
    }
    
    public bool IsPlayerDead => !_gameController.Player?.IsAlive ?? true;
    public bool IsWaitingForResurrection => _isWaitingForResurrection;
    public DateTime? DeathDetectedTime => _deathDetectedTime;
    public TimeSpan TimeSinceDeath => _deathDetectedTime.HasValue ? DateTime.Now - _deathDetectedTime.Value : TimeSpan.Zero;
    
    public event Action OnPlayerDeath;
    public event Action OnPlayerResurrection;
    
    public void CheckDeathStatus()
    {
        ThrowIfDisposed();
        
        if (!_settings.DeathAwarenessSettings.EnableDeathAwareness)
            return;
            
        var now = DateTime.Now;
        
        // Check if enough time has passed since last death check
        if (now - _lastDeathCheckTime < TimeSpan.FromMilliseconds(_settings.DeathAwarenessSettings.DeathCheckInterval))
            return;
            
        _lastDeathCheckTime = now;
        
        var isCurrentlyDead = IsPlayerDead;
        
        try
        {
            // Death detected
            if (isCurrentlyDead && !_wasPlayerDead)
            {
                _wasPlayerDead = true;
                _deathDetectedTime = now;
                _isWaitingForResurrection = true;
                
                DebugWindow.LogMsg("[DeathAwarenessService] Player death detected - pausing pickup operations");
                
                // Raise death event
                OnPlayerDeath?.Invoke();
            }
            // Resurrection detected
            else if (!isCurrentlyDead && _wasPlayerDead)
            {
                _wasPlayerDead = false;
                _isWaitingForResurrection = false;
                
                DebugWindow.LogMsg("[DeathAwarenessService] Player resurrection detected");
                
                if (_settings.DeathAwarenessSettings.AutoResumeAfterDeath)
                {
                    DebugWindow.LogMsg("[DeathAwarenessService] Auto-resuming pickup operations");
                }
                
                // Raise resurrection event
                OnPlayerResurrection?.Invoke();
            }
            // Check for resurrection timeout
            else if (_isWaitingForResurrection && isCurrentlyDead && _deathDetectedTime.HasValue)
            {
                var waitTime = now - _deathDetectedTime.Value;
                if (waitTime.TotalMilliseconds > _settings.DeathAwarenessSettings.ResurrectionTimeout)
                {
                    DebugWindow.LogMsg("[DeathAwarenessService] Resurrection timeout reached - stopping death handling");
                    ResetDeathState();
                }
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[DeathAwarenessService] Error in CheckDeathStatus: {ex.Message}");
        }
    }
    
    public void ResetDeathState()
    {
        ThrowIfDisposed();
        
        _wasPlayerDead = false;
        _deathDetectedTime = null;
        _isWaitingForResurrection = false;
        
        DebugWindow.LogMsg("[DeathAwarenessService] Death state reset");
    }
    
    public PickupStatistics GetPickupStatistics()
    {
        ThrowIfDisposed();
        return _pickupStatistics;
    }
    
    public void ClearPickupStatistics()
    {
        ThrowIfDisposed();
        
        _pickupStatistics = new PickupStatistics
        {
            SessionStartTime = DateTime.Now
        };
        
        DebugWindow.LogMsg("[DeathAwarenessService] Pickup statistics cleared");
    }
    
    public void RecordPickupAttempt(string itemName, bool success)
    {
        ThrowIfDisposed();
        
        try
        {
            _pickupStatistics.TotalAttempts++;
            _pickupStatistics.LastPickupTime = DateTime.Now;
            
            if (success)
            {
                _pickupStatistics.SuccessfulPickups++;
                DebugWindow.LogMsg($"[DeathAwarenessService] Successful pickup: {itemName}");
            }
            else
            {
                _pickupStatistics.FailedPickups++;
                DebugWindow.LogMsg($"[DeathAwarenessService] Failed pickup: {itemName}");
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[DeathAwarenessService] Error recording pickup attempt: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Checks if the player should be allowed to pickup items based on death state
    /// </summary>
    public bool ShouldAllowPickup()
    {
        ThrowIfDisposed();
        
        if (!_settings.DeathAwarenessSettings.EnableDeathAwareness)
            return true;
            
        // Don't allow pickup if player is dead or waiting for resurrection
        if (IsPlayerDead || IsWaitingForResurrection)
        {
            return false;
        }
        
        // Allow pickup if auto-resume is enabled and player is alive
        return _settings.DeathAwarenessSettings.AutoResumeAfterDeath || !_wasPlayerDead;
    }
    
    /// <summary>
    /// Gets a status string for debugging/display purposes
    /// </summary>
    public string GetStatusString()
    {
        ThrowIfDisposed();
        
        if (!_settings.DeathAwarenessSettings.EnableDeathAwareness)
            return "Death awareness disabled";
            
        if (IsPlayerDead)
            return $"Player is dead ({TimeSinceDeath.TotalSeconds:F1}s ago)";
            
        if (IsWaitingForResurrection)
            return $"Waiting for resurrection ({TimeSinceDeath.TotalSeconds:F1}s)";
            
        return "Player is alive";
    }
    
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DeathAwarenessService));
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        
        try
        {
            // Clean up events
            OnPlayerDeath = null;
            OnPlayerResurrection = null;
            
            DebugWindow.LogMsg("[DeathAwarenessService] Service disposed");
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[DeathAwarenessService] Error during disposal: {ex.Message}");
        }
    }
} 