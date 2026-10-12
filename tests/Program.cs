using System;
using System.Linq;
using Tylevo.ExamineAll.Core;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException(message); }
    private static string[] Ids(int count) => Enumerable.Range(1, count).Select(i => "item" + i).ToArray();

    private static int Main()
    {
        try
        {
            EmptyAndRepeatedRuns();
            BatchOrderAndSingleRequest();
            StopAndLateResponse();
            FailuresAndTimeouts();
            ResultFeedback();
            Console.WriteLine("Passed " + _checks + " examination checks.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void EmptyAndRepeatedRuns()
    {
        var run = new ExaminationRun();
        Check(run.Begin(Array.Empty<string>()), "Empty scan can complete.");
        Check(!run.Busy && run.State == ExaminationState.Completed && run.Total == 0, "Empty scan never dispatches.");
        Check(run.Next(0) == null, "Empty scan has no native request.");
        Check(run.Begin(new[] { "a", "a", "b", "", " " }), "A new click can scan new content.");
        Check(run.Total == 2, "Duplicate and empty IDs are excluded.");
        ExaminationBatch batch = run.Next(1)!;
        Check(batch.CopyIds().SequenceEqual(new[] { "a", "b" }), "Input order retained.");
        string[] copy = batch.CopyIds(); copy[0] = "wrong";
        Check(batch.CopyIds()[0] == "a", "Caller cannot mutate a captured batch.");
        Check(run.Confirmed == 0, "Submitting does not count as examination.");
        Check(run.Confirm(batch), "Confirmed response advances run.");
        Check(run.Confirmed == 2 && !run.Busy, "Complete run totals actual acknowledgments.");
        Check(run.Begin(new[] { "new-mod-item" }), "Another click accepts newly installed content.");
        Check(!run.Confirm(batch) && run.Confirmed == 0, "Old response cannot change a new run.");
        ExaminationBatch next = run.Next(2)!;
        Check(next.Run != batch.Run, "Each run has a new identity.");
        Check(run.Confirm(next) && run.Confirmed == 1, "Only new run's response counts.");
    }

    private static void BatchOrderAndSingleRequest()
    {
        var run = new ExaminationRun();
        Check(run.Begin(Ids(600)), "600-item scan starts.");
        ExaminationBatch first = run.Next(10)!;
        Check(first.Count == 256 && first.CopyIds()[0] == "item1", "First request bounded to 256.");
        Check(!run.Begin(Ids(1)), "Repeated button press cannot replace running job.");
        Check(run.Next(11) == null, "No second request before acknowledgment.");
        Check(run.Confirmed == 0 && run.Total == 600, "Queue acceptance cannot publish success.");
        Check(run.Confirm(first), "First batch confirms.");
        Check(!run.Confirm(first) && run.Confirmed == 256, "Duplicate response counts once.");
        ExaminationBatch second = run.Next(12)!;
        Check(second.Count == 256 && second.CopyIds()[0] == "item257", "Second batch follows first.");
        Check(!run.Confirm(first) && ReferenceEquals(run.Pending, second), "Stale acknowledgment cannot clear current batch.");
        Check(run.Confirm(second) && run.Confirmed == 512, "Second confirmation adds exactly its items.");
        ExaminationBatch last = run.Next(13)!;
        Check(last.Count == 88 && last.CopyIds().Last() == "item600", "Final partial batch retains every remaining item.");
        Check(run.Confirm(last) && run.Confirmed == 600 && !run.Busy, "Full count only after final acknowledgment.");
        Check(run.Next(14) == null, "Completed run cannot submit again without new click.");
    }

    private static void StopAndLateResponse()
    {
        var run = new ExaminationRun();
        run.Begin(Ids(600)); run.Stop();
        Check(run.State == ExaminationState.Stopped && run.Confirmed == 0 && !run.Busy, "Menu close before dispatch does nothing.");
        Check(run.Next(0) == null, "Closed menu cannot dispatch.");
        run.Begin(Ids(600));
        ExaminationBatch first = run.Next(0)!;
        run.Stop(); run.Stop();
        Check(run.Busy && run.State == ExaminationState.Stopping, "Dispatched save remains pending after close.");
        Check(!run.Begin(Ids(1)) && run.Next(1) == null, "Reopening cannot duplicate pending operation.");
        Check(run.Confirm(first), "Pending confirmation still accepted after closing.");
        Check(run.Confirmed == 256 && run.State == ExaminationState.Stopped, "Actual partial progress remains visible.");
        Check(run.Next(2) == null, "Unsent batches remain canceled after acknowledgment.");
        Check(!run.Confirm(first), "Late duplicate response ignored after stop.");
        run.Begin(Ids(600));
        first = run.Next(3)!; run.Confirm(first); run.Stop();
        Check(run.Confirmed == 256 && !run.Busy, "Close between batches preserves confirmed work only.");
    }

    private static void FailuresAndTimeouts()
    {
        var run = new ExaminationRun();
        run.Begin(Ids(600)); ExaminationBatch first = run.Next(0)!; run.Confirm(first);
        ExaminationBatch second = run.Next(1)!;
        run.Fail();
        Check(run.RequiresRestart && !run.Busy && run.Confirmed == 256, "Failure preserves only confirmed progress.");
        Check(!run.Confirm(second) && run.Confirmed == 256, "Uncertain batch is never marked successful.");
        Check(!run.Begin(Ids(1)) && run.Next(3) == null, "Uncertain failure blocks all automatic and manual retries.");
        run.Stop(); Check(run.RequiresRestart, "Closing cannot clear an uncertain failure.");

        run = new ExaminationRun(); run.Begin(Ids(1)); first = run.Next(20)!;
        Check(!run.CheckTimeout(79.99) && run.Busy, "Request may complete within timeout.");
        Check(run.CheckTimeout(80) && run.RequiresRestart, "Timeout is uncertain and sticky.");
        Check(!run.Confirm(first) && run.Confirmed == 0, "Late success after timeout cannot claim examination.");
        foreach (double now in new[] { double.NaN, double.PositiveInfinity, -1d })
        {
            run = new ExaminationRun(); run.Begin(Ids(1));
            Check(run.Next(now) == null && run.RequiresRestart, "Invalid dispatch clock fails before creating a request.");
            run = new ExaminationRun(); run.Begin(Ids(1)); run.Next(1);
            Check(run.CheckTimeout(now) && run.RequiresRestart, "Invalid pending clock stops work.");
        }
        run = new ExaminationRun(); run.Begin(Ids(1)); run.Next(5);
        Check(run.CheckTimeout(4) && run.RequiresRestart, "Clock rewind cannot make a pending batch live forever.");
    }

    private static void ResultFeedback()
    {
        var run = new ExaminationRun();
        Check(run.TakeResult() == null, "Idle frames cannot notify.");
        run.Begin(Ids(600));
        ExaminationBatch first = run.Next(0)!;
        Check(run.TakeResult() == null, "Dispatched or accepted work cannot notify completion.");
        run.Confirm(first);
        Check(run.TakeResult() == null, "An intermediate batch does not spam notifications.");
        ExaminationBatch second = run.Next(1)!;
        run.Stop();
        Check(run.TakeResult() == null, "Stopping waits for the in-flight outcome.");
        run.Confirm(second);
        ExaminationResult partial = run.TakeResult()!;
        Check(partial.State == ExaminationState.Stopped && partial.Confirmed == 512 && partial.Total == 600 && partial.IsWarning,
            "Stopped feedback counts the last acknowledged batch and excludes unsent items.");
        Check(run.TakeResult() == null && !run.Confirm(second), "Repeated frames and late responses cannot duplicate the alert.");
        run.Begin(new[] { "new" });
        run.Confirm(run.Next(2)!);
        ExaminationResult complete = run.TakeResult()!;
        Check(complete.State == ExaminationState.Completed && complete.Confirmed == 1 && !complete.IsWarning,
            "Next explicit run has its own success count.");
        Check(complete.Message == "Examine All: Examined 1 new item.", "One-item notification is readable.");
        Check(partial.Confirmed == 512 && partial.State == ExaminationState.Stopped, "Prior result snapshot cannot change during later runs.");
        run.Begin(Array.Empty<string>());
        ExaminationResult empty = run.TakeResult()!;
        Check(empty.Confirmed == 0 && !empty.IsWarning && empty.Message.Contains("already examined"),
            "Nothing-new click reports zero without a native request.");
        Check(run.TakeResult() == null, "Empty scan reports once.");

        run = new ExaminationRun(); run.Begin(Ids(600)); run.Confirm(run.Next(0)!);
        ExaminationBatch uncertain = run.Next(1)!;
        run.Fail();
        ExaminationResult failed = run.TakeResult()!;
        Check(failed.State == ExaminationState.Failed && failed.Confirmed == 256 && failed.Total == 600 && failed.IsWarning,
            "Failure excludes unconfirmed items while retaining acknowledged count.");
        Check(failed.Message.Contains("incomplete") && failed.Message.Contains("restart"), "Uncertain result never presents full success.");
        Check(!run.Confirm(uncertain) && run.TakeResult() == null, "Late success cannot revise or repeat failure feedback.");
        run = new ExaminationRun(); run.Begin(Ids(1)); run.Next(0); run.CheckTimeout(60);
        Check(run.TakeResult()!.Confirmed == 0, "Timeout does not count a queued item.");
        run = new ExaminationRun(); run.Begin(Ids(1)); run.Stop();
        Check(run.TakeResult()!.Confirmed == 0, "Cancel before dispatch reports zero.");
        run = new ExaminationRun(); run.Begin(Ids(600));
        while (run.Busy) run.Confirm(run.Next(0)!);
        ExaminationResult all = run.TakeResult()!;
        Check(all.Confirmed == 600 && all.Total == 600 && !all.IsWarning, "Completion alert reports every confirmed batch exactly once.");
    }
}
