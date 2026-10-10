using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

await VerificacionUsuarioBase.AvisarSiSalteaRlsAsync(app);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
