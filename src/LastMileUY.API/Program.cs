using LastMileUY.Compartido;
using System.Security.Claims;
using System.Threading.RateLimiting;
using LastMileUY.API.Infrastructure.Cache;
using LastMileUY.API.Infrastructure.Correlacion;
using LastMileUY.API.Infrastructure.Mensajeria;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Comercios.Application;
using LastMileUY.API.Modules.Comercios.Endpoints;
using LastMileUY.API.Modules.Destinatarios.Application;
using LastMileUY.API.Modules.Destinatarios.Endpoints;
using LastMileUY.API.Modules.Entregas.Application;
using LastMileUY.API.Modules.Entregas.Infrastructure;
using LastMileUY.API.Modules.Envios.Application;
using LastMileUY.API.Modules.Envios.Endpoints;
using LastMileUY.API.Modules.Identidad.Application;
using LastMileUY.API.Modules.Identidad.Domain;
using LastMileUY.API.Modules.Identidad.Endpoints;
using LastMileUY.API.Modules.Identidad.Infrastructure;
using LastMileUY.API.Modules.Mensajeria.Application;
using LastMileUY.API.Modules.Notificaciones.Application;
using LastMileUY.API.Modules.Notificaciones.Endpoints;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.API.Modules.Operadores.Endpoints;
using LastMileUY.API.Modules.Operadores.Infrastructure;
using LastMileUY.API.Modules.Rutas.Application;
using LastMileUY.API.Modules.Rutas.Endpoints;
using LastMileUY.API.Modules.Rutas.Infrastructure;
using LastMileUY.API.Modules.Seguimiento.Application;
using LastMileUY.API.Modules.Seguimiento.Endpoints;
using LastMileUY.API.Modules.Sincronizacion.Application;
using LastMileUY.API.Modules.Sincronizacion.Endpoints;
using LastMileUY.API.Modules.Integraciones.Application;
using LastMileUY.API.Modules.Integraciones.Endpoints;
using LastMileUY.API.Modules.Sincronizacion.Infrastructure;
using LastMileUY.API.Modules.Tarifas.Application;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using StackExchange.Redis;

var argumentosOperativos = args;
var argumentosHost = argumentosOperativos
    .Where(argumento => argumento is not "--check-migrations" and not "--apply-migrations")
    .ToArray();
var builder = WebApplication.CreateBuilder(argumentosHost);

var comprobarMigraciones = args.Contains("--check-migrations", StringComparer.OrdinalIgnoreCase);
var aplicarMigraciones = args.Contains("--apply-migrations", StringComparer.OrdinalIgnoreCase);

if (comprobarMigraciones || aplicarMigraciones)
{
    var cadenaMigraciones = builder.Configuration.GetConnectionString("Migraciones")
        ?? throw new InvalidOperationException("Falta ConnectionStrings__Migraciones.");

    var opcionesMigraciones = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(cadenaMigraciones)
        .Options;

    await using var dbMigraciones = new ApplicationDbContext(
        opcionesMigraciones,
        new ContextoOperadorFijo(null));

    var pendientes = (await dbMigraciones.Database.GetPendingMigrationsAsync()).ToArray();

    if (comprobarMigraciones)
    {
        if (pendientes.Length == 0)
        {
            Console.WriteLine("SIN_MIGRACIONES_PENDIENTES");
            return 0;
        }

        Console.WriteLine("MIGRACIONES_PENDIENTES:");
        foreach (var migracion in pendientes)
            Console.WriteLine(migracion);

        return 10;
    }

    await dbMigraciones.Database.MigrateAsync();
    Console.WriteLine("MIGRACIONES_APLICADAS");
    return 0;
}

