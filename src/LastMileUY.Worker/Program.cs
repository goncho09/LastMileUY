using LastMileUY.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Consume los eventos de envío que la API publica en RabbitMQ (ver docs/mensajeria.md)
builder.Services.AddHostedService<ConsumidorEventosEnvio>();

// Los registros muestran el identificador de correlación de cada evento
builder.Logging.AddSimpleConsole(opciones => opciones.IncludeScopes = true);

var host = builder.Build();
host.Run();
