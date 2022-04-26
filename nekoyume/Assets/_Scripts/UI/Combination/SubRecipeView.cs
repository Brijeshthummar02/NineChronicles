using Nekoyume.Game.Controller;
using Nekoyume.Model.Stat;
using Nekoyume.TableData;
using Nekoyume.UI.Module;
using Nekoyume.Helper;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using System;
using System.Globalization;
using Nekoyume.State;
using System.Numerics;
using Nekoyume.Model.Item;
using Libplanet;
using System.Security.Cryptography;
using Nekoyume.Extensions;
using Toggle = Nekoyume.UI.Module.Toggle;
using Nekoyume.L10n;

namespace Nekoyume.UI
{
    using Nekoyume.Model.Mail;
    using Nekoyume.UI.Scroller;
    using UniRx;

    public class SubRecipeView : MonoBehaviour
    {
        public struct RecipeInfo
        {
            public int RecipeId;
            public int? SubRecipeId;
            public BigInteger CostNCG;
            public int CostAP;
            public List<(HashDigest<SHA256>, int count)> Materials;
        }

        [Serializable]
        private struct OptionView
        {
            public GameObject ParentObject;
            public GameObject NormalObject;
            public GameObject LockedObject;
            public TextMeshProUGUI OptionText;
            public TextMeshProUGUI PercentageText;
        }

        [SerializeField] private GameObject toggleParent = null;
        [SerializeField] private List<Toggle> categoryToggles = null;
        [SerializeField] private RecipeCell recipeCell = null;
        [SerializeField] private TextMeshProUGUI titleText = null;
        [SerializeField] private TextMeshProUGUI statText = null;

        [SerializeField] private TextMeshProUGUI blockIndexText = null;
        [SerializeField] private TextMeshProUGUI greatSuccessRateText = null;

        [SerializeField] private List<OptionView> optionViews = null;
        [SerializeField] private List<OptionView> skillViews = null;
        [SerializeField] private TextMeshProUGUI levelText = null;

        [SerializeField] private RequiredItemRecipeView requiredItemRecipeView = null;

        [SerializeField] private ConditionalCostButton button = null;
        [SerializeField] private GameObject lockedObject = null;
        [SerializeField] private TextMeshProUGUI lockedText = null;

        public readonly Subject<RecipeInfo> CombinationActionSubject = new Subject<RecipeInfo>();

        private SheetRow<int> _recipeRow = null;
        private List<int> _subrecipeIds = null;
        private int _selectedIndex;
        private RecipeInfo _selectedRecipeInfo;

        private const string StatTextFormat = "{0} {1}";
        private const int MimisbrunnrRecipeIndex = 2;
        private IDisposable _disposableForOnDisable;

        private void Awake()
        {
            for (int i = 0; i < categoryToggles.Count; ++i)
            {
                var innerIndex = i;
                var toggle = categoryToggles[i];
                toggle.onValueChanged.AddListener(value =>
                {
                    if (!value) return;
                    AudioController.PlayClick();
                    ChangeTab(innerIndex);
                });
            }

            button.OnClickSubject
                .Subscribe(state =>
                {
                    if (state == ConditionalButton.State.Disabled)
                    {
                        return;
                    }

                    CombineCurrentRecipe();
                })
                .AddTo(gameObject);

            button.OnClickDisabledSubject
                .Subscribe(_ =>
                {
                    if (!CheckSubmittable(out var errorMessage, out var slotIndex))
                    {
                        OneLineSystem.Push(MailType.System, errorMessage, NotificationCell.NotificationType.Alert);
                    }
                })
                .AddTo(gameObject);
        }

        private void OnEnable()
        {
            if (_disposableForOnDisable != null)
            {
                _disposableForOnDisable.Dispose();
                _disposableForOnDisable = null;
            }

            _disposableForOnDisable = Craft.SharedModel.UnlockedRecipes?.Subscribe(_ =>
            {
                if (gameObject.activeSelf)
                {
                    UpdateButton();
                }
            });
        }

        private void OnDisable()
        {
            if (_disposableForOnDisable != null)
            {
                _disposableForOnDisable.Dispose();
                _disposableForOnDisable = null;
            }
        }