builder.Host.UseSerilog((contexto, servicios, configuracion) =>
{
    configuracion
        .ReadFrom.Configuration(contexto.Configuration)
        .ReadFrom.Services(servicios)
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
        .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Console(
            outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] " +
            "{SourceContext} {Message:lj} " +
            "{Properties:j}{NewLine}{Exception}");
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: "LastMileUY.API",
            serviceVersion: "1.0.0"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter()
        .AddSource(TelemetriaMensajeria.Nombre))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter(TelemetriaCache.Nombre)
        .AddMeter(TelemetriaSincronizacion.Nombre)
        .AddView(TelemetriaSincronizacion.NombreDemora, new ExplicitBucketHistogramConfiguration
        {
            Boundaries = [0.5, 1, 2, 5, 10, 30, 60, 300, 900, 1800, 3600, 7200, 14400, 28800]
        })
        .AddOtlpExporter(options =>
        {
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
            options.Endpoint = new Uri(
                "http://prometheus:9090/api/v1/otlp/v1/metrics");
        }));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IContextoOperador, ContextoOperadorHttp>();

builder.Services.AddScoped<ContextoCorrelacion>();
builder.Services.AddSingleton<SenalOutbox>();

builder.Services.AddDbContext<ApplicationDbContext>((servicios, options) =>
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
        .AddInterceptors(
            new InterceptorOperadorConexion(servicios.GetRequiredService<IContextoOperador>()),
            new InvalidacionCacheInterceptor(servicios.GetRequiredService<CacheDistribuido>()),
            new OutboxInterceptor(
                servicios.GetRequiredService<ContextoCorrelacion>(),
                servicios.GetRequiredService<SenalOutbox>()))
);

var conexionRabbit = builder.Configuration.GetConnectionString("RabbitMQ");

if (!string.IsNullOrWhiteSpace(conexionRabbit))
{
    var conexionOutbox = builder.Configuration.GetConnectionString("Outbox");

    if (string.IsNullOrWhiteSpace(conexionOutbox))
        throw new InvalidOperationException(
            "Con RabbitMQ configurado falta ConnectionStrings:Outbox (usuario lastmileuy_publicador).");

    builder.Services.AddSingleton(new ConexionRabbitMq(conexionRabbit));
    builder.Services.AddHostedService(servicios => new PublicadorOutbox(
        NpgsqlDataSource.Create(conexionOutbox),
        servicios.GetRequiredService<ConexionRabbitMq>(),
        servicios.GetRequiredService<SenalOutbox>(),
        servicios.GetRequiredService<ILogger<PublicadorOutbox>>()));
    builder.Services.AddHostedService(servicios =>
        new PublicadorReintentosOutboxWorker(
            NpgsqlDataSource.Create(conexionOutbox),
            servicios.GetRequiredService<ConexionRabbitMq>(),
            servicios.GetRequiredService<ILogger<PublicadorReintentosOutboxWorker>>()));
}

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
    .AddDefaultTokenProviders()
    .AddErrorDescriber<IdentityErrorDescriberEs>();

builder.Services.AddScoped<
    ISecurityStampValidator,
    UsuarioSecurityStampValidator>();

var directorioClaves = builder.Configuration["DataProtection:Directorio"];

if (!string.IsNullOrWhiteSpace(directorioClaves))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("LastMileUY.API")
        .PersistKeysToFileSystem(new DirectoryInfo(directorioClaves));
}

builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.Zero;

    options.OnRefreshingPrincipal = context =>
    {
        var operador = context.CurrentPrincipal?
            .FindFirst(ContextoOperadorHttp.ClaimOperadorId);

        var identidad = context.NewPrincipal?
            .Identities.FirstOrDefault();

        if (operador is not null &&
            identidad is not null &&
            !identidad.HasClaim(c =>
                c.Type == ContextoOperadorHttp.ClaimOperadorId))
        {
            identidad.AddClaim(new Claim(operador.Type, operador.Value));
        }

        return Task.CompletedTask;
    };
});

var conexionRedis = builder.Configuration.GetConnectionString("Redis");

if (!string.IsNullOrWhiteSpace(conexionRedis))
{
    var redis = ConnectionMultiplexer.Connect(ConexionRedis.Opciones(conexionRedis));

    builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
    builder.Services.AddSingleton<IMetricasCache, MetricasCacheRedis>();

    builder.Services.AddHostedService<LimpiezaCacheRedis>();

    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(redis);
        options.InstanceName = CasosDeCache.PrefijoRedis;
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
    builder.Services.AddSingleton<IMetricasCache, MetricasCacheMemoria>();
}

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "LastMileUY.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = builder.Configuration.GetSection("AppRepartidor:Origenes").Get<string[]>() is { Length: > 0 }
        ? SameSiteMode.None
        : SameSiteMode.Lax;

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

