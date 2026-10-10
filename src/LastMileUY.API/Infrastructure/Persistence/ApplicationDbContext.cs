using LastMileUY.API.Modules.Identidad.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Destinatarios.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Tarifas.Domain;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Seguimiento.Domain;
using LastMileUY.API.Modules.Notificaciones.Domain;

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Infrastructure.Persistence;

public class ApplicationDbContext : IdentityDbContext<Usuario, Rol, string>
{
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options
    ) : base(options)
    {
    }
    
    
    // Operadores
    public DbSet<Operador> Operadores => Set<Operador>();
    public DbSet<ReglaOperativa> ReglasOperativas => Set<ReglaOperativa>();
    public DbSet<Zona> Zonas => Set<Zona>();
    public DbSet<FranjaHoraria> FranjasHorarias => Set<FranjaHoraria>();

    // Comercios
    public DbSet<Comercio> Comercios => Set<Comercio>();
    public DbSet<OperadorComercio> OperadoresComercios => Set<OperadorComercio>();

    // Destinatarios
    public DbSet<Destinatario> Destinatarios => Set<Destinatario>();
    public DbSet<Direccion> Direcciones => Set<Direccion>();

    // Envíos
    public DbSet<Envio> Envios => Set<Envio>();
    public DbSet<Bulto> Bultos => Set<Bulto>();

    // Tarifas
    public DbSet<CuadroTarifario> CuadrosTarifarios => Set<CuadroTarifario>();
    public DbSet<Tarifa> Tarifas => Set<Tarifa>();
    public DbSet<AjusteTarifa> AjustesTarifa => Set<AjusteTarifa>();
    public DbSet<Liquidacion> Liquidaciones => Set<Liquidacion>();
    public DbSet<DetalleLiquidacion> DetallesLiquidacion => Set<DetalleLiquidacion>();

    // Rutas
    public DbSet<Repartidor> Repartidores => Set<Repartidor>();
    public DbSet<Vehiculo> Vehiculos => Set<Vehiculo>();
    public DbSet<Ruta> Rutas => Set<Ruta>();
    public DbSet<Parada> Paradas => Set<Parada>();
    public DbSet<Posicion> Posiciones => Set<Posicion>();
    public DbSet<Incidencia> Incidencias => Set<Incidencia>();
    public DbSet<Rendicion> Rendiciones => Set<Rendicion>();

    // Entregas
    public DbSet<IntentoEntrega> IntentosEntrega => Set<IntentoEntrega>();
    public DbSet<MotivoNoEntrega> MotivosNoEntrega => Set<MotivoNoEntrega>();
    public DbSet<PruebaEntrega> PruebasEntrega => Set<PruebaEntrega>();
    public DbSet<Devolucion> Devoluciones => Set<Devolucion>();

    // Seguimiento
    public DbSet<EventoEnvio> EventosEnvio => Set<EventoEnvio>();

    // Notificaciones
    public DbSet<Notificacion> Notificaciones => Set<Notificacion>();
    public DbSet<SuscripcionAvisos> SuscripcionesAvisos => Set<SuscripcionAvisos>();
    public DbSet<EntregaAviso> EntregasAvisos => Set<EntregaAviso>();
    
    // Configura las entidades y relaciones de la base de datos,
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ApplicationDbContext).Assembly
        );

        modelBuilder.Entity<Operador>()
            .HasMany(o => o.Comercios)
            .WithMany(c => c.Operadores)
            .UsingEntity<OperadorComercio>(
                right => right
                    .HasOne<Comercio>()
                    .WithMany()
                    .HasForeignKey(oc => oc.ComercioId)
                    .OnDelete(DeleteBehavior.Restrict),

                left => left
                    .HasOne<Operador>()
                    .WithMany()
                    .HasForeignKey(oc => oc.OperadorId)
                    .OnDelete(DeleteBehavior.Restrict),

                join =>
                {
                    join.ToTable("OperadoresComercios");

                    join.HasKey(oc => new
                    {
                        oc.OperadorId,
                        oc.ComercioId
                    });
                }
            );
    }
}