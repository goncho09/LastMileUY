using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using LastMileUY.DriverPWA;
using LastMileUY.DriverPWA.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// La dirección de la API sale de wwwroot/appsettings*.json; vacía, es la misma de la app
var urlApi = builder.Configuration["Api:Url"];

builder.Services.AddScoped(_ => new HttpClient(new CredencialesHandler { InnerHandler = new HttpClientHandler() })
{
    BaseAddress = new Uri(string.IsNullOrWhiteSpace(urlApi) ? builder.HostEnvironment.BaseAddress : urlApi)
});

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<RutasService>();
builder.Services.AddScoped<SincronizacionService>();

await builder.Build().RunAsync();
