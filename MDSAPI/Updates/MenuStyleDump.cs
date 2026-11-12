using System;
using System.Text;
using MDSAPI.Internal.Interop;
using MDSAPI.Logging;
using UnityEngine;
using UnityEngine.UI;
#if MDS_IL2CPP
using Il2CppTMPro;
#else
using TMPro;
#endif

namespace MDSAPI.Updates
{
    /// <summary>
    /// Development aid: writes the main menu's own screens to the log — every object's size,
    /// and for each image, text, button and layout the exact font, colour, sprite and spacing.
    /// MDSAPI's own UI is then built from these numbers instead of from taste. Off unless
    /// "LogMenuStyle": true in UserData/MDSAPI.json; runs once per launch.
    /// </summary>
    internal static class MenuStyleDump
    {
        private static bool _done;
        private static int _lines;
        private const int MaxLinesPerScreen = 160;

        private static readonly string[] Screens =
        {
            "UI.MainMenu.ConfirmExitScreen",
            "UI.MainMenu.MainMenuPopup",
            "UI.MainMenu.ContinueScreen",
            "UI.MainMenu.SettingsScreen",
            "UI.MainMenu.MainMenuController",
        };

        internal static void Run()
        {
            if (_done) return;
            _done = true;
            try
            {
                foreach (var name in Screens)
                {
                    var type = GameTypes.Find(name);
                    foreach (var o in UnityFind.Everything(type))
                    {
                        var c = UnityFind.AsComponent(o);
                        if (c == null || !c.gameObject.scene.IsValid()) continue; // the scene's copy, not the prefab
                        var sb = new StringBuilder();
                        var canvas = c.GetComponentInParent<Canvas>();
                        var scaler = canvas != null ? canvas.rootCanvas.GetComponent<CanvasScaler>() : null;
                        sb.Append("[style] ").Append(name).Append("  canvas=").Append(canvas != null ? canvas.rootCanvas.name : "-");
                        if (scaler != null)
                            sb.Append(" scaler=").Append(scaler.uiScaleMode).Append(' ').Append(scaler.referenceResolution).Append(" match=").Append(scaler.matchWidthOrHeight);
                        Log.Info(sb.ToString());
                        _lines = 0;
                        Walk(c.transform, 1);
                        break;
                    }
                }
            }
            catch (Exception ex) { Log.Error("Menu style dump failed.", ex); }
        }

        private static void Walk(Transform t, int depth)
        {
            if (depth > 9 || ++_lines > MaxLinesPerScreen) return;
            var sb = new StringBuilder();
            sb.Append(' ', depth * 2).Append(t.name);
            if (!t.gameObject.activeSelf) sb.Append(" (inactive)");
            var rt = t.GetComponent<RectTransform>();
            if (rt != null) sb.Append("  ").Append(Mathf.RoundToInt(rt.rect.width)).Append('x').Append(Mathf.RoundToInt(rt.rect.height));

            var img = t.GetComponent<Image>();
            if (img != null)
                sb.Append(" | Image ").Append(Hex(img.color)).Append(" sprite=").Append(img.sprite != null ? img.sprite.name : "none")
                  .Append(' ').Append(img.type).Append(img.sprite != null ? " border=" + img.sprite.border : "")
                  .Append(" ppu*=").Append(img.pixelsPerUnitMultiplier);
            var tmp = t.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
                sb.Append(" | TMP font=").Append(tmp.font != null ? tmp.font.name : "none").Append(" size=").Append(tmp.fontSize)
                  .Append(tmp.enableAutoSizing ? "(auto " + tmp.fontSizeMin + "-" + tmp.fontSizeMax + ")" : "")
                  .Append(" style=").Append(tmp.fontStyle).Append(' ').Append(Hex(tmp.color)).Append(" align=").Append(tmp.alignment)
                  .Append(" spacing=").Append(tmp.characterSpacing).Append(" \"").Append(Short(tmp.text)).Append('"');
            var txt = t.GetComponent<Text>();
            if (txt != null)
                sb.Append(" | Text font=").Append(txt.font != null ? txt.font.name : "none").Append(" size=").Append(txt.fontSize)
                  .Append(' ').Append(Hex(txt.color)).Append(" \"").Append(Short(txt.text)).Append('"');
            var btn = t.GetComponent<Button>();
            if (btn != null)
            {
                var cb = btn.colors;
                sb.Append(" | Button ").Append(btn.transition).Append(" n=").Append(Hex(cb.normalColor)).Append(" h=").Append(Hex(cb.highlightedColor))
                  .Append(" p=").Append(Hex(cb.pressedColor)).Append(" fade=").Append(cb.fadeDuration);
            }
            var lg = t.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (lg != null)
                sb.Append(" | ").Append(lg is VerticalLayoutGroup ? "VLG" : "HLG").Append(" pad=").Append(lg.padding.left).Append(',').Append(lg.padding.right)
                  .Append(',').Append(lg.padding.top).Append(',').Append(lg.padding.bottom).Append(" gap=").Append(lg.spacing).Append(" align=").Append(lg.childAlignment);
            var cg = t.GetComponent<CanvasGroup>();
            if (cg != null) sb.Append(" | Group a=").Append(cg.alpha);
            var outline = t.GetComponent<Outline>();
            if (outline != null) sb.Append(" | Outline ").Append(Hex(outline.effectColor)).Append(' ').Append(outline.effectDistance);
            var shadow = t.GetComponent<Shadow>();
            if (shadow != null && outline == null) sb.Append(" | Shadow ").Append(Hex(shadow.effectColor)).Append(' ').Append(shadow.effectDistance);

            Log.Info("[style]" + sb);
            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1);
        }

        private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);

        private static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\n", " / ");
            return s.Length > 50 ? s.Substring(0, 50) + "…" : s;
        }
    }
}
