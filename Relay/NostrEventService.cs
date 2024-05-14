using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LinqKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NNostr.Client;
using Relay.Data;

namespace Relay
{
    public class NostrEventService
    {
        private readonly IDbContextFactory<RelayDbContext> _dbContextFactory;
        private readonly ILogger<NostrEventService> _logger;
        private readonly IOptionsMonitor<RelayOptions> _options;
        private readonly StateManager _stateManager;
        public event EventHandler<NostrEventsMatched>? EventsMatched;
        public event EventHandler<RelayNostrEvent>? NewEvents;

        public NostrEventService(IDbContextFactory<RelayDbContext> dbContextFactory, ILogger<NostrEventService> logger,
            IOptionsMonitor<RelayOptions> options, StateManager stateManager)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
            _options = options;
            _stateManager = stateManager;
        }

        private long ComputeCost(RelayNostrEvent evt, out bool isToAdmin)
        {
            var adminPubKey = _options.CurrentValue.AdminPublicKey;
            isToAdmin = false;
            if (evt.PublicKey == adminPubKey)
            {
                isToAdmin = true;
                return 0;
            }

            if (evt.Kind == 4 && evt.Tags.Any(tag =>
                    tag.TagIdentifier == "p" &&
                    tag.Data.First().Equals(adminPubKey, StringComparison.InvariantCultureIgnoreCase)))
            {
                isToAdmin = true;
                return 0;
            }

            if (!_options.CurrentValue.EventCostPerByte)
            {
                return _options.CurrentValue.EventCost;
            }

            return _options.CurrentValue.EventCost *
                   Encoding.UTF8.GetByteCount(evt.ToJson<RelayNostrEvent, RelayNostrEventTag>(false));
        }