        public void SetData(SheetRow<int> recipeRow, List<int> subrecipeIds)
        {
            _recipeRow = recipeRow;
            _subrecipeIds = subrecipeIds;

            string title = null;
            if (recipeRow is EquipmentItemRecipeSheet.Row equipmentRow)
            {
                var resultItem = equipmentRow.GetResultEquipmentItemRow();
                title = resultItem.GetLocalizedName(true, false);

                var stat = resultItem.GetUniqueStat();
                var statValueText = stat.Type == StatType.SPD
                    ? (stat.ValueAsInt * 0.01m).ToString(CultureInfo.InvariantCulture)
                    : stat.ValueAsInt.ToString();
                statText.text = string.Format(StatTextFormat, stat.Type, statValueText);
                recipeCell.Show(equipmentRow, false);
            }
            else if (recipeRow is ConsumableItemRecipeSheet.Row consumableRow)
            {
                var resultItem = consumableRow.GetResultConsumableItemRow();
                title = resultItem.GetLocalizedName();

                var stat = resultItem.GetUniqueStat();
                var statValueText = stat.StatType == StatType.SPD
                    ? (stat.ValueAsInt * 0.01m).ToString(CultureInfo.InvariantCulture)
                    : stat.ValueAsInt.ToString();
                statText.text = string.Format(StatTextFormat, stat.StatType, statValueText);
                recipeCell.Show(consumableRow, false);
            }

            titleText.text = title;

            if (categoryToggles.Any())
            {
                var categoryToggle = categoryToggles[_selectedIndex];
                if (categoryToggle.isOn)
                {
                    ChangeTab(_selectedIndex);
                }
                else
                {
                    categoryToggle.isOn = true;
                }
            }
            else
            {
                ChangeTab(0);
            }
        }

        public void ResetSelectedIndex()
        {
            _selectedIndex = 0;
        }

        public void UpdateView()
        {
            ChangeTab(_selectedIndex);
        }

        private void ChangeTab(int index)
        {
            _selectedIndex = index;
            UpdateInformation(index);
        }

