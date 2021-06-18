using System;
using System.Collections.Generic;
using System.Threading.Channels;

namespace Relay
{
    public class StateManager
    {
        public MultiValueDictionary<string, string> ConnectionToSubscriptions =
            new();

        public MultiValueDictionary<string, string> SubscriptionToFilter =
            new();

        public MultiValueDictionary<string, string> ConnectionToFilter =
            new();


        public MultiValueDictionary<string, string> FilterToConnection =
            new();

        public readonly Channel<Tuple<string, string>> PendingMessages =
            Channel.CreateUnbounded<Tuple<string, string>>();


        public void RemoveSubscription(string connectionId, string id, out List<string> orphanedFilters)
        {
            //ugh this can be confusing
            orphanedFilters = new List<string>();

            if (ConnectionToSubscriptions.Remove(connectionId, id))
            {
                SubscriptionToFilter.TryGetValue(id, out var associatedFilters);
                foreach (var associatedFilter in associatedFilters)
                {
                    ConnectionToFilter.Remove(connectionId, associatedFilter);
                    FilterToConnection.Remove(associatedFilter, connectionId);
                }
                
            }
            
            
        }
    }
}