#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using Nekoyume.Blockchain;
using Nekoyume.Game.Controller;
using Nekoyume.L10n;
using Nekoyume.State;
using Nekoyume.TableData;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Nekoyume.UI.Model
{
    public class CombinationSlotLockObject : MonoBehaviour
    {
        private Button _button = null!;

        // TODO: 동적으로 이미지 변경?
        // private Image _costImage;

        [SerializeField]
        private TMP_Text costText = null!;

        [SerializeField]
        private GameObject loadingIndicator = null!;

        [SerializeField]
        private GameObject lockPriceObject = null!;

        private CostType _costType;
        private UnlockCombinationSlotCostSheet.Row? _data;

#region MonoBehavior
        private void Awake()
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(() =>
            {
                AudioController.PlayClick();
                ShowPaymentPopup();
            });
        }
#endregion MonoBehavior

        /// <summary>
        /// Lock오브젝트가 활성화 되면 항상 초기화, 액션 요청시 활성화
        /// 액션 실패시 비활성화
        /// </summary>
        /// <param name="isLoading"></param>
        public void SetLoading(bool isLoading)
        {
            loadingIndicator.SetActive(isLoading);
            lockPriceObject.SetActive(!isLoading);
        }

        private void ShowPaymentPopup()
        {
            if (_data == null)
            {
                NcDebug.LogError("TableData is null");
                return;
            }

            var paymentPopup = Widget.Find<PaymentPopup>();
            switch (_costType)
            {
                case CostType.Crystal:
                case CostType.NCG:
                    paymentPopup.ShowCheckPayment(
                        _costType,
                        GetBalance(),
                        GetCost(),
                        GetCheckCostMessageString(),
                        L10nManager.Localize(_costType == CostType.Crystal
                            ? "UI_NOT_ENOUGH_CRYSTAL"
                            : "UI_NOT_ENOUGH_NCG"),
                        OnPaymentSucceed,
                        () =>
                        {
                            // TODO
                        });
                    break;
                case CostType.GoldDust:
                case CostType.RubyDust:
                    paymentPopup.ShowCheckPaymentDust(
                        _costType,
                        GetBalance(),
                        GetCost(),
                        GetCheckCostMessageString(),
                        OnPaymentSucceed);
                    break;
            }
        }

        private void OnPaymentSucceed()
        {
            ActionManager.Instance.UnlockCombinationSlot(_data.SlotId);
            SetLoading(true);
        }

#region GetBalance
        private BigInteger GetBalance()
        {
            var inventory = States.Instance.CurrentAvatarState.inventory;
            return _costType switch
            {
                CostType.Crystal => States.Instance.CrystalBalance.MajorUnit,
                CostType.NCG => States.Instance.GoldBalanceState.Gold.MajorUnit,
                CostType.GoldDust => inventory.GetMaterialCount((int)_costType),
                CostType.RubyDust => inventory.GetMaterialCount((int)_costType),
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        private BigInteger GetCost()
        {
            if (_data != null)
            {
                return _costType switch
                {
                    CostType.Crystal => _data.CrystalPrice,
                    CostType.NCG => _data.NcgPrice,
                    CostType.GoldDust => _data.GoldenDustPrice,
                    CostType.RubyDust => _data.RubyDustPrice,
                    _ => throw new ArgumentOutOfRangeException()
                };
            }

            NcDebug.LogError("TableData is null");
            return BigInteger.Zero;
        }
#endregion GetBalance

        private string GetCheckCostMessageString()
        {
            var cost = GetCost();
            var usageMessage = L10nManager.Localize("UI_UNLOCK");
            var costTypeKey = _costType switch
            {
                CostType.Crystal => "UI_CRYSTAL",
                CostType.NCG => "UI_NCG",
                CostType.GoldDust => "ITEM_NAME_600201",
                CostType.RubyDust => "ITEM_NAME_600202",
                _ => throw new ArgumentOutOfRangeException()
            };
            var costTypeText = L10nManager.Localize(costTypeKey);

            return L10nManager.Localize(
                "UI_CONFIRM_PAYMENT_CURRENCY_FORMAT",
                cost,
                costTypeText,
                usageMessage);
        }

        public void SetData(UnlockCombinationSlotCostSheet.Row data)
        {
            _data = data;
            if (data.CrystalPrice > 0)
            {
                _costType = CostType.Crystal;
            }
            else if (data.GoldenDustPrice > 0)
            {
                _costType = CostType.GoldDust;
            }
            else if (data.RubyDustPrice > 0)
            {
                _costType = CostType.RubyDust;
            }
            else if (data.NcgPrice > 0)
            {
                _costType = CostType.NCG;
            }

            costText.text = GetCost().ToString();
        }
    }
}
