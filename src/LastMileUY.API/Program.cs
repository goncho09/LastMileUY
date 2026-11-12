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

// Comandos operativos para el despliegue. Se ejecutan dentro de la imagen exacta
// de la API, con la misma versión de EF Core y sus migraciones incluidas.
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

        // Código 10: el actualizador debe bloquear el despliegue automático.
        return 10;
    }

    await dbMigraciones.Database.MigrateAsync();
    Console.WriteLine("MIGRACIONES_APLICADAS");
    return 0;
}

// Logs estructurados para la API.
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

// Observabilidad: métricas y trazas de la API.
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
        // Aciertos y fallos del caché por caso (Infrastructure/Cache/TelemetriaCache)
        .AddMeter(TelemetriaCache.Nombre)
        // Operaciones de la app del repartidor y cuánto esperaron para sincronizarse
        .AddMeter(TelemetriaSincronizacion.Nombre)
        // Cortes pensados para la demora: de menos de un segundo (con señal) a horas (sin señal)
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

// Multitenancy (ADR-01): de qué operador es cada pedido.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IContextoOperador, ContextoOperadorHttp>();

// Identificador de correlación de cada pedido (lo carga UseCorrelacion, lo leen el outbox y los logs)
builder.Services.AddScoped<ContextoCorrelacion>();
builder.Services.AddSingleton<SenalOutbox>();

// Configura PostgreSQL como base de datos de la aplicación.
// El interceptor carga el operador en cada conexión para que apliquen las políticas RLS.
builder.Services.AddDbContext<ApplicationDbContext>((servicios, options) =>
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
        .AddInterceptors(
            new InterceptorOperadorConexion(servicios.GetRequiredService<IContextoOperador>()),
            // Al guardar cambios, borra del caché lo que quedó viejo (ver InvalidacionCacheInterceptor)
            new InvalidacionCacheInterceptor(servicios.GetRequiredService<CacheDistribuido>()),
            // Y deja en el outbox los eventos de los envíos, en el mismo guardado (ver docs/mensajeria.md)
            new OutboxInterceptor(
                servicios.GetRequiredService<ContextoCorrelacion>(),
                servicios.GetRequiredService<SenalOutbox>()))
);

// Mensajería: el publicador del outbox manda los eventos a RabbitMQ.
// Sin cadena "RabbitMQ" (pruebas que no la usan, la API fuera de Docker) no se publica: los
// eventos se siguen guardando en el outbox y salen cuando haya RabbitMQ.
var conexionRabbit = builder.Configuration.GetConnectionString("RabbitMQ");

if (!string.IsNullOrWhiteSpace(conexionRabbit))
{
    // El publicador lee el outbox con su propio usuario de la base (lastmileuy_publicador)
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
    .AddDefaultTokenProviders()
    .AddErrorDescriber<IdentityErrorDescriberEs>();

builder.Services.AddScoped<
    ISecurityStampValidator,
    UsuarioSecurityStampValidator>();

// Claves con las que ASP.NET cifra la cookie de sesión. Si se guardan en una carpeta
// (en Docker, un volumen), las sesiones sobreviven a reiniciar o recrear el contenedor.
var directorioClaves = builder.Configuration["DataProtection:Directorio"];

if (!string.IsNullOrWhiteSpace(directorioClaves))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("LastMileUY.API")
        .PersistKeysToFileSystem(new DirectoryInfo(directorioClaves));
}

// Revalidar la sesión en cada solicitud autenticada.
// Conservar el operador seleccionado por usuarios de comercio
// cuando Identity reconstruye los claims.
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

// Caché distribuido (ver ADR de caché): Redis, compartido por todas las instancias de la API.
// Sin cadena de conexión "Redis" (pruebas, o la API corriendo fuera de Docker) se usa
// un caché en memoria con la misma interfaz, así el resto del código no cambia.
var conexionRedis = builder.Configuration.GetConnectionString("Redis");

