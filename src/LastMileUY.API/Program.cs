using System.Security.Claims;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Destinatarios.Application;
using LastMileUY.API.Modules.Destinatarios.Endpoints;
using LastMileUY.API.Modules.Entregas.Endpoints;
using LastMileUY.API.Modules.Envios.Application;
using LastMileUY.API.Modules.Envios.Endpoints;
using LastMileUY.API.Modules.Identidad.Application;
using LastMileUY.API.Modules.Identidad.Domain;
using LastMileUY.API.Modules.Identidad.Endpoints;
using LastMileUY.API.Modules.Identidad.Infrastructure;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.API.Modules.Operadores.Endpoints;
using LastMileUY.API.Modules.Operadores.Infrastructure;
using LastMileUY.API.Modules.Rutas.Application;
using LastMileUY.API.Modules.Rutas.Endpoints;
using LastMileUY.API.Modules.Rutas.Infrastructure;
using LastMileUY.API.Modules.Sincronizacion.Application;
using LastMileUY.API.Modules.Sincronizacion.Endpoints;
using LastMileUY.API.Modules.Sincronizacion.Infrastructure;
using LastMileUY.API.Modules.Entregas.Infrastructure;
using LastMileUY.API.Modules.Entregas.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Multitenancy (ADR-01): de qué operador es cada pedido.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IContextoOperador, ContextoOperadorHttp>();

// Configura PostgreSQL como base de datos de la aplicación.
// El interceptor carga el operador en cada conexión para que apliquen las políticas RLS.
builder.Services.AddDbContext<ApplicationDbContext>((servicios, options) =>
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
        .AddInterceptors(new InterceptorOperadorConexion(
            servicios.GetRequiredService<IContextoOperador>()))
);


// Configurar ASPT.NET AUTH
builder.Services
    .AddIdentity<Usuario, Rol>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddClaimsPrincipalFactory<UsuarioClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<
    ISecurityStampValidator,
    UsuarioSecurityStampValidator>();

builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(1);
});

// Claves con las que ASP.NET cifra la cookie de sesión. Si se guardan en una carpeta
// (en Docker, un volumen), las sesiones sobreviven a reiniciar o recrear el contenedor.
var directorioClaves = builder.Configuration["DataProtection:Directorio"];

if (!string.IsNullOrWhiteSpace(directorioClaves))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("LastMileUY.API")
        .PersistKeysToFileSystem(new DirectoryInfo(directorioClaves));
}

// El operador que elige un usuario de comercio al entrar vive solo en la cookie (no en la base).
// Identity rearma los claims desde la base cada tanto (validación del security stamp):
// sin esto, el claim operador_id se perdería y el usuario de comercio empezaría a recibir 401.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.OnRefreshingPrincipal = context =>
    {
        var operador = context.CurrentPrincipal?.FindFirst(ContextoOperadorHttp.ClaimOperadorId);
        var identidad = context.NewPrincipal?.Identities.FirstOrDefault();

        if (operador is not null &&
            identidad is not null &&
            !identidad.HasClaim(c => c.Type == ContextoOperadorHttp.ClaimOperadorId))
        {
            identidad.AddClaim(new Claim(operador.Type, operador.Value));
        }

        return Task.CompletedTask;
    };
});

// Configurar COOKIES
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "LastMileUY.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;

    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;

    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };

    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

// Add services to the container.
builder.Services.AddControllers();

builder.Services.AddScoped<CrearEnvioService>();
builder.Services.AddScoped<ConsultasEnviosService>();
builder.Services.AddScoped<CambiarEstadoEnvioService>();
builder.Services.AddScoped<OperadorActualService>();
builder.Services.AddScoped<ConsultasDestinatariosService>();
builder.Services.AddScoped<OperadoresDeComercioService>();
builder.Services.AddScoped<PlanificarRutaService>();
builder.Services.AddScoped<ConsultasRutasService>();
builder.Services.AddScoped<SincronizarEntregasService>();
builder.Services.AddScoped<ConsultasSincronizacionService>();
builder.Services.AddScoped<ConfirmarEntregaService>();
builder.Services.AddSingleton<IAlmacenEvidencias, AlmacenEvidenciasEnDisco>();

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<SeleccionOperadorTemporalService>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("DriverPWA", policy =>
    {
        policy.WithOrigins(
                "http://localhost:8083",
                "https://localhost:7264"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});


var app = builder.Build();

// Primero las tablas (si corresponde), después los datos iniciales
await MigracionesAlIniciar.AplicarSiCorrespondeAsync(app);
await VerificacionUsuarioBase.AvisarSiSalteaRlsAsync(app);

await IdentitySeeder.SeedRolesAsync(app.Services);

// Configure the HTTP request pipeline y Scalar 
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    await IdentitySeeder.SeedAdminAsync(app.Services);
    await IdentitySeeder.SeedOperadorPruebaAsync(app.Services);
    await IdentitySeeder.SeedComercioPruebaAsync(app.Services);
    await IdentitySeeder.SeedDestinatariosPruebaAsync(app.Services);
    var operadoresPrueba = await IdentitySeeder.SeedOperadoresPruebaAsync(app.Services);
    await ReglasOperativasSeeder.SeedPruebaAsync(app.Services, operadoresPrueba);
    await RutasSeeder.SeedPruebaAsync(app.Services, operadoresPrueba);
    await MotivosNoEntregaSeeder.SeedPruebaAsync(app.Services, operadoresPrueba);
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("DriverPWA");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers(); 

// ENDPOINTS
app.MapAuthEndpoints();
app.MapEnviosEndpoints();
app.MapOperadorEndpoints();
app.MapDestinatariosEndpoints();
app.MapRutasEndpoints();
app.MapEntregasEndpoints();
app.MapSincronizacionEndpoints();

app.Run();

// Visible para las pruebas de punta a punta (WebApplicationFactory<Program>)
public partial class Program;
