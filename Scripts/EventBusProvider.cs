using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rosynant.EventBusLite
{
    /// <summary>
    /// Lazily-instantiated, <c>DontDestroyOnLoad</c> singleton that owns and caches one <see cref="EventBus{T}"/>
    /// per event type, and is the sole entry point for subscribing/broadcasting.
    /// <para>
    /// Use the static <see cref="Subscribe{T}"/> / <see cref="Broadcast{T}"/> methods — do not reach for
    /// <see cref="Instance"/> directly.
    /// </para>
    /// </summary>
    public class EventBusProvider : MonoBehaviour
    {
        private static EventBusProvider _instance;

        private Dictionary<Type, IEventBus> _eventBusCache = new();

        // Sticky for the remainder of the runtime session once shutdown begins,
        // so that any late call to Instance during teardown returns a real null instead of resurrecting a brand
        // new singleton GameObject moments before everything is destroyed anyway.
        private static bool _isEndingLifecycle;

        internal static EventBusProvider Instance
        {
            get
            {
                if (_instance == null) // Overloaded `==` also reports true for a destroyed-but-referenced object.
                {
                    // Collapse that "fake null" into a real null. Without this, _instance could keep pointing at
                    // a destroyed component, and any null check would be
                    // comparing against that stale reference rather than a clean slate.
                    _instance = null;

                    if (_isEndingLifecycle)
                    {
                        return null; // Refuse to resurrect a new singleton once the app/domain is tearing down.
                    }

                    _instance = new GameObject(nameof(EventBusProvider)).AddComponent<EventBusProvider>();
                    DontDestroyOnLoad(_instance.gameObject);
                }
                return _instance;
            }
        }

        /// <summary>
        /// Subscribes <paramref name="callback"/> to events of type <typeparamref name="T"/> via the singleton
        /// provider. Returns <c>null</c> if the provider is unavailable (e.g. mid-shutdown) — safe to pass
        /// straight into <see cref="EventBusExtensions.AddToSubscriptionBin"/>, which guards against null handles.
        /// </summary>
        public static IDisposable Subscribe<T>(Action<T> callback) where T : IEvent
        {
            // Captured once: re-reading the property would re-run its creation/teardown branching a second time
            // and risks the null-check and the subsequent dereference observing different states.
            EventBusProvider instance = Instance;

            if (instance == null)
            {
                return null;
            }

            return instance.ProcessSubscription(callback);
        }

        /// <summary>
        /// Broadcasts <paramref name="event"/> to all subscribers of type <typeparamref name="T"/> via the
        /// singleton provider. No-ops if the provider is unavailable (e.g. mid-shutdown).
        /// </summary>
        public static void Broadcast<T>(T @event) where T : IEvent
        {
            EventBusProvider instance = Instance;

            if (instance == null)
            {
                return;
            }

            instance.ProcessBroadcast(@event);
        }

        private IDisposable ProcessSubscription<T>(Action<T> callback) where T : IEvent
        {
            EventBus<T> eventBus = GetEventBus<T>();
            return eventBus.Subscribe(callback);
        }

        private void ProcessBroadcast<T>(T @event) where T : IEvent
        {
            EventBus<T> eventBus = GetEventBus<T>();
            eventBus.Broadcast(@event);
        }

        /// <summary>
        /// Clears the static lifecycle flags at the start of every runtime session — Editor Play Mode entry or
        /// app launch — regardless of whether a domain reload happened.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLifecycleState()
        {
            _isEndingLifecycle = false;
            _instance = null;
        }

        /// <summary>Returns the cached <see cref="EventBus{T}"/> for <typeparamref name="T"/>,
        /// creating and caching one on first use.
        /// </summary>
        private EventBus<T> GetEventBus<T>() where T : IEvent
        {
            EventBus<T> eventBus;

            if (_eventBusCache.TryGetValue(typeof(T), out IEventBus cachedEventBus))
            {
                // Safe, this dictionary only ever stores an EventBus<T> under the key typeof(T).
                eventBus = cachedEventBus as EventBus<T>;
            }
            else
            {
                eventBus = new EventBus<T>();
                _eventBusCache.Add(typeof(T), eventBus);
            }

            return eventBus;
        }

        private void OnDestroy()
        {
            _isEndingLifecycle = true;

            foreach (var eventBus in _eventBusCache.Values)
            {
                eventBus.Reset();
            }
            _eventBusCache.Clear();
        }

        private void OnApplicationQuit()
        {
            _isEndingLifecycle = true;
        }
    }
}
