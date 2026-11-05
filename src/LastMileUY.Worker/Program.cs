using LastMileUY.Contracts.Mensajeria;
using LastMileUY.Worker;
using Npgsql;
using OpenTelemetry.Exporter;
using Serilog;
using Serilog.Events;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

// Logs estructurados para el Worker.
builder.Services.AddSerilog((servicios, configuracion) =>
{
    configuracion
        .ReadFrom.Configuration(builder.Configuration)
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

// Observabilidad: trazas y métricas del Worker.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: "LastMileUY.Worker",
            serviceVersion: "1.0.0"))
    .WithTracing(tracing => tracing
        .AddHttpClientInstrumentation()
        .AddOtlpExporter()
        .AddSource(TelemetriaMensajeria.Nombre))
    .WithMetrics(metrics => metrics
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        // Intentos de avisos a comercios y avisos que quedaron fallidos (TelemetriaAvisos)
        .AddMeter(TelemetriaAvisos.Nombre)
        .AddOtlpExporter(options =>
        {
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
            options.Endpoint = new Uri(
                "http://prometheus:9090/api/v1/otlp/v1/metrics");
        }));

// Consume los eventos de envío que la API publica en RabbitMQ (ver docs/mensajeria.md)
builder.Services.AddSingleton(new OpcionesConsumidor(
    builder.Configuration.GetConnectionString("RabbitMQ")
        ?? throw new InvalidOperationException("Falta configurar ConnectionStrings:RabbitMQ."),
    builder.Configuration.GetSection("Mensajeria:EsperasReintento").Get<TimeSpan[]>()
        ?? OpcionesConsumidor.EsperasPorDefecto));

// La base con el usuario de la aplicación (lastmileuy_app): le aplica el RLS del ADR-01
builder.Services.AddSingleton(NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Falta configurar ConnectionStrings:DefaultConnection.")));

builder.Services.AddSingleton<RepositorioMensajesFallidos>();
builder.Services.AddSingleton<SenalAvisos>();
builder.Services.AddSingleton<IProcesadorEventos, ProcesadorAvisos>();
builder.Services.AddHostedService<ConsumidorEventosEnvio>();

// Manda los avisos por HTTP a los comercios. Avisos:PermitirDestinosLocales deja usar http y
// direcciones internas: solo para desarrollo, con el receptor de prueba (ver docs/mensajeria.md)
var permitirLocales = builder.Configuration.GetValue<bool>("Avisos:PermitirDestinosLocales");
builder.Services.AddHttpClient<ClienteAvisos>(http => http.Timeout = ClienteAvisos.TiempoMaximo)
    .ConfigurePrimaryHttpMessageHandler(() => ClienteAvisos.CrearManejador(permitirLocales));
builder.Services.AddHostedService<DespachadorAvisos>();


var host = builder.Build();

// Las métricas de avisos arrancan en 0, para que el tablero técnico vea desde el primer aviso
TelemetriaAvisos.Inicializar();
host.Run();
