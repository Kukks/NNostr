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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using Newtonsoft.Json.Linq;
using NNostr.Client;
using Relay.Data;

namespace Relay;

public class AdminChatBot : IHostedService
{
    private readonly NostrEventService _nostrEventService;
    private readonly IOptions<RelayOptions> _options;
    private readonly IDbContextFactory<RelayDbContext> _dbContextFactory;
    private readonly BTCPayServerService _btcPayServerService;
    private readonly ILogger<AdminChatBot> _logger;

    private readonly Channel<NostrEvent> PendingMessages = Channel.CreateUnbounded<NostrEvent>();

    public AdminChatBot(NostrEventService nostrEventService, IOptions<RelayOptions> options,
        IDbContextFactory<RelayDbContext> dbContextFactory, BTCPayServerService btcPayServerService, ILogger<AdminChatBot> logger)
    {
        _nostrEventService = nostrEventService;
        _options = options;
        _dbContextFactory = dbContextFactory;
        _btcPayServerService = btcPayServerService;
        _logger = logger;
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
        if (!string.IsNullOrEmpty(adminPubKey) && evt.Kind == 4 && evt.Tags.Any(tag =>
                tag.TagIdentifier == "p" &&
                tag.Data.First().Equals(adminPubKey, StringComparison.InvariantCultureIgnoreCase)))
        {
            var content = await evt.DecryptNip04EventAsync(_options.Value.AdminPrivateKey);
            //we have a dm!
            if (content.StartsWith("/"))
            {
                var split = content.Substring(1).Split(" ", StringSplitOptions.RemoveEmptyEntries);
                var args = split.Skip(1).ToArray();
                switch (split[0].ToLowerInvariant())
                {
                    case "topup":
                    {
                        await using var context = await _dbContextFactory.CreateDbContextAsync();
                        var topup = await context.BalanceTopups.FirstOrDefaultAsync(topup =>
                            topup.BalanceId == evt.PublicKey && topup.Status == BalanceTopup.TopupStatus.Pending);
                        Balance? b = null;

                        InvoiceData i = null;
                        if (topup is not null)
                        {
                            var status = await _btcPayServerService.HandleInvoice(topup.Id);
                            topup.Status = status.status;
                            if (topup.Status == BalanceTopup.TopupStatus.Complete)
                            {
                                await context.BalanceTransactions.AddAsync(new BalanceTransaction()
                                {
                                    Event = null,
                                    BalanceTopupId = topup.Id,
                                    BalanceId = topup.BalanceId,
                                    Timestamp = DateTimeOffset.UtcNow,
                                    Value = status.value,
                                });

                                b = await context.Balances.FindAsync(evt.PublicKey);
                                b!.CurrentBalance += status.value;
                                topup = null;
                            }
                            else if (topup.Status == BalanceTopup.TopupStatus.Expired)
                            {
                                topup = null;
                            }
                            else
                            {
                                i = status.invoiceData;
                            }
                        }

                        if (topup is null)
                        {
                            b ??= await context.Balances.FindAsync(evt.PublicKey);
                            if (b is null)
                            {
                                b = new Balance()
                                {
                                    PublicKey = evt.PublicKey,
                                    CurrentBalance = _options.Value.PubKeyCost * -1
                                };
                                await context.Balances.AddAsync(b);
                            }

                            i = await _btcPayServerService.CreateInvoice(evt.PublicKey);
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
                        await eventReply.EncryptNip04EventAsync(_options.Value.AdminPrivateKey);
                        eventReply.Signature = eventReply.ComputeSignature(_options.Value.AdminPrivateKey);
                        await _nostrEventService.AddEvent(new []{eventReply});
                        break;
                    }

                    case "balance":
                    {
                        await using var context = await _dbContextFactory.CreateDbContextAsync();
                        var b = await context.Balances.FindAsync(evt.PublicKey);
                        var eventReply = new NostrEvent()
                        {
                            Content = $"Your balance is: {b?.CurrentBalance ?? _options.Value.PubKeyCost * -1}.",
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
                        await eventReply.ComputeIdAndSignAsync(_options.Value.AdminPrivateKey);
                        _logger.LogInformation($"Sending reply {eventReply.Id} to {evt.PublicKey} ");
                        await _nostrEventService.AddEvent(new []{eventReply});
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