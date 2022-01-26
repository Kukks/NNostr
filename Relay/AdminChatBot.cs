using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using NNostr.Client;
using Relay.Data;

namespace Relay;

public class AdminChatBot:IHostedService
{
    private readonly NostrEventService _nostrEventService;
    private readonly IOptions<RelayOptions> _options;
    private readonly IDbContextFactory<RelayDbContext> _dbContextFactory;
    private readonly BTCPayServerClient _btcPayServerClient;

    private readonly Channel<NostrEvent> PendingMessages = Channel.CreateUnbounded<NostrEvent>();
    public AdminChatBot(NostrEventService nostrEventService, IOptions<RelayOptions> options, IDbContextFactory<RelayDbContext> dbContextFactory,
        BTCPayServerClient btcPayServerClient)
    {
        _nostrEventService = nostrEventService;
        _options = options;
        _dbContextFactory = dbContextFactory;
        _btcPayServerClient = btcPayServerClient;
        _nostrEventService.NewEvents += NostrEventServiceOnNewEvents;
    }

    private void NostrEventServiceOnNewEvents(object? sender, NostrEvent[] e)
    {
        foreach (var nostrEvent in e)
        {
            PendingMessages.Writer.TryWrite(nostrEvent);
        }

    }
    
    private async Task ProcessMessages(CancellationToken cancellationToken)
    {
        while (await PendingMessages.Reader.WaitToReadAsync(cancellationToken))
        {
            if (PendingMessages.Reader.TryRead(out var evt))
            {
                await HandleMessage(evt);
            }
        }
    }

    private async Task HandleMessage(NostrEvent evt)
    {
        var adminPubKey = _options.Value.AdminPublicKey;
        if(!string.IsNullOrEmpty(adminPubKey) && evt.Kind == 4 && evt.Tags.Any(tag => tag.TagIdentifier == "p" && tag.Data.First().Equals(adminPubKey, StringComparison.InvariantCultureIgnoreCase)))
        {
            //we have a dm!
            if (evt.Content.StartsWith("/"))
            {
                var split = evt.Content.Substring(1).Split(" ", StringSplitOptions.RemoveEmptyEntries);
                var args = split.Skip(1).ToArray();
                switch (split[0].ToLowerInvariant())
                {
                    case "topup":
                    {
                        await using var context = await _dbContextFactory.CreateDbContextAsync();
                        var topup = await context.BalanceTopups.FirstOrDefaultAsync(topup =>
                            topup.BalanceId == evt.PublicKey && topup.Status == BalanceTopup.TopupStatus.Pending);
                        InvoiceData i = null;
                        if (topup is not null)
                        {
                            i = await _btcPayServerClient.GetInvoice(_options.Value.BTCPayServerStoreId, topup.Id);
                            if (i.Status != InvoiceStatus.New)
                            {
                                
                                topup.Status = BalanceTopup.TopupStatus.Expired;
                                topup = null;
                            }
                        }
                        if (topup is null)
                        {
                            var b = await context.Balances.FindAsync(evt.PublicKey);
                            if (b is null)
                            {
                                b = new Balance()
                                {
                                    PublicKey = evt.PublicKey,
                                    CurrentBalance = _options.Value.PubKeyCost * -1
                                };
                                await context.Balances.AddAsync(b);
                            }

                            i = await _btcPayServerClient.CreateInvoice(_options.Value.BTCPayServerStoreId,
                                new CreateInvoiceRequest()
                                {
                                    Type = InvoiceType.TopUp,
                                    Currency = "BTC",
                                    Metadata = JObject.FromObject(new
                                    {
                                        evt.PublicKey
                                    }),
                                    Checkout = new InvoiceDataBase.CheckoutOptions()
                                    {
                                        Expiration = TimeSpan.MaxValue
                                    },
                                    AdditionalSearchTerms = new []{"nostr", evt.PublicKey}
                                });
                            topup = new BalanceTopup()
                            {
                                Status = BalanceTopup.TopupStatus.Pending,
                                BalanceId = b.PublicKey,
                                Id = i.Id
                            };

                            await context.BalanceTopups.AddAsync(topup);
                        }
                        var eventReply = new NostrEvent()
                        {
                            Content = $"Topup here: {i.CheckoutLink}",
                            Kind = 4,
                            PublicKey = _options.Value.AdminPublicKey,
                            Tags = new List<NostrEventTag>()
                            {
                                new()
                                {
                                    TagIdentifier = "p",
                                    Data = new List<string>()
                                    {
                                        evt.PublicKey
                                    }
                                },
                                new()
                                {
                                    TagIdentifier = "e",
                                    Data = new List<string>()
                                    {
                                        evt.Id
                                    }
                                }
                            }
                        };
                        eventReply.EncryptNip04Event(_options.Value.AdminPrivateKey);
                        eventReply.Signature = eventReply.ComputeSignature(_options.Value.AdminPrivateKey);
                        await _nostrEventService.AddEvent(eventReply);
                        break;
                    }
                    
                    case "balance":
                    {
                        await using var context = await _dbContextFactory.CreateDbContextAsync();
                        var b = await context.Balances.FindAsync(evt.PublicKey);
                        var eventReply = new NostrEvent()
                        {
                            Content = $"Your balance is: {b?.CurrentBalance ?? _options.Value.PubKeyCost}.",
                            Kind = 4,
                            PublicKey = _options.Value.AdminPublicKey,
                            Tags = new List<NostrEventTag>()
                            {
                                new()
                                {
                                    TagIdentifier = "p",
                                    Data = new List<string>()
                                    {
                                        evt.PublicKey
                                    }
                                },
                                new()
                                {
                                    TagIdentifier = "e",
                                    Data = new List<string>()
                                    {
                                        evt.Id
                                    }
                                }
                            }
                        };
                        eventReply.EncryptNip04Event(_options.Value.AdminPrivateKey);
                        eventReply.Signature = eventReply.ComputeSignature(_options.Value.AdminPrivateKey);
                        await _nostrEventService.AddEvent(eventReply);
                        break;
                    }
                }
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = ProcessMessages(cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}