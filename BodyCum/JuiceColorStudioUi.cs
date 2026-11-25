using System;
using HarmonyLib;
using Studio;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using KoikatsuStudio = Studio.Studio;

namespace BodyCum
{
    [HarmonyPatch(typeof(MPCharCtrl.LiquidInfo), nameof(MPCharCtrl.LiquidInfo.UpdateInfo))]
    internal static class JuiceColorStudioUi
    {
        internal static void Install() => Harmony.CreateAndPatchAll(typeof(JuiceColorStudioUi), BodyCumPlugin.GUID + ".JuiceColorUi");

        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void UpdateControls(MPCharCtrl.LiquidInfo __instance, OCIChar __0)
        {
            var section = __instance.objOpen;
            if (!section || __0 == null)
                return;

            var controls = section.GetComponent<JuiceColorStudioControls>();
            if (!controls)
            {
                var text = section.transform.Find("Text Face")?.GetComponent<Text>();
                var slider = section.transform.parent.Find("Etc/Nipple/Slider Nipple")?.GetComponent<Slider>();
                var button = section.transform.parent.Find("Etc/Color/Button")?.GetComponent<Button>();
                var layout = section.GetComponent<LayoutElement>();
                if (!text || !slider || !button || !layout)
                {
                    BodyCumPlugin.Log.LogDebug("Could not find the native Studio Juices control templates.");
                    return;
                }
                controls = section.AddComponent<JuiceColorStudioControls>();
                controls.Build(text, slider, button, layout);
            }
            controls.Select(__0);
        }
    }

    internal sealed class JuiceColorStudioControls : MonoBehaviour
    {
        private const string PaletteTitle = "Juice Color";
        internal const float RowHeight = 24f;
        private const float ColorPanelHeight = 6 * RowHeight;
        private const float AttachmentPanelStart = ColorPanelHeight + RowHeight;
        private const float PanelHeight = AttachmentPanelStart + HairJuiceStudioControls.Height + ClothesJuiceStudioControls.Height + 4f;

        private OCIChar _character;
        private RectTransform _panel;
        private Text _textTemplate;
        private Button _buttonTemplate;
        private Slider _sliderTemplate;
        private CanvasGroup _settingsGroup;
        private Image _powerIndicator;
        private Sprite _powerSprite;
        private Text _powerState;
        private bool? _displayedPower;
        private Image _swatch;
        private Text _hex;
        private Slider _opacity;
        private Slider _density;
        private Slider _brightness;
        private Text _opacityValue;
        private Text _densityValue;
        private Text _brightnessValue;
        private readonly Button[] _bellyButtons = new Button[3];
        private readonly Text[] _bellyLabels = new Text[3];
        private readonly Button[] _tongueButtons = new Button[3];
        private readonly Text[] _tongueLabels = new Text[3];
        private Button _allSlots;
        private Text _allSlotsText;
        private HairJuiceStudioControls _hair;
        private ClothesJuiceStudioControls _clothes;
        private bool _refreshing;

        internal void Build(Text text, Slider slider, Button button, LayoutElement layout)
        {
            _textTemplate = text;
            _sliderTemplate = slider;
            _buttonTemplate = button;
            var baseHeight = layout.preferredHeight;
            BuildPowerButton(baseHeight + 4f);
            var panelObject = new GameObject("SkinEffectsJuiceControls", typeof(RectTransform), typeof(CanvasGroup));
            _settingsGroup = panelObject.GetComponent<CanvasGroup>();
            _panel = (RectTransform)panelObject.transform;
            _panel.SetParent(transform, false);
            SetRect(_panel, 0f, baseHeight + 4f + RowHeight, 210f, PanelHeight);
            layout.preferredHeight = baseHeight + RowHeight + PanelHeight + 4f;

            BuildTierRow("Belly", 0, SetBelly, _bellyButtons, _bellyLabels);
            BuildTierRow("Tongue", 1, SetTongue, _tongueButtons, _tongueLabels);

            Label(_panel, "Juice color", 0f, 2 * RowHeight, 88f);
            var colorButton = PushButton(_panel, "Juice Color", 90f, 2 * RowHeight, 116f, OpenPalette);
            var swatchObject = new GameObject("Swatch", typeof(RectTransform), typeof(Image));
            _swatch = swatchObject.GetComponent<Image>();
            _swatch.raycastTarget = false;
            var swatchRect = (RectTransform)swatchObject.transform;
            swatchRect.SetParent(colorButton.transform, false);
            SetRect(swatchRect, 3f, 2f, 110f, 16f);
            _hex = Label(colorButton.transform, "", 0f, 0f, 116f, TextAnchor.MiddleCenter);

            _opacity = SliderRow(_panel, "Opacity", 3, 0, 100, SetOpacity, out _opacityValue);
            _density = SliderRow(_panel, "Density", 4, 0, 100, SetDensity, out _densityValue);
            _brightness = SliderRow(_panel, "Brightness", 5, 0, 200, SetBrightness, out _brightnessValue);

            Label(_panel, "All slots", 0f, ColorPanelHeight, 85f);
            _allSlots = PushButton(_panel, "Toggle all accessory and clothes slots", 90f, ColorPanelHeight, 116f, ToggleAllSlots);
            _allSlotsText = HairJuiceStudioControls.LabelButton(this, _allSlots, "All ON", 0f, 116f);
            _hair = new HairJuiceStudioControls(this, _panel, AttachmentPanelStart);
            _clothes = new ClothesJuiceStudioControls(this, _panel, AttachmentPanelStart + HairJuiceStudioControls.Height);
            RefreshPower();
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform.parent);
        }

