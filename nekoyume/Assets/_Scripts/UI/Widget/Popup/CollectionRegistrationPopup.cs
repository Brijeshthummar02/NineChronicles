using System;
using System.Collections.Generic;
using System.Linq;
using Nekoyume.EnumType;
using Nekoyume.Game.Controller;
using Nekoyume.Model.Collection;
using Nekoyume.Model.Item;
using Nekoyume.UI.Model;
using Nekoyume.UI.Module;
using UnityEngine;
using UnityEngine.UI;

namespace Nekoyume.UI
{
    using UniRx;
    public class CollectionRegistrationPopup : PopupWidget
    {
        [SerializeField] private Button closeButton;
        [SerializeField] private CollectionStat collectionStat;
        [SerializeField] private CollectionItemView[] collectionItemViews;
        [SerializeField] private ConditionalButton registrationButton;
        [SerializeField] private CollectionInventory collectionInventory;

        private readonly Dictionary<CollectionMaterial, ICollectionMaterial> _registeredItems = new();
        private CollectionMaterial _focusedRequiredItem;
        private Action<List<ICollectionMaterial>> _registerMaterials;

        protected override void Awake()
        {
            base.Awake();

            closeButton.onClick.AddListener(() =>
            {
                AudioController.PlayClick();
                CloseWidget.Invoke();
            });
            CloseWidget = () =>
            {
                Close();
            };

            registrationButton.OnSubmitSubject
                .Select(_ => collectionInventory.SelectedItem)
                .Subscribe(RegisterItem)
                .AddTo(gameObject);

            collectionInventory.SetInventory(OnClickInventoryItem);
        }

        private void RegisterItem(InventoryItem item)
        {
            ICollectionMaterial collectionMaterialItem;
            if (item.ItemBase is Equipment equipment)  // Todo : check if it's a non-fungible item
            {
                collectionMaterialItem = new NonFungibleCollectionMaterial
                {
                    ItemId = equipment.Id,
                    ItemCount = 1,
                    NonFungibleId = equipment.NonFungibleId,
                    Level = equipment.level,
                    OptionCount = equipment.GetOptionCount(),
                    SkillContains = equipment.Skills.Any()
                };
            }
            else
            {
                collectionMaterialItem = new FungibleCollectionMaterial
                {
                    ItemId = item.ItemBase.Id,
                    ItemCount = item.Count.Value,
                };
            }

            _registeredItems[_focusedRequiredItem] = collectionMaterialItem;

            // Focus next required item or Activate
            var notRegisteredItem = _registeredItems
                .FirstOrDefault(registeredItem => registeredItem.Value == null).Key;
            if (notRegisteredItem != null)
            {
                OnClickItem(notRegisteredItem);
            }
            else
            {
                var registeredItems = _registeredItems.Values.ToList();
                _registerMaterials?.Invoke(registeredItems);
                CloseWidget.Invoke();
            }
        }

        private void OnClickInventoryItem(InventoryItem item)
        {
            // Todo : Show item info
        }

        private void OnClickItem(CollectionMaterial collectionMaterial)
        {
            _focusedRequiredItem?.Focused.SetValueAndForceNotify(false);
            _focusedRequiredItem = collectionMaterial;
            _focusedRequiredItem.Focused.SetValueAndForceNotify(true);

            collectionInventory.SetRequiredItem(_focusedRequiredItem);

            var canRegister = _registeredItems.Any(registeredItem => registeredItem.Value == null);
            registrationButton.Text = canRegister
                ? "Register"
                : "Activate";
        }

        public void Show(
            CollectionModel model,
            Action<List<ICollectionMaterial>> register,
            bool ignoreShowAnimation = false)
        {
            collectionStat.Set(model);
            _registerMaterials = register;

            var materialCount = model.Row.Materials.Count;
            var itemSheet = Game.Game.instance.TableSheets.ItemSheet;

            _registeredItems.Clear();
            for (var i = 0; i < collectionItemViews.Length; i++)
            {
                collectionItemViews[i].gameObject.SetActive(i < materialCount);
                if (i >= materialCount)
                {
                    continue;
                }

                var material = model.Row.Materials[i];
                var itemRow = itemSheet[material.ItemId];

                var requiredItem = new CollectionMaterial(material, itemRow.Grade, itemRow.ItemType);
                collectionItemViews[i].Set(requiredItem, OnClickItem);
                _registeredItems.Add(requiredItem, null);
            }

            OnClickItem(_registeredItems.Keys.First());

            base.Show(ignoreShowAnimation);
        }
    }
}
