namespace HaulersDream.Core
{
    /// <summary>
    /// Pure decisions about how far Hauler's Dream stands down for Common Sense around crafting bills.
    /// No Verse/RimWorld refs (auto-compiled by the SDK default glob).
    ///
    /// <para>THE DISTINCTION THIS TYPE EXISTS TO KEEP (issue #243, reported again by Lensrub on 2026-08-03):
    /// "Common Sense owns the vanilla DoBill driver" and "Common Sense gathers the ingredients" are TWO DIFFERENT
    /// FACTS, and conflating them is what made the per-bench button honest about stopping and dishonest about
    /// starting. Common Sense's <c>MakeNewToils</c> Prefix takes the driver when EITHER of its two options is on,
    /// but only ONE of them makes it a gatherer. Verified against the shipped CommonSense.dll (1.6), decompiled:
    /// <list type="bullet">
    /// <item><c>JobDriver_DoBill_MakeNewToils_CommonSensePatch.Prefix</c> (:452-460) returns <c>true</c> — vanilla
    /// runs untouched — only when <c>!adv_cleaning &amp;&amp; !adv_haul_all_ings</c>. Either one on and it replaces
    /// the whole toil chain with its own <c>DoMakeToils</c>. That is DRIVER OWNERSHIP.</item>
    /// <item>Inside <c>DoMakeToils</c>, the gather chain is behind <c>if (Settings.adv_haul_all_ings &amp;&amp; …)</c>
    /// (:380). Its <c>else</c> branch (:426-432) yields RimWorld's own
    /// <c>JobDriver_DoBill.CollectIngredientsToils(B, A, C, false, true, placeInBillGiver)</c> — the identical call,
    /// argument for argument, that vanilla's <c>MakeNewToils</c> makes (Assembly-CSharp,
    /// <c>Verse.AI.JobDriver_DoBill:104</c>). So with the cleaning option alone, Common Sense holds the driver and
    /// hands the collecting straight back to vanilla. That is NOT gathering.</item>
    /// </list>
    /// The shipped rule ceded on driver ownership, so with cleaning on and haul-all off Common Sense gathered
    /// nothing, HD stayed stood down, and NOBODY gathered.</para>
    ///
    /// <para>WHY THE CEDE MUST STILL EXIST for the haul-all case: that branch ends with <c>TakeToHands</c> (:194,
    /// inventory → carry tracker) and <c>Toils_Haul.PlaceHauledThingInCell</c> (:414), which puts the ingredient on
    /// the bench floor, and its pickup toil sets <c>CompUnloadChecker.ShouldUnload</c> on what it took (:180-184).
    /// Against HD's own gather that is the gather → bench → unload loop the original cede was written to close.</para>
    ///
    /// <para>WHY HD's GATHER COMPOSES with the cleaning-only case: HD converts at the WORK-GIVER level (the bill's
    /// job becomes <c>HaulersDream_BillPrepGather</c>, which Common Sense does not patch at all), and the bill it
    /// hands back on the next work scan is sourced from the pawn's inventory. Vanilla's
    /// <c>CollectIngredientsToils</c> takes inventory-held ingredients natively —
    /// <c>Toils_Haul.StartCarryThing(…, canTakeFromInventory: true)</c> and
    /// <c>Toils_Goto.GotoThing(…, canGotoSpawnedParent: true)</c> (Assembly-CSharp, <c>JobDriver_DoBill:121-123</c>)
    /// — and that is the exact chain Common Sense's else branch yields. The only thing Common Sense adds in that
    /// configuration is its filth detour, appended AFTER the collecting and driven off the job's queue A, which the
    /// ingredient flow never touches.</para>
    /// </summary>
    public static class CommonSenseCedePolicy
    {
        /// <summary>
        /// Will Common Sense itself gather a bill's ingredients into a pawn's inventory?
        ///
        /// <para>This is the single fact behind BOTH halves of issue #243: it is the CEDE test (HD converts a bill's
        /// gather only when the answer is no) and it is the fact the per-bench notice reports ("another mod is doing
        /// the gathering"). They are deliberately one rule and not two, because the shipped bug was exactly the two
        /// drifting apart — the cede read driver ownership while the notice read gathering, so one configuration
        /// stopped HD without telling the player and without anyone taking over.</para>
        /// </summary>
        /// <param name="csPresent">Did Common Sense's settings type resolve? False means the mod is not loaded.</param>
        /// <param name="fieldsReadable">Did BOTH option fields bind by reflection? False means a fork or a rename
        /// moved them and HD cannot prove what the player chose.</param>
        /// <param name="advHaulAll">Common Sense's <c>adv_haul_all_ings</c> — its "pick up all ingredients before
        /// hauling them to the crafting place" option. Ships ON.</param>
        /// <returns>True when Common Sense is the gatherer and HD must stand aside.</returns>
        /// <remarks>
        /// Absent reads as false (FAIL-OPEN: HD behaves exactly as it does without the mod). Unreadable reads as
        /// true — the ONE deliberately fail-CLOSED path in this bridge: Common Sense ships the option ON, so when
        /// HD cannot prove the player turned it off, standing down is the direction that cannot reopen the
        /// gather → bench → unload loop.
        /// <para>→ NOTE: Common Sense's gather branch also requires the worker to be a player-faction humanlike
        /// (<c>JobDriver_DoBill_MakeNewToils_CommonSensePatch:380</c>), so a mech or a non-colonist crafter falls
        /// through to vanilla's collect even with the option on. That is deliberately NOT modelled here. HD already
        /// refuses to route a mech's bill at all (<c>BillRouteGate.WorkerMayShareCraft</c>), and folding a pawn into
        /// this rule would leave the per-bench notice — which has no pawn to ask about — unable to state the same
        /// fact. The residue is HD standing down for a crafter Common Sense would not have gathered for, which is
        /// today's behaviour and never a loop.</para>
        /// </remarks>
        public static bool CommonSenseGathersIngredients(bool csPresent, bool fieldsReadable, bool advHaulAll)
        {
            if (!csPresent) return false;
            if (!fieldsReadable) return true;
            return advHaulAll;
        }

