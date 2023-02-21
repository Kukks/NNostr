using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using NNostr.Client;
using Relay.Data;

namespace Relay;

public class AdminChatBot : IHostedService
{
    private readonly NostrEventService _nostrEventService;
    private readonly IOptionsMonitor<RelayOptions> _options;
    private readonly IDbContextFactory<RelayDbContext> _dbContextFactory;
    private readonly BTCPayServerService _btcPayServerService;
    private readonly ILogger<AdminChatBot> _logger;

    private readonly Channel<RelayNostrEvent> PendingMessages = Channel.CreateUnbounded<RelayNostrEvent>();

    public AdminChatBot(NostrEventService nostrEventService, IOptionsMonitor<RelayOptions> options,
        IDbContextFactory<RelayDbContext> dbContextFactory, BTCPayServerService btcPayServerService, ILogger<AdminChatBot> logger)
    {
        _nostrEventService = nostrEventService;
        _options = options;
        _dbContextFactory = dbContextFactory;
        _btcPayServerService = btcPayServerService;
        _logger = logger;
        _nostrEventService.NewEvents += NostrEventServiceOnNewEvents;
    }

    private void NostrEventServiceOnNewEvents(object? sender, RelayNostrEvent[] e)
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

    private async Task HandleMessage(RelayNostrEvent evt)
    {
        var adminPubKey = _options.CurrentValue.AdminPublicKey;
        if (!string.IsNullOrEmpty(adminPubKey) && evt.Kind == 4 && evt.Tags.Any(tag =>
                tag.TagIdentifier == "p" &&
                tag.Data.First().Equals(adminPubKey, StringComparison.InvariantCultureIgnoreCase)))
        {
            var content = await evt.DecryptNip04EventAsync<RelayNostrEvent, RelayNostrEventTag>(_options.CurrentValue.AdminPrivateKey);
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
                                    CurrentBalance = _options.CurrentValue.PubKeyCost * -1
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
                        await ReplyToEvent(evt, $"Topup here: {i.CheckoutLink}");
                        break;
                    }

                    case "balance":
                    {
                        await using var context = await _dbContextFactory.CreateDbContextAsync();
                        var b = await context.Balances.FindAsync(evt.PublicKey);
                        await ReplyToEvent(evt, $"Your balance is: {b?.CurrentBalance ?? _options.CurrentValue.PubKeyCost * -1}.");
                        break;
                    }
                    case "admin" when evt.PublicKey == adminPubKey:
                    {
                        switch (args.FirstOrDefault()?.ToLowerInvariant())
                        {
                            case "config":
                                await ReplyToEvent(evt, JsonSerializer.Serialize(_options.CurrentValue));
                                break;
                            case "factory-reset":
                                File.Delete(Program.SettingsOverrideFile);
                                await ReplyToEvent(evt, "Factory reset complete.");
                                break;
                            case "update":

                                var property = args.ElementAtOrDefault(1);
                                if (_writableOptions.Value.TryGetValue(property, out var pInfo))
                                {
                                    var propertyValue = args.ElementAtOrDefault(2);
                                 
                                    JsonObject? newOverride = new JsonObject();
                                    if (File.Exists(Program.SettingsOverrideFile))
                                    {
                                        var currentOverride = await File.ReadAllTextAsync(Program.SettingsOverrideFile);
                                        newOverride = JsonSerializer.Deserialize<JsonObject>(currentOverride)?? new JsonObject();
                                    }

                                    if (propertyValue is null)
                                    {
                                        newOverride.Remove(property);
                                    }
                                    else
                                    {
                                        
                                        var converter = TypeDescriptor.GetConverter(pInfo);
                                        var convertedObject = converter.ConvertFromString(propertyValue);
                                        newOverride[property] = JsonValue.Create(convertedObject);
                                    }
                                    await File.WriteAllTextAsync(Program.SettingsOverrideFile, JsonSerializer.Serialize(newOverride));
                                    
                                    await ReplyToEvent(evt, "Config updated");
                                }

                                break;
                                
                        }
                        break;
                    }
                }
            }
        }
    }
    
    private Lazy<Dictionary<string, PropertyInfo>> _writableOptions = new(() =>
    {
        var props = typeof(RelayOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(prop => prop.GetSetMethod() is not null).ToDictionary(prop => prop.Name.ToLowerInvariant());
        return props;
    });

    private async Task ReplyToEvent(RelayNostrEvent evt, string content)
    {
        var reply = new RelayNostrEvent()
        {
            Content = content,
            Kind = 4,
            CreatedAt = DateTimeOffset.UtcNow,
            PublicKey = _options.CurrentValue.AdminPublicKey,
            Tags = new List<RelayNostrEventTag>()
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
        await AddEvent(reply);
    }
    private async Task AddEvent(RelayNostrEvent eventReply)
    {
        await eventReply.EncryptNip04EventAsync<RelayNostrEvent, RelayNostrEventTag>(_options.CurrentValue.AdminPrivateKey);
        eventReply.Signature =
            eventReply.ComputeSignature<RelayNostrEvent, RelayNostrEventTag>(_options.CurrentValue.AdminPrivateKey);
        await _nostrEventService.AddEvent(new[] {eventReply});
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