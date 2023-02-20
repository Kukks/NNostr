using System;
using System.Collections.Generic;
using System.Threading.Channels;

namespace Relay
{
    public class StateManager
    {
        public ConcurrentMultiDictionary<string, string> ConnectionToSubscriptions =
            new();

        public ConcurrentMultiDictionary<string, string> SubscriptionToFilter =
            new();

        public ConcurrentMultiDictionary<string, string> ConnectionToFilter =
            new();


        public ConcurrentMultiDictionary<string, string> FilterToConnection =
            new();

        public readonly Channel<(string connectionId, string message)> PendingMessages =
            Channel.CreateUnbounded<(string, string)>();


        public void RemoveSubscription(string connectionId, string id, out List<string> orphanedFilters)
        {
            //ugh this can be confusing
            orphanedFilters = new List<string>();

            if (ConnectionToSubscriptions.Remove(connectionId, id) && SubscriptionToFilter.TryGetValues(id, out var associatedFilters))
            {
                foreach (var associatedFilter in associatedFilters)
                {
                    ConnectionToFilter.Remove(connectionId, associatedFilter);
                    FilterToConnection.Remove(associatedFilter, connectionId);
                }
                
            }
            
            
        }
    }
}