builder.Services.AddAuthentication()
    .AddScheme<AuthenticationSchemeOptions, AutenticacionClaveApi>(AutenticacionClaveApi.Esquema, null);

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(ApiPublicaV1Endpoints.Politica, politica => politica
        .AddAuthenticationSchemes(AutenticacionClaveApi.Esquema)
        .RequireAuthenticatedUser()
        .RequireRole("Comercio")
        .RequireClaim(AutenticacionClaveApi.ClaimClaveApiId));

    options.AddPolicy(ApiPublicaV1Endpoints.PoliticaPruebas, politica => politica
        .AddAuthenticationSchemes(AutenticacionClaveApi.Esquema)
        .RequireAuthenticatedUser()
        .RequireRole("Comercio")
        .RequireClaim(AutenticacionClaveApi.ClaimClaveApiId)
        .RequireClaim(ContextoOperadorHttp.ClaimAmbiente, ContextoOperadorHttp.AmbientePruebas));
});


builder.Services.AddScoped<CrearEnvioService>();
builder.Services.AddScoped<OpcionesEntregaService>();
builder.Services.AddScoped<ConsultasEnviosService>();
builder.Services.AddScoped<CambiarEstadoEnvioService>();
builder.Services.AddScoped<OperadorActualService>();
builder.Services.AddScoped<FuncionalidadesService>();
builder.Services.AddScoped<DepositoOperadorService>();
builder.Services.AddScoped<ReglasEntregaParcialService>();
builder.Services.AddScoped<ConsultasDestinatariosService>();
builder.Services.AddScoped<AltaDestinatariosService>();
builder.Services.AddScoped<OperadoresDeComercioService>();
builder.Services.AddScoped<PlanificarRutaService>();
builder.Services.AddScoped<SugerirOrdenService>();
builder.Services.AddScoped<ConsultasRutasService>();
builder.Services.AddScoped<SincronizarEntregasService>();
builder.Services.AddScoped<ConsultasSincronizacionService>();
builder.Services.AddScoped<SeguimientoPublicoService>();
builder.Services.AddScoped<SuscripcionesAvisosService>();
builder.Services.AddScoped<AvisosComercioService>();
builder.Services.AddScoped<ConsultasMensajesFallidosService>();
builder.Services.AddScoped<ReintentosMensajesFallidosService>();
builder.Services.AddScoped<GestionUsuariosService>();
builder.Services.AddScoped<AltaUsuarioInicialService>();
builder.Services.AddScoped<GestionOperadoresService>();
builder.Services.AddScoped<GestionComerciosService>();
builder.Services.AddScoped<ClavesApiService>();
builder.Services.AddScoped<ImportarEnviosService>();
builder.Services.AddScoped<AmbientePruebasService>();
builder.Services.AddScoped<SimularEnvioService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ZonasService>();
builder.Services.AddScoped<VehiculosService>();
builder.Services.AddScoped<MotivosNoEntregaService>();
builder.Services.AddScoped<ReglasOperativasService>();
builder.Services.AddScoped<CuadrosTarifariosService>();
builder.Services.AddScoped<TarificadorEnvios>();
builder.Services.AddSingleton<AmbientePruebasService.Cache>();
builder.Services.AddSingleton<IntentosFallidosClaveApi>();

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IAlmacenEvidencias, AlmacenEvidenciasEnDisco>();
builder.Services.AddSingleton<SeleccionOperadorTemporalService>();
builder.Services.AddSingleton<CacheDistribuido>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(SeguimientoEndpoints.PoliticaLimite, contexto =>
        RateLimitPartition.GetFixedWindowLimiter(
            contexto.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("Seguimiento:ConsultasPorMinuto", 120),
                Window = TimeSpan.FromMinutes(1)
            }));

    options.AddPolicy(ApiPublicaV1Endpoints.LimiteConsultas, contexto =>
        LimitePorClave(contexto, "PedidosPorMinuto", produccion: 120, pruebas: 60));
    options.AddPolicy(ApiPublicaV1Endpoints.LimiteLotes, contexto =>
        LimitePorClave(contexto, "LotesPorMinuto", produccion: 10, pruebas: 5));

    options.OnRejected = async (rechazo, ct) =>
    {
        if (rechazo.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
            rechazo.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(espera.TotalSeconds)).ToString();

        await rechazo.HttpContext.Response.WriteAsJsonAsync(
            new { mensaje = "Demasiados pedidos: esperá un momento y volvé a intentar." }, ct);
    };
});

