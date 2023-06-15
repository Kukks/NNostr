using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using NNostr.Client;

namespace Relay
{
    public class StateManager
    {
        public readonly ConcurrentMultiDictionary<string, string> ConnectionToSubscriptions =
            new();
        public readonly ConcurrentMultiDictionary<string, NostrSubscriptionFilter[]> ConnectionSubscriptionsToFilters =
            new();


        public readonly Channel<(string connectionId, string message)> PendingMessages =
            Channel.CreateUnbounded<(string, string)>();


        public void RemoveConnection(string connectionId)
        {
            ConnectionToSubscriptions.Remove(connectionId);
        }

        public void AddSubscription(string connectionId, string id, NostrSubscriptionFilter[] filters)
        {
            ConnectionToSubscriptions.Add(connectionId, id);
            ConnectionSubscriptionsToFilters.AddOrReplace($"{connectionId}-{id}", filters);
        }
        

        public void RemoveSubscription(string connectionId, string id)
        {
            ConnectionToSubscriptions.Remove(connectionId, id);
            ConnectionSubscriptionsToFilters.Remove($"{connectionId}-{id}");
        }
    }
}