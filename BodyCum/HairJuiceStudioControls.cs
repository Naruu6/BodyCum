using System;
using System.Globalization;
using Studio;
using UnityEngine;
using UnityEngine.UI;

namespace BodyCum
{
    internal sealed class HairJuiceStudioControls
    {
        internal const float Height = 5 * JuiceColorStudioControls.RowHeight;

        private readonly JuiceColorStudioControls _ui;
        private readonly bool _regularHair;
        private readonly Button[] _levelButtons = new Button[3];
        private readonly Text[] _levelLabels = new Text[3];
        private readonly Button _previous;
        private readonly Button _slot;
        private readonly Button _next;
        private readonly Text _slotLabel;
        private readonly Slider _horizontal;
        private readonly Slider _vertical;
        private readonly Slider _size;
        private readonly Text _horizontalValue;
        private readonly Text _verticalValue;
        private readonly Text _sizeValue;
        private OCIChar _character;
        private int _slotIndex = -1;
        private bool _refreshing;

        internal HairJuiceStudioControls(JuiceColorStudioControls ui, Transform parent, float y, bool regularHair)
        {
            _ui = ui;
            _regularHair = regularHair;
            var title = regularHair ? "Hair" : "Accessory";
            var prefix = regularHair ? "Hair" : "Acc.";
            var container = new GameObject("BodyCum" + title + "Controls", typeof(RectTransform));
            var panel = (RectTransform)container.transform;
            panel.SetParent(parent, false);
            JuiceColorStudioControls.SetRect(panel, 0f, y, 210f, Height);

            ui.Label(panel, title, 0f, 0f, 85f);
            var tiers = new[] { "OFF", "x1", "x2" };
            for (var level = 0; level < tiers.Length; level++)
            {
                var tier = level;
                var button = ui.PushButton(panel, title + " " + tiers[level], 88f + level * 40f, 0f, 38f, () => SetLevel(tier));
                _levelButtons[level] = button;
                _levelLabels[level] = LabelButton(ui, button, tiers[level], 0f, 38f);
            }

            const float slotY = JuiceColorStudioControls.RowHeight;
            ui.Label(panel, regularHair ? "Hair Slot" : "Acc Slot", 0f, slotY, 58f);
            _previous = ui.PushButton(panel, "Previous hair slot", 59f, slotY, 22f, () => MoveSlot(-1));
            LabelButton(ui, _previous, "<", 0f, 22f);
            _slot = ui.PushButton(panel, "Toggle hair slot", 83f, slotY, 99f, ToggleSlot);
            _slotLabel = LabelButton(ui, _slot, "", 2f, 95f);
            _slotLabel.resizeTextForBestFit = false;
            _slotLabel.supportRichText = false;
            _slotLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            _slotLabel.verticalOverflow = VerticalWrapMode.Truncate;
            _next = ui.PushButton(panel, "Next hair slot", 184f, slotY, 22f, () => MoveSlot(1));
            LabelButton(ui, _next, ">", 0f, 22f);

            _horizontal = ui.SliderRow(panel, prefix + " X", 2, -100, 100, SetHorizontal, out _horizontalValue);
            _vertical = ui.SliderRow(panel, prefix + " Y", 3, -100, 100, SetVertical, out _verticalValue);
            _size = ui.SliderRow(panel, prefix + " Size", 4, HairJuiceProfile.MinimumSize, HairJuiceProfile.MaximumSize, SetSize, out _sizeValue);

        }

        internal static Text LabelButton(JuiceColorStudioControls ui, Button button, string value, float x, float width)
        {
            button.targetGraphic.color = Color.white;
            button.transition = Selectable.Transition.ColorTint;
            SetButtonColors(button, false);
            var label = ui.Label(button.transform, value, x, 0f, width, TextAnchor.MiddleCenter);
            label.color = Color.white;
            return label;
        }

