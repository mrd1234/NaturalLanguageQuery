using System.Diagnostics;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NLQueryApp.Web.Components;
using NLQueryApp.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Get API base URL from configuration
Debug.WriteLine(builder.Configuration["ApiBaseUrl"]);
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5101";
Console.WriteLine($"Using API base URL: {apiBaseUrl}");

// Configure HttpClient
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });
builder.Services.AddScoped<ApiService>();

await builder.Build().RunAsync();