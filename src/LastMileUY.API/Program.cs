using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Envios.Application;
using LastMileUY.API.Modules.Envios.Endpoints;
using LastMileUY.API.Modules.Identidad.Domain;
using LastMileUY.API.Modules.Identidad.Endpoints;
using LastMileUY.API.Modules.Identidad.Infrastructure;
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

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

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
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers(); 

// ENDPOINTS
app.MapAuthEndpoints();
app.MapEnviosEndpoints();

app.Run();