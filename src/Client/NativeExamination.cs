using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.HandBook;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Screens;
using Tylevo.ExamineAll.Core;
using UnityEngine;

namespace Tylevo.ExamineAll.Client
{
    public sealed class NativeExamination : IDisposable
    {
        private readonly Action<string> _log;
        private readonly ExaminationRun _run = new ExaminationRun();
        private IEftSession? _session;
        private Profile? _profile;
        private MenuScreen.MainMenuScreenController? _controller;
        private Request? _request;
        private bool _disposed, _handbookDirty;

        private sealed class Request
        {
            private readonly object _gate = new object();
            private string? _failure;
            private bool _accepted;
            public readonly ExaminationBatch Batch;
            public readonly string[] Ids;
            public Task<IResult>? Task;
            public Request(ExaminationBatch batch) { Batch = batch; Ids = batch.CopyIds(); }
            public void Accepted(IResult result)
            {
                lock (_gate)
                {
                    if (result == null || !result.Succeed) _failure = result?.Error ?? "No acceptance result.";
                    else _accepted = true;
                }
            }
            public void Warning(IInventoryWarning warning) => Fail("Inventory warning " + warning.ErrorCode + ".");
            public void Error(int code, string message) => Fail("Inventory error " + code + ": " + message);
            private void Fail(string reason) { lock (_gate) _failure = reason; }
            public bool IsAccepted(out string? failure)
            { lock (_gate) { failure = _failure; return _accepted; } }
        }

        public NativeExamination(Action<string> log) => _log = log ?? throw new ArgumentNullException(nameof(log));
        public string Status { get; private set; } = "Examine every item, including mod items.";
        public bool Busy => _run.Busy;
        public bool RequiresRestart => _run.RequiresRestart;
        public ExaminationResult? TakeResult() => !_disposed && SameProfile() ? _run.TakeResult() : null;

        public void Begin()
        {
            if (_disposed || Busy || RequiresRestart) return;
            try
            {
                if (!TryContext(out IEftSession? session, out Profile? profile, out var controller) ||
                    session!.QueueStatus != EOperationQueueStatus.Idle || session.IsFlushing ||
                    !Singleton<ItemFactory>.Instantiated)
                { Status = "Wait for the main menu to finish loading."; return; }
                JsonType.ItemTemplates templates = Singleton<ItemFactory>.Instance.ItemTemplates;
                if (templates == null || profile!.Encyclopedia == null)
                { Status = "The item list isn't ready yet."; return; }
                var missing = new List<string>();
                foreach (KeyValuePair<MongoID, ItemTemplate> pair in templates)
                {
                    ItemTemplate template = pair.Value;
                    if (template != null && template._type == NodeType.Item && !template.ExaminedByDefault &&
                        !profile.Encyclopedia.ContainsKey(pair.Key)) missing.Add((string)pair.Key);
                }
                if (!ReferenceEquals(_profile, profile)) _handbookDirty = false;
                _session = session; _profile = profile; _controller = controller;
                if (!_run.Begin(missing)) return;
                Status = missing.Count == 0 ? "Everything is already examined." : "Examining 0 / " + missing.Count + " items...";
            }
            catch (Exception error)
            {
                Status = "Couldn't read the item list. Check the game log.";
                Log("Could not start examination: " + error);
            }
        }

        public void Tick(bool canContinue)
        {
            if (_disposed) return;
            try
            {
                if (!canContinue) Stop();
                if (_request != null)
                {
                    Request request = _request;
                    bool accepted = request.IsAccepted(out string? failure);
                    if (failure != null) { Fail(failure); return; }
                    if (!SameProfile()) { Fail("The active session or profile changed while waiting for the result."); return; }
                    Task<IResult>? task = request.Task;
                    if (task != null && task.IsCompleted)
                    {
                        if (!accepted) { Fail("The backend did not accept the item batch."); return; }
                        if (task.IsCanceled || task.IsFaulted)
                        { Fail(task.Exception?.GetBaseException().Message ?? "The examination request was canceled."); return; }
                        IResult result = task.GetAwaiter().GetResult();
                        if (result == null || !result.Succeed)
                        { Fail(result?.Error ?? "No examination result was returned."); return; }
                        // Native warnings/errors precede flush completion. Read them again after observing completion.
                        if (!request.IsAccepted(out failure) || failure != null)
                        { Fail(failure ?? "The backend did not accept the item batch."); return; }
                        if (!ReferenceEquals(_run.Pending, request.Batch))
                        { Fail("An old response no longer matches this run."); return; }
                        // The queue callback is only acceptance; this branch requires the later flush acknowledgment.
                        foreach (string id in request.Ids)
                            if (!_profile!.Encyclopedia.ContainsKey((MongoID)id)) _profile.Encyclopedia[(MongoID)id] = true;
                        _handbookDirty = true;
                        DetachRequest();
                        if (!_run.Confirm(request.Batch)) { Fail("An old response no longer matches this run."); return; }
                        UpdateStatus();
                    }
                    else if (_run.CheckTimeout(Time.realtimeSinceStartup))
                    { Fail("Timed out waiting for the examination result."); return; }
                }

                if (!Busy)
                {
                    if (!RequiresRestart && canContinue && _handbookDirty && SameProfile() && TryContext(out _, out _, out _)) RefreshHandbook();
                    return;
                }
                if (_request != null || _run.State != ExaminationState.Running) return;
                if (!canContinue || !CurrentMenu()) { Stop(); return; }
                if (_session!.QueueStatus == EOperationQueueStatus.Error)
                { Fail("The native inventory queue has an error."); return; }
                if (_session.QueueStatus != EOperationQueueStatus.Idle || _session.IsFlushing) return;
                ExaminationBatch? batch = _run.Next(Time.realtimeSinceStartup);
                if (batch == null) { if (RequiresRestart) Fail("Invalid request timing."); return; }
                var next = new Request(batch);
                _request = next;
                _session.InventoryWarningHappened += next.Warning;
                _session.InventoryErrorHappened += next.Error;
                _session.ReadEncyclopedia(next.Ids, next.Accepted);
                // ReadEncyclopedia's callback runs before its command is added to the queue.
                // Flush only after ReadEncyclopedia returns, never from inside that callback.
                next.Task = _session.FlushOperationQueue();
                ObserveLateFault(next.Task);
            }
            catch (Exception error) { Fail(error.ToString()); }
        }

