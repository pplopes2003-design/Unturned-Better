////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Data-only NPC radio traffic contract. DOES NOT transmit. Native radio
    /// frequency, equipped Walkie Talkie/Earpiece, power and recipient channel
    /// must be validated before creating a usable player interception event.
    /// </summary>
    public sealed class MobileNPCRadioMessage
    {
        public enum Kind
        {
            EnemyContact,
            NeedReinforcements,
            UsefulLoot,
            Airdrop,
            UnknownBase
        }

        public readonly Kind Type;
        public readonly Vector3 ReportedPosition;
        public readonly float ObservedAt;
        public readonly float ExpiresAt;
        public readonly float Confidence;
        public readonly MobileNPCFaction.Id Faction;
        public readonly int SenderGroupId;
        public readonly string CallSign;

        private MobileNPCRadioMessage(Kind type, Vector3 reportedPosition,
            float observedAt, float expiresAt, float confidence,
            MobileNPCFaction.Id faction, int senderGroupId, string callSign)
        {
            Type = type;
            ReportedPosition = reportedPosition;
            ObservedAt = observedAt;
            ExpiresAt = expiresAt;
            Confidence = confidence;
            Faction = faction;
            SenderGroupId = senderGroupId;
            CallSign = callSign;
        }

        public static bool TryCreate(Kind type, Vector3 observedRegion, float observedAt,
            float timeToLive, float confidence, MobileNPCFaction.Id senderFaction,
            int senderGroupId, string callSign, out MobileNPCRadioMessage message)
        {
            message = null;
            if (!IsFinite(observedRegion.x) || !IsFinite(observedRegion.y)
                || !IsFinite(observedRegion.z) || !IsFinite(observedAt)
                || !IsFinite(timeToLive) || timeToLive <= 0f
                || !IsFinite(confidence) || confidence <= 0f
                || senderGroupId <= 0 || string.IsNullOrEmpty(callSign))
                return false;
            message = new MobileNPCRadioMessage(type, observedRegion,
                observedAt, observedAt + Mathf.Min(timeToLive, 300f),
                Mathf.Clamp01(confidence), senderFaction, senderGroupId, callSign);
            return true;
        }

        public bool IsFresh(float now)
        {
            return IsFinite(now) && now >= ObservedAt && now < ExpiresAt;
        }

        /// <summary>
        /// Simulated NPC dialogue subtitle, intended ONLY for a future receiver
        /// already verified against Unturned's native radio frequency and gear.
        /// No server-to-player UI event is sent by this method.
        /// </summary>
        public string FormatInterceptedSubtitle()
        {
            string region = "setor " + Mathf.RoundToInt(ReportedPosition.x / 100f)
                + "/" + Mathf.RoundToInt(ReportedPosition.z / 100f);
            string reliability = Confidence >= 0.7f ? "confirmado"
                : Confidence >= 0.4f ? "provável" : "não confirmado";
            string speaker = "[" + CallSign + "] ";
            switch (Type)
            {
                case Kind.EnemyContact:
                    return speaker + "Contato suspeito perto do " + region + " (" + reliability + "). Mantenham atenção.";
                case Kind.NeedReinforcements:
                    return speaker + "Precisamos de reforços no " + region + ". Confirmem se conseguem chegar.";
                case Kind.UsefulLoot:
                    return speaker + "Possível material útil no " + region + ". Verifiquem antes de recolher.";
                case Kind.Airdrop:
                    return speaker + "Possível airdrop no " + region + ". Ainda precisamos confirmar.";
                case Kind.UnknownBase:
                    return speaker + "Estrutura desconhecida no " + region + ". Não sabemos quem ocupa.";
                default:
                    return speaker + "Mensagem não identificada.";
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
