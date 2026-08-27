using System;

namespace LockdownProtocol.Lobby.Invite
{
    public class InviteData
    {
        public string InviteId;
        public string InviterId;
        public string InviterNickname;
        public string RoomHostPlayerId;
        public string TargetPlayerId;
        public string RoomId;
        public string RoomName;
        public int CurrentPlayerCount; 
        public int MaxPlayerCount;
        public InviteState State;
        public DateTime CreateTime;
        public DateTime ExpireTime;

        public bool IsExpired(DateTime now) => now >= ExpireTime;

        public static InviteData Create(
            string inviterId, string inviterNickname, string roomHostPlayerId,
            string targetPlayerId,
            string roomId, string roomName, int currentPlayerCount, int maxPlayerCount,
            TimeSpan lifetime)
        {
            var now = DateTime.UtcNow;
            return new InviteData
            {
                InviteId = Guid.NewGuid().ToString("N"),
                InviterId = inviterId,
                InviterNickname = inviterNickname,
                RoomHostPlayerId = roomHostPlayerId,
                TargetPlayerId = targetPlayerId,
                RoomId = roomId,
                RoomName = roomName,
                CurrentPlayerCount = currentPlayerCount,
                MaxPlayerCount = maxPlayerCount,
                State = InviteState.Pending,
                CreateTime = now,
                ExpireTime = now + lifetime
            };
        }
    }
}