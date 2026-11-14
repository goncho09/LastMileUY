using LastMileUY.Contracts.Mensajeria;
using LastMileUY.Worker;
using Npgsql;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

var builder = Host.CreateApplicationBuilder(args);

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
        .AddMeter(TelemetriaAvisos.Nombre)
        .AddOtlpExporter(options =>
        {
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
            options.Endpoint = new Uri(
                "http://prometheus:9090/api/v1/otlp/v1/metrics");
        }));

builder.Services.AddSingleton(new OpcionesConsumidor(
    builder.Configuration.GetConnectionString("RabbitMQ")
        ?? throw new InvalidOperationException("Falta configurar ConnectionStrings:RabbitMQ."),
    builder.Configuration.GetSection("Mensajeria:EsperasReintento").Get<TimeSpan[]>()
        ?? OpcionesConsumidor.EsperasPorDefecto));

builder.Services.AddSingleton(NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Falta configurar ConnectionStrings:DefaultConnection.")));

builder.Services.AddSingleton<RepositorioMensajesFallidos>();
builder.Services.AddSingleton<SenalAvisos>();
builder.Services.AddSingleton<IProcesadorEventos, ProcesadorAvisos>();

builder.Services.AddHostedService<ConsumidorEventosEnvio>();
builder.Services.AddHostedService<ConsumidorCorreos>();

builder.Services.AddSingleton<IEnviadorCorreo, ServicioCorreo>();
builder.Services.AddSingleton<RepositorioCorreos>();
builder.Services.AddSingleton<ProcesadorCorreos>();

var permitirLocales = builder.Configuration.GetValue<bool>("Avisos:PermitirDestinosLocales");
builder.Services.AddHttpClient<ClienteAvisos>(http => http.Timeout = ClienteAvisos.TiempoMaximo)
    .ConfigurePrimaryHttpMessageHandler(() => ClienteAvisos.CrearManejador(permitirLocales));
builder.Services.AddHostedService<DespachadorAvisos>();


var host = builder.Build();

host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.Register(TelemetriaAvisos.Inicializar);
host.Run();