if (!string.IsNullOrWhiteSpace(conexionRedis))
{
    // Una sola conexión a Redis para el caché y para los contadores de aciertos y fallos.
    // Si Redis no está o se cae, la API sigue desde la base sin esperarlo (ver ConexionRedis).
    var redis = ConnectionMultiplexer.Connect(ConexionRedis.Opciones(conexionRedis));

    builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
    builder.Services.AddSingleton<IMetricasCache, MetricasCacheRedis>();

    // Al volver Redis borra lo cacheado: las invalidaciones de mientras estuvo caído se perdieron
    builder.Services.AddHostedService<LimpiezaCacheRedis>();

    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(redis);
        // Prefijo de todas las claves de la app en Redis
        options.InstanceName = CasosDeCache.PrefijoRedis;
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
    builder.Services.AddSingleton<IMetricasCache, MetricasCacheMemoria>();
}

// Configurar COOKIES
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "LastMileUY.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    // Lax: la app del repartidor le habla a la API por el mismo sitio (nginx le pasa /api). None
    // (la cookie viaja entre sitios) solo si hay orígenes de la app afuera, como con dotnet watch
    // en desarrollo (ver CORS más abajo)
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

// API pública (docs/api-publica.md): el sistema del comercio se autentica con una clave (X-Api-Key).
// Es un esquema aparte del de la cookie: solo lo usan los endpoints con la política ApiPublica (/api/v1),
// y esos endpoints no aceptan la cookie
builder.Services.AddAuthentication()
    .AddScheme<AuthenticationSchemeOptions, AutenticacionClaveApi>(AutenticacionClaveApi.Esquema, null);

// Las políticas por perfil van en cada endpoint (RequireAuthorization). Antes lo registraba de rebote
// AddControllers, que se sacó porque la API no tiene controllers
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(ApiPublicaV1Endpoints.Politica, politica => politica
        .AddAuthenticationSchemes(AutenticacionClaveApi.Esquema)
        .RequireAuthenticatedUser()
        .RequireRole("Comercio")
        .RequireClaim(AutenticacionClaveApi.ClaimClaveApiId));

    // Solo claves del ambiente de pruebas (la simulación de estados)
    options.AddPolicy(ApiPublicaV1Endpoints.PoliticaPruebas, politica => politica
        .AddAuthenticationSchemes(AutenticacionClaveApi.Esquema)
        .RequireAuthenticatedUser()
        .RequireRole("Comercio")
        .RequireClaim(AutenticacionClaveApi.ClaimClaveApiId)
        .RequireClaim(ContextoOperadorHttp.ClaimAmbiente, ContextoOperadorHttp.AmbientePruebas));
});


builder.Services.AddScoped<CrearEnvioService>();
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
builder.Services.AddSingleton<AmbientePruebasService.Cache>();
builder.Services.AddSingleton<IntentosFallidosClaveApi>();

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IAlmacenEvidencias, AlmacenEvidenciasEnDisco>();
builder.Services.AddSingleton<SeleccionOperadorTemporalService>();
builder.Services.AddSingleton<CacheDistribuido>();

// Seguimiento público: es el único endpoint sin sesión que lee envíos, así que tiene un tope de
// consultas por IP (429 al pasarlo). Probar códigos al azar no sirve igual (12 caracteres de 31
// posibles); el tope es para que nadie lo use para cargar la API.
// La página de seguimiento le consulta a la API desde su servidor (una sola IP para todos sus
// visitantes): por eso el tope es amplio acá y la página tiene el suyo, por visitante.
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

    // API pública: tope por clave, no por IP (varios comercios pueden salir por la misma IP, y un
    // comercio con mucho tráfico no frena a los demás). Diferenciado por tipo de pedido: un lote crea
    // hasta 100 envíos, así que tiene un tope propio, más bajo. Y por ambiente: las claves de prueba
    // tienen la mitad, porque son para probar la integración, no para cargar en volumen.
    // Todos se ajustan en la configuración.
    options.AddPolicy(ApiPublicaV1Endpoints.LimiteConsultas, contexto =>
        LimitePorClave(contexto, "PedidosPorMinuto", produccion: 120, pruebas: 60));
    options.AddPolicy(ApiPublicaV1Endpoints.LimiteLotes, contexto =>
        LimitePorClave(contexto, "LotesPorMinuto", produccion: 10, pruebas: 5));

    // Retry-After: cuántos segundos esperar, para que el integrador no reintente a ciegas
    options.OnRejected = async (rechazo, ct) =>
    {
        if (rechazo.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
            rechazo.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(espera.TotalSeconds)).ToString();

        await rechazo.HttpContext.Response.WriteAsJsonAsync(
            new { mensaje = "Demasiados pedidos: esperá un momento y volvé a intentar." }, ct);
    };
});