RateLimitPartition<string> LimitePorClave(HttpContext contexto, string tope, int produccion, int pruebas)
{
    var enPruebas = contexto.User.HasClaim(ContextoOperadorHttp.ClaimAmbiente, ContextoOperadorHttp.AmbientePruebas);
    var configuracion = enPruebas ? $"ApiPublica:Pruebas:{tope}" : $"ApiPublica:{tope}";

    return RateLimitPartition.GetFixedWindowLimiter(
        $"{configuracion}:{contexto.User.FindFirstValue(AutenticacionClaveApi.ClaimClaveApiId) ?? contexto.Connection.RemoteIpAddress?.ToString()}",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue(configuracion, enPruebas ? pruebas : produccion),
            Window = TimeSpan.FromMinutes(1)
        });
}

builder.Services.AddOpenApi(ApiPublicaV1Endpoints.Version, DocumentacionApiPublica.Configurar);

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddOpenApi("interna");
}

var origenesAppRepartidor = builder.Configuration
    .GetSection("AppRepartidor:Origenes").Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("DriverPWA", policy =>
    {
        policy.WithOrigins(origenesAppRepartidor)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddHealthChecks();

builder.Services.AgregarProxyInverso(builder.Configuration);

var app = builder.Build();

app.UsarProxyInverso();

app.Lifetime.ApplicationStarted.Register(() =>
{
    TelemetriaCache.Inicializar();
    TelemetriaSincronizacion.Inicializar();
});

await MigracionesAlIniciar.AplicarSiCorrespondeAsync(app);
await VerificacionUsuarioBase.AvisarSiSalteaRlsAsync(app);

await IdentitySeeder.SeedRolesAsync(app.Services);
if (!app.Environment.IsDevelopment() &&
    app.Configuration.GetValue<bool>("SeedAdmin:Enabled"))
{
    await IdentitySeeder.SeedAdminAsync(app.Services);
}

app.MapOpenApi("/api/v1/openapi.json");
app.MapScalarApiReference("/api/v1/docs", opciones => opciones
    .WithTitle("LastMileUY · API pública v1")
    .AddDocument(ApiPublicaV1Endpoints.Version, "API pública v1", "/api/v1/openapi.json")
    .AddPreferredSecuritySchemes(AutenticacionClaveApi.Esquema));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(opciones => opciones
        .AddDocument("interna", "API interna")
        .AddDocument(ApiPublicaV1Endpoints.Version, "API pública v1"));
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

app.UseCorrelacion();

app.Use((contexto, siguiente) =>
{
    contexto.Response.Headers.XContentTypeOptions = "nosniff";
    return siguiente(contexto);
});

app.UseCors("DriverPWA");

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<ValidarVinculoComercioMiddleware>();

app.MapAuthEndpoints();
app.MapUsuariosEndpoints();
app.MapEnviosEndpoints();
app.MapOperadorEndpoints();
app.MapConfiguracionOperadorEndpoints();
app.MapDestinatariosEndpoints();
app.MapRutasEndpoints();
app.MapSincronizacionEndpoints();
app.MapSeguimientoEndpoints();
app.MapSuscripcionesAvisosEndpoints();
app.MapAvisosEndpoints();
app.MapCacheEndpoints();
app.MapGestionComerciosEndpoints();
app.MapGestionOperadoresEndpoints();
app.MapClavesApiEndpoints();
app.MapApiPublicaV1Endpoints();

if (!string.IsNullOrWhiteSpace(conexionRabbit))
    app.MapMensajesFallidosEndpoints();

app.MapHealthChecks("/health");

app.Run();
return 0;

public partial class Program;
