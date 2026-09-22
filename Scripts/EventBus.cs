using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rosynant.EventBusLite
{
    public interface IEventBus
    {
        public void Reset();
    }

    /// <summary>Marker interface for all event types used with <see cref="EventBus{T}"/>.</summary>
    public interface IEvent
    {

    }

    /// <summary>
    /// Implemented by classes that subscribe to events and need to manage subscription lifetimes.
    /// Call <see cref="EventSubscriptionBin.Dispose"/> in <c>OnDestroy</c> to unsubscribe all listeners at once.
    /// </summary>
    public interface IEventSubscriber
    {
        /// <summary>Holds all active subscription handles for this subscriber.</summary>
        public EventSubscriptionBin EventSubscriptionBin { get; }
    }

    /// <summary>
    /// Collects subscription handles returned by <see cref="EventBus{T}.Subscribe"/>.
    /// Call <see cref="Dispose"/> to unsubscribe all registered listeners at once, typically in <c>OnDestroy</c>.
    /// </summary>
    public class EventSubscriptionBin : IDisposable
    {
        private readonly HashSet<IDisposable> _eventSubscriptionHandles = new();

        /// <summary>Adds a subscription handle to the bin.</summary>
        /// <param name="eventSubscriptionHandle">Handle returned by <see cref="EventBus{T}.Subscribe"/>.</param>
        public void Add(IDisposable eventSubscriptionHandle)
        {
            if (eventSubscriptionHandle == null)
            {
                Debug.LogWarning("[EventSubscriptionBin] Trying to Add() an eventSubscriptionHandle that is null. "
                    + "EventBusProvider is probably unavailable.");
                return;
            }

            _eventSubscriptionHandles.Add(eventSubscriptionHandle);
        }

        /// <summary>Disposes all handles in the bin, unsubscribing every registered listener.</summary>
        public void Dispose()
        {
            foreach (var handle in _eventSubscriptionHandles)
            {
                handle?.Dispose();
            }

            _eventSubscriptionHandles.Clear();
        }
    }

    /// <summary>
    /// Type-safe event bus for a single event type <typeparamref name="T"/>. Decouples broadcasters from listeners — neither needs a direct reference to the other.
    /// <para>
    /// Instances are created and cached per event type by <see cref="EventBusProvider"/>, which owns their lifetime —
    /// do not instantiate <see cref="EventBus{T}"/> directly.
    /// </para>
    /// <para>
    /// Subscribers must store the <see cref="IDisposable"/> handle returned by <see cref="Subscribe"/> and dispose it
    /// when done, typically via <see cref="EventSubscriptionBin"/> in <c>OnDestroy</c>.
    /// </para>
    /// <para>
    /// Note that the duplicate-subscription guard does not apply to lambdas: two separate
    /// lambda expressions are different instances, so subscribing the same lambda twice will register it twice.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The event type. Must implement <see cref="IEvent"/>.</typeparam>
    public class EventBus<T> : IEventBus where T : IEvent
    {
        private readonly List<Action<T>> _listeners = new();

        // Pending queues: subscribe/unsubscribe during a broadcast is deferred here and applied
        // after iteration completes.
        private readonly List<Action<T>> _delayedSubscriptions = new();
        private readonly List<Action<T>> _delayedUnsubscriptions = new();

        private bool _isBroadcasting = false;

        /// <summary>
        /// Registers <paramref name="callback"/> to be invoked when an event of type <typeparamref name="T"/> is broadcast.
        /// </summary>
        /// <param name="callback">The method to invoke on broadcast.</param>
        /// <returns>A handle that unsubscribes <paramref name="callback"/> when disposed.</returns>
        public IDisposable Subscribe(Action<T> callback)
        {
            if (_isBroadcasting)
            {
                if (!_delayedSubscriptions.Contains(callback))
                {
                    _delayedSubscriptions.Add(callback);
                }
            }
            else
            {
                if (!_listeners.Contains(callback))
                {
                    _listeners.Add(callback);
                }
            }

            return new EventSubscriptionHandle(callback, this);
        }

        /// <summary>
        /// Delivers <paramref name="event"/> to all listeners registered for type <typeparamref name="T"/>.
        /// Re-entrant calls during an active broadcast are ignored and logged as warnings.
        /// </summary>
        /// <param name="event">The event instance to deliver.</param>
        public void Broadcast(T @event)
        {
            // Re-entrant broadcasts are dropped.
            // The assumption is that broadcasting inside a listener is always a design mistake here.
            if (_isBroadcasting)
            {
                Debug.LogWarning($"[EventBus] Re-entrant broadcast of {typeof(T).Name} was ignored.");
                return;
            }

            _isBroadcasting = true;

            foreach (var listener in _listeners)
            {
                try
                {
                    listener(@event);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            _isBroadcasting = false;

            if (_delayedUnsubscriptions.Count > 0)
            {
                foreach (var listener in _delayedUnsubscriptions)
                {
                    _listeners.Remove(listener);
                }
                _delayedUnsubscriptions.Clear();
            }

            if (_delayedSubscriptions.Count > 0)
            {
                // Dedup here rather than in Subscribe. Unsubscriptions above are already applied, so a
                // callback that was unsubscribed then re-subscribed mid-broadcast is correctly re-added,
                // while a plain re-subscribe of a still-present callback is skipped.
                foreach (var listener in _delayedSubscriptions)
                {
                    if (!_listeners.Contains(listener))
                    {
                        _listeners.Add(listener);
                    }
                }
                _delayedSubscriptions.Clear();
            }
        }

        /// <summary>
        /// Clears all registered listeners.
        /// </summary>
        public void Reset()
        {
            _listeners.Clear();
            _delayedSubscriptions.Clear();
            _delayedUnsubscriptions.Clear();
        }

        internal void Unsubscribe(Action<T> callback)
        {
            if (_isBroadcasting)
            {
                // If the callback is still pending subscription, cancel it outright.
                if (!_delayedSubscriptions.Remove(callback))
                {
                    _delayedUnsubscriptions.Add(callback);
                }
            }
            else
            {
                _listeners.Remove(callback);
                _delayedSubscriptions.Remove(callback);
            }
        }

        private sealed class EventSubscriptionHandle : IDisposable
        {
            private readonly Action<T> _callback;
            private readonly EventBus<T> _eventBus;

            internal EventSubscriptionHandle(Action<T> callback, EventBus<T> eventBus)
            {
                _callback = callback;
                _eventBus = eventBus;
            }

            public void Dispose() => _eventBus.Unsubscribe(_callback);
        }
    }
}
