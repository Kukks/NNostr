using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NBitcoin;
using Newtonsoft.Json.Linq;
using Relay.Data;

namespace Relay;

public class BTCPayServerService : IHostedService
{
    private readonly IOptionsMonitor<RelayOptions> _monitor;

    private readonly Channel<WebhookInvoiceEvent> PendingMessages = Channel.CreateUnbounded<WebhookInvoiceEvent>();

    private BTCPayServerClient _btcPayServerClient => new BTCPayServerClient(_monitor.CurrentValue.BTCPayServerUri,
        _monitor.CurrentValue.BTCPayServerApiKey);

    public BTCPayServerService(IOptionsMonitor<RelayOptions> monitor)
    {
        _monitor = monitor;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }


    public async Task<(BalanceTopup.TopupStatus status, long value, InvoiceData invoiceData)> HandleInvoice(string i)
    {
        var inv = await _btcPayServerClient.GetInvoice(_monitor.CurrentValue.BTCPayServerStoreId, i);
        return await HandleInvoice(inv).ContinueWith(task => (task.Result.status, task.Result.value, inv));
    }

    public Task<(BalanceTopup.TopupStatus status, long value)> HandleInvoice(InvoiceData i)
    {
        var val = Money.FromUnit(i.Amount, MoneyUnit.BTC).Satoshi;
        return Task.FromResult(i.Status switch
        {
            InvoiceStatus.Expired or InvoiceStatus.Invalid => (BalanceTopup.TopupStatus.Expired, val),
            InvoiceStatus.Settled => (BalanceTopup.TopupStatus.Complete, val),
            _ => (BalanceTopup.TopupStatus.Pending, val)
        });
    }

    public async Task<InvoiceData> CreateInvoice(string pubkey)
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
                    Expiration = TimeSpan.MaxValue
                },
                AdditionalSearchTerms = new []{"nostr", pubkey}
            });
           
    }
}