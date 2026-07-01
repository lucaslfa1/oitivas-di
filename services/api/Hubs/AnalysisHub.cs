using Microsoft.AspNetCore.SignalR;

namespace SinistroAPI.Hubs;

public class AnalysisHub : Hub
{
    // Métodos que o cliente pode chamar, se necessário.
    // Por enquanto, o servidor apenas envia eventos para os clientes.
    
    public async Task JoinGroup(string connectionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, connectionId);
    }

    public async Task SendMembersMessage(string username, string message)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var safeUsername = username.Trim();
        var safeMessage = message.Trim();

        if (safeUsername.Length > 50)
        {
            safeUsername = safeUsername[..50];
        }

        if (safeMessage.Length > 500)
        {
            safeMessage = safeMessage[..500];
        }

        await Clients.All.SendAsync(
            "ReceiveMembersMessage",
            safeUsername,
            safeMessage,
            DateTimeOffset.UtcNow
        );
    }
}
