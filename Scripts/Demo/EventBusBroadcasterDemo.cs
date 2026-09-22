using UnityEngine;

namespace Rosynant.EventBusLite.Demo
{
    public struct TestEvent : IEvent
    {
        public Color Color { get; set; }

        public TestEvent(Color color)
        {
            Color = color;
        }
    }

    public class EventBusBroadcasterDemo : MonoBehaviour
    {
        [SerializeField]
        private Color _color;
        [SerializeField]
        private bool _randomizeColor;

        public void BroadcastTestEvent()
        {
            if (_randomizeColor)
            {
                _color = Random.ColorHSV();
            }
            
            EventBusProvider.Broadcast(new TestEvent(_color));
        }
    }
}