        internal static void SetButtonColors(Button button, bool selected)
        {
            var colors = button.colors;
            colors.normalColor = selected ? new Color32(103, 103, 103, 255) : new Color32(71, 71, 71, 255);
            colors.highlightedColor = selected ? new Color32(113, 113, 113, 255) : new Color32(89, 89, 89, 255);
            colors.pressedColor = new Color32(55, 55, 55, 255);
            colors.disabledColor = new Color32(60, 60, 60, 255);
            colors.colorMultiplier = 1f;
            button.colors = colors;
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
            if (_character == null || BodyCumPlugin.HairJuices == null)
                return;

            var profile = BodyCumPlugin.HairJuices.GetHairProfile(_character, _regularHair);
            var character = _character.charInfo;
            GameObject root;
            string identity;
            if (!TryGetSlot(character, _slotIndex, out root, out identity))
            {
                _slotIndex = FirstSelectedSlot(character, profile);
                if (_slotIndex < 0)
                    _slotIndex = FindSlot(character, -1, 1);
                TryGetSlot(character, _slotIndex, out root, out identity);
            }

            var hasSlot = _slotIndex >= 0 && root && identity != null;
            string selectedAsset;
            var hasSelection = hasSlot && profile != null && profile.Slots.TryGetValue(_slotIndex, out selectedAsset) && selectedAsset == identity;
            _slot.interactable = hasSlot;
            _previous.interactable = _next.interactable = hasSlot;
            SetButtonColors(_slot, hasSelection);
            if (hasSlot)
            {
                var name = _regularHair ? RegularHairJuiceParts.GetLabel(character, _slotIndex, root) :
                    HairJuiceFeature.GetAccessoryLabel(character, _slotIndex, root);
                SetSlotLabel((hasSelection ? "[ON] " : "[  ] ") + name);
            }
            else
            {
                SetSlotLabel(_regularHair ? "No hair" : "No accessory");
            }

            var level = profile?.Level ?? 0;
            for (var index = 0; index < _levelLabels.Length; index++)
            {
                var selected = index == level;
                SetButtonColors(_levelButtons[index], selected);
                _levelLabels[index].fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
            }

            _refreshing = true;
            try
            {
                _horizontal.value = profile?.Horizontal ?? 0;
                _vertical.value = profile?.Vertical ?? 0;
                _size.value = profile?.Size ?? HairJuiceProfile.DefaultSize;
                _horizontalValue.text = ((int)_horizontal.value).ToString();
                _verticalValue.text = ((int)_vertical.value).ToString();
                _sizeValue.text = ((int)_size.value) + "%";
            }
            finally { _refreshing = false; }
            _ui.RefreshAllSlots();
        }

        private void SetSlotLabel(string value)
        {
            value = value.Replace('\r', ' ').Replace('\n', ' ');
            var generator = _slotLabel.cachedTextGeneratorForLayout;
            var settings = _slotLabel.GetGenerationSettings(Vector2.zero);
            var limit = _slotLabel.rectTransform.rect.width * _slotLabel.pixelsPerUnit;
            if (generator.GetPreferredWidth(value, settings) > limit)
            {
                var starts = StringInfo.ParseCombiningCharacters(value);
                for (var i = starts.Length - 1; i >= 0; i--)
                {
                    var shortened = value.Substring(0, starts[i]) + "…";
                    if (generator.GetPreferredWidth(shortened, settings) > limit)
                        continue;
                    value = shortened;
                    break;
                }
            }
            _slotLabel.text = value;
        }

        private bool TryGetSlot(ChaControl character, int slot, out GameObject root, out string identity) =>
            HairJuiceFeature.TryGetHairSlot(character, slot, _regularHair, out root, out identity);

        private int FirstSelectedSlot(ChaControl character, HairJuiceProfile profile)
        {
            if (profile != null)
            {
                var found = int.MaxValue;
                foreach (var entry in profile.Slots)
                    if (entry.Key < found && TryGetSlot(character, entry.Key, out _, out var identity) && identity == entry.Value)
                        found = entry.Key;
                return found == int.MaxValue ? -1 : found;
            }
            return -1;
        }

        private int FindSlot(ChaControl character, int current, int direction)
        {
            var count = character ? (_regularHair ? character.objHair : character.objAccessory)?.Length ?? 0 : 0;
            if (current < 0 || current >= count)
                current = direction == 1 ? count - 1 : 0;
            for (var offset = 1; offset <= count; offset++)
            {
                var index = (current + direction * offset + count) % count;
                if (TryGetSlot(character, index, out _, out _))
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
            if (_character == null || !TryGetSlot(_character.charInfo, _slotIndex, out _, out var identity))
                return;
            var slots = BodyCumPlugin.HairJuices.EditHairProfile(_character, _regularHair).Slots;
            if (slots.TryGetValue(_slotIndex, out var selected) && selected == identity)
                slots.Remove(_slotIndex);
            else
                slots[_slotIndex] = identity;
            Refresh();
        }

        private void SetLevel(int value)
        {
            if (_character == null)
                return;
            BodyCumPlugin.HairJuices.EditHairProfile(_character, _regularHair).Level = value;
            Refresh();
        }

        private void SetHorizontal(float value)
        {
            if (_refreshing || _character == null)
                return;
            var offset = Mathf.RoundToInt(value);
            BodyCumPlugin.HairJuices.EditHairProfile(_character, _regularHair).Horizontal = offset;
            _horizontalValue.text = offset.ToString();
        }

        private void SetVertical(float value)
        {
            if (_refreshing || _character == null)
                return;
            var offset = Mathf.RoundToInt(value);
            BodyCumPlugin.HairJuices.EditHairProfile(_character, _regularHair).Vertical = offset;
            _verticalValue.text = offset.ToString();
        }

        private void SetSize(float value)
        {
            if (_refreshing || _character == null)
                return;
            var size = Mathf.RoundToInt(value);
            BodyCumPlugin.HairJuices.EditHairProfile(_character, _regularHair).Size = size;
            _sizeValue.text = size + "%";
        }
    }
}
