using System;
using System.Collections.Generic;
using MDSAPI.Internal.Interop;
using MDSAPI.Logging;
using MDSAPI.Unity;
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
    /// The main-menu card for updates and requirements, and the opt-out dialog.
    /// <para>
    /// Nothing here is styled from taste. The font, button sprite, button colours, text
    /// colours and the dialog's backdrop are taken at runtime from the game's own main-menu
    /// popup (MainMenuPopup — the "Incompatible game version" message), so both read as part
    /// of the game and follow it if TVGS restyles. The numbers that are written down below
    /// were measured from that popup and the Continue/Settings screens on 0.4.7f12.
    /// </para>
    /// <para>
    /// The card takes the first screen corner nothing else is drawn in — the game's own menu
    /// or another mod's text — and if every corner is taken, it waits and opens over the
    /// Continue (saves) screen instead. Main menu only; never during play.
    /// </para>
    /// </summary>
    internal static class UpdateNotice
    {
        private sealed class Item
        {
            internal string Title, Detail, Url;
            internal bool IsUpdate;
        }

        private enum Spot { BottomLeft, BottomRight, TopLeft, TopRight, SavesScreen }

        internal static MdsApiSettings Settings;

        private static readonly List<Item> Items = new();
        private static bool _dirty, _dismissed;

        // ui
        private static GameObject _root, _dialog;
        private static RectTransform _card;
        private static CanvasGroup _cardGroup;
        private static Spot _spot = Spot.BottomLeft;
        private static bool _placed;
        private static float _fade, _recheck, _menuTime;

        // Measured from the game's main-menu screens (0.4.7f12). Used as given; the font,
        // sprite and button colours themselves are read from the game at runtime.
        private static readonly Color Backdrop = new Color32(0, 0, 0, 200);      // screen background #000000C8
        private static readonly Color TextMain = Color.white;                  // titles and values
        private static readonly Color TextSecondary = new Color32(160, 160, 160, 255); // "More than a year ago" #A0A0A0
        private static readonly Color Warning = new Color32(255, 112, 112, 255);  // "Incompatible game version" #FF7070
        private const float TitleSize = 18f, BodySize = 18f, SmallSize = 12f;    // the game's three text sizes
        private const float Step = 5f;                                         // the game's layout gap
        private static readonly Color PanelGrey = new Color32(34, 34, 34, 255);  // settings tab bar #222222
        private const float BodySmall = 14f;                                   // the game's middle size (tab labels)
        private const float Margin = 24f, CardWidth = 400f, DialogWidth = 520f;

        private static readonly Access<bool> SavesOpen = new("UI.MainMenu.ContinueScreen", "IsOpen");

        // ---- content ------------------------------------------------------------------------

        internal static void AddUpdate(string name, string latest, string installed, string url) =>
            Add(new Item
            {
                Title = name + " " + latest,
                Detail = "You have " + installed,
                Url = url,
                IsUpdate = true,
            });

        internal static void AddRequirement(string mod, string needs, string installed, string url) =>
            Add(new Item
            {
                Title = mod + " needs MDSAPI " + needs,
                Detail = "You have " + installed + ". " + mod + " is off until you update.",
                Url = url,
            });

        private static void Add(Item item)
        {
            foreach (var i in Items) if (i.Title == item.Title) return;
            Items.Add(item);
            _dirty = true;
        }

        // ---- per frame (main thread) ----------------------------------------------------------

        internal static void Tick(bool inWorld)
        {
            try
            {
                if (Items.Count == 0 || _dismissed) { Hide(); return; }
                if (inWorld) { _menuTime = 0f; Hide(); return; }

                // Let the menu finish appearing first, so its own UI is in place when we look
                // for a free corner (and the card does not arrive under the intro fade).
                if ((_menuTime += Time.unscaledDeltaTime) < 1.5f) { Hide(); return; }
                if (Settings != null && Settings.LogMenuStyle) MenuStyleDump.Run();

                if (_root == null || _dirty) Build();
                if (_root == null) return;
                _root.SetActive(true);

                if (_dialog != null)
                {
                    if (Input.GetKeyDown(KeyCode.Escape)) CloseDialog();
                    return;
                }

                // Where to sit: the first free corner, checked again now and then in case
                // another mod puts something there after we did.
                if (!_placed || (_recheck -= Time.unscaledDeltaTime) <= 0f)
                {
                    _recheck = 2f;
                    if (!_placed || (_spot != Spot.SavesScreen && Occupied(CornerRect(_spot)))) Place();
                }

                bool visible = _spot != Spot.SavesScreen || SavesScreenOpen();
                _card.gameObject.SetActive(visible);
                if (!visible) { _fade = 0f; return; }

                // The game's menu screens fade in and nothing else; so does this.
                if (_fade < 1f)
                {
                    _fade = Mathf.Min(1f, _fade + Time.unscaledDeltaTime / 0.2f);
                    _cardGroup.alpha = 1f - (1f - _fade) * (1f - _fade);
                }
                // an underline follows its link's hover tint, which lives on the text's renderer
                foreach (var (text, line) in _underlines)
                    if (text != null && line != null) line.color = text.color * text.canvasRenderer.GetColor();
            }
            catch (Exception ex)
            {
                Failures.Once("the update notice", ex);
                _dismissed = true;
                Hide();
            }
        }

        private static void Hide()
        {
            if (_root != null) _root.SetActive(false);
            _fade = 0f;
        }

        // ---- placement ----------------------------------------------------------------------

        private static void Place()
        {
            var before = _spot;
            _spot = Spot.SavesScreen;
            foreach (var s in new[] { Spot.BottomLeft, Spot.BottomRight, Spot.TopLeft, Spot.TopRight })
                if (!Occupied(CornerRect(s))) { _spot = s; break; }

            Anchor(_spot);
            _placed = true;
            _fade = 0f;
            if (_spot != before || !_loggedSpot)
            {
                _loggedSpot = true;
                Log.Info(_spot == Spot.SavesScreen
                    ? "Update notice: every menu corner is in use, so it will show on the Continue screen."
                    : "Update notice shown in the " + Words(_spot) + " corner.");
            }
        }

        private static bool _loggedSpot;

        private static string Words(Spot s) => s switch
        {
            Spot.BottomLeft => "bottom-left",
            Spot.BottomRight => "bottom-right",
            Spot.TopLeft => "top-left",
            _ => "top-right",
        };

        private static void Anchor(Spot s)
        {
            Vector2 a = s switch
            {
                Spot.BottomLeft => new Vector2(0, 0),
                Spot.BottomRight => new Vector2(1, 0),
                Spot.TopLeft => new Vector2(0, 1),
                Spot.TopRight => new Vector2(1, 1),
                _ => new Vector2(0.5f, 1),
            };
            _card.anchorMin = _card.anchorMax = _card.pivot = a;
            _card.anchoredPosition = Anchored(s);
        }

        private static Vector2 Anchored(Spot s) => s switch
        {
            Spot.BottomLeft => new Vector2(Margin, Margin),
            Spot.BottomRight => new Vector2(-Margin, Margin),
            Spot.TopLeft => new Vector2(Margin, -Margin),
            Spot.TopRight => new Vector2(-Margin, -Margin),
            _ => new Vector2(0, -96f),
        };

        /// <summary>The screen-pixel rect the card would cover in a corner.</summary>
        private static Rect CornerRect(Spot s)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_card);
            float scale = _root.GetComponent<Canvas>().scaleFactor;
            float w = _card.rect.width * scale, h = _card.rect.height * scale, m = Margin * scale;
            float x = s == Spot.BottomLeft || s == Spot.TopLeft ? m : Screen.width - m - w;
            float y = s == Spot.BottomLeft || s == Spot.BottomRight ? m : Screen.height - m - h;
            return new Rect(x, y, w, h);
        }

        /// <summary>
        /// True when any visible UI — the game's menu, another mod's text — overlaps the rect.
        /// Full-screen layers (backgrounds, vignettes) are ignored, as are 3D world-space
        /// canvases; neither is something the card would be covering.
        /// </summary>
        private static bool Occupied(Rect area)
        {
            float screenArea = (float)Screen.width * Screen.height;
            foreach (var o in UnityFind.All(typeof(Graphic)))
            {
                var g = o as Graphic ?? Cast.To(o, typeof(Graphic)) as Graphic;
                if (g == null || !g.enabled || g.color.a < 0.05f) continue;
                if (g.transform.IsChildOf(_root.transform)) continue;
                var canvas = g.canvas;
                if (canvas == null || !canvas.enabled) continue;
                var root = canvas.rootCanvas;
                if (root.renderMode == RenderMode.WorldSpace) continue;

                var r = ScreenRect(g.rectTransform, root);
                if (r.width <= 0f || r.height <= 0f) continue;
                if (r.width * r.height > screenArea * 0.25f) continue;
                if (r.Overlaps(area)) return true;
            }
            return false;
        }

        private static Rect ScreenRect(RectTransform rt, Canvas root)
        {
            var rect = rt.rect;
            var a = rt.TransformPoint(new Vector3(rect.xMin, rect.yMin, 0f));
            var b = rt.TransformPoint(new Vector3(rect.xMax, rect.yMax, 0f));
            if (root.renderMode == RenderMode.ScreenSpaceCamera && root.worldCamera != null)
            {
                a = RectTransformUtility.WorldToScreenPoint(root.worldCamera, a);
                b = RectTransformUtility.WorldToScreenPoint(root.worldCamera, b);
            }
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private static object _savesScreen;

        private static bool SavesScreenOpen()
        {
            if (!UnityFind.Alive(_savesScreen)) _savesScreen = UnityFind.First(GameTypes.Find("UI.MainMenu.ContinueScreen"));
            return _savesScreen != null && SavesOpen.Get(_savesScreen);
        }

        // ---- the game's own look, read at runtime ----------------------------------------------

        private static TMP_FontAsset _font;          // OpenSans-SemiBold SDF on 0.4.7f12
        private static GameObject _gameButton;       // the popup's "Close" button, cloned for ours
        private static Sprite _roundedSprite;        // Rectangle_RoundedEdges, the game's button shape
        private static float _roundedPpu = 40f;
        private static CanvasScaler _gameScaler;
        private static Sprite _warning;

        private static readonly Access<object> PopupTitle = new("UI.MainMenu.MainMenuPopup", "Title");

        /// <summary>
        /// Finds the game's main-menu popup and takes what we need from it. False when it is not
        /// there (a game update reworked the menu); the notice then stays in the log only.
        /// </summary>
        private static bool ReadGameStyle()
        {
            if (_font != null && _gameButton != null) return true;

            object popup = null;
            foreach (var o in UnityFind.Everything(GameTypes.Find("UI.MainMenu.MainMenuPopup")))
            {
                var c = UnityFind.AsComponent(o);
                if (c != null && c.gameObject.scene.IsValid()) { popup = o; break; }
            }
            var root = UnityFind.AsComponent(popup);
            if (root == null) return false;

            var title = Cast.To(PopupTitle.Get(popup), typeof(TextMeshProUGUI)) as TextMeshProUGUI;
            _font = title != null ? title.font : null;

            var close = root.transform.Find("Close");
            var button = close != null ? close.GetComponent<Button>() : null;
            if (button == null)
                foreach (var b in root.GetComponentsInChildren<Button>(true)) { button = b; break; }
            _gameButton = button != null ? button.gameObject : null;
            var buttonImage = _gameButton != null ? _gameButton.GetComponent<Image>() : null;
            if (buttonImage != null) { _roundedSprite = buttonImage.sprite; _roundedPpu = buttonImage.pixelsPerUnitMultiplier; }

            // Walked by hand: under IL2CPP the generic GetComponentInParent<T>(bool) throws a
            // constraint VerificationException inside Il2CppInterop (seen on 0.4.7f12).
            _gameScaler = null;
            for (var t = root.transform; t != null; t = t.parent)
            {
                var s = t.GetComponent<CanvasScaler>();
                if (s != null) _gameScaler = s; // keep going: the outermost one is the root canvas's
            }

            return _font != null && _gameButton != null;
        }

        // ---- building -----------------------------------------------------------------------

        private static void Build()
        {
            _dirty = false;
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _placed = false;
            _underlines.Clear();

            if (!ReadGameStyle())
            {
                Failures.Once("Update notice: the game's main-menu popup was not found, so the notice is in the log only.");
                _dismissed = true;
                return;
            }
            _warning ??= WarningSprite();

            _root = new GameObject("MDSAPI.UpdateNotice");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = _gameScaler != null ? _gameScaler.referenceResolution : new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = _gameScaler != null ? _gameScaler.matchWidthOrHeight : 0f;
            _root.AddComponent<GraphicRaycaster>();

            BuildCard();
        }

        private static void BuildCard()
        {
            bool required = false, updates = false;
            foreach (var i in Items) { if (i.IsUpdate) updates = true; else required = true; }

            _card = Node("Card", _root.transform);
            _card.sizeDelta = new Vector2(CardWidth, 0);
            _cardGroup = _card.gameObject.AddComponent<CanvasGroup>();
            Background(_card, Backdrop);
            var col = Column(_card, 15, 15, 2 * Step);
            col.padding = new RectOffset(15, 15, 10, 12);

            // title row: mark, title, close
            var header = Row(_card, 2 * Step);
            Icon(header.transform, _warning, 18);
            Fill(Label(header.transform, required ? "Update required" : "Update available", TitleSize, TextMain));
            // "×" only if the game's font has the glyph; a missing one renders as a box
            bool hasCross = _font.HasCharacter('\u00D7');
            var close = Label(header.transform, hasCross ? "\u00D7" : "Close", hasCross ? TitleSize : SmallSize, TextSecondary);
            Link(close, () => { _dismissed = true; Hide(); });

            foreach (var item in Items)
            {
                var row = Row(_card, 3 * Step);
                var info = Node("Info", row.transform);
                Fill(info);
                Column(info, 0, 0, 1, fitHeight: false);
                Label(info, item.Title, BodySize, TextMain);
                Label(info, item.Detail, SmallSize, TextSecondary);
                var url = item.Url;
                GameButton(row.transform, "Download", 96, 30, () => Open(url));
            }

            if (updates)
            {
                Divider(_card);
                var footer = Row(_card, 0);
                var optOut = Label(footer.transform, "Opt out of automated checks", SmallSize, TextSecondary);
                Underline(optOut);
                Link(optOut, OpenDialog);
            }

            _cardGroup.alpha = 0f;
        }

        // ---- the opt-out dialog: the game's own popup backdrop, the game's font and buttons -----

        private static void OpenDialog()
        {
            if (_dialog != null) return;

            _dialog = Node("OptOut", _root.transform).gameObject;
            var rt = _dialog.GetComponent<RectTransform>();
            Stretch(rt);

            // A plain, even dim over the whole screen. (A copy of the game's popup background was
            // tried first: it is sized for the game's own panel band, so stretched to the screen its
            // pattern showed as a lighter box behind the dialog.) It also takes the clicks, so
            // nothing on the menu behind can be pressed while this is open.
            var dim = rt.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.6f);
            dim.raycastTarget = true;

            var panel = Node("Dialog", rt);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(DialogWidth, 0);
            Background(panel, PanelGrey);
            var col = Column(panel, 20, 20, 3 * Step);
            col.padding = new RectOffset(20, 20, 16, 20);

            Label(panel, "Turn off update checks?", TitleSize, Warning);
            Label(panel,
                "Once a day, MDSAPI asks GitHub for the newest version of MDSAPI and of the mods you have " +
                "that use it, so this menu can tell you when one is out. It only reads version numbers: " +
                "nothing is downloaded, and nothing about you or your saves is sent.",
                BodySmall, TextMain);

            var repos = new System.Text.StringBuilder("It connects to:");
            foreach (var r in UpdateCheck.Repos()) repos.Append("\n    ").Append(r);
            Label(panel, repos.ToString(), SmallSize, TextSecondary);

            Label(panel, "This can only be undone by setting \"CheckForUpdates\": true in UserData/MDSAPI.json.",
                  BodySmall, Warning);

            var buttons = Row(panel, 2 * Step);
            buttons.childAlignment = TextAnchor.MiddleRight;
            GameButton(buttons.transform, "Cancel", 104, 30, CloseDialog);
            GameButton(buttons.transform, "Confirm", 104, 30, ConfirmOptOut);

            rt.SetAsLastSibling();
        }

        private static void CloseDialog()
        {
            if (_dialog != null) UnityEngine.Object.Destroy(_dialog);
            _dialog = null;
        }

        private static void ConfirmOptOut()
        {
            if (Settings != null)
            {
                Settings.CheckForUpdates = false;
                Settings.Save();
            }
            Log.Info("Automatic update checks turned off. Set \"CheckForUpdates\": true in UserData/MDSAPI.json to turn them back on.");
            Items.RemoveAll(i => i.IsUpdate);
            CloseDialog();
            _dirty = true;
            if (Items.Count == 0) Hide();
        }

        private static void Open(string url)
        {
            // Only ever a page on a site we name; a bad catalog entry cannot send players elsewhere.
            if (UpdateCheck.IsTrustedUrl(url)) Application.OpenURL(url);
        }

        // ---- small builders -----------------------------------------------------------------

        private static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// The panel shape, on its own child that layout ignores. On the panel itself, the
        /// Image would report the sprite's size as a preferred height and the layout would
        /// stretch the panel to it — the tall empty card of the first try.
        /// </summary>
        private static void Background(RectTransform panel, Color color)
        {
            var bg = Node("Background", panel);
            bg.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Stretch(bg);
            var img = bg.gameObject.AddComponent<Image>();
            img.sprite = _roundedSprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = _roundedPpu;
            img.color = color;
            img.raycastTarget = true; // clicks on the panel stop here
        }

        private static VerticalLayoutGroup Column(RectTransform rt, int padX, int padY, float spacing, bool fitHeight = true)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(padX, padX, padY, padY);
            v.spacing = spacing;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            // only on a layout root: a fitter on a child of another layout group fights its parent
            if (fitHeight) rt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return v;
        }

        /// <summary>Takes the row's leftover width — and only that, so long text wraps instead of pushing.</summary>
        private static void Fill(Component c)
        {
            var le = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 0; le.preferredWidth = 0; le.flexibleWidth = 1;
        }

        private static void Divider(RectTransform parent)
        {
            var d = Node("Divider", parent);
            var img = d.gameObject.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.08f);
            img.raycastTarget = false;
            var le = d.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 1;
        }

        // TMP's <u> needs an underline glyph the game's font atlas does not carry (it rendered
        // nothing), so the line is drawn: 1px, the text's width, just under its baseline.
        private static void Underline(TextMeshProUGUI text)
        {
            var line = Node("Underline", text.transform);
            line.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            line.anchorMin = new Vector2(0f, 0f);
            line.anchorMax = new Vector2(1f, 0f);
            line.pivot = new Vector2(0.5f, 1f);
            line.sizeDelta = new Vector2(0f, 1f);
            line.anchoredPosition = new Vector2(0f, 1f);
            var img = line.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            // tinted with the text by the link's hover, since both sit under one Button target
            img.color = Color.white;
            _underlines.Add((text, img));
        }

        private static readonly List<(TextMeshProUGUI Text, Image Line)> _underlines = new();

        private static HorizontalLayoutGroup Row(Component parent, float spacing)
        {
            var rt = Node("Row", parent.transform);
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            return h;
        }

        private static TextMeshProUGUI Label(Component parent, string text, float size, Color color)
        {
            var rt = Node("Text", parent.transform);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = _font;
            t.fontSize = size;
            t.color = color;
            t.text = text;
            t.richText = true;
#pragma warning disable CS0618 // textWrappingMode is newer than some TMP builds the game ships
            t.enableWordWrapping = true;
#pragma warning restore CS0618
            t.alignment = TextAlignmentOptions.Left;
            t.raycastTarget = false;
            rt.gameObject.AddComponent<LayoutElement>();
            return t;
        }

        private static void Icon(Transform parent, Sprite sprite, float size)
        {
            var rt = Node("Icon", parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = Warning;
            img.preserveAspect = true;
            img.raycastTarget = false;
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.preferredHeight = le.minWidth = le.minHeight = size;
        }

        // Text that brightens to white under the cursor — how the game's own menu items respond.
        private static void Link(TextMeshProUGUI text, Action onClick)
        {
            text.raycastTarget = true;
            var b = text.gameObject.AddComponent<Button>();
            b.targetGraphic = text;
            var cb = b.colors;
            var c = text.color;
            text.color = Color.white;               // tint does the colouring: normal = secondary grey
            cb.normalColor = c;
            cb.highlightedColor = Color.white;
            cb.pressedColor = c;
            cb.selectedColor = c;
            cb.fadeDuration = 0.1f;
            b.colors = cb;
            Cross.OnClick(b, onClick);
        }

        /// <summary>
        /// A copy of the game's own popup button — its sprite, colours and hover, untouched —
        /// with our label and our click. Its prefab listeners are replaced, not added to.
        /// </summary>
        private static void GameButton(Transform parent, string label, float width, float height, Action onClick)
        {
            var go = UnityEngine.Object.Instantiate(_gameButton, parent);
            StripGameScripts(go);
            go.name = label;
            go.SetActive(true);
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = width;
            le.minHeight = le.preferredHeight = height;
            foreach (var text in go.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                text.text = label;
                text.fontSize = BodySmall; // the popup's 18 is for its one large button; ours sit in rows
                break;
            }
            var frame = go.transform.Find("SelectedFrame");
            if (frame != null) frame.gameObject.SetActive(false);
            var b = go.GetComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent();
            Cross.OnClick(b, onClick);
        }

        /// <summary>
        /// Removes the game's own scripts from a copied object, so the copy is only its look —
        /// a copied MenuScreen or popup script would otherwise act on the game's menu.
        /// </summary>
        private static void StripGameScripts(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var name = Cast.FullTypeName(c);
                if (name != null && (name.StartsWith("ScheduleOne", StringComparison.Ordinal) ||
                                     name.StartsWith("Il2CppScheduleOne", StringComparison.Ordinal)))
                    UnityEngine.Object.DestroyImmediate(c);
            }
        }

        // ---- the warning mark -----------------------------------------------------------------

        // A flat triangle with an exclamation mark cut out, drawn once at runtime in white and
        // tinted by the Image. The game has no warning glyph of its own to borrow.
        private static Sprite WarningSprite()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Vector2 top = new Vector2(32, 60), left = new Vector2(3, 6), right = new Vector2(61, 6);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = Mathf.Min(Edge(p, left, right), Mathf.Min(Edge(p, right, top), Edge(p, top, left)));
                    float a = Mathf.Clamp01(d + 0.5f);
                    bool bar = Mathf.Abs(p.x - 32) < 3.6f && p.y > 24 && p.y < 46;
                    bool dot = (p - new Vector2(32, 15)).sqrMagnitude < 3.8f * 3.8f;
                    if ((bar || dot) && d > 1f) a = 0f;
                    tex.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            tex.Apply();
            UnityEngine.Object.DontDestroyOnLoad(tex);
            var s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            UnityEngine.Object.DontDestroyOnLoad(s);
            return s;
        }

        private static float Edge(Vector2 p, Vector2 a, Vector2 b)
        {
            var e = b - a;
            return (e.x * (p.y - a.y) - e.y * (p.x - a.x)) / e.magnitude;
        }
    }
}
