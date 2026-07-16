using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Hatchly.App;
using Hatchly.App.Services;
using Hatchly.Core;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<RateService>();
builder.Services.AddSingleton<RaiseCalculator>();
builder.Services.AddSingleton<TroughCalculator>();

await builder.Build().RunAsync();
