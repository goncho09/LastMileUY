using LastMileUY.Backoffice.Servicios;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

// De dónde salen los envíos: datos de prueba en memoria o la API real por HTTP.
var api = builder.Configuration.GetSection("Api");

if (api.GetValue<bool>("UsarDatosDePrueba"))
{
    builder.Services.AddSingleton<IEnviosApi, EnviosApiDePrueba>();
}
else
{
    var urlBase = api["UrlBase"]
        ?? throw new InvalidOperationException("Falta configurar Api:UrlBase.");

    builder.Services.AddHttpClient<IEnviosApi, EnviosApiHttp>(cliente =>
    {
        cliente.BaseAddress = new Uri(urlBase);
        cliente.Timeout = TimeSpan.FromSeconds(10);
    });
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
