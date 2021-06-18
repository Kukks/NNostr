using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Relay.Data;

namespace Relay
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContextFactory<RelayDbContext>();
            services.AddSingleton<NostrEventService>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseWebSockets();
            app.UseMiddleware<WebsocketMiddleware>();
        }
    }
    
    
    public class WebSocketHandler
    {
        private readonly IEnumerable<INostrMessageHandler> _nostrMessageHandlers;
        protected ConnectionManager WebSocketWebSocketConnectionManager { get; set; }

        public WebSocketHandler(ConnectionManager webSocketWebSocketConnectionManager,IEnumerable<INostrMessageHandler> nostrMessageHandlers)
        {
            _nostrMessageHandlers = nostrMessageHandlers;
            WebSocketWebSocketConnectionManager = webSocketWebSocketConnectionManager;
        }

        public virtual async Task OnConnected(WebSocket socket)
        {
            WebSocketWebSocketConnectionManager.AddSocket(socket);
        }

        public virtual async Task OnDisconnected(WebSocket socket)
        {
            await WebSocketWebSocketConnectionManager.RemoveSocket(WebSocketWebSocketConnectionManager.GetId(socket));
        }

        public async Task SendMessageAsync(WebSocket socket, string message)
        {
            if(socket.State != WebSocketState.Open)
                return;

            await socket.SendAsync(buffer: new ArraySegment<byte>(array: Encoding.ASCII.GetBytes(message),
                                                                  offset: 0, 
                                                                  count: message.Length),
                                   messageType: WebSocketMessageType.Text,
                                   endOfMessage: true,
                                   cancellationToken: CancellationToken.None);          
        }

        public async Task SendMessageAsync(string socketId, string message)
        {
            await SendMessageAsync(WebSocketWebSocketConnectionManager.GetSocketById(socketId), message);
        }

        public async Task SendMessageToAllAsync(string message)
        {
            foreach(var pair in WebSocketWebSocketConnectionManager.GetAll())
            {
                if(pair.Value.State == WebSocketState.Open)
                    await SendMessageAsync(pair.Value, message);
            }
        }

        public Task ReceiveAsync(WebSocket socket, WebSocketReceiveResult result, string msg)
        {
            return Task.WhenAll(_nostrMessageHandlers.Select(handler => Task.Run(() => handler.Handle(msg))));
        }
    }

    public class WebsocketMiddleware : IMiddleware
    {
        private readonly WebSocketHandler _webSocketHandler;

        public WebsocketMiddleware(WebSocketHandler webSocketHandler)
        {
            _webSocketHandler = webSocketHandler;
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            if (context.Request.Path.StartsWithSegments(new PathString("ws")) && context.WebSockets.IsWebSocketRequest)
            {
                var socket = await context.WebSockets.AcceptWebSocketAsync();
                await _webSocketHandler.OnConnected(socket);

                await Receive(socket, async (result, buffer) =>
                {

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        await _webSocketHandler.ReceiveAsync(socket, result, buffer);
                        return;
                    }

                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocketHandler.OnDisconnected(socket);
                        return;
                    }

                });
            }

            await next.Invoke(context);
        }

        private async Task Receive(WebSocket socket, Action<WebSocketReceiveResult, string> handleMessage)
        {
            while (socket.State == WebSocketState.Open)
            {
                var buffer = new ArraySegment<byte>(new byte[1024 * 4]);
                string message = null;
                WebSocketReceiveResult result = null;
                try
                {
                    await using (var ms = new MemoryStream())
                    {
                        do
                        {
                            result = await socket.ReceiveAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                            ms.Write(buffer.Array, buffer.Offset, result.Count);
                        } while (!result.EndOfMessage);

                        ms.Seek(0, SeekOrigin.Begin);

                        using (var reader = new StreamReader(ms, Encoding.UTF8))
                        {
                            message = await reader.ReadToEndAsync().ConfigureAwait(false);
                        }
                    }

                    handleMessage(result, message);
                }
                catch (WebSocketException e)
                {
                    if (e.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                    {
                        socket.Abort();
                    }
                }
            }

            await _webSocketHandler.OnDisconnected(socket);
        }

    }
}