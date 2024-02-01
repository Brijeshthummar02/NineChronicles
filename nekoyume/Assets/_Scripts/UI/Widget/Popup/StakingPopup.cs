using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Nekoyume.Blockchain;
using Nekoyume.Game;
using Nekoyume.Game.Controller;
using Nekoyume.L10n;
using Nekoyume.Model.Item;
using Nekoyume.Model.State;
using Nekoyume.State;
using Nekoyume.TableData;
using Nekoyume.UI.Module;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nekoyume.UI
{
    using UniRx;

    public class StakingPopup : PopupWidget
    {
        [Header("Top")]
        [SerializeField] private Image levelIconImage;
        [SerializeField] private Image[] levelImages;
        [SerializeField] private TextMeshProUGUI depositText;
        [SerializeField] private Button closeButton;

        [Header("Center")]
        [SerializeField] private StakingBuffBenefitsView[] buffBenefitsViews;
        [SerializeField] private StakingInterestBenefitsView[] interestBenefitsViews;
        [SerializeField] private TextMeshProUGUI remainingBlockText;
        [SerializeField] private ConditionalButton archiveButton;

        [SerializeField] private StakingBenefitsListView[] benefitsListViews;

        [Header("Bottom")]
        [SerializeField] private CategoryTabButton currentBenefitsTabButton;
        [SerializeField] private CategoryTabButton levelBenefitsTabButton;
        [SerializeField] private GameObject currentBenefitsTab;
        [SerializeField] private GameObject levelBenefitsTab;

        [SerializeField] private StakeIconDataScriptableObject stakeIconData;

        [SerializeField]
        private TMP_InputField stakingNcgInputField;

        private readonly ReactiveProperty<BigInteger> _deposit = new();
        private readonly Module.ToggleGroup _toggleGroup = new ();

        // hard-coded constant values. it is arena ncg bonus.
        private readonly Dictionary<int, int> _arenaNcgBonus = new()
        {
            {1, 0}, {2, 100}, {3, 200}, {4, 200}, {5, 200}, {6, 200}, {7, 200}, {8, 200}
        };

        private readonly StakingBenefitsListView.Model[] _cachedModel =
            new StakingBenefitsListView.Model[9];

        public const string StakingUrl = "ninechronicles-launcher://open/monster-collection";
        private const string ActionPointBuffFormat = "{0} <color=#1FFF00>{1}% DC</color>";
        private const string BuffBenefitRateFormat = "{0} <color=#1FFF00>+{1}%</color>";
        private const string RemainingBlockFormat = "<Style=G5>{0}({1})";

        private bool _benefitListViewsInitialized;

        protected override void Awake()
        {
            base.Awake();

            _toggleGroup.RegisterToggleable(currentBenefitsTabButton);
            _toggleGroup.RegisterToggleable(levelBenefitsTabButton);
            currentBenefitsTabButton.OnClick.Subscribe(_ =>
            {
                currentBenefitsTab.SetActive(true);
                levelBenefitsTab.SetActive(false);
            }).AddTo(gameObject);
            levelBenefitsTabButton.OnClick.Subscribe(_ =>
            {
                currentBenefitsTab.SetActive(false);
                levelBenefitsTab.SetActive(true);
            }).AddTo(gameObject);
            closeButton.onClick.AddListener(() =>
            {
                AudioController.PlayClick();
                Close();
            });

            // TODO: 여기를 ClaimStakeReward action 보내는 버튼으로 바꿔야함
            archiveButton.OnSubmitSubject.Subscribe(_ =>
            {
                AudioController.PlayClick();
                ActionManager.Instance
                    .ClaimStakeReward(States.Instance.CurrentAvatarState.address)
                    .Subscribe();
                archiveButton.Interactable = false;
            }).AddTo(gameObject);

            stakingNcgInputField.onEndEdit.AddListener(value =>
            {
                ActionManager.Instance.Stake(BigInteger.Parse(value)).Subscribe();
            });
        }

        public override void Initialize()
        {
            SetBenefitsListViews();
            _deposit.Subscribe(OnDepositEdited).AddTo(gameObject);
            Game.Game.instance.Agent.BlockIndexSubject.ObserveOnMainThread()
                .Where(_ => gameObject.activeSelf).Subscribe(OnBlockUpdated)
                .AddTo(gameObject);
        }

        public override void Show(bool ignoreStartAnimation = false)
        {
            SetView();
            base.Show(ignoreStartAnimation);
        }

        public void SetView()
        {
            SetBenefitsListViews();
            var deposit = States.Instance.StakedBalanceState?.Gold.MajorUnit ?? 0;
            var blockIndex = Game.Game.instance.Agent.BlockIndex;

            _deposit.Value = deposit;
            OnBlockUpdated(blockIndex);
            _toggleGroup.SetToggledOffAll();
            currentBenefitsTabButton.OnClick.OnNext(currentBenefitsTabButton);
            currentBenefitsTabButton.SetToggledOn();
        }

        private void OnDepositEdited(BigInteger deposit)
        {
            var states = States.Instance;
            var level = states.StakingLevel;

            levelIconImage.sprite = stakeIconData.GetIcon(level, IconType.Small);
            for (var i = 0; i < levelImages.Length; i++)
            {
                levelImages[i].enabled = i < level;
            }

            depositText.text = deposit.ToString("N0");
            buffBenefitsViews[0].Set(
                string.Format(BuffBenefitRateFormat, L10nManager.Localize("ARENA_REWARD_BONUS"),
                    _cachedModel[level].ArenaRewardBuff));
            buffBenefitsViews[1].Set(
                string.Format(BuffBenefitRateFormat, L10nManager.Localize("GRINDING_CRYSTAL_BONUS"),
                    _cachedModel[level].CrystalBuff));
            buffBenefitsViews[2].Set(
                string.Format(ActionPointBuffFormat, L10nManager.Localize("STAGE_AP_BONUS"),
                    _cachedModel[level].ActionPointBuff));

            for (int i = 0; i <= 7; i++)
            {
                benefitsListViews[i].Set(i, level);
            }
        }

        private void OnBlockUpdated(long blockIndex)
        {
            var states = States.Instance;
            var level = states.StakingLevel;
            var deposit = states.StakedBalanceState?.Gold.MajorUnit ?? 0;
            var regularSheet = states.StakeRegularRewardSheet;
            var regularFixedSheet = states.StakeRegularFixedRewardSheet;
            var stakeStateV2 = states.StakeStateV2;
            var rewardBlockInterval = stakeStateV2.HasValue
                ? (int) stakeStateV2.Value.Contract.RewardInterval
                : (int) StakeState.RewardInterval;

            TryGetWaitedBlockIndex(blockIndex, rewardBlockInterval, out var waitedBlockRange);
            var rewardCount = (int) waitedBlockRange / rewardBlockInterval;
            regularSheet.TryGetValue(level, out var regular);
            regularFixedSheet.TryGetValue(level, out var regularFixed);

            var materialSheet = TableSheets.Instance.MaterialItemSheet;
            for (var i = 0; i < interestBenefitsViews.Length; i++)
            {
                var result = GetReward(regular, regularFixed, (long) deposit, i);
                result *= Mathf.Max(rewardCount, 1);
                if (result <= 0)
                {
                    interestBenefitsViews[i].gameObject.SetActive(false);
                    continue;
                }

                interestBenefitsViews[i].gameObject.SetActive(true);
                if (regular != null)
                {
                    switch (regular.Rewards[i].Type)
                    {
                        case StakeRegularRewardSheet.StakeRewardType.Item:
                            interestBenefitsViews[i].Set(
                                ItemFactory.CreateMaterial(materialSheet,
                                    regular.Rewards[i].ItemId),
                                (int) result);
                            break;
                        case StakeRegularRewardSheet.StakeRewardType.Rune:
                            interestBenefitsViews[i].Set(
                                regular.Rewards[i].ItemId,
                                (int) result);
                            break;
                        case StakeRegularRewardSheet.StakeRewardType.Currency:
                            interestBenefitsViews[i].Set(
                                regular.Rewards[i].CurrencyTicker,
                                (int) result);
                            break;
                    }
                }
            }

            var remainingBlock = Math.Max(rewardBlockInterval - waitedBlockRange, 0);
            remainingBlockText.text = string.Format(
                RemainingBlockFormat,
                remainingBlock.ToString("N0"),
                remainingBlock.BlockRangeToTimeSpanString());
        }

        private static bool TryGetWaitedBlockIndex(
            long blockIndex,
            int rewardBlockInterval,
            out long waitedBlockRange)
        {
            var stakeState = States.Instance.StakeStateV2;
            if (stakeState is null)
            {
                waitedBlockRange = 0;
                return false;
            }

            var started = stakeState.Value.StartedBlockIndex;
            var received = stakeState.Value.ReceivedBlockIndex;
            if (received > 0)
            {
                waitedBlockRange = blockIndex - received;
                waitedBlockRange += (received - started) % rewardBlockInterval;
            }
            else
            {
                waitedBlockRange = blockIndex - started;
            }

            return true;
        }

        private void SetBenefitsListViews()
        {
            if (_benefitListViewsInitialized)
            {
                return;
            }

            var regularSheet = States.Instance.StakeRegularRewardSheet;
            var regularFixedSheet = States.Instance.StakeRegularFixedRewardSheet;
            if (regularSheet is null || regularFixedSheet is null)
            {
                return;
            }

            _cachedModel[0] = new StakingBenefitsListView.Model();
            var stakingMultiplierSheet =
                TableSheets.Instance.CrystalMonsterCollectionMultiplierSheet;
            for (var level = 1; level <= 8; level++)
            {
                var model = new StakingBenefitsListView.Model();

                if (regularSheet.TryGetValue(level, out var regular)
                    && regularFixedSheet.TryGetValue(level, out var regularFixed))
                {
                    model.RequiredDeposit = regular.RequiredGold;
                    model.HourGlassInterest = GetReward(regular, regularFixed, regular.RequiredGold, 0);
                    model.ApPotionInterest = GetReward(regular, regularFixed, regular.RequiredGold, 1);
                    model.RuneInterest = GetReward(regular, regularFixed, regular.RequiredGold, 2);

                    model.CrystalInterest = GetReward(regular, regularFixed, regular.RequiredGold, GetCrystalRewardIndex(regular));
                    model.GoldenPowderInterest = GetReward(regular, regularFixed, regular.RequiredGold, GetGoldenPowderRewardIndex(regular));
                    model.GoldenMeatInterest = GetReward(regular, regularFixed, regular.RequiredGold, GetGoldenMeatRewardIndex(regular));
                }

                if (stakingMultiplierSheet.TryGetValue(level, out var row))
                {
                    model.CrystalBuff = row.Multiplier;
                }

                model.ActionPointBuff = 100 - TableSheets.Instance
                    .StakeActionPointCoefficientSheet[level].Coefficient;
                model.ArenaRewardBuff = _arenaNcgBonus[level];

                benefitsListViews[level].Set(level, model);
                _cachedModel[level] = model;
            }

            _benefitListViewsInitialized = true;
        }

        private static long GetReward(
            StakeRegularRewardSheet.Row regular,
            StakeRegularFixedRewardSheet.Row regularFixed,
            long deposit, int index)
        {
            if (regular is null || regularFixed is null)
            {
                return 0;
            }

            if (regular.Rewards.Count <= index || index < 0)
            {
                return 0;
            }

            var result = (long)Math.Truncate(deposit / regular.Rewards[index].DecimalRate);
            var levelBonus = regularFixed.Rewards.FirstOrDefault(
                reward => reward.ItemId == regular.Rewards[index].ItemId)?.Count ?? 0;

            return result + levelBonus;
        }

        private static int GetCrystalRewardIndex(StakeRegularRewardSheet.Row regular)
        {
            for (int i = 0; i < regular.Rewards.Count; i++)
            {
                if (regular.Rewards[i].CurrencyTicker == "CRYSTAL")
                    return i;
            }
            return -1;
        }

        private static int GetGoldenPowderRewardIndex(StakeRegularRewardSheet.Row regular)
        {
            for (int i = 0; i < regular.Rewards.Count; i++)
            {
                if (regular.Rewards[i].ItemId == 600201)
                    return i;
            }
            return -1;
        }

        private static int GetGoldenMeatRewardIndex(StakeRegularRewardSheet.Row regular)
        {
            for (int i = 0; i < regular.Rewards.Count; i++)
            {
                if (regular.Rewards[i].ItemId == 800202)
                    return i;
            }
            return -1;
        }
    }
}
