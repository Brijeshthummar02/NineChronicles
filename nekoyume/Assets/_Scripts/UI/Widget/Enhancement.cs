using System;
using System.Collections;
using System.Collections.Generic;
using Nekoyume.L10n;
using Nekoyume.Model.Item;
using Nekoyume.Model.Mail;
using Nekoyume.State;
using Nekoyume.UI.Module;
using UnityEngine;
using System.Numerics;
using Nekoyume.Action;
using Nekoyume.Extensions;
using Nekoyume.Game.Controller;
using Nekoyume.Helper;
using Nekoyume.TableData;
using Nekoyume.UI.Model;
using Nekoyume.UI.Scroller;
using TMPro;
using UnityEngine.UI;

namespace Nekoyume.UI
{
    using Nekoyume.EnumType;
    using Nekoyume.UI.Module.Common;
    using System.Linq;
    using UniRx;

    public class Enhancement : Widget
    {
        [SerializeField]
        private EnhancementInventory enhancementInventory;

        [SerializeField]
        private ConditionalCostButton upgradeButton;

        [SerializeField]
        private Button closeButton;

        [SerializeField]
        private UpgradeEquipmentSlot baseSlot;

        [SerializeField]
        private UpgradeEquipmentSlot materialSlot;

        [SerializeField]
        private TextMeshProUGUI successRatioText;

        [SerializeField]
        private TextMeshProUGUI requiredBlockIndexText;

        [SerializeField]
        private TextMeshProUGUI itemNameText;

        [SerializeField]
        private TextMeshProUGUI currentLevelText;

        [SerializeField]
        private TextMeshProUGUI nextLevelText;

        [SerializeField]
        private TextMeshProUGUI materialGuideText;

        [SerializeField]
        private EnhancementOptionView mainStatView;

        [SerializeField]
        private List<EnhancementOptionView> statViews;

        [SerializeField]
        private List<EnhancementOptionView> skillViews;

        [SerializeField]
        private TextMeshProUGUI levelText;

        [SerializeField]
        private GameObject noneContainer;

        [SerializeField]
        private GameObject itemInformationContainer;

        [SerializeField]
        private Animator animator;

        [SerializeField]
        private SkillPositionTooltip skillTooltip;

        [SerializeField]
        private Slider expSlider;

        [SerializeField]
        private TextMeshProUGUI sliderPercentText;

        [SerializeField]
        private TextMeshProUGUI levelStateText;

        [SerializeField]
        private EnhancementSelectedMaterialItemScroll enhancementSelectedMaterialItemScroll;

        private static readonly int HashToRegisterBase =
            Animator.StringToHash("RegisterBase");

        private static readonly int HashToPostRegisterBase =
            Animator.StringToHash("PostRegisterBase");

        private static readonly int HashToPostRegisterMaterial =
            Animator.StringToHash("PostRegisterMaterial");

        private static readonly int HashToUnregisterMaterial =
            Animator.StringToHash("UnregisterMaterial");

        private static readonly int HashToClose =
            Animator.StringToHash("Close");


        private EnhancementCostSheetV3 _costSheet;
        private BigInteger _costNcg = 0;
        private string _errorMessage;
        private IOrderedEnumerable<KeyValuePair<int, EnhancementCostSheetV3.Row>> _decendingbyExpCostSheet;

        protected override void Awake()
        {
            base.Awake();
            closeButton.onClick.AddListener(Close);
            CloseWidget = Close;
        }

        public override void Initialize()
        {
            base.Initialize();

            upgradeButton.OnSubmitSubject
                .Subscribe(_ => OnSubmit())
                .AddTo(gameObject);

            _costSheet = Game.Game.instance.TableSheets.EnhancementCostSheetV3;
            _decendingbyExpCostSheet = _costSheet.OrderByDescending(r => r.Value.Exp);
            baseSlot.RemoveButton.onClick.AddListener(() => enhancementInventory.DeselectItem(true));
            //materialSlot.RemoveButton.onClick.AddListener(() => enhancementInventory.DeselectItem());
        }

        public override void Show(bool ignoreShowAnimation = false)
        {
            Clear();
            enhancementInventory.Set(ShowItemTooltip, UpdateInformation);
            base.Show(ignoreShowAnimation);
        }

        public void Show(ItemSubType itemSubType, Guid itemId, bool ignoreShowAnimation = false)
        {
            Show(ignoreShowAnimation);
            StartCoroutine(CoSelect(itemSubType, itemId));
        }

        private IEnumerator CoSelect(ItemSubType itemSubType, Guid itemId)
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            enhancementInventory.Select(itemSubType, itemId);
        }

        private void Close()
        {
            animator.Play(HashToClose);
            Close(true);
            Find<CombinationMain>().Show();
        }

