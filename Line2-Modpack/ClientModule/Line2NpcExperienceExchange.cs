using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Physical group conversation from Line 1. One firsthand experience
    /// may be reported to a nearby living same-group companion. Receiver
    /// updates weaker hearsay only; received reports never become outbound
    /// reports and cannot create a target or grant a path.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2NpcExperienceExchange : MonoBehaviour
    {
        private const float HearingRange = 14f;
        private const float ConversationCooldown = 18f;
        private const int MaxListeners = 3;
        private readonly List<PrototypeActor> _nearby =
            new List<PrototypeActor>(32);
        private SteamNpcPrototypeRuntime _runtime;
        private PrototypeActor _speaker;
        private Line2NPCGroupMember _group;
        private Line2MobileNPCAdaptiveMemory _memory;
        private float _nextAttempt;
        private float _nextLog;

        internal void Initialize(SteamNpcPrototypeRuntime runtime, PrototypeActor speaker)
        {
            _runtime = runtime;
            _speaker = speaker;
            _group = GetComponent<Line2NPCGroupMember>();
            _memory = GetComponent<Line2MobileNPCAdaptiveMemory>();
            _nextAttempt = Time.time + UnityEngine.Random.Range(6f, 11f);
        }

        private void Update()
        {
            if (Time.time < _nextAttempt) return;
            _nextAttempt = Time.time + 3f;
            if (_runtime == null || _speaker == null || _memory == null ||
                _group == null || !_group.Joined || _speaker.IsDead ||
                !_speaker.isActiveAndEnabled || _speaker.PlayerVisible ||
                _speaker.RecentlyDamaged || !Provider.isServer ||
                Dedicator.IsDedicatedServer || !Level.isLoaded ||
                (Provider.clients != null && Provider.clients.Count > 1))
                return;
            Line2MobileNPCAdaptiveMemory.SpokenLesson lesson;
            if (!_memory.TryGetUnspokenLessonServer(out lesson))
                return;
            _runtime.CopyLiveActors(_nearby, 32);
            int heard = 0;
            for (int i = 0; i < _nearby.Count && heard < MaxListeners; ++i)
            {
                PrototypeActor listener = _nearby[i];
                if (listener == null || listener == _speaker ||
                    listener.IsDead || !listener.isActiveAndEnabled ||
                    listener.PlayerVisible || listener.RecentlyDamaged)
                    continue;
                Line2NPCGroupMember listenerGroup =
                    listener.GetComponent<Line2NPCGroupMember>();
                if (listenerGroup == null || !listenerGroup.Joined ||
                    listenerGroup.GroupId != _group.GroupId ||
                    listenerGroup.Faction != _group.Faction)
                    continue;
                Vector3 from = transform.position + Vector3.up * 1.5f;
                Vector3 to = listener.transform.position + Vector3.up * 1.5f;
                if ((to - from).sqrMagnitude > HearingRange * HearingRange)
                    continue;
                // No sound through walls. Do not need the recipient's LOS
                // to the player's position: only conversation line clearance.
                if (Physics.Linecast(from, to, RayMasks.BLOCK_COLLISION,
                    QueryTriggerInteraction.Ignore))
                    continue;
                Line2MobileNPCAdaptiveMemory recipient =
                    listener.GetComponent<Line2MobileNPCAdaptiveMemory>();
                if (recipient == null ||
                    !recipient.ReceiveSpokenLessonServer(lesson))
                    continue;
                ++heard;
            }
            _nearby.Clear();
            if (heard <= 0) return;
            _memory.ConfirmLessonSpokenServer(lesson.Sequence);
            _nextAttempt = Time.time + ConversationCooldown;
            if (Time.time >= _nextLog)
            {
                _nextLog = Time.time + 20f;
                ProbeLogger.Log("NPC_EXPERIENCE_SHARED: speaker=" +
                    _speaker.Identity + " listeners=" + heard +
                    " kind=" + lesson.Kind +
                    " (weaker secondhand evidence, no target or route)");
            }
        }

        private void OnDisable() { _nearby.Clear(); }
    }
}
