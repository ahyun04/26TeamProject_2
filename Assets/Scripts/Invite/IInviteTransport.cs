using System;
using System.Threading.Tasks;

namespace LockdownProtocol.Lobby.Invite
{
    public interface IInviteTransport
    {
        Task<PlayerPresenceSnapshot> QueryPresence(string playerId);

        void DeliverInvite(InviteData invite);

        void NotifyResult(string playerId, string inviteId, InviteState finalState, string message, string approvedRoomId = null);

        event Action<string /*inviteId*/, bool /*accepted*/> InviteResponseReceived;
    }
}