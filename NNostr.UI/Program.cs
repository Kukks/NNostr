using BlazingPay.WebCommon;
using Fluxor;
using NNostr.UI;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NNostr.UI.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddScoped<LocalStorageService>();
builder.Services.AddScoped<SessionStorageService>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped(sp => new HttpClient {BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)});
builder.Services.AddFluxor(options => options.ScanAssemblies(typeof(Program).Assembly).UseReduxDevTools());
await builder.Build().RunAsync();