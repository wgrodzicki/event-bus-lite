using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rosynant.EventBusLite.Tests
{
    /// <summary>
    /// Exercises <see cref="EventBus{T}"/> directly — it's a plain C# class with no MonoBehaviour
    /// dependencies, so EditMode is sufficient and keeps these fast.
    /// </summary>
    public class EventBusTests
    {
        private struct TestEvent : IEvent
        {
            public int Number;

            public TestEvent(int number) => Number = number;
        }

        private EventBus<TestEvent> _eventBus;

        [SetUp]
        public void SetUp()
        {
            // Fresh instance per test — EventBus<T> is normally provider-owned and long-lived,
            // but instantiating it directly here keeps each test isolated from the others.
            _eventBus = new EventBus<TestEvent>();
        }

        [Test]
        public void Broadcast_DeliversEventToSubscribedListener()
        {
            TestEvent? received = null;
            _eventBus.Subscribe(e => received = e);

            _eventBus.Broadcast(new TestEvent(42));

            Assert.That(received, Is.Not.Null);
            Assert.That(received.Value.Number, Is.EqualTo(42));
        }

        [Test]
        public void Broadcast_DeliversToMultipleListeners_InSubscriptionOrder()
        {
            var callOrder = new List<int>();
            _eventBus.Subscribe(_ => callOrder.Add(1));
            _eventBus.Subscribe(_ => callOrder.Add(2));
            _eventBus.Subscribe(_ => callOrder.Add(3));

            _eventBus.Broadcast(new TestEvent(0));

            Assert.That(callOrder, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void Subscribe_SameNamedDelegateTwice_RegistersOnlyOnce()
        {
            int callCount = 0;
            void Listener(TestEvent e) => callCount++;

            _eventBus.Subscribe(Listener);
            _eventBus.Subscribe(Listener);
            _eventBus.Broadcast(new TestEvent(0));

            Assert.That(callCount, Is.EqualTo(1));
        }

        [Test]
        public void Subscribe_TwoDistinctLambdas_RegistersBoth()
        {
            // Documents the caveat called out in EventBus<T>'s XML remarks: the duplicate guard compares
            // delegates, and two separate (even textually identical) lambda expressions are distinct
            // delegate instances, so both get registered.
            int callCount = 0;

            _eventBus.Subscribe(_ => callCount++);
            _eventBus.Subscribe(_ => callCount++);
            _eventBus.Broadcast(new TestEvent(0));

            Assert.That(callCount, Is.EqualTo(2));
        }

        [Test]
        public void Dispose_UnsubscribesListener()
        {
            int callCount = 0;
            IDisposable handle = _eventBus.Subscribe(_ => callCount++);

            handle.Dispose();
            _eventBus.Broadcast(new TestEvent(0));

            Assert.That(callCount, Is.Zero);
        }

        [Test]
        public void Broadcast_ListenerThrows_ExceptionIsCaughtAndOtherListenersStillRun()
        {
            int secondListenerCallCount = 0;
            _eventBus.Subscribe(_ => throw new InvalidOperationException("boom"));
            _eventBus.Subscribe(_ => secondListenerCallCount++);

            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: boom"));
            _eventBus.Broadcast(new TestEvent(0));

            Assert.That(secondListenerCallCount, Is.EqualTo(1));
        }

        [Test]
        public void Broadcast_Reentrant_IsDroppedAndLogsWarning()
        {
            int outerCallCount = 0;

            _eventBus.Subscribe(e =>
            {
                outerCallCount++;
                _eventBus.Broadcast(e); // re-entrant — must be ignored, not queued or recursively delivered
            });

            LogAssert.Expect(LogType.Warning, new Regex("Re-entrant broadcast"));
            _eventBus.Broadcast(new TestEvent(0));

            Assert.That(outerCallCount, Is.EqualTo(1), "the re-entrant call must not trigger another delivery pass");
        }

        [Test]
        public void Subscribe_DuringBroadcast_OnlyTakesEffectOnTheNextBroadcast()
        {
            int lateListenerCallCount = 0;
            void LateListener(TestEvent e) => lateListenerCallCount++;

            _eventBus.Subscribe(_ => _eventBus.Subscribe(LateListener));

            _eventBus.Broadcast(new TestEvent(0));
            Assert.That(lateListenerCallCount, Is.Zero, "a listener subscribed mid-broadcast must not run during that same broadcast");

            _eventBus.Broadcast(new TestEvent(0));
            Assert.That(lateListenerCallCount, Is.EqualTo(1), "it should be active for the following broadcast");
        }

        [Test]
        public void Subscribe_DuringBroadcast_AlreadyRegisteredDelegate_IsNotDuplicated()
        {
            // The deferred-subscription path (used while broadcasting) bypasses the normal _listeners
            // dedup check, so it must apply its own guard: re-subscribing a delegate that is already
            // registered must not enqueue a second copy of it. Without the guard, callCount would reach
            // 3 across the two passes instead of 2.
            int callCount = 0;
            void Listener(TestEvent e) => callCount++;

            _eventBus.Subscribe(Listener);
            // A separate listener re-subscribes the already-registered Listener mid-broadcast.
            _eventBus.Subscribe(_ => _eventBus.Subscribe(Listener));

            _eventBus.Broadcast(new TestEvent(0)); // pass 1: applies the (no-op) deferred re-subscription
            _eventBus.Broadcast(new TestEvent(0)); // pass 2: Listener must still fire exactly once

            Assert.That(callCount, Is.EqualTo(2), "the already-registered delegate must stay registered exactly once");
        }

        [Test]
        public void Unsubscribe_DuringBroadcast_StillRunsForTheCurrentBroadcastButNotLater()
        {
            int callCount = 0;
            IDisposable handle = null;
            handle = _eventBus.Subscribe(_ =>
            {
                callCount++;
                handle.Dispose();
            });

            _eventBus.Broadcast(new TestEvent(0));
            Assert.That(callCount, Is.EqualTo(1), "a listener must still run for the broadcast that triggered its own removal");

            _eventBus.Broadcast(new TestEvent(0));
            Assert.That(callCount, Is.EqualTo(1), "it must not run again afterwards");
        }

        [Test]
        public void UnsubscribeThenResubscribe_DuringBroadcast_KeepsListenerRegistered()
        {
            // Unsubscribing and re-subscribing the same delegate within a single broadcast nets out to
            // "still subscribed". Because both operations are deferred, the removal is applied first and
            // the re-add must survive it — the listener should fire on the following broadcast.
            int callCount = 0;
            void Listener(TestEvent e) => callCount++;
            IDisposable handle = _eventBus.Subscribe(Listener);

            _eventBus.Subscribe(_ =>
            {
                handle.Dispose();                        // deferred unsubscribe
                handle = _eventBus.Subscribe(Listener);  // deferred re-subscribe
            });

            _eventBus.Broadcast(new TestEvent(0)); // pass 1: applies the deferred unsub + resub
            callCount = 0;                         // ignore pass-1 deliveries; assert on the next pass

            _eventBus.Broadcast(new TestEvent(0));
            Assert.That(callCount, Is.EqualTo(1), "the re-subscribed listener must remain registered exactly once");
        }

        [Test]
        public void Reset_ClearsAllListeners()
        {
            int callCount = 0;
            _eventBus.Subscribe(_ => callCount++);

            _eventBus.Reset();
            _eventBus.Broadcast(new TestEvent(0));

            Assert.That(callCount, Is.Zero);
        }
    }
}
