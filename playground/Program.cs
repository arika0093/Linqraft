using Linqraft.Playground;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Register HttpClient for API calls (GitHub stars, etc.)
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Compilation services are registered lazily when the Playground page is accessed
// This allows the heavy CodeAnalysis assemblies to be loaded on demand

await builder.Build().RunAsync();
