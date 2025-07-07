using System;
using System.Collections.Concurrent;
using System.Threading;
using ExileCore;

namespace PickIt.Services;

/// <summary>
/// Thread-safe service manager that replaces static field access
/// </summary>
public sealed class PickItServiceManager : IDisposable
{
    private static readonly Lazy<PickItServiceManager> _instance = new(() => new PickItServiceManager());
    private readonly ConcurrentDictionary<Type, object> _services = new();
    private readonly ReaderWriterLockSlim _lock = new();
    private volatile bool _disposed = false;

    public static PickItServiceManager Instance => _instance.Value;

    private PickItServiceManager() { }

    public void RegisterService<T>(T service) where T : class
    {
        ThrowIfDisposed();
        
        _lock.EnterWriteLock();
        try
        {
            _services.AddOrUpdate(typeof(T), service, (key, oldValue) => service);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public T GetService<T>() where T : class
    {
        ThrowIfDisposed();
        
        _lock.EnterReadLock();
        try
        {
            return _services.TryGetValue(typeof(T), out var service) ? service as T : null;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public T GetRequiredService<T>() where T : class
    {
        var service = GetService<T>();
        if (service == null)
        {
            throw new InvalidOperationException($"Service of type {typeof(T).Name} is not registered");
        }
        return service;
    }

    public bool IsServiceRegistered<T>() where T : class
    {
        ThrowIfDisposed();
        
        _lock.EnterReadLock();
        try
        {
            return _services.ContainsKey(typeof(T));
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void UnregisterService<T>() where T : class
    {
        ThrowIfDisposed();
        
        _lock.EnterWriteLock();
        try
        {
            _services.TryRemove(typeof(T), out _);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Clear()
    {
        ThrowIfDisposed();
        
        _lock.EnterWriteLock();
        try
        {
            _services.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PickItServiceManager));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _lock.EnterWriteLock();
        try
        {
            _disposed = true;
            
            // Dispose services that implement IDisposable
            foreach (var service in _services.Values)
            {
                if (service is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception ex)
                    {
                        // Log error but don't throw during disposal
                        DebugWindow.LogError($"Error disposing service {service.GetType().Name}: {ex.Message}");
                    }
                }
            }
            
            _services.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
            _lock.Dispose();
        }
    }
} 