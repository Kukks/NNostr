using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NBitcoin;
using Newtonsoft.Json.Linq;
using Relay.Data;

namespace Relay;

public class BTCPayServerService : IHostedService
{
    private readonly IOptionsMonitor<RelayOptions> _monitor;
    private readonly NostrEventService _nostrEventService;
    private readonly IDbContextFactory<RelayDbContext> _dbContextFactory;

    private readonly Channel<WebhookInvoiceEvent> PendingMessages = Channel.CreateUnbounded<WebhookInvoiceEvent>();
    private IDisposable? _disposable;

    private BTCPayServerClient? _btcPayServerClient => new(_monitor.CurrentValue.BTCPayServerUri,
        _monitor.CurrentValue.BTCPayServerApiKey);

    public BTCPayServerService(IOptionsMonitor<RelayOptions> monitor, NostrEventService nostrEventService,
        IDbContextFactory<RelayDbContext> dbContextFactory)
    {
        _monitor = monitor;
        _nostrEventService = nostrEventService;
        _dbContextFactory = dbContextFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _disposable = _monitor.OnChange(options =>
        {
            //update client
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _disposable?.Dispose();
        return Task.CompletedTask;
    }


    public async Task<(BalanceTopup? topup, Balance balance, InvoiceData? invoice)> GetActiveTopupOrBalance(string balanceId)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var topup = await context.BalanceTopups.Include(balanceTopup => balanceTopup.Balance).FirstOrDefaultAsync(
            topup =>
                topup.BalanceId == balanceId && topup.Status == BalanceTopup.TopupStatus.Pending);

        if (topup is not null)
        {
            var status = await HandleInvoice(topup.Id);
            if (status is null)
            {
                return (topup, topup.Balance, null);
            }
            if (status.Value.status != BalanceTopup.TopupStatus.Pending)
            {
                topup.Status = status.Value.status;
                if (status.Value.status == BalanceTopup.TopupStatus.Complete)
                {
                    await context.BalanceTransactions.AddAsync(new BalanceTransaction()
                    {
                        Id = topup.Id,
                        Event = null,
                        BalanceTopupId = topup.Id,
                        BalanceId = topup.BalanceId,
                        Timestamp = DateTimeOffset.UtcNow,
                        Value = status.Value.value,
                    });
                    topup.Balance.CurrentBalance += status.Value.value;
                }
            }
            else
            {
                return (topup, topup!.Balance, status.Value.invoiceData);
            }
        }

        var balance = await context.Balances.FindAsync(balanceId);

        if (balance is null)
        {
            balance = new Balance()
            {
                PublicKey = balanceId,
                CurrentBalance = _monitor.CurrentValue.PubKeyCost * -1
            };
            await context.Balances.AddAsync(balance);
        }

        var invoice = await CreateInvoice(balanceId);
        if (invoice is not null)
        {
            topup = new BalanceTopup()
            {
                Status = BalanceTopup.TopupStatus.Pending,
                BalanceId = balanceId,
                Id = invoice.Id,
                Balance = balance
            };

            await context.BalanceTopups.AddAsync(topup);
        }
        

        await context.SaveChangesAsync();
        return (topup, balance, invoice);
    }

    public async Task<(string balanceId, long balance)?> GenerateBalanceTransaction(string invoiceId)
    {
        var status = await HandleInvoice(invoiceId);
        if (status is null)
        {
            return null;
        }
        if (status.Value.status != BalanceTopup.TopupStatus.Pending)
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync();
            var topup = await context.BalanceTopups.Include(balanceTopup => balanceTopup.Balance)
                .FirstOrDefaultAsync(balanceTopup => balanceTopup.Id == invoiceId);
            var updated = false;
            if (topup is not null)
            {
                topup.Status = status.Value.status;
                if (status.Value.status == BalanceTopup.TopupStatus.Complete)
                {
                    await context.BalanceTransactions.AddAsync(new BalanceTransaction()
                    {
                        Id = topup.Id,
                        Event = null,
                        BalanceTopupId = topup.Id,
                        BalanceId = topup.BalanceId,
                        Timestamp = DateTimeOffset.UtcNow,
                        Value = status.Value.value,
                    });
                    topup.Balance.CurrentBalance += status.Value.value;
                    updated = true;
                }
            }
            await context.SaveChangesAsync();
            if (!updated)
            {
                return null;
            }
            return (topup!.BalanceId, topup.Balance.CurrentBalance);
            
        }
        
        return null;
    }
    
    private async Task<(BalanceTopup.TopupStatus status, long value, InvoiceData invoiceData)?> HandleInvoice(string i)
    {
        InvoiceData? inv = null;
        try
        {
            inv = await _btcPayServerClient.GetInvoice(_monitor.CurrentValue.BTCPayServerStoreId, i);
        }
        catch (Exception e)
        {
        }
        if (inv is null)
        {
            return null;
        }
        return await HandleInvoice(inv).ContinueWith(task => (task.Result.status, task.Result.value, inv));
    }

    private Task<(BalanceTopup.TopupStatus status, long value)> HandleInvoice(InvoiceData i)
    {
        var val = Money.FromUnit(i.Amount, MoneyUnit.BTC).Satoshi;
        return Task.FromResult(i.Status switch
        {
            InvoiceStatus.Expired or InvoiceStatus.Invalid => (BalanceTopup.TopupStatus.Expired, val),
            InvoiceStatus.Settled => (BalanceTopup.TopupStatus.Complete, val),
            _ => (BalanceTopup.TopupStatus.Pending, val)
        });
    }

    public async Task<InvoiceData?> CreateInvoice(string pubkey)
    {
        try
        {
            return await _btcPayServerClient.CreateInvoice(_monitor.CurrentValue.BTCPayServerStoreId,
                new CreateInvoiceRequest()
                {
                    Type = InvoiceType.TopUp,
                    Currency = "BTC",
                    Metadata = JObject.FromObject(new
                    {
                        pubkey
                    }),
                    Checkout = new InvoiceDataBase.CheckoutOptions()
                    {
                        Expiration = TimeSpan.FromDays(30),
                    },
                    AdditionalSearchTerms = new[] {"nostr", pubkey}
                });
        }
        catch (Exception e)
        {
            return null;
        }
    }
}