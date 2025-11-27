using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Linqraft.Playground;
using Linqraft.Playground.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton<TemplateService>();
builder.Services.AddSingleton<CodeGenerationService>();
builder.Services.AddSingleton<SemanticHighlightingService>();

await builder.Build().RunAsync();
