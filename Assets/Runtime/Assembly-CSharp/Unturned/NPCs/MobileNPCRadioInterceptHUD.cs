using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Single-player HOST ONLY textual rendering of a simulated NPC radio
    /// transmission. No fake microphone voice, public chat broadcast or UI
    /// cursor/HUD manipulation. Does not run on dedicated servers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MobileNPCRadioInterceptHUD : MonoBehaviour
    {
        private static MobileNPCRadioInterceptHUD instance;
        private uint activeFrequency;
        private string subtitle;
        private float visibleUntil;

        private void Awake()
        {
            instance = this;
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        /// <summary>
        /// Same equipment and frequency conditions as PlayerVoice's native
        /// radio culling. Player cannot read the report by simply being near
        /// an NPC or by possessing an unequipped walkie in inventory.
        /// </summary>
        public static bool CanLocalPlayerIntercept(uint frequency)
        {
            if (!Provider.isServer || Dedicator.IsDedicatedServer)
                return false;
            Player local = Player.player;
            return local != null && local.quests != null && local.voice != null
                && local.life != null && !local.life.isDead
                && local.voice.canHearRadio
                && local.quests.radioFrequency == frequency;
        }

        public static void PresentServerTransmission(uint frequency,
            MobileNPCRadioMessage message)
        {
            if (instance == null || message == null || !message.IsFresh(Time.time)
                || !CanLocalPlayerIntercept(frequency))
                return;
            instance.activeFrequency = frequency;
            instance.subtitle = message.FormatInterceptedSubtitle();
            instance.visibleUntil = Time.time + 5f;
        }

#if !DEDICATED_SERVER
        private void OnGUI()
        {
            if (string.IsNullOrEmpty(subtitle))
                return;
            // Revocation is irreversible for this transmission. Tuning away,
            // removing the earpiece, or dying must not let the player recover
            // an old overheard message by quickly tuning back in.
            if (Time.time >= visibleUntil
                || !CanLocalPlayerIntercept(activeFrequency))
            {
                subtitle = null;
                visibleUntil = 0f;
                return;
            }
            float width = Mathf.Min(520f, Screen.width - 32f);
            if (width < 120f)
                return;
            Rect panel = new Rect((Screen.width - width) * 0.5f, 70f,
                width, 70f);
            GUI.Box(panel, GUIContent.none);
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.normal.textColor = Color.white;
            style.wordWrap = true;
            style.fontSize = Mathf.Clamp(Screen.height / 72, 12, 18);
            GUI.Label(new Rect(panel.x + 12f, panel.y + 8f,
                panel.width - 24f, panel.height - 12f),
                "[RÁDIO " + activeFrequency + "] " + subtitle, style);
        }
#endif
    }
}