// ApiPublica:PedidosPorMinuto, ApiPublica:Pruebas:PedidosPorMinuto, etc.
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

// Documentación de la API (OpenAPI + Scalar):
// - "v1", la de la API pública: se publica siempre, también en producción (/api/v1/docs), porque es
//   la que leen los integradores de los comercios. Solo trae los endpoints de /api/v1.
// - "interna", la del resto de la API (portal, Backoffice, app del repartidor): solo en desarrollo.
builder.Services.AddOpenApi(ApiPublicaV1Endpoints.Version, DocumentacionApiPublica.Configurar);

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddOpenApi("interna");
}

// CORS: solo hace falta cuando la app del repartidor se sirve desde otro origen que la API
// (en desarrollo, con dotnet watch). En Docker nginx le pasa /api a la API y es el mismo origen.
// Los orígenes van en la configuración (AppRepartidor:Origenes), no en el código.
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

// La IP real del visitante detrás del proxy inverso (src/Compartido/ProxyInverso.cs)
builder.Services.AgregarProxyInverso(builder.Configuration);

var app = builder.Build();

// Primero de todo: los topes por IP, los logs y las redirecciones ven la IP y el esquema reales
app.UsarProxyInverso();

// Las métricas propias arrancan en 0, para que el tablero técnico vea desde el primer evento.
// Recién cuando arrancó el host: OpenTelemetry empieza a escuchar las métricas al arrancar, y lo que
// se mide antes se pierde (las series en 0 no aparecían y el primer evento no se veía en el tablero)
app.Lifetime.ApplicationStarted.Register(() =>
{
    TelemetriaCache.Inicializar();
    TelemetriaSincronizacion.Inicializar();
});

// Primero las tablas (si corresponde), después los datos iniciales
await MigracionesAlIniciar.AplicarSiCorrespondeAsync(app);
await VerificacionUsuarioBase.AvisarSiSalteaRlsAsync(app);

await IdentitySeeder.SeedRolesAsync(app.Services);
if (!app.Environment.IsDevelopment() &&
    app.Configuration.GetValue<bool>("SeedAdmin:Enabled"))
{
    await IdentitySeeder.SeedAdminAsync(app.Services);
}

// Documentación navegable de la API pública, en todos los ambientes (ver docs/api-publica.md)
app.MapOpenApi("/api/v1/openapi.json");
app.MapScalarApiReference("/api/v1/docs", opciones => opciones
    .WithTitle("LastMileUY · API pública v1")
    .AddDocument(ApiPublicaV1Endpoints.Version, "API pública v1", "/api/v1/openapi.json")
    .AddPreferredSecuritySchemes(AutenticacionClaveApi.Esquema));

// Solo en desarrollo: la documentación de toda la API (Scalar en /scalar) y los datos de prueba
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

// Primero, así todo lo que pase en el pedido (y sus registros) lleva el identificador
app.UseCorrelacion();

// Encabezados de seguridad (ver docs/seguridad.md): el navegador no adivina el tipo de contenido
// (las imágenes de evidencia se sirven con su tipo real, revisado al guardarlas)
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

// ENDPOINTS
app.MapAuthEndpoints();
app.MapUsuariosEndpoints();
app.MapEnviosEndpoints();
app.MapOperadorEndpoints();
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

// Habilitar consulta de mensajes fallidos cuando RabbitMQ está configurado.
if (!string.IsNullOrWhiteSpace(conexionRabbit))
    app.MapMensajesFallidosEndpoints();

app.MapHealthChecks("/health");

app.Run();
return 0;

// Visible para las pruebas de punta a punta (WebApplicationFactory<Program>)
public partial class Program;
