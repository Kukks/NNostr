using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using LinqKit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebSockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NBitcoin;
using Newtonsoft.Json.Linq;
using NNostr.Client;
using Relay.Data;

namespace Relay
{
    public class Startup
    {
        private readonly IConfiguration _configuration;

        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContextFactory<RelayDbContext>(builder =>
            {
                var connString = _configuration.GetConnectionString(RelayDbContext.DatabaseConnectionStringName);
                if (string.IsNullOrEmpty(connString))
                {
                    throw new Exception("Database: Connection string not set");
                }

                builder.UseNpgsql(connString, optionsBuilder => { optionsBuilder.EnableRetryOnFailure(10); });
                builder.WithExpressionExpanding();
            });
            services.AddHostedService<MigrationHostedService>();
            services.AddHostedService(provider => provider.GetService<EventNostrMessageHandler>());
            services.AddHostedService(provider => provider.GetService<BTCPayServerService>());
            services.AddHostedService<AdminChatBot>();
            services.AddLogging();
            services.AddOptions<RelayOptions>();
            services.AddSingleton<NostrEventService>();
            services.AddSingleton<BTCPayServerService>();
            services.AddSingleton<StateManager>();
            services.AddSingleton<ConnectionManager>();
            services.AddSingleton<WebSocketHandler>();
            services.AddSingleton<WebsocketMiddleware>();
            services.AddSingleton<Nip11Middleware>();
            services.AddSingleton<RestMiddleware>();
            services.AddSingleton<INostrMessageHandler, CloseNostrMessageHandler>();
            services.AddSingleton<EventNostrMessageHandler>();
            services.AddSingleton<INostrMessageHandler, EventNostrMessageHandler>(provider =>
                provider.GetService<EventNostrMessageHandler>());
            services.AddSingleton<INostrMessageHandler, RequestNostrMessageHandler>();
            services.AddSingleton<IHostedService>(provider => provider.GetService<ConnectionManager>());
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, WebSocketHandler webSocketHandler)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseWebSockets();
            app.UseMiddleware<WebsocketMiddleware>();
            app.UseMiddleware<Nip11Middleware>();
            app.UseMiddleware<RestMiddleware>();
        }
    }

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
}