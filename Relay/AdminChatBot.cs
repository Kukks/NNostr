using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBitcoin.Secp256k1;
using NNostr.Client;
using NNostr.Client.Protocols;
using Relay.Data;

namespace Relay;

public class AdminChatBot : IHostedService
{
    private readonly WebSocketHandler _webSocketHandler;
    private readonly StateManager _stateManager;
    private readonly NostrEventService _nostrEventService;
    private readonly IOptionsMonitor<RelayOptions> _options;
    private readonly IDbContextFactory<RelayDbContext> _dbContextFactory;
    private readonly BTCPayServerService _btcPayServerService;
    private readonly ILogger<AdminChatBot> _logger;

    private readonly Channel<RelayNostrEvent> PendingMessages = Channel.CreateUnbounded<RelayNostrEvent>();

    public AdminChatBot(NostrEventService nostrEventService, IOptionsMonitor<RelayOptions> options,
        IDbContextFactory<RelayDbContext> dbContextFactory, BTCPayServerService btcPayServerService,
        ILogger<AdminChatBot> logger, WebSocketHandler webSocketHandler, StateManager stateManager)
    {
        _webSocketHandler = webSocketHandler;
        _stateManager = stateManager;
        _nostrEventService = nostrEventService;
        _options = options;
        _dbContextFactory = dbContextFactory;
        _btcPayServerService = btcPayServerService;
        _logger = logger;
        _nostrEventService.NewEvents += NostrEventServiceOnNewEvents;
        _webSocketHandler.NewConnection += WebSocketHandlerOnNewConnection;
        options.OnChange(relayOptions =>
        {
            _logger.LogInformation("RELAY OPTIONS CHANGED \n{0}", JsonSerializer.Serialize(relayOptions));
            if (relayOptions.AdminPrivateKey is not null)
            {
                TemporaryAdminPrivateKey = null;
            }
        });
    }

    public static ECPrivKey? TemporaryAdminPrivateKey { get; internal set; }

    private void WebSocketHandlerOnNewConnection(object? sender, string newConnection)
    {
        if (_options.CurrentValue.AdminKey is null)
        {
            if (_options.CurrentValue.AdminPrivateKey is null && TemporaryAdminPrivateKey is null)
            {
                Context.Instance.TryCreateECPrivKey(RandomUtils.GetBytes(32), out var privKey);
                TemporaryAdminPrivateKey = privKey;
                _logger.LogInformation(
                    "Admin private key is null, generated a temporary one so that user can configure relay");
            }

            if (_options.CurrentValue.AdminPrivateKey is null)
            {
                string message =
                    $"This relay has not yet been configured. We have generated a temporary admin key that you can use to configure. Simply import the following {TemporaryAdminPrivateKey.ToNIP19()} and send a DM to itself with \"/admin config\" to see config and \"/admin update {{CONFIG}} to set config.";
                _stateManager.PendingMessages.Writer.TryWrite((newConnection,
                    JsonSerializer.Serialize(new[] {"NOTICE", message})));
            }
        }
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
            if (!PendingMessages.Reader.TryRead(out var evt)) continue;
            try
            {
                await HandleMessage(evt);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error handling message");
            }
        }
    }

    private ECPrivKey AdminKey => _options.CurrentValue.AdminPrivateKey ?? TemporaryAdminPrivateKey;
private string AdminPubKey => AdminKey.CreateXOnlyPubKey().ToBytes().AsSpan().ToHex();
    private async Task HandleMessage(RelayNostrEvent evt)
    {
        var adminPubKey = AdminPubKey;
        if (!string.IsNullOrEmpty(adminPubKey) && evt.Kind == 4 && evt.Tags.Any(tag =>
                tag.TagIdentifier == "p" &&
                tag.Data.First().Equals(adminPubKey, StringComparison.InvariantCultureIgnoreCase)))
        {
            var content =
                await evt.DecryptNip04EventAsync<RelayNostrEvent, RelayNostrEventTag>(AdminKey);
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
                        await ReplyToEvent(evt,
                            $"Your balance is: {b?.CurrentBalance ?? _options.CurrentValue.PubKeyCost * -1}.");
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
                                try
                                {
                                    var json = content.Substring(content.IndexOf('{'));
                                    var newOverride = JsonSerializer.Deserialize<RelayOptions>(json);
                                    if (!newOverride.Validate(out var errors))
                                    {
                                        await ReplyToEvent(evt,
                                            "config was invalid. Errors: " + string.Join(Environment.NewLine, errors));
                                    }

                                    await File.WriteAllTextAsync(Program.SettingsOverrideFile,
                                        JsonSerializer.Serialize(newOverride));

                                    await ReplyToEvent(evt, "Config updated.");
                                }
                                catch (Exception e)
                                {
                                    await ReplyToEvent(evt,
                                        "config was invalid or could not be parsed. the command is /admin update {json} where {json} is the json format from /admin config.");
                                }


                                break;
                        }

                        break;
                    }
                }
            }
        }
    }

    private async Task ReplyToEvent(RelayNostrEvent evt, string content)
    {
        var reply = new RelayNostrEvent()
        {
            Content = content,
            Kind = 4,
            CreatedAt = DateTimeOffset.UtcNow,
            PublicKey = AdminPubKey,
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
        await eventReply.EncryptNip04EventAsync<RelayNostrEvent, RelayNostrEventTag>(
            AdminKey);
        eventReply.Id = eventReply.ComputeId<RelayNostrEvent, RelayNostrEventTag>();
        eventReply.Signature =
            eventReply.ComputeSignature<RelayNostrEvent, RelayNostrEventTag>(AdminKey);
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