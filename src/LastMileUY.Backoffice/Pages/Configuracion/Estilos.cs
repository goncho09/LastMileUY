namespace LastMileUY.Backoffice.Pages.Configuracion;

public static class Estilos
{
    public const string Etiqueta = "mb-1.5 block text-[13px] font-medium text-slate-300";

    public const string Campo =
        "w-full rounded-xl border border-white/[0.07] bg-field px-3 py-2 text-sm text-white outline-none " +
        "placeholder:text-slate-500 transition-colors focus:border-violet-400 focus:ring-2 focus:ring-violet-500/20";

    public const string ErrorCampo = "mt-1 block text-xs text-red-300";

    public const string Casilla = "h-4 w-4 rounded border-white/20 bg-field accent-violet-600";

    public const string BotonPrincipal =
        "inline-flex items-center justify-center gap-2 rounded-md bg-violet-600 px-4 py-2 text-sm font-semibold " +
        "text-white transition-colors hover:bg-violet-500";

    public const string BotonSecundario =
        "inline-flex items-center justify-center rounded-xl border border-white/[0.07] bg-white/5 px-4 py-2 text-sm " +
        "font-semibold text-slate-200 no-underline transition-colors hover:bg-white/10 hover:text-white";

    public const string Tarjeta = "overflow-hidden rounded-2xl border border-white/[0.07] bg-surface";

    public const string EncabezadoTarjeta = "border-b border-white/[0.07] px-5 py-4";

    public const string Tabla = "w-full min-w-[640px] text-left text-sm";

    public const string CabeceraTabla = "border-b border-white/[0.07] text-xs font-medium text-slate-500";

    public const string Celda = "px-4 py-3 align-top";

    public const string Activo = "inline-flex items-center gap-1.5 text-xs font-medium text-emerald-300 before:h-1.5 before:w-1.5 before:rounded-full before:bg-emerald-400";

    public const string Inactivo = "inline-flex items-center gap-1.5 text-xs font-medium text-slate-400 before:h-1.5 before:w-1.5 before:rounded-full before:bg-slate-500";
}
