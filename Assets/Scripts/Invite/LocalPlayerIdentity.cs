using UnityEngine;

namespace LockdownProtocol.Lobby.Invite
{
    public static class LocalPlayerIdentity
    {
        private const string PrefsKey = "LockdownProtocol_TempPlayerId";

        private static string _cachedId;

        public static string PlayerId
        {
            get
            {
                if (!string.IsNullOrEmpty(_cachedId)) return _cachedId;

                _cachedId = PlayerPrefs.GetString(PrefsKey, string.Empty);
                if (string.IsNullOrEmpty(_cachedId))
                {
                    _cachedId = System.Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(PrefsKey, _cachedId);
                    PlayerPrefs.Save();
                }

                return _cachedId;
            }
        }
    }
}