        public void Stop()
        {
            if (_disposed || !Busy) return;
            _run.Stop();
            UpdateStatus();
        }

        private bool SameProfile()
        {
            if (_session == null || _profile == null || !Singleton<ClientApplication<IEftSession>>.Instantiated) return false;
            IEftSession current = Singleton<ClientApplication<IEftSession>>.Instance.GetClientBackEndSession();
            return ReferenceEquals(current, _session) && ReferenceEquals(current.Profile, _profile) &&
                ReferenceEquals(current.GetProfileBySide(ESideType.Pmc), _profile) && _profile.Encyclopedia != null;
        }

        private bool CurrentMenu() => SameProfile() &&
            TryContext(out IEftSession? session, out Profile? profile, out var controller) &&
            ReferenceEquals(session, _session) && ReferenceEquals(profile, _profile) && ReferenceEquals(controller, _controller);

        private static bool TryContext(out IEftSession? session, out Profile? profile,
            out MenuScreen.MainMenuScreenController? controller)
        {
            session = null; profile = null; controller = null;
            if (Application.isBatchMode || !Application.isFocused || Singleton<GameWorld>.Instantiated ||
                !Singleton<ClientApplication<IEftSession>>.Instantiated || !CommonUI.Instantiated || CommonUI.Instance == null)
                return false;
            MenuScreen menu = CommonUI.Instance.MenuScreen;
            if (menu == null || !menu.gameObject.activeInHierarchy) return false;
            controller = EftScreenManager.Instance.CurrentScreenController as MenuScreen.MainMenuScreenController;
            if (controller == null || controller.GetType() != typeof(MenuScreen.MainMenuScreenController) ||
                !controller.Active || controller.Closed) return false;
            session = Singleton<ClientApplication<IEftSession>>.Instance.GetClientBackEndSession();
            if (session == null) return false;
            profile = session.Profile;
            return profile != null && ReferenceEquals(profile, controller.Profile) &&
                ReferenceEquals(profile, session.GetProfileBySide(ESideType.Pmc));
        }

        private void UpdateStatus()
        {
            if (_run.State == ExaminationState.Running)
                Status = "Examining " + _run.Confirmed + " / " + _run.Total + " items...";
            else if (_run.State == ExaminationState.Stopping)
                Status = "Finishing the current batch...";
            else if (_run.State == ExaminationState.Stopped)
                Status = "Stopped. Examined " + _run.Confirmed + " items.";
            else if (_run.State == ExaminationState.Completed)
                Status = "Examined " + _run.Confirmed + " items.";
        }

        private void RefreshHandbook()
        {
            if (!Singleton<Handbook>.Instantiated || _profile?.EftStats == null) return;
            try
            {
                Singleton<Handbook>.Instance.UpdateProfile(_profile.Encyclopedia, _profile.EftStats.CarriedQuestItems);
                _handbookDirty = false;
            }
            catch (Exception error)
            {
                _handbookDirty = false;
                Status = "Examined " + _run.Confirmed + " items. Reopen the game to refresh the handbook.";
                Log("Examination was confirmed, but the handbook could not refresh: " + error);
            }
        }

        private void Fail(string reason)
        {
            _run.Fail();
            DetachRequest();
            Status = "Couldn't confirm the result. Restart the game before trying again.";
            Log("Examination stopped after " + _run.Confirmed + " confirmed items: " + reason);
        }

        private void DetachRequest()
        {
            Request? request = _request;
            _request = null;
            if (request == null || _session == null) return;
            _session.InventoryWarningHappened -= request.Warning;
            _session.InventoryErrorHappened -= request.Error;
        }

        private static void ObserveLateFault(Task task) => task.ContinueWith(
            completed => { _ = completed.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        private void Log(string message) { try { _log(message); } catch { } }

        public void Dispose()
        {
            if (_disposed) return;
            _run.Stop();
            _disposed = true;
            DetachRequest();
            _session = null; _profile = null; _controller = null;
        }
    }
}
