using LastMileUY.API.Modules.Identidad.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Destinatarios.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Tarifas.Domain;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.API.Modules.Sincronizacion.Domain;
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

    // Operador y comercio del pedido actual. Los filtros globales los leen en cada consulta.
    public int? OperadorActual => contextoOperador.OperadorId;

    public int? ComercioActual => contextoOperador.ComercioId;

    
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

    // Sincronización de la app del repartidor
    public DbSet<OperacionSincronizada> OperacionesSincronizadas => Set<OperacionSincronizada>();
    
    // Configura las entidades y relaciones de la base de datos,
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ApplicationDbContext).Assembly
        );

        // Primera barrera del multitenancy: toda entidad de un operador
        // se filtra sola por el operador actual (y por el comercio, si es de un comercio).
        var entidadesDeOperador = modelBuilder.Model.GetEntityTypes()
            .Select(t => t.ClrType)
            .Where(t => typeof(IPerteneceAOperador).IsAssignableFrom(t))
            .ToList();

        foreach (var tipo in entidadesDeOperador)
        {
            var metodo = typeof(IPerteneceAComercio).IsAssignableFrom(tipo)
                ? MetodoFiltroOperadorYComercio
                : MetodoFiltroOperador;

            metodo.MakeGenericMethod(tipo).Invoke(this, [modelBuilder]);
        }

        AplicarFiltrosDeTablasHijas(modelBuilder);

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

    private static readonly MethodInfo MetodoFiltroOperadorYComercio =
        typeof(ApplicationDbContext).GetMethod(
            nameof(AplicarFiltroOperadorYComercio),
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    // Sin operador (OperadorActual null) la comparación da falso y no se ve nada.
    private void AplicarFiltroOperador<T>(ModelBuilder modelBuilder)
        where T : class, IPerteneceAOperador
    {
        modelBuilder.Entity<T>().HasQueryFilter(e => e.OperadorId == OperadorActual);
    }

    // Segundo nivel: un usuario de comercio solo ve lo de su comercio.
    // Un usuario del operador (sin comercio) ve lo de todos los comercios del operador.
    private void AplicarFiltroOperadorYComercio<T>(ModelBuilder modelBuilder)
        where T : class, IPerteneceAOperador, IPerteneceAComercio
    {
        modelBuilder.Entity<T>().HasQueryFilter(e =>
            e.OperadorId == OperadorActual
            && (ComercioActual == null || e.ComercioId == ComercioActual));
    }

    // Tablas que no tienen OperadorId: se ven solo si se ve su "padre", que ya está filtrado.
    // Es el mismo criterio que las políticas RLS de la migración AislamientoCompleto.
    private void AplicarFiltrosDeTablasHijas(ModelBuilder modelBuilder)
    {
        // Hijas del envío
        modelBuilder.Entity<Bulto>().HasQueryFilter(x => Envios.Any(e => e.Id == x.EnvioId));
        modelBuilder.Entity<EventoEnvio>().HasQueryFilter(x => Envios.Any(e => e.Id == x.EnvioId));
        modelBuilder.Entity<IntentoEntrega>().HasQueryFilter(x => Envios.Any(e => e.Id == x.EnvioId));
        modelBuilder.Entity<Parada>().HasQueryFilter(x => Envios.Any(e => e.Id == x.EnvioId));
        modelBuilder.Entity<Devolucion>().HasQueryFilter(x => Envios.Any(e => e.Id == x.EnvioId));
        modelBuilder.Entity<Notificacion>().HasQueryFilter(x => Envios.Any(e => e.Id == x.EnvioId));
        modelBuilder.Entity<EntregaAviso>().HasQueryFilter(x => Envios.Any(e => e.Id == x.EnvioId));
        modelBuilder.Entity<PruebaEntrega>().HasQueryFilter(x => IntentosEntrega.Any(i => i.Id == x.IntentoEntregaId));

        // Hijas de la ruta
        modelBuilder.Entity<Incidencia>().HasQueryFilter(x => Rutas.Any(r => r.Id == x.RutaId));
        modelBuilder.Entity<Posicion>().HasQueryFilter(x => Rutas.Any(r => r.Id == x.RutaId));
        modelBuilder.Entity<Rendicion>().HasQueryFilter(x => Rutas.Any(r => r.Id == x.RutaId));

        // Tarifas y liquidaciones
        modelBuilder.Entity<Tarifa>().HasQueryFilter(x => CuadrosTarifarios.Any(c => c.Id == x.CuadroTarifarioId));
        modelBuilder.Entity<AjusteTarifa>().HasQueryFilter(x => Tarifas.Any(t => t.Id == x.TarifaId));
        modelBuilder.Entity<DetalleLiquidacion>().HasQueryFilter(x => Liquidaciones.Any(l => l.Id == x.LiquidacionId));

        // Hijas de la zona
        modelBuilder.Entity<FranjaHoraria>().HasQueryFilter(x => Zonas.Any(z => z.Id == x.ZonaId));

        // Destinatarios: son del comercio. El usuario de un comercio ve los suyos;
        // el operador ve los de los comercios con los que trabaja.
        modelBuilder.Entity<Destinatario>().HasQueryFilter(d =>
            ComercioActual != null
                ? d.ComercioId == ComercioActual
                : OperadoresComercios.Any(oc => oc.ComercioId == d.ComercioId && oc.OperadorId == OperadorActual));
        modelBuilder.Entity<Direccion>().HasQueryFilter(x => Destinatarios.Any(d => d.Id == x.DestinatarioId));

        // Comercios: el usuario de un comercio ve el suyo; el operador, los comercios con los que trabaja.
        // Solo filtro de EF, sin RLS: al dar de alta un comercio todavía no está vinculado a ningún
        // operador, y RLS no dejaría devolver su Id recién creado.
        modelBuilder.Entity<Comercio>().HasQueryFilter(c =>
            ComercioActual != null
                ? c.Id == ComercioActual
                : OperadoresComercios.Any(oc => oc.ComercioId == c.Id && oc.OperadorId == OperadorActual));
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

        // Segundo nivel: un usuario de comercio solo crea cosas de su comercio.
        // Un usuario del operador (sin comercio) elige el comercio, y RLS verifica que trabajen juntos.
        foreach (var entrada in ChangeTracker.Entries<IPerteneceAComercio>())
        {
            if (entrada.State == EntityState.Added && ComercioActual is not null)
            {
                if (entrada.Entity.ComercioId == 0)
                {
                    entrada.Entity.ComercioId = ComercioActual.Value;
                }
                else if (entrada.Entity.ComercioId != ComercioActual)
                {
                    throw new InvalidOperationException(
                        $"No se puede crear {entrada.Entity.GetType().Name} para otro comercio.");
                }
            }
            else if (entrada.State == EntityState.Modified
                     && entrada.Property(e => e.ComercioId).IsModified)
            {
                throw new InvalidOperationException(
                    $"No se puede cambiar el comercio de {entrada.Entity.GetType().Name}.");
            }
        }
    }
}