        private void BuildTierRow(string name, int row, Action<byte> setLevel, Button[] buttons, Text[] labels)
        {
            var y = row * RowHeight;
            Label(_panel, name, 0f, y, 85f);
            var tiers = new[] { "OFF", "x1", "x2" };
            for (byte level = 0; level < tiers.Length; level++)
            {
                var chosen = level;
                var button = PushButton(_panel, name + " " + tiers[level], 88f + level * 40f, y, 38f, () => setLevel(chosen));
                buttons[level] = button;
                labels[level] = HairJuiceStudioControls.LabelButton(this, button, tiers[level], 0f, 38f);
            }
        }

        private void BuildPowerButton(float y)
        {
            var buttonObject = new GameObject("BodyCum Enable", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(transform, false);
            SetRect((RectTransform)buttonObject.transform, 0f, y, 156f, 20f);
            buttonObject.GetComponent<Image>().color = Color.clear;

            var label = Label(buttonObject.transform, "BodyCum", 0f, 0f, 84f);
            label.fontStyle = FontStyle.Bold;
            label.color = Color.white;

            var sourceToggle = transform.parent.Find("Etc/Son")?.GetComponentInChildren<Toggle>(true);
            var sourceOn = sourceToggle ? sourceToggle.graphic as Image : null;
            var sourceBackground = sourceToggle ? sourceToggle.targetGraphic as Image : null;
            Image background = null;
            if (sourceOn && sourceOn.overrideSprite && sourceBackground && sourceBackground.overrideSprite)
            {
                // Use the Studio switch artwork without cloning the native toggle or its callbacks.
                background = AddPowerImage(buttonObject.transform, "Indicator Background");
                background.sprite = sourceBackground.overrideSprite;
                background.color = sourceBackground.color;
                _powerIndicator = AddPowerImage(buttonObject.transform, "Indicator");
                _powerIndicator.sprite = sourceOn.overrideSprite;
                _powerIndicator.color = sourceOn.color;
            }
            else
            {
                _powerIndicator = AddPowerImage(buttonObject.transform, "Indicator");
                _powerSprite = CreatePowerSprite();
                _powerIndicator.sprite = _powerSprite;
            }

            _powerState = Label(buttonObject.transform, "", 112f, 0f, 44f);
            _powerState.name = "Power State";
            _powerState.color = Color.white;
            var powerButton = buttonObject.GetComponent<Button>();
            powerButton.targetGraphic = background ? background : _powerIndicator;
            powerButton.onClick.AddListener(() =>
            {
                if (_character == null)
                    return;
                BodyCumPlugin.SetCharacterEnabled(_character, !BodyCumPlugin.IsCharacterEnabled(_character));
                RefreshPower();
            });
        }

        private static Image AddPowerImage(Transform parent, string name)
        {
            var indicator = new GameObject(name, typeof(RectTransform), typeof(Image));
            indicator.transform.SetParent(parent, false);
            SetRect((RectTransform)indicator.transform, 88f, 1f, 18f, 18f);
            var image = indicator.GetComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            return image;
        }

        private static Sprite CreatePowerSprite()
        {
            const int size = 32;
            var pixels = new Color32[size * size];
            var highlightCenter = new Vector2(-.24f, .28f);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var point = new Vector2((x + .5f) / size, (y + .5f) / size) * 2f - Vector2.one;
                var radius = point.magnitude;
                var reflection = Mathf.Clamp01(1f - (point - highlightCenter).sqrMagnitude * 10f);
                var shade = Mathf.Clamp01(1f - .45f * radius + .28f * reflection);
                var alpha = Mathf.Clamp01((.99f - radius) * 16f);
                var gray = (byte)Mathf.RoundToInt(shade * 255f);
                pixels[y * size + x] = new Color32(gray, gray, gray, (byte)Mathf.RoundToInt(alpha * 255f));
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), Vector2.one * .5f);
        }

        private void RefreshPower()
        {
            var enabled = BodyCumPlugin.IsCharacterEnabled(_character);
            if (!_powerIndicator || _displayedPower == enabled)
                return;

            _displayedPower = enabled;
            if (_powerSprite)
                _powerIndicator.color = enabled ? new Color32(52, 240, 100, 255) : new Color32(143, 149, 154, 255);
            else
                _powerIndicator.enabled = enabled;
            _powerState.text = enabled ? "ON" : "OFF";
            _settingsGroup.alpha = enabled ? 1f : .6f;
            _settingsGroup.interactable = enabled;
            _settingsGroup.blocksRaycasts = enabled;
            if (!enabled)
                ClosePalette();
        }

        internal void Select(OCIChar character)
        {
            if (!ReferenceEquals(_character, character))
                ClosePalette();
            _character = character;
            RefreshPower();
            _hair?.Select(character);
            _clothes?.Select(character);
            Refresh();
        }

        private void OnEnable()
        {
            RefreshPower();
            Refresh();
            _hair?.Select(_character);
            _clothes?.Select(_character);
        }
        private void Update() => RefreshPower();
        private void OnDisable() => ClosePalette();

        private void OnDestroy()
        {
            if (!_powerSprite)
                return;
            Destroy(_powerSprite.texture);
            Destroy(_powerSprite);
        }

        private void Refresh()
        {
            if (!_panel || _character == null || !_hex || BodyCumPlugin.JuiceColors == null)
                return;
            var colors = BodyCumPlugin.JuiceColors;
            var settings = colors.GetSettings(_character);
            var color = settings.Color;
            _refreshing = true;
            try
            {
                _swatch.color = color;
                _hex.text = $"#{color.r:X2}{color.g:X2}{color.b:X2}";
                _hex.color = (color.r * .299f + color.g * .587f + color.b * .114f) > 150f ? Color.black : Color.white;
                _opacity.value = settings.Opacity;
                _opacityValue.text = settings.Opacity + "%";
                _density.value = settings.Density;
                _densityValue.text = settings.Density + "%";
                _brightness.value = settings.Brightness;
                _brightnessValue.text = settings.Brightness + "%";
                var body = _character.charInfo;
                RefreshTierRow(_bellyButtons, _bellyLabels, colors.GetBellyLevel(_character), body && body.hiPoly && body.sex == 1);
                RefreshTierRow(_tongueButtons, _tongueLabels, colors.GetTongueLevel(_character), body && body.hiPoly);
                RefreshAllSlots();
            }
            finally
            {
                _refreshing = false;
            }
        }

        internal void RefreshAllSlots()
        {
            if (!_allSlots)
                return;
            var feature = BodyCumPlugin.HairJuices;
            var selected = false;
            _allSlots.interactable = feature != null && feature.TryGetAllSlotSelection(_character, out selected);
            _allSlotsText.text = selected ? "All OFF" : "All ON";
            HairJuiceStudioControls.SetButtonColors(_allSlots, selected);
        }

        private void ToggleAllSlots()
        {
            var feature = BodyCumPlugin.HairJuices;
            if (feature == null || !feature.TryGetAllSlotSelection(_character, out var selected))
                return;
            feature.SetAllSlots(_character, !selected);
            _hair.Select(_character);
            _clothes.Select(_character);
            RefreshAllSlots();
        }

        private static void RefreshTierRow(Button[] buttons, Text[] labels, byte selected, bool interactable)
        {
            for (var level = 0; level < buttons.Length; level++)
            {
                var active = level == selected;
                buttons[level].interactable = interactable;
                HairJuiceStudioControls.SetButtonColors(buttons[level], active);
                labels[level].fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        private void OpenPalette()
        {
            if (_character == null)
                return;
            var target = _character;
            var palette = KoikatsuStudio.Instance.colorPalette;
            if (!palette)
                return;
            if (palette.isOpen && palette.Check(PaletteTitle))
            {
                ClosePalette();
                return;
            }

            // The native close button can hide the canvas without clearing its reactive flag.
            if (palette.visible && !palette.isOpen)
                palette.visible = false;

            palette.Setup(PaletteTitle, BodyCumPlugin.JuiceColors.GetSettings(target).Color, color =>
            {
                if (!ReferenceEquals(_character, target))
                    return;
                var settings = BodyCumPlugin.JuiceColors.GetSettings(target);
                settings.Color = new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f), 255);
                Apply(settings);
            }, false);
        }

        private void ClosePalette()
        {
            if (!Singleton<KoikatsuStudio>.IsInstance())
                return;
            var palette = KoikatsuStudio.Instance.colorPalette;
            if (!palette || !palette.Check(PaletteTitle))
                return;
            palette.visible = false;
            if (palette.Check(PaletteTitle))
                palette.Close();
        }

        private void SetOpacity(float value)
        {
            if (_refreshing || _character == null)
                return;
            var settings = BodyCumPlugin.JuiceColors.GetSettings(_character);
            settings.Opacity = Mathf.RoundToInt(value);
            Apply(settings);
        }

        private void SetBelly(byte level)
        {
            if (_character == null)
                return;
            BodyCumPlugin.JuiceColors.SetBellyLevel(_character, level);
            Refresh();
        }

        private void SetTongue(byte level)
        {
            if (_character == null)
                return;
            BodyCumPlugin.JuiceColors.SetTongueLevel(_character, level);
            Refresh();
        }

        private void SetBrightness(float value)
        {
            if (_refreshing || _character == null)
                return;
            var settings = BodyCumPlugin.JuiceColors.GetSettings(_character);
            settings.Brightness = Mathf.RoundToInt(value);
            Apply(settings);
        }

        private void SetDensity(float value)
        {
            if (_refreshing || _character == null)
                return;
            var settings = BodyCumPlugin.JuiceColors.GetSettings(_character);
            settings.Density = Mathf.RoundToInt(value);
            Apply(settings);
        }

        private void Apply(JuiceColorSettings settings)
        {
            BodyCumPlugin.JuiceColors.SetOverride(_character, settings);
            Refresh();
        }

        internal Slider SliderRow(Transform parent, string label, int row, int min, int max, UnityAction<float> callback, out Text value)
        {
            var y = row * RowHeight;
            Label(parent, label, 0f, y, 77f);
            var slider = Instantiate(_sliderTemplate, parent, false);
            slider.name = label;
            slider.onValueChanged = new Slider.SliderEvent();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = true;
            slider.onValueChanged.AddListener(callback);
            SetRect((RectTransform)slider.transform, 77f, y, 96f, 20f);
            slider.gameObject.SetActive(true);
            value = Label(parent, "", 176f, y, 30f, TextAnchor.MiddleRight);
            return slider;
        }

        internal Button PushButton(Transform parent, string name, float x, float y, float width, UnityAction callback)
        {
            var button = Instantiate(_buttonTemplate, parent, false);
            button.name = name;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(callback);
            SetRect((RectTransform)button.transform, x, y, width, 20f);
            button.gameObject.SetActive(true);
            return button;
        }

        internal Text Label(Transform parent, string value, float x, float y, float width, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var label = Instantiate(_textTemplate, parent, false);
            label.name = value.Length > 0 ? value : "Value";
            label.text = value;
            label.alignment = alignment;
            label.raycastTarget = false;
            SetRect((RectTransform)label.transform, x, y, width, 20f);
            label.gameObject.SetActive(true);
            return label;
        }

        internal static void SetRect(RectTransform rect, float x, float y, float width, float height)
        {
            var topLeft = new Vector2(0f, 1f);
            rect.anchorMin = topLeft;
            rect.anchorMax = topLeft;
            rect.pivot = topLeft;
            rect.localScale = Vector3.one;
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
