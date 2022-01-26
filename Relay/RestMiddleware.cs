using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Relay;

public class RestMiddleware : IMiddleware
{
    private readonly IOptions<RelayOptions> _options;
    private readonly NostrEventService _nostrEventService;

    public RestMiddleware(IOptions<RelayOptions> options, NostrEventService nostrEventService)
    {
        _options = options;
        _nostrEventService = nostrEventService;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (_options.Value.EnableNip11 && !context.WebSockets.IsWebSocketRequest &&
            context.Request.Headers.Accept.Any(s =>
                s.Equals("application/json", StringComparison.InvariantCultureIgnoreCase)))
        {
            using var streamReader = new StreamReader(context.Request.Body);
            var body = await streamReader.ReadToEndAsync();
            var filters = JsonSerializer.Deserialize<NostrSubscriptionFilter[]>(body);
            var evts = await _nostrEventService.FetchData(filters);

            context.Response.Clear();
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsync(JsonSerializer.Serialize(evts));
            return;
        }

        await next.Invoke(context);
    }

    public class Nip11Response
    {
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("description")] public string Description { get; set; }
        [JsonPropertyName("pubkey")] public string PubKey { get; set; }
        [JsonPropertyName("contact")] public string Contact { get; set; }
        [JsonPropertyName("supported_nips")] public int[] SupportedNips { get; set; }
        [JsonPropertyName("software")] public string Software { get; set; }
        [JsonPropertyName("version")] public string Version { get; set; }
    }
}