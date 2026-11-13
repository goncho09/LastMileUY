using LastMileUY.DriverPWA;
using LastMileUY.DriverPWA.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var urlApi = builder.Configuration["Api:Url"];

builder.Services.AddScoped(_ => new HttpClient(new CredencialesHandler { InnerHandler = new HttpClientHandler() })
{
    BaseAddress = new Uri(string.IsNullOrWhiteSpace(urlApi) ? builder.HostEnvironment.BaseAddress : urlApi)
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AlmacenLocal>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<RutasService>();
builder.Services.AddScoped<SincronizacionService>();
builder.Services.AddScoped<ColaOperacionesService>();
builder.Services.AddScoped<OperacionesConfirmadasService>();
builder.Services.AddScoped<BultosIdentificadosService>();

await builder.Build().RunAsync();
