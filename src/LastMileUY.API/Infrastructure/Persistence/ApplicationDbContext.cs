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

using System.Reflection;
using LastMileUY.API.Infrastructure.Multitenancy;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Infrastructure.Persistence;

public class ApplicationDbContext : IdentityDbContext<Usuario, Rol, string>
{
    private readonly IContextoOperador contextoOperador;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        IContextoOperador contextoOperador
    ) : base(options)
    {
        this.contextoOperador = contextoOperador;
    }

    // Operador del pedido actual. Los filtros globales lo leen en cada consulta.
    public int? OperadorActual => contextoOperador.OperadorId;

    
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

        // Primera barrera del multitenancy: toda entidad de un operador
        // se filtra sola por el operador actual.
        var entidadesDeOperador = modelBuilder.Model.GetEntityTypes()
            .Select(t => t.ClrType)
            .Where(t => typeof(IPerteneceAOperador).IsAssignableFrom(t))
            .ToList();

        foreach (var tipo in entidadesDeOperador)
        {
            MetodoFiltroOperador.MakeGenericMethod(tipo).Invoke(this, [modelBuilder]);
        }

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

    private static readonly MethodInfo MetodoFiltroOperador =
        typeof(ApplicationDbContext).GetMethod(
            nameof(AplicarFiltroOperador),
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    // Sin operador (OperadorActual null) la comparación da falso y no se ve nada.
    private void AplicarFiltroOperador<T>(ModelBuilder modelBuilder)
        where T : class, IPerteneceAOperador
    {
        modelBuilder.Entity<T>().HasQueryFilter(e => e.OperadorId == OperadorActual);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AsignarYValidarOperador();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AsignarYValidarOperador();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // Lo nuevo queda a nombre del operador actual, y nada se puede pasar a otro operador.
    private void AsignarYValidarOperador()
    {
        foreach (var entrada in ChangeTracker.Entries<IPerteneceAOperador>())
        {
            if (entrada.State == EntityState.Added)
            {
                if (OperadorActual is null)
                {
                    throw new InvalidOperationException(
                        $"No se puede crear {entrada.Entity.GetType().Name} sin un operador en el contexto.");
                }

                if (entrada.Entity.OperadorId == 0)
                {
                    entrada.Entity.OperadorId = OperadorActual.Value;
                }
                else if (entrada.Entity.OperadorId != OperadorActual)
                {
                    throw new InvalidOperationException(
                        $"No se puede crear {entrada.Entity.GetType().Name} para otro operador.");
                }
            }
            else if (entrada.State == EntityState.Modified
                     && entrada.Property(e => e.OperadorId).IsModified)
            {
                throw new InvalidOperationException(
                    $"No se puede cambiar el operador de {entrada.Entity.GetType().Name}.");
            }
        }
    }
}