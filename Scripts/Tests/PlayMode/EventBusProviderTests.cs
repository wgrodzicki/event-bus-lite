using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rosynant.EventBusLite.Tests
{
    public class EventBusProviderTests
    {
        private struct TestEventA : IEvent
        {
            public int Number;

            public TestEventA(int number) => Number = number;
        }

        private struct TestEventB : IEvent
        {
            public string Text;

            public TestEventB(string text) => Text = text;
        }

        private readonly List<IDisposable> _subscriptions = new();

        [TearDown]
        public void TearDown()
        {
            // Deliberately dispose through the handles returned by Subscribe rather than reaching for the
            // singleton's internals (or destroying its GameObject) to "reset" it between tests.
            // Disposing leaves the cached buses in place but empty, which is exactly the steady state a real
            // game would be in between scenes — and exactly what well-behaved subscribers (EventSubscriptionBin
            // in OnDestroy) do anyway, so this also doubles as a check that the disposal path works.
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }
            _subscriptions.Clear();
        }

        [UnityTest]
        public IEnumerator Subscribe_And_Broadcast_DeliversEventThroughTheProvider()
        {
            TestEventA? received = null;
            _subscriptions.Add(EventBusProvider.Subscribe<TestEventA>(e => received = e));

            EventBusProvider.Broadcast(new TestEventA(7));
            yield return null;

            Assert.That(received, Is.Not.Null);
            Assert.That(received.Value.Number, Is.EqualTo(7));
        }

        [UnityTest]
        public IEnumerator Subscribe_LazilyCreatesAPersistentSingletonGameObject()
        {
            _subscriptions.Add(EventBusProvider.Subscribe<TestEventA>(_ => { }));
            yield return null;

            var providerObject = GameObject.Find(nameof(EventBusProvider));
            Assert.That(providerObject, Is.Not.Null, "EventBusProvider should lazily create its own backing GameObject");
            Assert.That(providerObject.GetComponent<EventBusProvider>(), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator DifferentEventTypes_AreRoutedToTheirOwnSubscribersOnly()
        {
            int aCallCount = 0;
            int bCallCount = 0;

            _subscriptions.Add(EventBusProvider.Subscribe<TestEventA>(_ => aCallCount++));
            _subscriptions.Add(EventBusProvider.Subscribe<TestEventB>(_ => bCallCount++));

            EventBusProvider.Broadcast(new TestEventA(1));
            yield return null;

            Assert.That(aCallCount, Is.EqualTo(1));
            Assert.That(bCallCount, Is.Zero, "a TestEventA broadcast must not reach"
                + "TestEventB subscribers — each event type owns a separate EventBus<T>");
        }

        [UnityTest]
        public IEnumerator Dispose_UnsubscribesFromTheUnderlyingBus()
        {
            int callCount = 0;
            IDisposable handle = EventBusProvider.Subscribe<TestEventA>(_ => callCount++);

            handle.Dispose();
            EventBusProvider.Broadcast(new TestEventA(0));
            yield return null;

            Assert.That(callCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator DisposingOneSubscription_DoesNotAffectOthersOnTheSameBus()
        {
            int firstCallCount = 0;
            int secondCallCount = 0;

            IDisposable first = EventBusProvider.Subscribe<TestEventA>(_ => firstCallCount++);
            _subscriptions.Add(EventBusProvider.Subscribe<TestEventA>(_ => secondCallCount++));

            first.Dispose();
            EventBusProvider.Broadcast(new TestEventA(0));
            yield return null;

            Assert.That(firstCallCount, Is.Zero, "the disposed subscription should no longer receive events");
            Assert.That(secondCallCount, Is.EqualTo(1), "other subscriptions on the same EventBus<T> must be unaffected");
        }

        [UnityTest]
        public IEnumerator Broadcast_WithNoSubscribers_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => EventBusProvider.Broadcast(new TestEventA(0)));
            yield return null;
        }
    }
}
