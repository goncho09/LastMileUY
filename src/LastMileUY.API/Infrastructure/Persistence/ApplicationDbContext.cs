using System.Reflection;
using LastMileUY.API.Infrastructure.Mensajeria;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Destinatarios.Domain;
using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Identidad.Domain;
using LastMileUY.API.Modules.Mensajeria.Domain;
using LastMileUY.API.Modules.Notificaciones.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.API.Modules.Seguimiento.Domain;
using LastMileUY.API.Modules.Sincronizacion.Domain;
using LastMileUY.API.Modules.Tarifas.Domain;
using LastMileUY.API.Modules.Integraciones.Domain;
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

    public int? OperadorActual => contextoOperador.OperadorId;

    public int? ComercioActual => contextoOperador.ComercioId;

    public bool EnAmbientePruebasActual => contextoOperador.EnAmbientePruebas;

    public DbSet<Operador> Operadores => Set<Operador>();
    public DbSet<ReglaOperativa> ReglasOperativas => Set<ReglaOperativa>();
    public DbSet<ReglaEntregaParcial> ReglasEntregaParcial => Set<ReglaEntregaParcial>();
    public DbSet<FuncionalidadOperador> FuncionalidadesOperador => Set<FuncionalidadOperador>();
    public DbSet<Zona> Zonas => Set<Zona>();
    public DbSet<FranjaHoraria> FranjasHorarias => Set<FranjaHoraria>();

    public DbSet<Comercio> Comercios => Set<Comercio>();
    public DbSet<OperadorComercio> OperadoresComercios => Set<OperadorComercio>();

    public DbSet<Destinatario> Destinatarios => Set<Destinatario>();
    public DbSet<Direccion> Direcciones => Set<Direccion>();

    public DbSet<Envio> Envios => Set<Envio>();
    public DbSet<Bulto> Bultos => Set<Bulto>();

    public DbSet<CuadroTarifario> CuadrosTarifarios => Set<CuadroTarifario>();
    public DbSet<Tarifa> Tarifas => Set<Tarifa>();
    public DbSet<AjusteTarifa> AjustesTarifa => Set<AjusteTarifa>();
    public DbSet<Liquidacion> Liquidaciones => Set<Liquidacion>();
    public DbSet<DetalleLiquidacion> DetallesLiquidacion => Set<DetalleLiquidacion>();

    public DbSet<Repartidor> Repartidores => Set<Repartidor>();
    public DbSet<Vehiculo> Vehiculos => Set<Vehiculo>();
    public DbSet<Ruta> Rutas => Set<Ruta>();
    public DbSet<Parada> Paradas => Set<Parada>();
    public DbSet<BultoParada> BultosParada => Set<BultoParada>();
    public DbSet<Posicion> Posiciones => Set<Posicion>();
    public DbSet<Incidencia> Incidencias => Set<Incidencia>();
    public DbSet<Rendicion> Rendiciones => Set<Rendicion>();

    public DbSet<IntentoEntrega> IntentosEntrega => Set<IntentoEntrega>();
    public DbSet<MotivoNoEntrega> MotivosNoEntrega => Set<MotivoNoEntrega>();
    public DbSet<PruebaEntrega> PruebasEntrega => Set<PruebaEntrega>();
    public DbSet<Devolucion> Devoluciones => Set<Devolucion>();

    public DbSet<ResultadoBultoIntento> ResultadosBultosIntentos
        => Set<ResultadoBultoIntento>();

    public DbSet<EventoEnvio> EventosEnvio => Set<EventoEnvio>();

    public DbSet<Notificacion> Notificaciones => Set<Notificacion>();
    public DbSet<SuscripcionAvisos> SuscripcionesAvisos => Set<SuscripcionAvisos>();
    public DbSet<ClaveApi> ClavesApi => Set<ClaveApi>();
    public DbSet<EntregaAviso> EntregasAvisos => Set<EntregaAviso>();
    public DbSet<IntentoAviso> IntentosAvisos => Set<IntentoAviso>();

    public DbSet<MensajeFallido> MensajesFallidos => Set<MensajeFallido>();
    public DbSet<MensajeReintentoOutbox> MensajesReintentoOutbox
        => Set<MensajeReintentoOutbox>();

    public DbSet<CorreoSeguimiento> CorreosSeguimiento
        => Set<CorreoSeguimiento>();

    public DbSet<OperacionSincronizada> OperacionesSincronizadas => Set<OperacionSincronizada>();

    public DbSet<MensajeOutbox> MensajesOutbox => Set<MensajeOutbox>();

    public DbSet<MensajeProcesado> MensajesProcesados => Set<MensajeProcesado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ApplicationDbContext).Assembly
        );

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

        modelBuilder.Entity<MensajeReintentoOutbox>(entity =>
        {
            entity.ToTable("MensajesReintentoOutbox");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.MensajeId)
                .IsRequired();

            entity.Property(x => x.Contenido)
                .IsRequired();

            entity.Property(x => x.FechaCreacion)
                .IsRequired();

            entity.HasOne(x => x.MensajeFallido)
                .WithMany()
                .HasForeignKey(x => x.MensajeFallidoId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => x.MensajeFallidoId);

            entity.HasIndex(x => x.FechaCreacion)
                .HasFilter("\"FechaPublicacion\" IS NULL");
        });

    }

    private static readonly MethodInfo MetodoFiltroOperador =
        typeof(ApplicationDbContext).GetMethod(
            nameof(AplicarFiltroOperador),
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo MetodoFiltroOperadorYComercio =
        typeof(ApplicationDbContext).GetMethod(
            nameof(AplicarFiltroOperadorYComercio),
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    private void AplicarFiltroOperador<T>(ModelBuilder modelBuilder)
        where T : class, IPerteneceAOperador
    {
        modelBuilder.Entity<T>().HasQueryFilter(e => e.OperadorId == OperadorActual);
    }

    private void AplicarFiltroOperadorYComercio<T>(ModelBuilder modelBuilder)
        where T : class, IPerteneceAOperador, IPerteneceAComercio
    {
        modelBuilder.Entity<T>().HasQueryFilter(e =>
            e.OperadorId == OperadorActual
            && (ComercioActual == null || e.ComercioId == ComercioActual));
    }

    private void AplicarFiltrosDeTablasHijas(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Bulto>().HasQueryFilter(x => Envios.Any(e => e.Id == x.EnvioId));
        modelBuilder.Entity<EventoEnvio>().HasQueryFilter(x => Envios.Any(e => e.Id == x.EnvioId));
        modelBuilder.Entity<IntentoEntrega>().HasQueryFilter(x => Envios.Any(e => e.Id == x.EnvioId));
        modelBuilder.Entity<Parada>().HasQueryFilter(x => Envios.Any(e => e.Id == x.EnvioId));
        modelBuilder.Entity<Devolucion>().HasQueryFilter(x => Envios.Any(e => e.Id == x.EnvioId));
        modelBuilder.Entity<Notificacion>().HasQueryFilter(x => Envios.Any(e => e.Id == x.EnvioId));
        modelBuilder.Entity<EntregaAviso>().HasQueryFilter(x => Envios.Any(e => e.Id == x.EnvioId));
        modelBuilder.Entity<IntentoAviso>().HasQueryFilter(x => EntregasAvisos.Any(a => a.Id == x.EntregaAvisoId));
        modelBuilder.Entity<MensajeOutbox>().HasQueryFilter(x => Envios.Any(e => e.Id == x.EnvioId));
        modelBuilder.Entity<PruebaEntrega>().HasQueryFilter(x => IntentosEntrega.Any(i => i.Id == x.IntentoEntregaId));
        modelBuilder.Entity<ResultadoBultoIntento>()
            .HasQueryFilter(x => IntentosEntrega.Any(i => i.Id == x.IntentoEntregaId));

        modelBuilder.Entity<Incidencia>().HasQueryFilter(x => Rutas.Any(r => r.Id == x.RutaId));
        modelBuilder.Entity<Posicion>().HasQueryFilter(x => Rutas.Any(r => r.Id == x.RutaId));
        modelBuilder.Entity<Rendicion>().HasQueryFilter(x => Rutas.Any(r => r.Id == x.RutaId));
        modelBuilder.Entity<BultoParada>().HasQueryFilter(x => Paradas.Any(p => p.Id == x.ParadaId));

        modelBuilder.Entity<Tarifa>().HasQueryFilter(x => CuadrosTarifarios.Any(c => c.Id == x.CuadroTarifarioId));
        modelBuilder.Entity<AjusteTarifa>().HasQueryFilter(x => Tarifas.Any(t => t.Id == x.TarifaId));
        modelBuilder.Entity<DetalleLiquidacion>().HasQueryFilter(x => Liquidaciones.Any(l => l.Id == x.LiquidacionId));

        modelBuilder.Entity<FranjaHoraria>().HasQueryFilter(x => Zonas.Any(z => z.Id == x.ZonaId));

        modelBuilder.Entity<Destinatario>().HasQueryFilter(d =>
            (ComercioActual != null
                ? d.ComercioId == ComercioActual
                : OperadoresComercios.Any(oc =>
                    oc.ComercioId == d.ComercioId &&
                    oc.OperadorId == OperadorActual &&
                    oc.Activo))
            && d.EsPrueba == EnAmbientePruebasActual);

        modelBuilder.Entity<Direccion>().HasQueryFilter(x =>
            Destinatarios.Any(d => d.Id == x.DestinatarioId));

        modelBuilder.Entity<Comercio>().HasQueryFilter(c =>
            ComercioActual != null
                ? c.Id == ComercioActual
                : OperadoresComercios.Any(oc =>
                    oc.ComercioId == c.Id &&
                    oc.OperadorId == OperadorActual &&
                    oc.Activo));

        modelBuilder.Entity<CorreoSeguimiento>()
            .HasQueryFilter(x =>
                x.OperadorId == OperadorActual &&
                x.ComercioId == ComercioActual);
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

        foreach (var entrada in ChangeTracker.Entries<Destinatario>())
        {
            if (entrada.State == EntityState.Added)
            {
                entrada.Entity.EsPrueba = EnAmbientePruebasActual;
            }
            else if (entrada.State == EntityState.Modified
                     && entrada.Property(e => e.EsPrueba).IsModified)
            {
                throw new InvalidOperationException("No se puede pasar un destinatario de un ambiente a otro.");
            }
        }
    }
}