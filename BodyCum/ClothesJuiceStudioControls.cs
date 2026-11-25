using Studio;
using UnityEngine;
using UnityEngine.UI;

namespace BodyCum
{
    internal sealed class ClothesJuiceStudioControls
    {
        internal const float Height = 5 * JuiceColorStudioControls.RowHeight;

        private readonly JuiceColorStudioControls _ui;
        private readonly Button[] _levelButtons = new Button[3];
        private readonly Text[] _levelLabels = new Text[3];
        private readonly Button _previous;
        private readonly Button _slot;
        private readonly Text _slotLabel;
        private readonly Button _next;
        private readonly Slider _horizontal;
        private readonly Slider _vertical;
        private readonly Slider _size;
        private readonly Text _horizontalValue;
        private readonly Text _verticalValue;
        private readonly Text _sizeValue;
        private OCIChar _character;
        private int _slotIndex = -1;
        private bool _refreshing;

        internal ClothesJuiceStudioControls(JuiceColorStudioControls ui, Transform parent, float y)
        {
            _ui = ui;
            var container = new GameObject("SkinEffectsClothesJuiceControls", typeof(RectTransform));
            var panel = (RectTransform)container.transform;
            panel.SetParent(parent, false);
            JuiceColorStudioControls.SetRect(panel, 0f, y, 210f, Height);

            ui.Label(panel, "Clothes", 0f, 0f, 75f);
            var tiers = new[] { "OFF", "x1", "x2" };
            for (var level = 0; level < tiers.Length; level++)
            {
                var tier = level;
                var button = ui.PushButton(panel, "Clothes " + tiers[level], 88f + level * 40f, 0f, 38f, () => SetLevel(tier));
                _levelButtons[level] = button;
                _levelLabels[level] = HairJuiceStudioControls.LabelButton(ui, button, tiers[level], 0f, 38f);
            }

            const float slotY = JuiceColorStudioControls.RowHeight;
            ui.Label(panel, "Cloth Slot", 0f, slotY, 58f);
            _previous = ui.PushButton(panel, "Previous clothes slot", 59f, slotY, 22f, () => MoveSlot(-1));
            HairJuiceStudioControls.LabelButton(ui, _previous, "<", 0f, 22f);
            _slot = ui.PushButton(panel, "Toggle clothes slot", 83f, slotY, 99f, ToggleSlot);
            _slotLabel = HairJuiceStudioControls.LabelButton(ui, _slot, "", 2f, 95f);
            _slotLabel.resizeTextForBestFit = true;
            _slotLabel.resizeTextMinSize = 8;
            _slotLabel.resizeTextMaxSize = _slotLabel.fontSize;
            _slotLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            _slotLabel.verticalOverflow = VerticalWrapMode.Truncate;
            _next = ui.PushButton(panel, "Next clothes slot", 184f, slotY, 22f, () => MoveSlot(1));
            HairJuiceStudioControls.LabelButton(ui, _next, ">", 0f, 22f);

            _horizontal = ui.SliderRow(panel, "Cloth X", 2, -100, 100, SetHorizontal, out _horizontalValue);
            _vertical = ui.SliderRow(panel, "Cloth Y", 3, -100, 100, SetVertical, out _verticalValue);
            _size = ui.SliderRow(panel, "Cloth Size", 4, ClothesJuiceProfile.MinimumSize, ClothesJuiceProfile.MaximumSize, SetSize, out _sizeValue);

        }

        internal void Select(OCIChar character)
        {
            if (!ReferenceEquals(_character, character))
            {
                _character = character;
                _slotIndex = -1;
            }
            Refresh();
        }