        private void ShowItemTooltip(EnhancementInventoryItem model, RectTransform target)
        {
            var tooltip = ItemTooltip.Find(model.ItemBase.ItemType);
            tooltip.Show(model, enhancementInventory.GetSubmitText(),
                !model.Disabled.Value,
                () => enhancementInventory.SelectItem(),
                () => enhancementInventory.ClearSelectedItem(),
                () => NotificationSystem.Push(MailType.System,
                    L10nManager.Localize("NOTIFICATION_MISMATCH_MATERIAL"),
                    NotificationCell.NotificationType.Alert));
        }

        private void OnSubmit()
        {
            var (baseItem, materialItems) = enhancementInventory.GetSelectedModels();

            //Equip Upgragd ToDO
            if (!IsInteractableButton(baseItem, materialItems))
            {
                NotificationSystem.Push(MailType.System, _errorMessage,
                    NotificationCell.NotificationType.Alert);
                return;
            }

            if (States.Instance.GoldBalanceState.Gold.MajorUnit < _costNcg)
            {
                _errorMessage = L10nManager.Localize("UI_NOT_ENOUGH_NCG");
                NotificationSystem.Push(MailType.System, _errorMessage,
                    NotificationCell.NotificationType.Alert);
                return;
            }

            EnhancementAction(baseItem, materialItems);
        }

        private void EnhancementAction(Equipment baseItem, List<Equipment> materialItems)
        {
            var slots = Find<CombinationSlotsPopup>();
            if (!slots.TryGetEmptyCombinationSlot(out var slotIndex))
            {
                return;
            }

            var sheet = Game.Game.instance.TableSheets.EnhancementCostSheetV3;

            var targetExp = baseItem.Exp + materialItems.Aggregate(0L, (total, m) => total + m.Exp);
            EnhancementCostSheetV3.Row targetRow;
            try
            {
                targetRow = _decendingbyExpCostSheet
                .First(row =>
                    row.Value.ItemSubType == baseItem.ItemSubType &&
                    row.Value.Grade == baseItem.Grade &&
                    row.Value.Exp <= targetExp
                ).Value;
            }
            catch
            {
                targetRow = new EnhancementCostSheetV3.Row();
            }

            var avatarAddress = States.Instance.CurrentAvatarState.address;
            slots.SetCaching(avatarAddress, slotIndex, true, targetRow.RequiredBlockIndex,
                itemUsable: baseItem);

            NotificationSystem.Push(MailType.Workshop,
                L10nManager.Localize("NOTIFICATION_ITEM_ENHANCEMENT_START"),
                NotificationCell.NotificationType.Information);

            Game.Game.instance.ActionManager
                .ItemEnhancement(baseItem, materialItems, slotIndex, _costNcg).Subscribe();

            enhancementInventory.DeselectItem(true);

            StartCoroutine(CoCombineNPCAnimation(baseItem, targetRow.RequiredBlockIndex, Clear));
        }

        private void Clear()
        {
            ClearInformation();
            enhancementInventory.DeselectItem(true);
        }

        private bool IsInteractableButton(IItem item, List<Equipment> materials)
        {
            if (item is null || materials.Count == 0)
            {
                _errorMessage = L10nManager.Localize("UI_SELECT_MATERIAL_TO_UPGRADE");
                return false;
            }

            if (States.Instance.CurrentAvatarState.actionPoint < GameConfig.EnhanceEquipmentCostAP)
            {
                _errorMessage = L10nManager.Localize("NOTIFICATION_NOT_ENOUGH_ACTION_POWER");
                return false;
            }

            if (!Find<CombinationSlotsPopup>().TryGetEmptyCombinationSlot(out _))
            {
                _errorMessage = L10nManager.Localize("NOTIFICATION_NOT_ENOUGH_SLOTS");
                return false;
            }

            return true;
        }

        private IEnumerator CoCombineNPCAnimation(ItemBase itemBase,
            long blockIndex,
            System.Action action,
            bool isConsumable = false)
        {
            var loadingScreen = Find<CombinationLoadingScreen>();
            loadingScreen.Show();
            loadingScreen.SetItemMaterial(new Item(itemBase), isConsumable);
            loadingScreen.SetCloseAction(action);
            Push();
            yield return new WaitForSeconds(.5f);

            var format = L10nManager.Localize("UI_COST_BLOCK");
            var quote = string.Format(format, blockIndex);
            loadingScreen.AnimateNPC(itemBase.ItemType, quote);
        }

        private void ClearInformation()
        {
            itemNameText.text = string.Empty;
            currentLevelText.text = string.Empty;
            nextLevelText.text = string.Empty;
            successRatioText.text = "0%";
            requiredBlockIndexText.text = "0";

            expSlider.value = 0;
            sliderPercentText.text = "0%";

            levelStateText.text = string.Empty;

            mainStatView.gameObject.SetActive(false);
            foreach (var stat in statViews)
            {
                stat.gameObject.SetActive(false);
            }

            foreach (var skill in skillViews)
            {
                skill.gameObject.SetActive(false);
            }
        }