        private void UpdateInformation(int index)
        {
            long blockIndex = 0;
            decimal greatSuccessRate = 0m;
            BigInteger costNCG = 0;
            int costAP = 0;
            int recipeId = 0;
            int? subRecipeId = null;
            List<(HashDigest<SHA256> material, int count)> materialList
                = new List<(HashDigest<SHA256> material, int count)>();

            var equipmentRow = _recipeRow as EquipmentItemRecipeSheet.Row;
            var consumableRow = _recipeRow as ConsumableItemRecipeSheet.Row;
            foreach (var optionView in optionViews)
            {
                optionView.ParentObject.SetActive(false);
            }
            foreach (var skillView in skillViews)
            {
                skillView.ParentObject.SetActive(false);
            }

            var isLocked = false;
            if (equipmentRow != null)
            {
                isLocked = !Craft.SharedModel.UnlockedRecipes.Value.Contains(_recipeRow.Key);
                var baseMaterialInfo = new EquipmentItemSubRecipeSheet.MaterialInfo(
                    equipmentRow.MaterialId,
                    equipmentRow.MaterialCount);
                blockIndex = equipmentRow.RequiredBlockIndex;
                costNCG = equipmentRow.RequiredGold;
                costAP = equipmentRow.RequiredActionPoint;
                recipeId = equipmentRow.Id;
                var baseMaterial = CreateMaterial(equipmentRow.MaterialId, equipmentRow.MaterialCount);
                materialList.Add(baseMaterial);

                if (_subrecipeIds != null &&
                    _subrecipeIds.Any())
                {
                    toggleParent.SetActive(true);
                    subRecipeId = _subrecipeIds[index];
                    var subRecipe = Game.Game.instance.TableSheets
                        .EquipmentItemSubRecipeSheetV2[subRecipeId.Value];
                    var options = subRecipe.Options;

                    blockIndex += subRecipe.RequiredBlockIndex;
                    greatSuccessRate = options
                        .Select(x => x.Ratio.NormalizeFromTenThousandths())
                        .Aggregate((a, b) => a * b);

                    SetOptions(options, isLocked);

                    var sheet = Game.Game.instance.TableSheets.ItemRequirementSheet;
                    var resultItemRow = equipmentRow.GetResultEquipmentItemRow();

                    if (!sheet.TryGetValue(resultItemRow.Id, out var row))
                    {
                        levelText.enabled = false;
                    }
                    else
                    {
                        var level = index == MimisbrunnrRecipeIndex ? row.MimisLevel : row.Level;
                        levelText.text = L10nManager.Localize("UI_REQUIRED_LEVEL", level);
                        var hasEnoughLevel = States.Instance.CurrentAvatarState.level >= level;
                        levelText.color = hasEnoughLevel ?
                            Palette.GetColor(EnumType.ColorType.ButtonEnabled) :
                            Palette.GetColor(EnumType.ColorType.TextDenial);

                        levelText.enabled = true;
                    }

                    requiredItemRecipeView.SetData(
                        baseMaterialInfo,
                        subRecipe.Materials,
                        true,
                        isLocked);

                    costNCG += subRecipe.RequiredGold;

                    var subMaterials = subRecipe.Materials
                        .Select(x => CreateMaterial(x.Id, x.Count));
                    materialList.AddRange(subMaterials);
                }
                else
                {
                    toggleParent.SetActive(false);
                    requiredItemRecipeView.SetData(baseMaterialInfo, null, true, isLocked);
                }
            }
            else if (consumableRow != null)
            {
                blockIndex = consumableRow.RequiredBlockIndex;
                requiredItemRecipeView.SetData(consumableRow.Materials, true);
                costNCG = (BigInteger)consumableRow.RequiredGold;
                costAP = consumableRow.RequiredActionPoint;
                recipeId = consumableRow.Id;

                var sheet = Game.Game.instance.TableSheets.ItemRequirementSheet;
                var resultItemRow = consumableRow.GetResultConsumableItemRow();

                if (!sheet.TryGetValue(resultItemRow.Id, out var row))
                {
                    levelText.enabled = false;
                }
                else
                {
                    levelText.text = L10nManager.Localize("UI_REQUIRED_LEVEL", row.Level);
                    var hasEnoughLevel = States.Instance.CurrentAvatarState.level >= row.Level;
                    levelText.color = hasEnoughLevel ?
                        Palette.GetColor(EnumType.ColorType.ButtonEnabled) :
                        Palette.GetColor(EnumType.ColorType.TextDenial);

                    levelText.enabled = true;
                }

                var materials = consumableRow.Materials
                    .Select(x => CreateMaterial(x.Id, x.Count));
                materialList.AddRange(materials);
            }


            blockIndexText.text = isLocked ? "???" : blockIndex.ToString();
            greatSuccessRateText.text = isLocked ? "???" :
                greatSuccessRate == 0m ? "-" : greatSuccessRate.ToString("0.0%");

            var recipeInfo = new RecipeInfo
            {
                CostNCG = costNCG,
                CostAP = costAP,
                RecipeId = recipeId,
                SubRecipeId = subRecipeId,
                Materials = materialList
            };
            _selectedRecipeInfo = recipeInfo;

            if (equipmentRow != null)
            {
                UpdateButton();
            }
            else if (consumableRow != null)
            {
                var submittable = CheckMaterialAndSlot();
                button.SetCost(ConditionalCostButton.CostType.NCG, (int)_selectedRecipeInfo.CostNCG);
                button.Interactable = submittable;
                button.gameObject.SetActive(true);
                lockedObject.SetActive(false);
            }
        }

        private void UpdateButton()
        {
            button.Interactable = false;
            if (_selectedRecipeInfo.Equals(default))
            {
                return;
            }

            if (Craft.SharedModel.UnlockedRecipes.Value.Contains(_selectedRecipeInfo.RecipeId))
            {
                var submittable = CheckMaterialAndSlot();
                button.SetCost(ConditionalCostButton.CostType.NCG, (int)_selectedRecipeInfo.CostNCG);
                button.Interactable = submittable;
                button.gameObject.SetActive(true);
                lockedObject.SetActive(false);
            }
            else if (Craft.SharedModel.UnlockingRecipes.Contains(_selectedRecipeInfo.RecipeId))
            {
                button.gameObject.SetActive(false);
                lockedObject.SetActive(true);
                lockedText.text = L10nManager.Localize("UI_LOADING_STATES");
            }
            else
            {
                button.gameObject.SetActive(false);
                lockedObject.SetActive(true);
                lockedText.text = L10nManager.Localize("UI_INFORM_UNLOCK_RECIPE");
            }
        }

