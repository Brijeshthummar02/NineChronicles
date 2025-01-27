using Nekoyume.EnumType;
using Nekoyume.Game.Controller;
using Nekoyume.L10n;
using Nekoyume.Model.Mail;
using Nekoyume.State;
using Nekoyume.UI.Scroller;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Nekoyume.UI.Module
{
    public class ConditionalCostButton : ConditionalButton
    {
        public struct CostParam
        {
            public CostType type;
            public long cost;

            public CostParam(CostType type, long cost)
            {
                this.type = type;
                this.cost = cost;
            }
        }

        [Serializable]
        private struct CostObject
        {
            public CostType type;
            public List<CostText> costTexts;
        }

        [Serializable]
        private struct CostText
        {
            public GameObject parent;
            public TMP_Text text;
        }

        [SerializeField]
        private bool showCostAlert = true;

        [SerializeField]
        private List<CostObject> costObjects = null;

        [SerializeField]
        private List<GameObject> costParents = null;

        private readonly Dictionary<CostType, long> _costMap = new();

        public long CrystalCost =>
            _costMap.TryGetValue(CostType.Crystal, out var cost)
                ? cost
                : 0L;

        public int ArenaTicketCost =>
            _costMap.TryGetValue(CostType.ArenaTicket, out var cost)
                ? (int)cost
                : 0;

        public int EventDungeonTicketCost =>
            _costMap.TryGetValue(CostType.EventDungeonTicket, out var cost)
                ? (int)cost
                : 0;

        public void SetCost(params CostParam[] costs)
        {
            _costMap.Clear();
            foreach (var cost in costs)
            {
                if (cost.cost > 0)
                {
                    _costMap[cost.type] = cost.cost;
                }
            }

            UpdateObjects();
        }

        public void SetCost(CostType type, long cost)
        {
            _costMap.Clear();
            if (cost > 0)
            {
                _costMap[type] = cost;
            }

            UpdateObjects();
        }

        public override void UpdateObjects()
        {
            base.UpdateObjects();

            var showCost = _costMap.Count > 0;
            foreach (var parent in costParents)
            {
                parent.SetActive(showCost);
            }

            foreach (var costObject in costObjects)
            {
                var exist = _costMap.ContainsKey(costObject.type);
                foreach (var costText in costObject.costTexts)
                {
                    costText.parent.SetActive(exist);
                }

                if (!exist)
                {
                    continue;
                }

                foreach (var costText in costObject.costTexts)
                {
                    var cost = _costMap[costObject.type];
                    costText.text.text = cost.ToString();
                    costText.text.color = CheckCostOfType(costObject.type, cost) ? Palette.GetColor(ColorType.ButtonEnabled) : Palette.GetColor(ColorType.TextDenial);
                }
            }
        }

        /// <summary>
        /// Checks if costs are enough to pay for current avatar.
        /// </summary>
        /// <returns>
        /// Type of cost that is not enough to pay. If <see cref="CostType.None"/> is returned, costs are enough to pay.
        /// </returns>
        protected CostType CheckCost()
        {
            foreach (var pair in _costMap)
            {
                var type = pair.Key;
                var cost = pair.Value;

                switch (type)
                {
                    case CostType.NCG:
                    case CostType.Crystal:
                    case CostType.ActionPoint:
                    case CostType.Hourglass:
                    case CostType.ApPotion:
                    case CostType.GoldDust:
                        break;
                    default:
                        return CostType.None;
                }

                if (!CheckCostOfType(type, cost))
                {
                    return type;
                }
            }

            return CostType.None;
        }

        public static bool CheckCostOfType(CostType type, long cost)
        {
            switch (type)
            {
                case CostType.NCG:
                    return States.Instance.GoldBalanceState.Gold.MajorUnit >= cost;
                case CostType.Crystal:
                    return States.Instance.CrystalBalance.MajorUnit >= cost;
                case CostType.ActionPoint:
                    return ReactiveAvatarState.ActionPoint >= cost;
                case CostType.ArenaTicket:
                    return RxProps.ArenaTicketsProgress.Value.currentTickets >= cost;
                case CostType.EventDungeonTicket:
                    return RxProps.EventDungeonTicketProgress.Value.currentTickets >= cost;
                case CostType.SilverDust:
                case CostType.GoldDust:
                case CostType.RubyDust:
                case CostType.EmeraldDust:
                    var inventory = States.Instance.CurrentAvatarState.inventory;
                    var count = inventory.GetMaterialCount((int)type);
                    return count >= cost;
                case CostType.Hourglass:
                case CostType.ApPotion:
                    var blockIndex = Game.Game.instance.Agent.BlockIndex;
                    inventory = States.Instance.CurrentAvatarState.inventory;
                    count = inventory.GetUsableItemCount(type, blockIndex);
                    return count >= cost;
                default:
                    return true;
            }
        }

        protected override bool CheckCondition()
        {
            return CheckCost() == CostType.None
                && base.CheckCondition();
        }

        protected override void OnClickButton()
        {
            if (showCostAlert)
            {
                switch (CheckCost())
                {
                    case CostType.None:
                        break;
                    case CostType.NCG:
                        OneLineSystem.Push(
                            MailType.System,
                            L10nManager.Localize("UI_NOT_ENOUGH_NCG"),
                            NotificationCell.NotificationType.Alert);
                        break;
                    case CostType.Crystal:
                        OneLineSystem.Push(
                            MailType.System,
                            L10nManager.Localize("UI_NOT_ENOUGH_CRYSTAL"),
                            NotificationCell.NotificationType.Alert);
                        break;
                    case CostType.ActionPoint:
                        OneLineSystem.Push(
                            MailType.System,
                            L10nManager.Localize("ERROR_ACTION_POINT"),
                            NotificationCell.NotificationType.Alert);
                        break;
                    case CostType.Hourglass:
                        OneLineSystem.Push(
                            MailType.System,
                            L10nManager.Localize("UI_NOT_ENOUGH_HOURGLASS"),
                            NotificationCell.NotificationType.Alert);
                        break;
                    case CostType.ApPotion:
                        OneLineSystem.Push(
                            MailType.System,
                            L10nManager.Localize("UI_NOT_ENOUGH_AP_POTION"),
                            NotificationCell.NotificationType.Alert);
                        break;
                    case CostType.GoldDust:
                        OneLineSystem.Push(
                            MailType.System,
                            L10nManager.Localize("UI_NOT_ENOUGH_GOLD_DUST"),
                            NotificationCell.NotificationType.Alert);
                        break;
                }
            }

            base.OnClickButton();
        }
    }
}
