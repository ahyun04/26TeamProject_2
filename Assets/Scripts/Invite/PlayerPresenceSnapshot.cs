namespace LockdownProtocol.Lobby.Invite
{
    public struct PlayerPresenceSnapshot
    {
        public bool Exists;
        public bool IsOnline;
        public bool IsInAnyRoom;
        public bool IsInGame;

        public static PlayerPresenceSnapshot Offline => new PlayerPresenceSnapshot { Exists = true, IsOnline = false };
        public static PlayerPresenceSnapshot NotFound => new PlayerPresenceSnapshot { Exists = false };
    }
}