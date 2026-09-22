using System;

namespace Rosynant.EventBusLite
{
    public static class EventBusExtensions
    {
        /// <summary>
        /// Adds this subscription handle to <paramref name="bin"/>.
        /// </summary>
        /// <param name="disposable">The handle returned by <see cref="EventBus{T}.Subscribe"/>.</param>
        /// <param name="bin">The bin that will dispose this handle in <c>OnDestroy</c>.</param>
        public static void AddToSubscriptionBin(this IDisposable disposable, EventSubscriptionBin bin)
        {
            bin.Add(disposable);
        }
    }
}