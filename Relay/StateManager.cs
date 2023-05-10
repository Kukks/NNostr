using System;
using System.Collections.Generic;
using System.Linq;
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


        public void RemoveConnection(string connectionId, out List<string> orphanedFilters)
        {
            
            orphanedFilters = new List<string>();
            if (ConnectionToSubscriptions.TryGetValues(connectionId, out var subscriptions))
            {
                foreach (var subscription in subscriptions)
                {
                    SubscriptionToFilter.Remove(subscription, connectionId);
                }
            }

            ConnectionToSubscriptions.Remove(connectionId);
            ConnectionToFilter.Remove(connectionId);
            var filters = FilterToConnection.GetKeysContainingValue(connectionId);
            foreach (var filter in filters)
            {
                FilterToConnection.Remove(filter, connectionId);
                if (FilterToConnection.TryGetValues(filter, out var connections) && connections.Any() is false)
                {
                    orphanedFilters.Add(filter);
                }
            }
        }
        
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