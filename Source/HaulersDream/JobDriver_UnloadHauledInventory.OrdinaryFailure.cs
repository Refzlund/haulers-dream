using System;
using System.Collections;
using System.Runtime.ExceptionServices;
using Verse.AI;

namespace HaulersDream
{
    public partial class JobDriver_UnloadHauledInventory
    {
        private bool OrdinaryUnloadCurrent(Job activation, OrdinaryUnloadTransit receipt)
            => ReferenceEquals(job, activation) && ReferenceEquals(pawn?.CurJob, activation)
                && ReferenceEquals(pawn?.jobs?.curDriver, this) && ReferenceEquals(ordinaryTransit, receipt);

        private void HandleOrdinaryUnloadFailure(Job activation, OrdinaryUnloadTransit receipt, Exception failure)
        {
            if (OrdinaryUnloadCurrent(activation, receipt))
                ExceptionDispatchInfo.Capture(failure).Throw();

            // Native JobUtility.TryStartErrorRecoverJob ends pawn.jobs.curJob, even when its
            // concreteDriver argument names an OLD driver. Throwing here would kill replacement
            // work. Report this abandoned operation explicitly without entering native recovery.
            string message = "Ordinary unload failed after its activation or receipt was replaced; "
                + "kept the replacement work. Original failure:\n";
            try
            {
                HDDebugLog.Enqueue("ERR [ordinary-unload] abandoned activation failed; reporting original error.");
                message += DescribeOrdinaryUnloadFailure(failure);
                HDLog.Err(message);
            }
            catch (Exception reportFailure)
            {
                // The diagnostic boundary itself must not throw into native recovery either.
                // Keep the built primary/secondary text on the disk-only logger if console fails.
                try { HDDebugLog.Enqueue("ERR [ordinary-unload] " + message
                    + "\nReporting also failed: " + HDFault.Render(reportFailure)); }
                catch { /* Last-resort logger boundary; never replace the pawn's current work. */ }
            }
        }

        private static string DescribeOrdinaryUnloadFailure(Exception failure)
        {
            string message = HDFault.Render(failure);
            // Exception.ToString/Render omit Data. Include exact transfer/placement recovery
            // strings both when reporting and when nesting a secondary failure in a primary.
            foreach (DictionaryEntry entry in failure.Data)
                if (entry.Key is string key && (key.StartsWith("HaulersDream.OrdinaryUnload.", StringComparison.Ordinal)
                    || key.StartsWith("HaulersDream.OrdinaryPlacement.", StringComparison.Ordinal)))
                    message += "\n" + key + ": " + entry.Value;
            return message;
        }
    }
}
