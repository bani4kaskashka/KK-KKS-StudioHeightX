using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using KKAPI;
using KKAPI.Studio;
using Studio;
using UnityEngine;

namespace StudioHeight
{
    [BepInProcess("CharaStudio")]
    [BepInDependency(KoikatuAPI.GUID, "1.15")]
    [BepInPlugin(GUID, PluginName, Version)]
    public class StudioHeightPlugin : BaseUnityPlugin
    {
        public const string GUID = "bucky.kk.studioheight";
#if KKS
        public const string PluginName = "KKS_StudioHeight";
#else
        public const string PluginName = "KK_StudioHeight";
#endif
        public const string Version = "1.0.2";

        // Same calibration constant HeightBarX uses to turn objRoot-local Y units into cm
        private const float CmPerUnit = 103.09278f;

        public enum HeightUnits
        {
            Centimeters,
            FeetInches,
        }

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<KeyboardShortcut> _toggleHotkey;
        private ConfigEntry<HeightUnits> _units;
        private ConfigEntry<int> _fontSize;
        private ConfigEntry<float> _overlayX;
        private ConfigEntry<float> _overlayY;

        private readonly GUIStyle _labelStyle = new GUIStyle();
        private string _labelText;
        private OCIChar _currentChar;

        private ChaControl _cachedControl;
        private Renderer[] _headRenderers;
        private float _nextRendererRefresh;

        private bool _dragging;
        private Vector2 _dragOffset;
        private Vector2 _dragPosition;

        private bool _forceHide;
        private bool _warnedCalcFailed;

        private static readonly Vector3[] BoundsCorners =
        {
            new Vector3(1, 1, 1), new Vector3(1, 1, -1), new Vector3(1, -1, 1), new Vector3(1, -1, -1),
            new Vector3(-1, 1, 1), new Vector3(-1, 1, -1), new Vector3(-1, -1, 1), new Vector3(-1, -1, -1),
        };

        private void Awake()
        {
            _enabled = Config.Bind("General", "Show height overlay", true,
                "Show name and height of the character selected in the Workspace. " +
                "Height is measured from the character's root (at the feet) to the top of the head mesh in the current pose, " +
                "using the same cm conversion as HeightBarX. " +
                "Warning: if ABMX bone edits move or scale the feet/legs, the root origin no longer matches the soles and the number will be off.");
            _toggleHotkey = Config.Bind("General", "Toggle overlay hotkey", KeyboardShortcut.Empty,
                "Hotkey to toggle the height overlay.");
            _units = Config.Bind("General", "Units", HeightUnits.Centimeters,
                "Unit system used to display heights.");
            _fontSize = Config.Bind("Appearance", "Font size", 20,
                new ConfigDescription("Size of the overlay text.", new AcceptableValueRange<int>(10, 40)));
            _overlayX = Config.Bind("Appearance", "Overlay position X", 0.5f,
                new ConfigDescription("Horizontal position as a fraction of screen width. The overlay can also be dragged with the mouse.",
                    new AcceptableValueRange<float>(0f, 1f)));
            _overlayY = Config.Bind("Appearance", "Overlay position Y", 0.97f,
                new ConfigDescription("Vertical position as a fraction of screen height. The overlay can also be dragged with the mouse.",
                    new AcceptableValueRange<float>(0f, 1f)));

            // v1.0.0 shipped a bottom-left default that sat on top of Studio's button column;
            // configs still parked exactly there get moved to the current default once
            if (Mathf.Approximately(_overlayX.Value, 0.01f) && Mathf.Approximately(_overlayY.Value, 0.90f))
            {
                _overlayX.Value = (float)_overlayX.DefaultValue;
                _overlayY.Value = (float)_overlayY.DefaultValue;
            }

            _labelStyle.normal.textColor = Color.white;

            // Hide the overlay while Screencap takes a screenshot, same trick HeightBarX uses
            var screencap = Type.GetType("Screencap.ScreenshotManager, Screencap");
            if (screencap != null)
            {
                screencap.GetEvent("OnPreCapture", BindingFlags.Static | BindingFlags.Public)
                    ?.AddEventHandler(null, (Action)(() => _forceHide = true));
                screencap.GetEvent("OnPostCapture", BindingFlags.Static | BindingFlags.Public)
                    ?.AddEventHandler(null, (Action)(() => _forceHide = false));
            }
        }

        private void Update()
        {
            if (_toggleHotkey.Value.IsDown())
                _enabled.Value = !_enabled.Value;

            _currentChar = null;
            _labelText = null;

            if (!_enabled.Value || _forceHide || !StudioAPI.StudioLoaded)
                return;

            try
            {
                var oci = StudioAPI.GetSelectedCharacters().FirstOrDefault();
                if (oci == null || oci.charInfo == null || oci.charInfo.objRoot == null)
                    return;

                if (!TryCalculateHeight(oci.charInfo, out var baseCm, out var scaleY))
                    return;

                var scaledCm = baseCm * scaleY;
                var name = GetCharaName(oci);

                _labelText = Mathf.Abs(scaleY - 1f) < 0.001f
                    ? $"{name} — {FormatLength(baseCm)}"
                    : $"{name} — Base {FormatLength(baseCm)} | Scaled {FormatLength(scaledCm)} ({scaleY * 100f:F0}%)";
                _currentChar = oci;
                _warnedCalcFailed = false;
            }
            catch (Exception ex)
            {
                if (!_warnedCalcFailed)
                {
                    _warnedCalcFailed = true;
                    Logger.LogWarning("Failed to calculate character height: " + ex);
                }
            }
        }

