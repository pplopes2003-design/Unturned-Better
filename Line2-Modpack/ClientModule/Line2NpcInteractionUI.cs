using System;
using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Local in-game conversation with actual NPC entities. Uses the native
    /// interaction binding, Escape and a finite XP-for-owned-item transaction.
    /// This is not a quest UI and never creates world drops or free inventory.
    /// </summary>
    internal sealed class Line2NpcInteractionUI : MonoBehaviour
    {
        private readonly List<PrototypeActor> _actors = new List<PrototypeActor>(32);
        private readonly RaycastHit[] _conversationHits = new RaycastHit[16];
        private SteamNpcPrototypeRuntime _runtime;
        private PrototypeActor _selected;
        private bool _show;
        private bool _shop;
        private int _itemIndex;
        private CursorLockMode _oldLock;
        private bool _oldVisible;
        private string _feedback = "";
        private Rect _rect = new Rect(90f, 85f, 460f, 340f);

        private static bool Authorized()
        {
            return Provider.isServer && !Dedicator.IsDedicatedServer &&
                (Provider.clients == null || Provider.clients.Count <= 1) &&
                Provider.hasCheats && Level.isLoaded && Player.player != null;
        }

        private void OnDisable() { Close(); }
        private void OnDestroy() { Close(); }

        private void Close()
        {
            if (_show)
            {
                Cursor.lockState = _oldLock;
                Cursor.visible = _oldVisible;
            }
            _show = false;
            _shop = false;
            _selected = null;
        }

        private bool CanStillTalk()
        {
            if (!Authorized() || _selected == null || _selected.IsDead ||
                _selected.Disposition == "hostile" ||
                !Line2NpcCombatTotalPolicy.AllowTrade(_selected.CombatMode))
                return false;
            Player player = Player.player;
            return player != null && player.life != null && !player.life.isDead &&
                (player.transform.position - _selected.transform.position).sqrMagnitude <= 4.0f * 4.0f &&
                HasConversationSight(player, _selected);
        }

        private bool HasConversationSight(Player player, PrototypeActor actor)
        {
            if (player == null || actor == null) return false;
            Vector3 eye = player.transform.position + Vector3.up * 1.45f;
            Vector3 target = actor.transform.position + Vector3.up * 1.25f;
            Vector3 delta = target - eye;
            if (delta.sqrMagnitude < 0.04f) return false;
            int count = Physics.RaycastNonAlloc(eye, delta.normalized,
                _conversationHits, delta.magnitude,
                RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
            if (count >= _conversationHits.Length) return false;
            for (int i = 0; i < count; ++i)
            {
                Transform block = _conversationHits[i].transform;
                _conversationHits[i] = default(RaycastHit);
                if (block == null || block == player.transform ||
                    block.IsChildOf(player.transform) ||
                    block == actor.transform ||
                    block.IsChildOf(actor.transform))
                    continue;
                return false;
            }
            return true;
        }

        private void Update()
        {
            if (_show)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F7) ||
                    !CanStillTalk())
                    Close();
                return;
            }
            if (!Authorized() || Cursor.lockState != CursorLockMode.Locked ||
                !Input.GetKeyDown(ControlsSettings.interact)) return;
            if (_runtime == null) _runtime = GetComponent<SteamNpcPrototypeRuntime>();
            if (_runtime == null) return;
            _runtime.CopyLiveActors(_actors, 32);
            Player player = Player.player;
            Camera camera = Camera.main;
            if (player == null || camera == null) return;
            PrototypeActor closest = null;
            float bestSq = 3f * 3f;
            Vector3 view = camera.transform.forward;
            foreach (PrototypeActor actor in _actors)
            {
                if (actor == null || actor.IsDead ||
                    actor.Disposition == "hostile" ||
                    !Line2NpcCombatTotalPolicy.AllowTrade(actor.CombatMode))
                    continue;
                Vector3 vector = actor.transform.position +
                    Vector3.up * 1.25f - camera.transform.position;
                float sq = vector.sqrMagnitude;
                if (sq >= bestSq || Vector3.Dot(view, vector.normalized) < 0.70f)
                    continue;
                RaycastHit hit;
                if (Physics.Raycast(camera.transform.position, vector.normalized,
                    out hit, vector.magnitude, ~0, QueryTriggerInteraction.Ignore))
                {
                    Transform target = hit.transform;
                    if (target != actor.transform && !target.IsChildOf(actor.transform))
                        continue;
                }
                bestSq = sq;
                closest = actor;
            }
            if (closest == null) return;
            _selected = closest;
            _feedback = "O que deseja conversar?";
            _oldLock = Cursor.lockState;
            _oldVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _show = true;
            _shop = false;
            _itemIndex = 0;
        }

        private void OnGUI()
        {
            if (_show && CanStillTalk())
                _rect = GUI.Window(879322, _rect, DrawConversation, "Unturned 2 — Conversa");
        }

        private void DrawConversation(int id)
        {
            PrototypeActor actor = _selected;
            if (actor == null) return;
            GUI.Label(new Rect(14, 26, 426, 24),
                actor.DisplayName + " | " + actor.Disposition +
                (actor.IsRecruited ? " | Aliado" : ""));
            if (!_shop)
            {
                if (GUI.Button(new Rect(14, 61, 206, 35), "1. Conversar"))
                    _feedback = actor.Disposition == "friendly"
                        ? "Estou procurando suprimentos e um lugar seguro."
                        : "Não confio em estranhos. Mantenha as armas abaixadas.";
                if (GUI.Button(new Rect(228, 61, 213, 35),
                    actor.IsRecruited ? "2. Encerrar acompanhamento" : "2. Recrutar"))
                {
                    bool requested = !actor.IsRecruited;
                    _feedback = actor.TrySetRecruitedServer(requested)
                        ? (requested ? "Agora acompanho você." : "Vou seguir meu próprio caminho.")
                        : "Não foi possível alterar a aliança.";
                }
                if (GUI.Button(new Rect(14, 105, 427, 35), "3. Ver mercadorias"))
                {
                    _shop = true;
                    _itemIndex = 0;
                    _feedback = "Itens do próprio inventário do NPC. Pagamento em XP.";
                }
            }
            else
            {
                DrawShop(actor);
            }
            GUI.TextArea(new Rect(14, 239, 427, 48), _feedback);
            if (GUI.Button(new Rect(14, 297, 427, 29), _shop ? "Voltar" : "Sair (Esc)"))
            {
                if (_shop) _shop = false;
                else Close();
            }
            GUI.DragWindow(new Rect(0, 0, 460, 22));
        }

        private static uint Price(ItemAsset asset, Item item)
        {
            if (asset == null || item == null) return 0;
            string rarity = asset.rarity.ToString();
            uint basePrice = rarity == "MYTHICAL" ? 220u :
                rarity == "LEGENDARY" ? 120u :
                rarity == "EPIC" ? 65u :
                rarity == "RARE" ? 30u :
                rarity == "UNCOMMON" ? 12u : 5u;
            return basePrice;
        }

        private void DrawShop(PrototypeActor actor)
        {
            Line2MobileNPCInventory inventory = actor.Inventory;
            if (inventory == null || inventory.UsedSlots == 0)
            {
                GUI.Label(new Rect(14, 70, 427, 25), "Nenhum item disponível.");
                return;
            }
            _itemIndex = Mathf.Clamp(_itemIndex, 0, inventory.UsedSlots - 1);
            Item held;
            uint ownedInstanceID;
            if (!inventory.TryGetItemSnapshot(
                _itemIndex, out held, out ownedInstanceID))
            {
                GUI.Label(new Rect(14, 70, 427, 25), "Estoque indisponível.");
                return;
            }
            ItemAsset asset = Assets.find(EAssetType.ITEM, held.id) as ItemAsset;
            uint cost = Price(asset, held);
            GUI.Label(new Rect(14, 63, 427, 44),
                (asset != null ? asset.FriendlyName : "Item desconhecido") +
                " | ID " + held.id + " | quantidade " + held.amount);
            GUI.Label(new Rect(14, 105, 427, 25),
                "Preço: " + cost + " XP | Seus XP: " +
                (Player.player != null && Player.player.skills != null
                    ? Player.player.skills.experience : 0u));
            if (GUI.Button(new Rect(14, 142, 132, 35), "Anterior"))
                _itemIndex = (_itemIndex + inventory.UsedSlots - 1) % inventory.UsedSlots;
            if (GUI.Button(new Rect(155, 142, 132, 35), "Próximo"))
                _itemIndex = (_itemIndex + 1) % inventory.UsedSlots;
            if (GUI.Button(new Rect(295, 142, 146, 35), "Comprar"))
                _feedback = TryPurchase(actor, ownedInstanceID, held, cost);
        }

        private static bool SameSaleSnapshot(Item current, Item expected)
        {
            if (current == null || expected == null ||
                current.id != expected.id ||
                current.amount != expected.amount ||
                current.quality != expected.quality)
                return false;
            byte[] a = current.state, b = expected.state;
            if (a == null || b == null)
                return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; ++i)
                if (a[i] != b[i]) return false;
            return true;
        }

        private string TryPurchase(PrototypeActor actor,
            uint ownedInstanceID, Item expected, uint price)
        {
            if (!Authorized() || actor == null || actor.IsDead ||
                actor != _selected || actor.Disposition == "hostile" ||
                price == 0 || expected == null || ownedInstanceID == 0 ||
                !CanStillTalk())
                return "Comércio bloqueado.";
            Player player = Player.player;
            Line2MobileNPCInventory source = actor.Inventory;
            if (player == null || player.life == null || player.life.isDead ||
                player.skills == null || player.inventory == null ||
                source == null || player.skills.experience < price ||
                (player.transform.position - actor.transform.position).sqrMagnitude > 16f)
                return "Sem experiência suficiente ou fora do alcance.";
            Item owned;
            if (!source.TryGetOwnedInstanceSnapshotServer(
                ownedInstanceID, out owned) ||
                !SameSaleSnapshot(owned, expected))
                return "Estoque alterado; consulte novamente.";
            ItemAsset actualAsset = Assets.find(EAssetType.ITEM, owned.id) as ItemAsset;
            // The UI's displayed price is not transaction authority:
            // recompute against the actual owned item before withdrawal.
            if (actualAsset == null || Price(actualAsset, owned) != price)
                return "Preço ou estoque alterado; consulte novamente.";

            Item transferred;
            if (!source.TryTakeOwnedInstanceForReservedTransferServer(
                ownedInstanceID, out transferred))
                return "Item ou reserva do vendedor indisponível.";
            // The vacated slot was already reserved together with the
            // exact item. No external inventory callback can interleave
            // between withdrawal and reservation.

            bool received = false;
            try { received = player.inventory.tryAddItem(transferred, true); }
            catch (Exception error)
            {
                ProbeLogger.Log("NPC_TRADE_PLAYER_ADD_FAILED: " + error);
            }
            if (!received)
            {
                if (!source.TryStoreReservedItemServer(transferred))
                    ProbeLogger.Log("NPC_TRADE_CRITICAL: rollback failed, inspect inventory");
                return "Sem espaço no inventário do jogador; compra não concluída.";
            }
            source.ReleaseReservedSlotServer();
            player.skills.askSpend(price);
            ProbeLogger.Log("NPC_TRADE_OK: npc=" + actor.Identity +
                " item=" + transferred.id + " priceXP=" + price);
            return "Compra concluída. " + price + " XP utilizados.";
        }
    }
}
