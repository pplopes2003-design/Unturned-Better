using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Cheap, read-only local status refresh for the private Steam Server Code.
    /// The game server writes the file; never query Steamworks game-server
    /// APIs from this client or invent a code from a timer.
    /// </summary>
    internal sealed class MenuPlayFriendsHostCodeTicker : MonoBehaviour
    {
        private float nextPoll;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Dedicator.IsDedicatedServer) return;
            GameObject obj = new GameObject("FriendsHostCodeTicker");
            DontDestroyOnLoad(obj);
            obj.AddComponent<MenuPlayFriendsHostCodeTicker>();
        }

        private void Update()
        {
            if (!MenuPlayFriendsHostUI.IsActive || Time.unscaledTime < nextPoll)
                return;
            nextPoll = Time.unscaledTime + 1f;
            MenuPlayFriendsHostUI.RefreshSteamCodeDisplay();
        }
    }
}
