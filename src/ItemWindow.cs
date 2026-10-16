using System;
using System.Collections.Generic;
using System.Reflection;
using Core.Services;
using Items.Interface.Service;
using Items.Model;
using MelonLoader;
using Players;
using Players.Services;
using TMPro;
using UI.Common;
using UI.Interface.Model;
using UI.Inventory;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ItemMenu
{
    public class ItemWindow : MonoBehaviour, IClosableMenu
    {
        static readonly HashSet<string> DisabledScripts = new HashSet<string>
        {
            "InventoryGrid",
            "InventoryMenu",
            "InventoryMouseActionsTracker",
            "InventorySlotProvider",
            "TooltipEnabler",
            "HoldItemCursorActivator",
            "DestroySlotUI",
            "CursorFollow",
            "ItemSlotLocationProvider"
        };

        static readonly FieldInfo IconField = typeof(InventorySlotUI).GetField("icon", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo QuantityField = typeof(InventorySlotUI).GetField("quantity", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo HighlightField = typeof(InventorySlotUI).GetField("highlight", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo PrefabField = typeof(PlayerInventoryActivator).GetField("inventoryCanvasPrefab", BindingFlags.Instance | BindingFlags.NonPublic);

        static ItemWindow _current;
        static bool _warned;

        PlayerInventoryActivator _inventory;
        ItemTooltip _tooltip;
        TMP_Text _title;
        List<Behaviour> _frozen;
        bool _tracked;
        bool _closed;
        int _titlePasses = 4;

        public static bool IsOpen => _current != null;

        public static void Open()
        {
            if (_current != null)
                return;

            PlayerInventoryActivator activator = Object.FindAnyObjectByType<PlayerInventoryActivator>(FindObjectsInactive.Include);
            GameObject prefab = activator == null || PrefabField == null ? null : PrefabField.GetValue(activator) as GameObject;
            if (activator == null || prefab == null)
            {
                if (!_warned)
                {
                    _warned = true;
                    MelonLogger.Warning("Item menu could not find the inventory screen.");
                }
                return;
            }

            GameObject holder = new GameObject("ItemMenuHolder");
            holder.SetActive(false);
            GameObject menu = Object.Instantiate(prefab, holder.transform);
            menu.name = "ItemMenu";
            UntagServices(menu);
            DisableInventoryScripts(menu);

            ItemWindow window = menu.AddComponent<ItemWindow>();
            window._inventory = activator;
            if (!window.Build())
            {
                Object.Destroy(holder);
                return;
            }

            _current = window;
            Transform parent = activator.transform.parent;
            menu.transform.SetParent(parent, false);
            menu.SetActive(true);
            Object.Destroy(holder);
        }

        public static void CloseCurrent()
        {
            if (_current != null)
                _current.Close();
        }

        public void Close()
        {
            if (_closed)
                return;
            _closed = true;
            Release();
            if (_current == this)
                _current = null;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            Release();
            if (_current == this)
                _current = null;
        }

        void OnEnable()
        {
            MenuTracker tracker = Loc<MenuTracker>();
            if (tracker == null)
                return;
            tracker.TrackMenu(this);
            _tracked = true;
            FreezeControls();
        }

        void LateUpdate()
        {
            FreezeControls();
            if (_titlePasses > 0 && _title != null)
            {
                _titlePasses--;
                _title.text = "Items";
            }

            if (_inventory != null && _inventory.IsInventoryActive)
                Close();
        }

        bool Build()
        {
            List<ItemSettings> materials = Materials();
            if (materials == null || materials.Count == 0)
            {
                MelonLogger.Warning("Item menu could not read the material list.");
                return false;
            }

            InventorySlotUI[] slots = GetComponentsInChildren<InventorySlotUI>(true);
            var main = new List<InventorySlotUI>();
            var rest = new List<InventorySlotUI>();
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].origin == InventoryType.MainInventory)
                    main.Add(slots[i]);
                else
                    rest.Add(slots[i]);
            }

            var cells = new List<InventorySlotUI>();
            if (main.Count > 0)
                cells.AddRange(main);
            else
                cells.AddRange(slots);

            if (main.Count > 0)
            {
                for (int i = 0; i < rest.Count && cells.Count < materials.Count; i++)
                    cells.Add(rest[i]);
                for (int i = 0; i < rest.Count; i++)
                {
                    if (!cells.Contains(rest[i]))
                        rest[i].gameObject.SetActive(false);
                }
            }

            if (cells.Count == 0)
            {
                MelonLogger.Warning("Item menu could not find inventory slots.");
                return false;
            }

            HideMass();
            _tooltip = GetComponentInChildren<ItemTooltip>(true);
            _title = FindTitleText();
            if (_title != null)
            {
                Silence(_title.gameObject);
                if (_title.transform.parent != null)
                    Silence(_title.transform.parent.gameObject);
                _title.text = "Items";
            }

            int shown = Mathf.Min(cells.Count, materials.Count);
            for (int i = 0; i < cells.Count; i++)
            {
                if (i < shown)
                    Bind(cells[i], materials[i]);
                else
                    Clear(cells[i]);
            }

            if (shown < materials.Count)
                MelonLogger.Warning("Item menu is showing " + shown + " of " + materials.Count + " materials.");
            return true;
        }

        void Bind(InventorySlotUI slot, ItemSettings settings)
        {
            StripTriggers(slot.gameObject);
            RawImage icon = IconField == null ? null : IconField.GetValue(slot) as RawImage;
            TMP_Text label = QuantityField == null ? null : QuantityField.GetValue(slot) as TMP_Text;
            Texture2D picture = Thumbnail(settings.id);
            if (picture == null)
                picture = settings.icon;
            if (icon != null)
            {
                icon.texture = picture;
                icon.enabled = picture != null;
            }
            if (label != null)
            {
                Silence(label.gameObject);
                RectTransform rect = label.rectTransform;
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0.36f);
                rect.offsetMin = new Vector2(2f, 0f);
                rect.offsetMax = new Vector2(-2f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Ellipsis;
                label.enableAutoSizing = true;
                label.fontSizeMin = 10f;
                label.fontSizeMax = 20f;
                label.color = Color.white;
                label.text = settings.itemName;
                label.enabled = true;
                label.transform.SetAsLastSibling();
            }

            ItemChoice choice = slot.gameObject.GetComponent<ItemChoice>();
            if (choice == null)
                choice = slot.gameObject.AddComponent<ItemChoice>();
            choice.Setup(this, slot, settings);
        }

        static void Clear(InventorySlotUI slot)
        {
            StripTriggers(slot.gameObject);
            ItemChoice choice = slot.GetComponent<ItemChoice>();
            if (choice != null)
                Destroy(choice);
            RawImage icon = IconField == null ? null : IconField.GetValue(slot) as RawImage;
            TMP_Text label = QuantityField == null ? null : QuantityField.GetValue(slot) as TMP_Text;
            if (icon != null)
                icon.enabled = false;
            if (label != null)
                label.enabled = false;
            GameObject highlight = HighlightField == null ? null : HighlightField.GetValue(slot) as GameObject;
            if (highlight != null)
                highlight.SetActive(false);
        }

        public void ShowTip(InventorySlotUI slot, string label)
        {
            if (_tooltip == null || slot == null)
                return;
            RectTransform rect = slot.transform as RectTransform;
            if (rect != null)
            {
                Vector3 corner = rect.TransformPoint(rect.rect.max);
                _tooltip.transform.position = corner;
            }
            _tooltip.Show(label);
        }

        public void HideTip()
        {
            if (_tooltip != null)
                _tooltip.Hide();
        }

        static List<ItemSettings> Materials()
        {
            IItemSettingsProvider provider = Loc<IItemSettingsProvider>();
            if (provider == null)
                return null;

            object list = Field(provider, "items");
            if (list == null)
                return null;
            ItemSettings[] all = Field(list, "items") as ItemSettings[];
            if (all == null)
                return null;

            var materials = new List<ItemSettings>();
            for (int i = 0; i < all.Length; i++)
            {
                ItemSettings settings = all[i];
                if (settings == null || settings.toolSettings != null)
                    continue;
                if (string.IsNullOrWhiteSpace(settings.itemName))
                    continue;
                materials.Add(settings);
            }

            materials.Sort((a, b) => string.Compare(a.itemName, b.itemName, StringComparison.OrdinalIgnoreCase));
            return materials;
        }

        static Texture2D Thumbnail(uint itemId)
        {
            IItemThumbnailProvider pictures = Loc<IItemThumbnailProvider>();
            if (pictures == null)
                return null;
            try
            {
                return pictures.GetThumbnail(itemId);
            }
            catch (Exception)
            {
                return null;
            }
        }

        TMP_Text FindTitleText()
        {
            TMP_Text best = null;
            float size = 0f;
            TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (!texts[i].gameObject.activeSelf)
                    continue;
                if (texts[i].GetComponentInParent<InventorySlotUI>(true) != null)
                    continue;
                if (texts[i].fontSize <= size)
                    continue;
                size = texts[i].fontSize;
                best = texts[i];
            }
            return best;
        }

        void HideMass()
        {
            TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                string value = texts[i].text ?? "";
                string objectName = texts[i].gameObject.name ?? "";
                if (value.IndexOf("Mass", StringComparison.OrdinalIgnoreCase) < 0
                    && objectName.IndexOf("Mass", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                texts[i].gameObject.SetActive(false);
            }
        }

        void FreezeControls()
        {
            if (_frozen == null)
            {
                _frozen = new List<Behaviour>();
                MonoBehaviour[] behaviours = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                for (int i = 0; i < behaviours.Length; i++)
                {
                    MonoBehaviour behaviour = behaviours[i];
                    if (behaviour == null || behaviour == this)
                        continue;
                    string name = behaviour.GetType().Name;
                    if (!name.StartsWith("Player", StringComparison.Ordinal))
                        continue;
                    bool control = name.EndsWith("Control", StringComparison.Ordinal) || name.EndsWith("Controls", StringComparison.Ordinal);
                    if (!control)
                        continue;
                    if (name == "PlayerEscapeControl" || name == "PlayerInventoryControl" || name == "PlayerChatControl" || name == "PlayerGuideControl")
                        continue;
                    if (!behaviour.enabled)
                        continue;
                    _frozen.Add(behaviour);
                }
            }

            for (int i = 0; i < _frozen.Count; i++)
            {
                if (_frozen[i] != null)
                    _frozen[i].enabled = false;
            }
        }

        void Release()
        {
            HideTip();
            MenuTracker tracker = null;
            if (_tracked)
            {
                _tracked = false;
                tracker = Loc<MenuTracker>();
                if (tracker != null)
                    tracker.UntrackMenu(this);
            }

            if (_frozen == null)
                return;
            if (tracker == null)
                tracker = Loc<MenuTracker>();
            if (tracker != null && tracker.HasClosableMenuOpen())
                return;

            for (int i = 0; i < _frozen.Count; i++)
            {
                if (_frozen[i] != null)
                    _frozen[i].enabled = true;
            }
            _frozen = null;
        }

        static void DisableInventoryScripts(GameObject menu)
        {
            Behaviour[] behaviours = menu.GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                Behaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;
                string name = behaviour.GetType().Name;
                if (!DisabledScripts.Contains(name))
                    continue;
                if (name == "DestroySlotUI")
                    behaviour.gameObject.SetActive(false);
                behaviour.enabled = false;
            }
        }

        static void UntagServices(GameObject root)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                GameObject go = transforms[i].gameObject;
                try
                {
                    if (go.CompareTag("ServiceObject"))
                        go.tag = "Untagged";
                }
                catch (Exception)
                {
                }
            }
        }

        static void StripTriggers(GameObject slot)
        {
            EventTrigger[] triggers = slot.GetComponentsInChildren<EventTrigger>(true);
            for (int i = 0; i < triggers.Length; i++)
                DestroyImmediate(triggers[i]);
        }

        static void Silence(GameObject go)
        {
            Behaviour[] behaviours = go.GetComponents<Behaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != null && behaviours[i].GetType().Name == "LocalizeStringEvent")
                    behaviours[i].enabled = false;
            }
        }

        static object Field(object owner, string name)
        {
            if (owner == null)
                return null;
            FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(owner);
        }

        static T Loc<T>() where T : class
        {
            try
            {
                return ServiceLocator.GetService<T>();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public class ItemChoice : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        ItemWindow _window;
        InventorySlotUI _slot;
        uint _itemId;
        byte _stack;
        string _label;
        GameObject _highlight;

        public void Setup(ItemWindow window, InventorySlotUI slot, ItemSettings settings)
        {
            _window = window;
            _slot = slot;
            _itemId = settings.id;
            _stack = settings.maxStackSize == 0 ? (byte)1 : settings.maxStackSize;
            _label = settings.itemName;
            FieldInfo highlight = typeof(InventorySlotUI).GetField("highlight", BindingFlags.Instance | BindingFlags.NonPublic);
            _highlight = highlight == null ? null : highlight.GetValue(slot) as GameObject;
            if (_highlight != null)
                _highlight.SetActive(false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;
            TrackedPlayerServer player = LocalPlayer();
            if (player == null)
            {
                MelonLogger.Warning("Item menu could not find the player.");
                return;
            }

            try
            {
                player.AddItemToInventory(new ItemDrop { ItemId = _itemId, Quantity = _stack });
            }
            catch (Exception ex)
            {
                MelonLogger.Error("Item menu could not add " + _label + ".", ex);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_highlight != null)
                _highlight.SetActive(true);
            if (_window != null)
                _window.ShowTip(_slot, _label);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_highlight != null)
                _highlight.SetActive(false);
            if (_window != null)
                _window.HideTip();
        }

        static TrackedPlayerServer LocalPlayer()
        {
            try
            {
                PlayersClientTracker clients = ServiceLocator.GetService<PlayersClientTracker>();
                PlayersServerTracker servers = ServiceLocator.GetService<PlayersServerTracker>();
                if (clients == null || clients.LocalPlayer == null || servers == null)
                    return null;

                uint id = clients.LocalPlayer.Id;
                if (servers.GetPlayer(id) is TrackedPlayerServer found)
                    return found;

                foreach (TrackedPlayerServer server in servers.ActivePlayers)
                {
                    if (server != null && server.Id == id)
                        return server;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("Item menu could not find the player. " + ex.Message);
            }

            return null;
        }
    }
}
