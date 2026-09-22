using UnityEngine;
using UnityEngine.UI;

namespace Rosynant.EventBusLite.Demo
{
    public class EventBusSubscriberDemo : MonoBehaviour, IEventSubscriber
    {
        [SerializeField]
        private Image _image;

        public EventSubscriptionBin EventSubscriptionBin { get; } = new();

        private void Start()
        {
            EventBusProvider.Subscribe<TestEvent>(OnTestEvent).AddToSubscriptionBin(EventSubscriptionBin);
        }

        private void OnTestEvent(TestEvent testEvent)
        {
            _image.color = testEvent.Color;
        }

        private void OnDestroy()
        {
            EventSubscriptionBin.Dispose();
        }
    }
}