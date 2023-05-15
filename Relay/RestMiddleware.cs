using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;
using NNostr.Client;
using Relay.Data;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Relay;

public class RestMiddleware : IMiddleware
{
    private readonly IOptionsMonitor<RelayOptions> _options;
    private readonly NostrEventService _nostrEventService;
    private readonly BTCPayServerService _btcPayServerService;
    private readonly AdminChatBot _adminChatBot;

    public RestMiddleware(IOptionsMonitor<RelayOptions> options, NostrEventService nostrEventService,
        BTCPayServerService btcPayServerService, AdminChatBot adminChatBot)
    {
        _options = options;
        _nostrEventService = nostrEventService;
        _btcPayServerService = btcPayServerService;
        _adminChatBot = adminChatBot;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!context.WebSockets.IsWebSocketRequest &&
            context.Request.Headers.ContentType.Any(s =>
                s.Equals("application/json", StringComparison.InvariantCultureIgnoreCase)))
        {
            using var streamReader = new StreamReader(context.Request.Body);
            var body = await streamReader.ReadToEndAsync();

            if (context.Request.Path.Value?.EndsWith("api/req", StringComparison.InvariantCultureIgnoreCase) is true)
            {
                var filters = JsonSerializer.Deserialize<NostrSubscriptionFilter[]>(body);
                var evts = await _nostrEventService.FetchData(filters);

                context.Response.Clear();
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsync(JsonSerializer.Serialize(evts));
            }
            else if (context.Request.Path.Value?.EndsWith("btcpay/webhook", StringComparison.InvariantCultureIgnoreCase)
                         is
                         true && context.Request.Headers.TryGetValue("BTCPay-Sig", out var signature))
            {
                var evt = JsonConvert.DeserializeObject<WebhookInvoiceEvent>(body);
                if (!string.IsNullOrEmpty(_options.CurrentValue.BTCPayServerWebhookSecret))
                {
                    signature = signature.ToString();
                    var expectedSig =
                        $"sha256={Encoders.Hex.EncodeData(NBitcoin.Crypto.Hashes.HMACSHA256(Encoding.UTF8.GetBytes(_options.CurrentValue.BTCPayServerWebhookSecret), Encoding.UTF8.GetBytes(body)))}";
                    if (signature != expectedSig)
                    {
                        context.Response.Clear();
                        context.Response.StatusCode = 401;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(new {error = "Invalid signature"}));
                        return;
                    }
                }

                if (evt.Type == WebhookEventType.InvoiceSettled &&
                    evt.StoreId == _options.CurrentValue.BTCPayServerStoreId)
                {
                    var result = await _btcPayServerService.GenerateBalanceTransaction(evt.InvoiceId);
                    if (result is not null)
                    {
                        _ = _adminChatBot.ReplyToEvent(null, result.Value.balanceId,
                            $"Your balance was just updated to {result.Value.balance}");
                    }
                }
            }

            return;
        }

        await next.Invoke(context);
    }
}