        private void SetOptions(
            List<EquipmentItemSubRecipeSheetV2.OptionInfo> optionInfos,
            bool isLocked)
        {
            var tableSheets = Game.Game.instance.TableSheets;
            var optionSheet = tableSheets.EquipmentItemOptionSheet;
            var skillSheet = tableSheets.SkillSheet;
            var options = optionInfos
                .Select(x => (ratio: x.Ratio, option: optionSheet[x.Id]))
                .ToList();

            var statOptions = optionInfos
                .Select(x => (ratio: x.Ratio, option: optionSheet[x.Id]))
                .Where(x => x.option.StatType != StatType.NONE)
                .ToList();

            var skillOptions = optionInfos
                .Select(x => (ratio: x.Ratio, option: optionSheet[x.Id]))
                .Except(statOptions)
                .ToList();

            var siblingIndex = 0;
            foreach (var (ratio, option) in options)
            {
                if (option.StatType != StatType.NONE)
                {
                    var optionView = optionViews.First(x => !x.ParentObject.activeSelf);
                    if (!isLocked)
                    {
                        optionView.OptionText.text = option.OptionRowToString();
                        optionView.PercentageText.text = (ratio.NormalizeFromTenThousandths()).ToString("0%");
                        optionView.ParentObject.transform.SetSiblingIndex(siblingIndex);
                    }
                    optionView.ParentObject.SetActive(true);
                    optionView.NormalObject.SetActive(!isLocked);
                    optionView.LockedObject.SetActive(isLocked);
                }
                else
                {
                    var skillView = skillViews.First(x => !x.ParentObject.activeSelf);
                    if (!isLocked)
                    {
                        var description = skillSheet.TryGetValue(option.SkillId, out var skillRow) ?
                            skillRow.GetLocalizedName() : string.Empty;
                        skillView.OptionText.text = description;
                        skillView.PercentageText.text = (ratio.NormalizeFromTenThousandths()).ToString("0%");
                        skillView.ParentObject.transform.SetSiblingIndex(siblingIndex);
                    }
                    skillView.ParentObject.SetActive(true);
                    skillView.NormalObject.SetActive(!isLocked);
                    skillView.LockedObject.SetActive(isLocked);
                }

                ++siblingIndex;
            }
        }

        public void CombineCurrentRecipe()
        {
            var loadingScreen = Widget.Find<CombinationLoadingScreen>();
            if (loadingScreen.isActiveAndEnabled)
            {
                return;
            }

            CombinationActionSubject.OnNext(_selectedRecipeInfo);
        }

        private bool CheckMaterialAndSlot()
        {
            if (!CheckMaterial(_selectedRecipeInfo.Materials))
            {
                return false;
            }

            var slots = Widget.Find<CombinationSlotsPopup>();
            if (!slots.TryGetEmptyCombinationSlot(out var _))
            {
                return false;
            }

            return true;
        }

        public bool CheckSubmittable(out string errorMessage, out int slotIndex)
        {
            slotIndex = -1;
            if (States.Instance.AgentState is null)
            {
                errorMessage = L10nManager.Localize("FAILED_TO_GET_AGENTSTATE");
                return false;
            }

            if (States.Instance.CurrentAvatarState is null)
            {
                errorMessage = L10nManager.Localize("FAILED_TO_GET_AVATARSTATE");
                return false;
            }

            if (States.Instance.GoldBalanceState.Gold.MajorUnit < _selectedRecipeInfo.CostNCG)
            {
                errorMessage = L10nManager.Localize("UI_NOT_ENOUGH_NCG");
                return false;
            }

            if (States.Instance.CurrentAvatarState.actionPoint < _selectedRecipeInfo.CostAP)
            {
                errorMessage = L10nManager.Localize("ERROR_ACTION_POINT");
                return false;
            }

            if (!CheckMaterial(_selectedRecipeInfo.Materials))
            {
                errorMessage = L10nManager.Localize("NOTIFICATION_NOT_ENOUGH_MATERIALS");
                return false;
            }

            var slots = Widget.Find<CombinationSlotsPopup>();
            if (!slots.TryGetEmptyCombinationSlot(out slotIndex))
            {
                var message = L10nManager.Localize("NOTIFICATION_NOT_ENOUGH_SLOTS");
                errorMessage = message;
                return false;
            }

            errorMessage = null;
            return true;
        }

        private bool CheckMaterial(List<(HashDigest<SHA256> material, int count)> materials)
        {
            var inventory = States.Instance.CurrentAvatarState.inventory;

            foreach (var material in materials)
            {
                var itemCount = inventory.TryGetFungibleItems(material.material, out var outFungibleItems)
                            ? outFungibleItems.Sum(e => e.count)
                            : 0;

                if (material.count > itemCount)
                {
                    return false;
                }
            }

            return true;
        }

        private (HashDigest<SHA256>, int) CreateMaterial(int id, int count)
        {
            var row = Game.Game.instance.TableSheets.MaterialItemSheet[id];
            var material = ItemFactory.CreateMaterial(row);
            return (material.FungibleId, count);
        }
    }
}