        private void Refresh()
        {
            var character = _character?.charInfo;
            var feature = BodyCumPlugin.HairJuices;
            var profile = _character != null && feature != null ? feature.GetClothes(_character) : null;
            if (!ClothesJuiceParts.TryGet(character, _slotIndex, out var root, out var identity))
            {
                _slotIndex = FirstSelectedSlot(character, profile);
                if (_slotIndex < 0)
                    _slotIndex = FindSlot(character, -1, 1);
                ClothesJuiceParts.TryGet(character, _slotIndex, out root, out identity);
            }

            var hasSlot = _slotIndex >= 0 && root && identity != null;
            var isSelected = hasSlot && profile != null && profile.Slots.TryGetValue(_slotIndex, out var selected) && selected == identity;
            var canEdit = character && feature != null;
            _slot.interactable = _previous.interactable = _next.interactable = hasSlot;
            HairJuiceStudioControls.SetButtonColors(_slot, isSelected);
            if (hasSlot)
            {
                var name = ClothesJuiceParts.GetLabel(character, _slotIndex, root);
                if (name.Length > 15)
                    name = name.Substring(0, 14) + "…";
                _slotLabel.text = (isSelected ? "[ON] " : "[  ] ") + name;
            }
            else
            {
                _slotLabel.text = "No clothing";
            }

            var level = profile?.Level ?? 0;
            for (var index = 0; index < _levelButtons.Length; index++)
            {
                var active = index == level;
                _levelButtons[index].interactable = canEdit;
                HairJuiceStudioControls.SetButtonColors(_levelButtons[index], active);
                _levelLabels[index].fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
            }

            _horizontal.interactable = _vertical.interactable = _size.interactable = canEdit;
            _refreshing = true;
            try
            {
                _horizontal.value = profile?.Horizontal ?? 0;
                _vertical.value = profile?.Vertical ?? 0;
                _size.value = profile?.Size ?? ClothesJuiceProfile.DefaultSize;
                _horizontalValue.text = ((int)_horizontal.value).ToString();
                _verticalValue.text = ((int)_vertical.value).ToString();
                _sizeValue.text = ((int)_size.value) + "%";
            }
            finally { _refreshing = false; }
            _ui.RefreshAllSlots();
        }

        private static int FirstSelectedSlot(ChaControl character, ClothesJuiceProfile profile)
        {
            if (profile == null)
                return -1;
            for (var index = 0; index < ClothesJuiceParts.SlotCount; index++)
                if (profile.Slots.TryGetValue(index, out var saved) &&
                    ClothesJuiceParts.TryGet(character, index, out _, out var loaded) && loaded == saved)
                    return index;
            return -1;
        }

        private static int FindSlot(ChaControl character, int current, int direction)
        {
            const int count = ClothesJuiceParts.SlotCount;
            if (current < 0)
                current = direction == 1 ? count - 1 : 0;
            for (var offset = 1; offset <= count; offset++)
            {
                var index = (current + direction * offset + count) % count;
                if (ClothesJuiceParts.TryGet(character, index, out _, out _))
                    return index;
            }
            return -1;
        }

        private void MoveSlot(int direction)
        {
            if (_character == null)
                return;
            _slotIndex = FindSlot(_character.charInfo, _slotIndex, direction);
            Refresh();
        }

        private void ToggleSlot()
        {
            var feature = BodyCumPlugin.HairJuices;
            if (_character == null || feature == null || !ClothesJuiceParts.TryGet(_character.charInfo, _slotIndex, out _, out var identity))
                return;
            var slots = feature.EditClothes(_character).Slots;
            if (slots.TryGetValue(_slotIndex, out var selected) && selected == identity)
                slots.Remove(_slotIndex);
            else
                slots[_slotIndex] = identity;
            Refresh();
        }

        private void SetLevel(int level)
        {
            var feature = BodyCumPlugin.HairJuices;
            if (_character == null || feature == null)
                return;
            feature.EditClothes(_character).Level = level;
            Refresh();
        }

        private void SetHorizontal(float value)
        {
            var feature = BodyCumPlugin.HairJuices;
            if (_refreshing || _character == null || feature == null)
                return;
            var offset = Mathf.RoundToInt(value);
            feature.EditClothes(_character).Horizontal = offset;
            _horizontalValue.text = offset.ToString();
        }

        private void SetVertical(float value)
        {
            var feature = BodyCumPlugin.HairJuices;
            if (_refreshing || _character == null || feature == null)
                return;
            var offset = Mathf.RoundToInt(value);
            feature.EditClothes(_character).Vertical = offset;
            _verticalValue.text = offset.ToString();
        }

        private void SetSize(float value)
        {
            var feature = BodyCumPlugin.HairJuices;
            if (_refreshing || _character == null || feature == null)
                return;
            var size = Mathf.RoundToInt(value);
            feature.EditClothes(_character).Size = size;
            _sizeValue.text = size + "%";
        }
    }
}
