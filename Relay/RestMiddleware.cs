using System;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using NNostr.Client;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Relay;

public class RestMiddleware : IMiddleware
{
    private readonly IOptionsMonitor<RelayOptions> _options;
    private readonly NostrEventService _nostrEventService;
    private readonly BTCPayServerService _btcPayServerService;

    public RestMiddleware(IOptionsMonitor<RelayOptions> options, NostrEventService nostrEventService, BTCPayServerService btcPayServerService)
    {
        _options = options;
        _nostrEventService = nostrEventService;
        _btcPayServerService = btcPayServerService;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (_options.CurrentValue.EnableNip11 && !context.WebSockets.IsWebSocketRequest &&
            context.Request.Headers.Accept.Any(s =>
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
            }else if (context.Request.Path.Value?.EndsWith("btcpay/webhook", StringComparison.InvariantCultureIgnoreCase) is
                      true)
            {
                var evt = JsonConvert.DeserializeObject<WebhookInvoiceEvent>(body);
                var i = await _btcPayServerService.HandleInvoice(evt.InvoiceId);
            }
            
            return;
        }

        await next.Invoke(context);
    }
}