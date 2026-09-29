using System;
using System.Collections;
using System.Runtime.ExceptionServices;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    // One actual driver invocation, including native Job pooling identity. This is
    // deliberately separate from the cargo receipt, which must settle after cancellation.
    internal sealed class RefuelActivation
    {
        internal readonly Pawn Pawn;
        internal readonly Job Job;
        internal readonly int JobId;
        internal readonly Map Map;
        internal readonly LocalTargetInfo TargetA;
        private readonly JobDriver_BulkRefuel driver;

        internal RefuelActivation(JobDriver_BulkRefuel driver)
        {
            this.driver = driver;
            Pawn = driver?.pawn;
            Job = driver?.job;
            JobId = Job?.loadID ?? -1;
            Map = Pawn?.Map;
            TargetA = Job?.targetA ?? LocalTargetInfo.Invalid;
        }

        internal bool Current => driver != null && !driver.ended && Pawn != null && Job != null && JobId >= 0
            && ReferenceEquals(driver.pawn, Pawn) && ReferenceEquals(driver.job, Job)
            && Job.loadID == JobId && ReferenceEquals(Pawn.CurJob, Job)
            && ReferenceEquals(Pawn.jobs?.curDriver, driver);

        // Intent can change without ending this activation. Such a change refuses
        // further old work, but a failure still belongs to Current for native recovery.
        internal bool IntentCurrent => Current && ReferenceEquals(Pawn.Map, Map)
            && Job.targetA == TargetA;

        internal void HandleFailure(Exception failure)
        {
            if (Current) ExceptionDispatchInfo.Capture(failure).Throw();

            // Native TryStartErrorRecoverJob ends the pawn's CURRENT job even when
            // concreteDriver identifies an abandoned driver. Do not route an old
            // refuel failure into that recovery path after a callback replaces work.
            string message = "Ordinary refuel failed after its activation was replaced; "
                + "kept the replacement work. Original failure:\n";
            try
            {
                HDDebugLog.Enqueue("ERR [ordinary-refuel] abandoned activation failed; reporting original error.");
                message += HDFault.Render(failure);
                foreach (DictionaryEntry entry in failure.Data)
                    if (entry.Key is string key && key.StartsWith("HaulersDream.Refuel.", StringComparison.Ordinal))
                        message += "\n" + key + ": " + entry.Value;
                HDLog.Err(message);
            }
            catch (Exception reportingFailure)
            {
                try { HDDebugLog.Enqueue("ERR [ordinary-refuel] " + message
                    + "\nReporting also failed: " + HDFault.Render(reportingFailure)); }
                catch { /* Never let the diagnostic boundary terminate replacement work. */ }
            }
        }
    }
}