        public async Task<(string eventId, bool success, string reason, List<NostrEventsMatched> eventsMatcheds)> AddEvent(RelayNostrEvent evt)
        {
            if (!((_options.CurrentValue.Nip22BackwardLimit is null ||
                 (DateTimeOffset.UtcNow - evt.CreatedAt) <= _options.CurrentValue.Nip22BackwardLimit) &&
                (_options.CurrentValue.Nip22ForwardLimit is null ||
                 (evt.CreatedAt - DateTimeOffset.UtcNow) <= _options.CurrentValue.Nip22ForwardLimit)))
            {
                return (evt.Id, false,
                    "invalid: event creation date is too far off from the current time. Is your system clock in sync?", null);
            }

            await using var context = await _dbContextFactory.CreateDbContextAsync();
            if (_options.CurrentValue.EventCost > 0 || _options.CurrentValue.PubKeyCost > 0)
            {
                var cost = ComputeCost(evt, out var isToAdmin);

                if (isToAdmin || cost == 0)
                {
                }
                else
                {
                    var balance = await context.Balances.FindAsync(evt.PublicKey);
                    if (balance is null || (balance.CurrentBalance - cost) < 0)
                    {
                        return (evt.Id, false,
                            "invalid: this relay has a cost associated with this event and you did not have sufficient balance", null);
                    }

                    balance.CurrentBalance -= cost;
                }
            }

            if (_options.CurrentValue.EnableNip09 && evt.Kind == 5)
            {
                var eventsToDeleteByPubKey = evt.GetTaggedData<RelayNostrEvent, RelayNostrEventTag>("e");

                await context.Events.Where(evt2 =>
                        evt2.PublicKey.Equals(evt.PublicKey) &&
                        !evt2.Deleted &&
                        (eventsToDeleteByPubKey.Contains(evt2.Id)))
                    .ForEachAsync(evt2 =>
                    {
                        // clients still receive a copy of the original note so we shouldnt remove from filter results
                        // removedEvents.Add(evt2);  
                        evt2.Deleted = true;
                    });
            }

            var isReplaceble = evt.Kind is 0 or 3 or >= 10000 and < 20000;
            if (isReplaceble)
            {
                context.Events.RemoveRange(context.Events.Where(evt2 =>
                    evt2.PublicKey.Equals(evt.PublicKey) && evt.Kind == evt2.Kind &&
                    evt2.CreatedAt < evt.CreatedAt));
            }

            var isEphemeral = evt.Kind is (>= 20000 and < 30000);

            isReplaceble = evt.Kind is >= 30000 and < 40000;


            if (isReplaceble)
            {
                var dValue = evt.GetTaggedData<RelayNostrEvent, RelayNostrEventTag>("d").FirstOrDefault() ?? "";

                context.Events.RemoveRange(context.Events.Where(evt2 =>
                    evt2.PublicKey.Equals(evt.PublicKey) &&
                    evt.Kind == evt2.Kind &&
                    evt2.CreatedAt < evt.CreatedAt &&
                    evt2.Tags.Any(tag => tag.TagIdentifier == "d" &&
                                         tag.Data.Contains(dValue))));
            }

            if (context.ChangeTracker.HasChanges())
            {
                try
                {
                    var executionStrategy = context.Database.CreateExecutionStrategy();
                    await executionStrategy.ExecuteAsync(async () =>
                    {
                        await using var tx = await context.Database.BeginTransactionAsync();
                        try
                        {
                            await context.SaveChangesAsync();
                            await tx.CommitAsync();
                        
                        }
                        catch (Exception e)
                        {
                            await tx.RollbackAsync();
                            _logger.LogError(e, "An error occurred around this event's processing");
                            throw;
                        }
                    });
                }
                catch (Exception e)
                {
                   
                    return (evt.Id, false, "an error occurred around this event's processing", null);
                }
                
                
            }

            var inserted = isEphemeral || 0 < await context.Events.Upsert(evt).NoUpdate().RunAsync();

            if (!inserted)
            {
                return (evt.Id, true, "duplicate: Event has been processed before", null);
            }
            else
            {
                if (!isEphemeral)
                {
                    var i = 0;
                    evt.Tags.ForEach(tag =>
                    {
                        tag.EventId = evt.Id;
                        tag.Id = $"{evt.Id}-{i}-{tag.TagIdentifier}";
                        i++;
                    });
                    await context.EventTags.UpsertRange(evt.Tags).NoUpdate().RunAsync();
                }
                
                
                List<NostrEventsMatched> eventsMatcheds = new();
                _stateManager.ConnectionSubscriptionsToFilters.Keys.ForEach(pair =>
                {
                    if (!_stateManager.ConnectionSubscriptionsToFilters.TryGetValues(pair, out var values)) return;
                    foreach (var subscriptionFilter in values)
                    {
                        var matched = new[] {evt}.Filter<RelayNostrEvent, RelayNostrEventTag>(subscriptionFilter)
                            .ToArray();
                        if (!matched.Any()) continue;

                        var connectionId = pair[..pair.IndexOf('-')];
                        var subscriptionId = pair[(pair.IndexOf('-') + 1)..];
                        eventsMatcheds.Add(new NostrEventsMatched()
                        {
                            Events = matched,
                            ConnectionId = connectionId,
                            SubscriptionId = subscriptionId,
                            OnSent = new TaskCompletionSource()
                            
                        });
                    }
                });


                NewEvents?.Invoke(this, evt);
                eventsMatcheds.ForEach(InvokeMatched);
                
                return (evt.Id, true, "", eventsMatcheds);
            }
        }

        public void InvokeMatched(NostrEventsMatched eventsMatched)
        {
            EventsMatched?.Invoke(this, eventsMatched);
        }

        public async Task<RelayNostrEvent[]> GetFromDB(NostrSubscriptionFilter[] filter)
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync();
            return await context.Events
                .Include(e => e.Tags)
                .Where(e => !e.Deleted)
                .Filter<RelayNostrEvent, RelayNostrEventTag>(filter).ToArrayAsync();
        }
    }
}