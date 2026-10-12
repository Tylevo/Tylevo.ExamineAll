using System;
using System.Collections.Generic;

namespace Tylevo.ExamineAll.Core
{
    public enum ExaminationState { Idle, Running, Stopping, Completed, Stopped, Failed }

    public sealed class ExaminationResult
    {
        internal ExaminationResult(ExaminationState state, int confirmed, int total)
        { State = state; Confirmed = confirmed; Total = total; }
        public ExaminationState State { get; }
        public int Confirmed { get; }
        public int Total { get; }
        public bool IsWarning => State != ExaminationState.Completed;
        public string Message
        {
            get
            {
                string count = Confirmed.ToString("N0");
                string noun = Confirmed == 1 ? "item" : "items";
                if (State == ExaminationState.Failed)
                    return "Examine All: " + count + " " + noun + " confirmed examined. Result incomplete; restart the game.";
                if (State == ExaminationState.Stopped)
                    return "Examine All stopped: " + count + " of " + Total.ToString("N0") + " new items examined.";
                if (Total == 0)
                    return "Examine All: 0 new items. Everything is already examined.";
                return "Examine All: Examined " + count + " new " + noun + ".";
            }
        }
    }

    public sealed class ExaminationBatch
    {
        private readonly string[] _ids;
        internal ExaminationBatch(long run, string[] ids, double startedAt)
        { Run = run; _ids = ids; StartedAt = startedAt; }
        public long Run { get; }
        public int Count => _ids.Length;
        public double StartedAt { get; }
        public string[] CopyIds() => (string[])_ids.Clone();
    }

    // Queue acceptance is deliberately absent: only a confirmed backend response advances a run.
    public sealed class ExaminationRun
    {
        public const int BatchSize = 256;
        public const double TimeoutSeconds = 60;
        private string[] _ids = Array.Empty<string>();
        private int _next;
        private long _generation;
        private bool _resultTaken;
        public ExaminationState State { get; private set; }
        public ExaminationBatch? Pending { get; private set; }
        public int Total { get; private set; }
        public int Confirmed { get; private set; }
        public bool Busy => State == ExaminationState.Running || State == ExaminationState.Stopping;
        public bool RequiresRestart => State == ExaminationState.Failed;

        public bool Begin(IEnumerable<string> ids)
        {
            if (Busy || RequiresRestart) return false;
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            var unique = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<string>();
            foreach (string id in ids)
                if (!string.IsNullOrWhiteSpace(id) && unique.Add(id)) ordered.Add(id);
            _ids = ordered.ToArray();
            _next = Confirmed = 0;
            Total = _ids.Length;
            _generation++;
            _resultTaken = false;
            Pending = null;
            State = Total == 0 ? ExaminationState.Completed : ExaminationState.Running;
            return true;
        }

        public ExaminationBatch? Next(double now)
        {
            if (State != ExaminationState.Running || Pending != null) return null;
            if (!Finite(now) || now < 0) { Fail(); return null; }
            int count = Math.Min(BatchSize, _ids.Length - _next);
            if (count == 0) return null;
            var ids = new string[count];
            Array.Copy(_ids, _next, ids, 0, count);
            _next += count;
            Pending = new ExaminationBatch(_generation, ids, now);
            return Pending;
        }

        public bool Confirm(ExaminationBatch batch)
        {
            if (!Busy || !ReferenceEquals(batch, Pending) || batch.Run != _generation) return false;
            Confirmed += batch.Count;
            Pending = null;
            if (State == ExaminationState.Stopping) State = ExaminationState.Stopped;
            else if (_next == _ids.Length) State = ExaminationState.Completed;
            if (!Busy) _ids = Array.Empty<string>();
            return true;
        }

        public void Stop()
        {
            if (State != ExaminationState.Running) return;
            State = Pending == null ? ExaminationState.Stopped : ExaminationState.Stopping;
            _ids = Array.Empty<string>();
        }

        public bool CheckTimeout(double now)
        {
            if (Pending == null) return false;
            if (Finite(now) && now >= Pending.StartedAt && now - Pending.StartedAt < TimeoutSeconds) return false;
            Fail();
            return true;
        }

        public void Fail()
        {
            State = ExaminationState.Failed;
            Pending = null;
            _ids = Array.Empty<string>();
        }

        // Consume only when feedback can be shown. Pending/accepted requests never
        // produce a completion result, including the final batch after menu close.
        public ExaminationResult? TakeResult()
        {
            if (_resultTaken || (State != ExaminationState.Completed &&
                State != ExaminationState.Stopped && State != ExaminationState.Failed)) return null;
            _resultTaken = true;
            return new ExaminationResult(State, Confirmed, Total);
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
