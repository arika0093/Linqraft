using Linqraft.Playground;
using Linqraft.Playground.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Register HttpClient for API calls (GitHub stars, etc.)
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
});

// Register shared compilation service first (singleton)
builder.Services.AddSingleton<SharedCompilationService>();

// Register services that depend on shared compilation
builder.Services.AddSingleton<TemplateService>();
builder.Services.AddSingleton<CodeGenerationService>();
builder.Services.AddSingleton<SemanticHighlightingService>();
builder.Services.AddSingleton<CSharpSyntaxHighlighter>();
builder.Services.AddScoped<UrlStateService>();

await builder.Build().RunAsync();
