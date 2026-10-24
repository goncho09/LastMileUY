using System.Security.Claims;
using LastMileUY.Backoffice.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace LastMileUY.Backoffice.SignalR;

[Authorize(Policy = Permisos.PoliticaBackoffice)]
public class EnviosHub : Hub
{
    public static string GrupoOperador(int operadorId)
        => $"operador:{operadorId}";

    public override async Task OnConnectedAsync()
    {
        var valor = Context.User?.FindFirstValue("operador_id");

        if (!int.TryParse(valor, out var operadorId) || operadorId <= 0)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            GrupoOperador(operadorId));

        await base.OnConnectedAsync();
    }
}