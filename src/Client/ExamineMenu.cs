using System;
using Comfort.Common;
using EFT;
using EFT.UI;
using EFT.UI.Screens;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Tylevo.ExamineAll.Client
{
    // A child of the native main menu. It borrows the menu's canvas and input system.
    public sealed class ExamineMenu : IDisposable
    {
        private readonly Action _clicked;
        private readonly Action _menuClosed;
        private MenuScreen? _menu;
        private MenuScreen.MainMenuScreenController? _controller;
        private EftScreenManager? _manager;
        private GameObject? _root;
        private Button? _button;
        private Text? _buttonText, _statusText;
        private Font? _font;
        private string _status = "Examine every item, including mod items.";
        private bool _busy, _requiresRestart, _available, _disposed;

        public ExamineMenu(Action clicked, Action menuClosed)
        {
            _clicked = clicked ?? throw new ArgumentNullException(nameof(clicked));
            _menuClosed = menuClosed ?? throw new ArgumentNullException(nameof(menuClosed));
        }

        public bool CanRun => !_disposed && !Application.isBatchMode && CanInteract();

        public void SetState(string status, bool busy, bool requiresRestart = false)
        {
            if (_disposed) return;
            _status = status ?? string.Empty;
            _busy = busy;
            _requiresRestart = requiresRestart;
            RefreshView();
        }

        public void Tick()
        {
            if (_disposed || Application.isBatchMode) return;
            if (!CurrentMenu())
            {
                Detach();
                if (_disposed) return;
                Attach();
            }
            bool available = CanInteract();
            bool lostAvailability = _available && !available;
            _available = available;
            if (!available)
            {
                if (_root != null) _root.SetActive(false);
                if (lostAvailability) _menuClosed();
                return;
            }
            if (_root == null) CreateView();
            RefreshView();
            if (_root != null) _root.SetActive(true);
        }

        private void Attach()
        {
            if (!CommonUI.Instantiated || CommonUI.Instance == null) return;
            MenuScreen menu = CommonUI.Instance.MenuScreen;
            if (menu == null || !menu.gameObject.activeInHierarchy) return;
            EftScreenManager manager = EftScreenManager.Instance;
            var controller = manager.CurrentScreenController as MenuScreen.MainMenuScreenController;
            if (controller == null || controller.GetType() != typeof(MenuScreen.MainMenuScreenController) ||
                !controller.Active || controller.Closed) return;
            _menu = menu;
            _manager = manager;
            _controller = controller;
            controller.OnClose += Detach;
            manager.OnScreenChanged += ScreenChanged;
        }

        private bool CurrentMenu() => _menu != null && _controller != null && _manager != null &&
            _menu.gameObject.activeInHierarchy && _controller.Active && !_controller.Closed &&
            ReferenceEquals(_manager.CurrentScreenController, _controller) &&
            CommonUI.Instantiated && CommonUI.Instance != null &&
            ReferenceEquals(CommonUI.Instance.MenuScreen, _menu);

        private bool CanInteract()
        {
            if (!CurrentMenu() || !Application.isFocused || Singleton<GameWorld>.Instantiated ||
                !Cursor.visible || Cursor.lockState == CursorLockMode.Locked) return false;
            EventSystem events = EventSystem.current;
            if (events == null || !events.isActiveAndEnabled || events.currentInputModule == null ||
                !events.currentInputModule.isActiveAndEnabled) return false;
            CanvasGroup group = _menu!.GetComponent<CanvasGroup>();
            if (group != null && (!group.interactable || !group.blocksRaycasts || group.alpha < .99f)) return false;
            CommonUI common = CommonUI.Instance;
            if (Active(common.HideoutScreenRear) || Active(common.HideoutScreenOverlay)) return false;
            if (!PreloaderUI.Instantiated || PreloaderUI.Instance == null) return true;
            PreloaderUI preloader = PreloaderUI.Instance;
            return !Active(preloader._loader) && !Active(preloader._hideoutLoadingScreen) &&
                !Active(preloader._pveLoadingScreen) && !Active(preloader._raidStartIntro) &&
                !(preloader._overlapBlackImage != null && preloader._overlapBlackImage.isActiveAndEnabled &&
                    preloader._overlapBlackImage.color.a > .01f);
        }

        private static bool Active(Component? component) => component != null && component.gameObject.activeInHierarchy;
        private static bool Active(GameObject? obj) => obj != null && obj.activeInHierarchy;

        private void ScreenChanged(EEftScreenType ignored)
        {
            if (!CurrentMenu()) Detach();
        }

        private void Click()
        {
            // Native button callbacks can arrive after the screen or focus changed.
            if (_disposed || _busy || _requiresRestart || _root == null || !_root.activeInHierarchy || !CanInteract()) return;
            _busy = true;
            RefreshView();
            try { _clicked(); }
            catch (Exception exception)
            {
                SetState("Could not start. Check the game log.", false, _requiresRestart);
                Debug.LogException(exception);
            }
        }

        private void CreateView()
        {
            Canvas canvas = _menu!.GetComponentInParent<Canvas>();
            if (canvas == null || !canvas.isActiveAndEnabled) return;
            _font = FindFont(_menu.transform);
            if (_font == null) return;
            try
            {
                _root = new GameObject("Tylevo.ExamineAll.Menu", typeof(RectTransform));
                _root.layer = 5;
                _root.SetActive(false);
                _root.transform.SetParent(_menu.transform, false);
                var panel = (RectTransform)_root.transform;
                panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.zero;
                panel.anchoredPosition = new Vector2(24, 88);
                panel.sizeDelta = new Vector2(320, 108);
                // A nested canvas may provide raycasting, but never creates a global canvas or EventSystem.
                if (canvas.GetComponent<GraphicRaycaster>() == null)
                {
                    _root.AddComponent<Canvas>().overrideSorting = false;
                    _root.AddComponent<GraphicRaycaster>();
                }
                Image background = _root.AddComponent<Image>();
                background.color = new Color(.025f, .03f, .026f, .82f);
                background.raycastTarget = false;
                Image buttonImage = Image(panel, "ExamineButton", new Color(.13f, .14f, .12f));
                Position(buttonImage.rectTransform, 12, 12, 156, 34);
                buttonImage.raycastTarget = true;
                _button = buttonImage.gameObject.AddComponent<Button>();
                _button.targetGraphic = buttonImage;
                _button.navigation = new Navigation { mode = Navigation.Mode.None };
                ColorBlock colors = _button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1.5f, 1.5f, 1.4f);
                colors.pressedColor = new Color(1.9f, 1.8f, 1.5f);
                colors.selectedColor = colors.normalColor;
                colors.disabledColor = new Color(.65f, .65f, .65f);
                colors.fadeDuration = .08f;
                _button.colors = colors;
                _button.onClick.AddListener(Click);
                _buttonText = Label(buttonImage.transform, "Caption", 16);
                Position(_buttonText.rectTransform, 4, 3, 148, 28);
                _buttonText.alignment = TextAnchor.MiddleCenter;
                _buttonText.fontStyle = FontStyle.Bold;
                _statusText = Label(panel, "Status", 14);
                Position(_statusText.rectTransform, 12, 54, 296, 42);
                _statusText.alignment = TextAnchor.UpperLeft;
                RefreshView();
            }
            catch
            {
                DestroyView();
                throw;
            }
        }

        private void RefreshView()
        {
            if (_button != null) _button.interactable = !_busy && !_requiresRestart;
            if (_buttonText != null) _buttonText.text = _busy ? "Examining..." : "Examine all";
            if (_statusText != null) _statusText.text = _status;
        }

        private static Image Image(Transform parent, string name, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            obj.layer = 5;
            obj.transform.SetParent(parent, false);
            Image image = obj.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private Text Label(Transform parent, string name, int size)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            obj.layer = 5;
            obj.transform.SetParent(parent, false);
            Text text = obj.GetComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.color = new Color(.90f, .90f, .85f);
            text.raycastTarget = false;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static void Position(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static Font? FindFont(Transform parent)
        {
            foreach (Text text in parent.GetComponentsInChildren<Text>(true))
                if (Usable(text.font)) return text.font;
            foreach (Font font in Resources.FindObjectsOfTypeAll<Font>())
                if (font.name.IndexOf("Bender", StringComparison.OrdinalIgnoreCase) >= 0 && Usable(font)) return font;
            try
            {
                Font fallback = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return Usable(fallback) ? fallback : null;
            }
            catch (ArgumentException) { return null; }
        }

        private static bool Usable(Font? font) => font != null && font.HasCharacter('E') && font.HasCharacter('x');

        private void DestroyView()
        {
            if (_button != null) _button.onClick.RemoveListener(Click);
            if (_root != null)
            {
                _root.SetActive(false);
                UnityEngine.Object.Destroy(_root);
            }
            _root = null;
            _button = null;
            _buttonText = _statusText = null;
            // These fonts belong to the native UI or Resources, never to this mod.
            _font = null;
        }

        private void Detach()
        {
            bool hadMenu = _controller != null || _menu != null;
            var controller = _controller;
            var manager = _manager;
            _controller = null;
            _manager = null;
            _menu = null;
            _available = false;
            if (controller != null) controller.OnClose -= Detach;
            if (manager != null) manager.OnScreenChanged -= ScreenChanged;
            DestroyView();
            if (hadMenu) _menuClosed();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Detach();
        }
    }
}