        /// <summary>
        /// Does Common Sense hold the vanilla <c>JobDriver_DoBill</c> driver — i.e. does its <c>MakeNewToils</c>
        /// Prefix replace the toil chain rather than letting vanilla's run?
        ///
        /// <para>→ GOTCHA: this is NOT the cede test and must never be used as one. That mistake is issue #243. Its
        /// one legitimate reader is the <c>allowBatchUnderCommonSense</c> opt-in, whose player-facing promise is
        /// "hand all cooking and crafting over to Common Sense" — a promise about Common Sense being in charge of
        /// the bill, which is precisely driver ownership and stays keyed to it, so narrowing the cede leaves that
        /// setting meaning exactly what it meant before.</para>
        /// </summary>
        /// <param name="csPresent">Did Common Sense's settings type resolve?</param>
        /// <param name="fieldsReadable">Did both option fields bind by reflection?</param>
        /// <param name="advCleaning">Common Sense's <c>adv_cleaning</c> — its "clean the room between bills"
        /// option. Ships ON. It takes the driver without making Common Sense a gatherer.</param>
        /// <param name="advHaulAll">Common Sense's <c>adv_haul_all_ings</c>.</param>
        /// <returns>True when Common Sense's replacement toil chain is what will run.</returns>
        public static bool CommonSenseOwnsDoBillDriver(bool csPresent, bool fieldsReadable, bool advCleaning, bool advHaulAll)
        {
            if (!csPresent) return false;
            if (!fieldsReadable) return true;
            return advCleaning || advHaulAll;
        }

        /// <summary>
        /// Belt-and-suspenders (#2): should the automatic unload pass DEFER because the pawn's current/queued
        /// vanilla DoBill needs the tagged carried stock? Identity predicate — the impure bill-matching work
        /// (InventoryShare.IsUsableForBill over CurJob + jobQueue) lives in PawnUnloadChecker, this pins the
        /// named contract unit-visibly (mirrors UnloadPolicy.HasPendingRealWork's thin-pure shape).
        /// </summary>
        public static bool ShouldDeferUnloadForActiveBill(bool curOrQueuedJobIsDoBillMatchingTagged)
            => curOrQueuedJobIsDoBillMatchingTagged;
    }
}