        /// <summary>
        /// HeightBarX math: height = Y distance from objRoot (floor between the feet) to the measuring
        /// point, taken in objRoot-local space, times 103.09278 cm per unit. HeightBarX gets its measuring
        /// point from a bar the user aligns by hand in maker; here the game's a_n_headtop node (the
        /// head-top accessory attach point, sitting on the skull apex and tracking head sliders) stands in
        /// for that bar. Local space makes the result independent of Studio's character scaling;
        /// multiplying by the root's lossy Y scale gives the scaled world height back.
        /// </summary>
        private bool TryCalculateHeight(ChaControl chara, out float baseCm, out float scaleY)
        {
            baseCm = 0f;
            var root = chara.objRoot.transform;
            scaleY = root.lossyScale.y;
            if (scaleY <= 0f)
                return false;

            var headTop = chara.GetReferenceInfo(ChaReference.RefObjKey.a_n_headtop);
            if (headTop != null)
            {
                baseCm = root.InverseTransformPoint(headTop.transform.position).y * CmPerUnit;
                return baseCm > 0f;
            }

            // Fallback: highest point of the head renderers. SkinnedMeshRenderer bounds are padded,
            // so this reads several cm too tall — better than nothing if the attach node is missing.
            if (chara != _cachedControl || Time.unscaledTime >= _nextRendererRefresh)
            {
                _headRenderers = chara.objHead != null
                    ? chara.objHead.GetComponentsInChildren<Renderer>(false)
                    : new Renderer[0];
                _cachedControl = chara;
                _nextRendererRefresh = Time.unscaledTime + 1f;
            }

            var maxLocalY = float.MinValue;
            foreach (var renderer in _headRenderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                var bounds = renderer.bounds;
                foreach (var corner in BoundsCorners)
                {
                    var world = bounds.center + Vector3.Scale(bounds.extents, corner);
                    var localY = root.InverseTransformPoint(world).y;
                    if (localY > maxLocalY) maxLocalY = localY;
                }
            }

            if (maxLocalY <= float.MinValue)
                return false;

            baseCm = maxLocalY * CmPerUnit;
            return baseCm > 0f;
        }

        private static string GetCharaName(OCIChar oci)
        {
            var name = oci.charInfo.chaFile?.parameter?.fullname;
            if (string.IsNullOrEmpty(name))
                name = oci.treeNodeObject != null ? oci.treeNodeObject.textName : "Unknown";
            return name.Trim();
        }

        private string FormatLength(float cm)
        {
            if (_units.Value == HeightUnits.FeetInches)
                return $"{Mathf.FloorToInt(cm * 0.0328084f)}'{cm * 0.3937007f % 12f:F1}\"";
            return cm.ToString("F1") + " cm";
        }

        private void OnGUI()
        {
            if (_labelText == null || _currentChar == null)
                return;

            _labelStyle.fontSize = _fontSize.Value;
            var content = new GUIContent(_labelText);
            var size = _labelStyle.CalcSize(content);

            var pos = _dragging
                ? _dragPosition
                : new Vector2(_overlayX.Value * (Screen.width - size.x), _overlayY.Value * (Screen.height - size.y));
            var rect = new Rect(pos.x, pos.y, size.x, size.y);

            HandleDrag(rect, size);
            if (_dragging)
                rect.position = _dragPosition;

            DrawOutlinedLabel(rect, content);
        }

        private void HandleDrag(Rect rect, Vector2 size)
        {
            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && rect.Contains(e.mousePosition))
                    {
                        _dragging = true;
                        _dragOffset = e.mousePosition - rect.position;
                        _dragPosition = rect.position;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (_dragging)
                    {
                        _dragPosition = e.mousePosition - _dragOffset;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (_dragging)
                    {
                        _dragging = false;
                        // Committing on release avoids a config file write per mouse move
                        _overlayX.Value = Mathf.Clamp01(_dragPosition.x / Mathf.Max(1f, Screen.width - size.x));
                        _overlayY.Value = Mathf.Clamp01(_dragPosition.y / Mathf.Max(1f, Screen.height - size.y));
                        e.Use();
                    }
                    break;
            }
        }

        private void DrawOutlinedLabel(Rect rect, GUIContent content)
        {
            var oldColor = _labelStyle.normal.textColor;
            _labelStyle.normal.textColor = Color.black;
            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    if (x == 0 && y == 0) continue;
                    GUI.Label(new Rect(rect.x + x, rect.y + y, rect.width, rect.height), content, _labelStyle);
                }
            }
            _labelStyle.normal.textColor = oldColor;
            GUI.Label(rect, content, _labelStyle);
        }
    }
}