        private void UpdateInformation(EnhancementInventoryItem baseModel,
            List<EnhancementInventoryItem> materialModels)
        {
            _costNcg = 0;
            if (baseModel is null)
            {
                baseSlot.RemoveMaterial();
                //materialSlot.RemoveMaterial();
                noneContainer.SetActive(true);
                itemInformationContainer.SetActive(false);
                animator.Play(HashToRegisterBase);
                enhancementSelectedMaterialItemScroll.UpdateData(materialModels, true);
                closeButton.interactable = true;
                ClearInformation();
            }
            else
            {
                if (!baseSlot.IsExist)
                {
                    animator.Play(HashToPostRegisterBase);
                }

                baseSlot.AddMaterial(baseModel.ItemBase);

                enhancementSelectedMaterialItemScroll.UpdateData(materialModels);
                if(materialModels.Count != 0)
                {
                    enhancementSelectedMaterialItemScroll.JumpTo(materialModels[materialModels.Count - 1]);
                    animator.Play(HashToPostRegisterMaterial);
                    noneContainer.SetActive(false);
                }
                else
                {
                    noneContainer.SetActive(true);
                }

                var equipment = baseModel.ItemBase as Equipment;
                if (!ItemEnhancement.TryGetRow(equipment, _costSheet, out var baseItemCostRow))
                {
                    baseItemCostRow = new EnhancementCostSheetV3.Row();
                }

                var targetExp = (baseModel.ItemBase as Equipment).Exp + materialModels.Aggregate(0L, (total, m) => total + (m.ItemBase as Equipment).Exp);

                EnhancementCostSheetV3.Row targetRow;
                try
                {
                    targetRow = _decendingbyExpCostSheet
                    .First(row =>
                        row.Value.ItemSubType == equipment.ItemSubType &&
                        row.Value.Grade == equipment.Grade &&
                        row.Value.Exp <= targetExp
                    ).Value;
                }
                catch
                {
                    targetRow = baseItemCostRow;
                }

                itemInformationContainer.SetActive(true);

                ClearInformation();


                _costNcg = targetRow.Cost - baseItemCostRow.Cost;
                upgradeButton.SetCost(CostType.NCG, (long)_costNcg);

                var slots = Find<CombinationSlotsPopup>();
                upgradeButton.Interactable = slots.TryGetEmptyCombinationSlot(out var _);

                itemNameText.text = equipment.GetLocalizedNonColoredName();
                currentLevelText.text = $"+{equipment.level}";
                nextLevelText.text = $"+{targetRow.Level}";

                var targetRangeRows = _costSheet.Values.
                    Where((r) =>
                    r.Grade == equipment.Grade &&
                    r.ItemSubType == equipment.ItemSubType &&
                    equipment.level <= r.Level &&
                    r.Level <= targetRow.Level + 1
                    ).ToList();

                if(equipment.level == 0)
                {
                    targetRangeRows.Insert(0, new EnhancementCostSheetV3.Row());
                }

                if(targetRangeRows.Count < 2)
                {
                    Debug.LogError("[Enhancement] Faild Get TargetRangeRows");
                }
                else
                {
                    var nextExp = targetRangeRows[targetRangeRows.Count - 1].Exp;
                    var prevExp = targetRangeRows[targetRangeRows.Count - 2].Exp;
                    var lerp = Mathf.InverseLerp(prevExp, nextExp, targetExp);
                    expSlider.value = lerp;
                    sliderPercentText.text = $"{(int)(lerp * 100)}%";
                }
                
                levelStateText.text = $"Lv. {targetRow.Level}/{ItemEnhancement.GetEquipmentMaxLevel(equipment, _costSheet)}";

                //expSlider

/*                var sheet = Game.Game.instance.TableSheets.ItemRequirementSheet;
                if (!sheet.TryGetValue(equipment.Id, out var requirementRow))
                {
                    levelText.enabled = false;
                }
                else
                {
                    levelText.text =
                        L10nManager.Localize("UI_REQUIRED_LEVEL", requirementRow.Level);
                    var hasEnoughLevel =
                        States.Instance.CurrentAvatarState.level >= requirementRow.Level;
                    levelText.color = hasEnoughLevel
                        ? Palette.GetColor(EnumType.ColorType.ButtonEnabled)
                        : Palette.GetColor(EnumType.ColorType.TextDenial);

                    levelText.enabled = true;
                }*/

                var itemOptionInfo = new ItemOptionInfo(equipment);

                if (baseItemCostRow.BaseStatGrowthMin != 0 && baseItemCostRow.BaseStatGrowthMax != 0)
                {
                    var (mainStatType, mainValue, _) = itemOptionInfo.MainStat;
                    var mainAdd = (int)Math.Max(1,
                        (mainValue * baseItemCostRow.BaseStatGrowthMax.NormalizeFromTenThousandths()));
                    mainStatView.gameObject.SetActive(true);
                    mainStatView.Set(mainStatType.ToString(),
                        mainStatType.ValueToString(mainValue),
                        $"(<size=80%>max</size> +{mainStatType.ValueToString(mainAdd)})");
                }

                var stats = itemOptionInfo.StatOptions;
                for (var i = 0; i < stats.Count; i++)
                {
                    statViews[i].gameObject.SetActive(true);
                    var statType = stats[i].type;
                    var statValue = stats[i].value;
                    var count = stats[i].count;

                    if (baseItemCostRow.ExtraStatGrowthMin == 0 && baseItemCostRow.ExtraStatGrowthMax == 0)
                    {
                        statViews[i].Set(statType.ToString(),
                            statType.ValueToString(statValue),
                            string.Empty,
                            count);
                    }
                    else
                    {
                        var statAdd = Math.Max(1,
                            (int)(statValue *
                                  baseItemCostRow.ExtraStatGrowthMax.NormalizeFromTenThousandths()));
                        statViews[i].Set(statType.ToString(),
                            statType.ValueToString(statValue),
                            $"(<size=80%>max</size> +{statType.ValueToString(statAdd)})",
                            count);
                    }
                }

                var skills = itemOptionInfo.SkillOptions;
                for (var i = 0; i < skills.Count; i++)
                {
                    skillViews[i].gameObject.SetActive(true);
                    var skill = skills[i];
                    var skillName = skill.skillRow.GetLocalizedName();
                    var power = skill.power;
                    var chance = skill.chance;
                    var ratio = skill.statPowerRatio;
                    var refStatType = skill.refStatType;
                    var effectString = SkillExtensions.EffectToString(
                        skill.skillRow.Id,
                        skill.skillRow.SkillType,
                        power,
                        ratio,
                        refStatType);
                    var isBuff =
                        skill.skillRow.SkillType == Nekoyume.Model.Skill.SkillType.Buff ||
                        skill.skillRow.SkillType == Nekoyume.Model.Skill.SkillType.Debuff;

                    if (baseItemCostRow.ExtraSkillDamageGrowthMin == 0 && baseItemCostRow.ExtraSkillDamageGrowthMax == 0 &&
                        baseItemCostRow.ExtraSkillChanceGrowthMin == 0 && baseItemCostRow.ExtraSkillChanceGrowthMax == 0)
                    {
                        var view = skillViews[i];
                        view.Set(skillName,
                            $"{L10nManager.Localize("UI_SKILL_POWER")} : {effectString}",
                            string.Empty,
                            $"{L10nManager.Localize("UI_SKILL_CHANCE")} : {chance}",
                            string.Empty);
                        var skillRow = skill.skillRow;
                        view.SetDescriptionButton(() =>
                        {
                            skillTooltip.Show(skillRow, chance, chance, power, power, ratio, ratio, refStatType);
                            skillTooltip.transform.position = view.DescriptionPosition;
                        });
                    }
                    else
                    {
                        var powerAdd = Math.Max(isBuff || power == 0 ? 0 : 1,
                            (int)(power *
                                  baseItemCostRow.ExtraSkillDamageGrowthMax.NormalizeFromTenThousandths()));
                        var ratioAdd = Math.Max(0,
                            (int)(ratio *
                                  baseItemCostRow.ExtraSkillDamageGrowthMax.NormalizeFromTenThousandths()));
                        var chanceAdd = Math.Max(1,
                            (int)(chance *
                                  baseItemCostRow.ExtraSkillChanceGrowthMax.NormalizeFromTenThousandths()));
                        var totalPower = power + powerAdd;
                        var totalChance = chance + chanceAdd;
                        var totalRatio = ratio + ratioAdd;
                        var skillRow = skill.skillRow;

                        var powerString = SkillExtensions.EffectToString(
                            skillRow.Id,
                            skillRow.SkillType,
                            powerAdd,
                            ratioAdd,
                            skill.refStatType);

                        var view = skillViews[i];
                        view.Set(skillName,
                            $"{L10nManager.Localize("UI_SKILL_POWER")} : {effectString}",
                            $"(<size=80%>max</size> +{powerString})",
                            $"{L10nManager.Localize("UI_SKILL_CHANCE")} : {chance}",
                            $"(<size=80%>max</size> +{chanceAdd}%)");
                        view.SetDescriptionButton(() =>
                        {
                            skillTooltip.Show(
                                skillRow, totalChance, totalChance, totalPower, totalPower, totalRatio, totalRatio, refStatType);
                            skillTooltip.transform.position = view.DescriptionPosition;
                        });
                    }
                }
            }
        }
    }
}
