using System;
using BepInEx;
using Comfort.Common;
using EFT.Communications;
using EFT.UI;
using Tylevo.ExamineAll.Client;
using UnityEngine;

namespace Tylevo.ExamineAll
{
    [BepInPlugin("com.tylevo.examineall", "Tylevo-ExamineAll", "1.0.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const string InspectedGameMvid = "cc2d80b0-6d5b-4cb1-a581-6d2cc901d4c7";
        private NativeExamination? _examination;
        private ExamineMenu? _menu;

        private void Awake()
        {
            if (Application.isBatchMode) { enabled = false; return; }
            if (typeof(MenuScreen).Module.ModuleVersionId.ToString() != InspectedGameMvid)
            {
                Logger.LogError("Examine All 1.0.0 requires the inspected SPT 4.1.6 / EFT 40743 client.");
                enabled = false;
                return;
            }
            _examination = new NativeExamination(message => Logger.LogInfo(message));
            _menu = new ExamineMenu(Begin, () => _examination?.Stop());
            _menu.SetState(_examination.Status, false);
            Logger.LogInfo("Examine All ready. The button appears on the main menu.");
        }

        private void Begin()
        {
            if (_menu?.CanRun != true || _examination == null) return;
            _examination.Begin();
            _menu.SetState(_examination.Status, _examination.Busy, _examination.RequiresRestart);
            ShowResult();
        }

        private void Update()
        {
            try
            {
                if (_menu == null || _examination == null) return;
                _menu.Tick();
                _examination.Tick(_menu.CanRun);
                _menu.SetState(_examination.Status, _examination.Busy, _examination.RequiresRestart);
                ShowResult();
            }
            catch (Exception error)
            {
                Logger.LogError("Examine All stopped: " + error.GetBaseException().Message);
                enabled = false;
            }
        }

        private void OnDisable() => Cleanup();
        private void OnDestroy() => Cleanup();
        private void ShowResult()
        {
            // Native notifications silently discard messages before their manager
            // exists. Leave the result pending until the main menu can display it.
            if (_menu?.CanRun != true || _examination == null ||
                !Singleton<NotificationManager>.Instantiated) return;
            var result = _examination.TakeResult();
            if (result == null) return;
            Logger.LogInfo(result.Message);
            try
            {
                if (result.IsWarning)
                    NotificationManager.DisplayWarningNotification(result.Message, ENotificationDurationType.Long);
                else
                    NotificationManager.DisplayMessageNotification(result.Message, ENotificationDurationType.Long,
                        ENotificationIconType.Note);
            }
            catch (Exception error)
            {
                // A failed toast must never invalidate an acknowledged examination.
                Logger.LogWarning("Could not display examination notification; result remains in the menu and log: " +
                    error.GetBaseException().Message);
            }
        }

        private void Cleanup()
        {
            _menu?.Dispose();
            _menu = null;
            _examination?.Dispose();
            _examination = null;
        }
    }
}
