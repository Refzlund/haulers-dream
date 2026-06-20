using System;
using System.Reflection;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// RimWorld Multiplayer (MP, <c>rwmt.Multiplayer</c>) compatibility — SOFT dependency.
    ///
    /// <para>MP runs the simulation in deterministic lockstep: every client replays the same command stream and
    /// ticks the same game state, so the WHOLE autonomous side of Hauler's Dream (WorkGivers, JobDrivers, the
    /// per-tick GameComponent logic, every Harmony patch on in-tick vanilla code) is already MP-safe — MP replays
    /// it identically on every client. This is why a job-pipeline hauling mod like Pick Up And Haul needs ZERO MP
    /// integration. The only things MP does NOT cover for free are PLAYER-INITIATED state mutations that bypass the
    /// job system (a gizmo flipping a saved bool, a dialog writing the GameComponent directly). Those must be
    /// routed through a SYNCED method so the mutation runs once, as a command, on every client. That — plus making
    /// in-tick iteration deterministic (see the <c>thingIDNumber</c> tiebreaks across the bulk-load/sweep code) and
    /// guarding mid-game settings edits — is the entire MP surface.</para>
    ///
    /// <para>WHY this is a SOFT dep with no runtime dll: the mod references <c>RimWorld.MultiplayerAPI</c> at
    /// COMPILE time only (<c>ExcludeAssets="runtime"</c> — the <c>0MultiplayerAPI.dll</c> is NOT shipped). When MP
    /// is active it loads the API assembly into the process, so our references resolve; when MP is absent the API
    /// assembly is never loaded, so we must never touch an <c>Multiplayer.API</c> type unless MP is present. We
    /// guarantee that by JIT isolation: <see cref="Active"/> is computed from <see cref="ModLister"/> (a Verse type,
    /// no MP reference), and EVERY method that touches an <c>Multiplayer.API</c> type lives in the nested
    /// <see cref="MpHooks"/> class and is only ever CALLED from behind the <see cref="Active"/> gate. The CLR JITs a
    /// method on first call, so a never-called <see cref="MpHooks"/> method never resolves the absent assembly. The
    /// <c>[SyncMethod]</c> attributes elsewhere are pure metadata — never resolved unless reflected (which only
    /// happens inside <see cref="MpHooks.Register"/>), so they are inert in a non-MP game. This is the same
    /// single-assembly pattern LWM Deep Storage ships. Mirrors the other <c>*Compat</c> shims: detect once, do
    /// nothing when absent.</para>
    /// </summary>
    [StaticConstructorOnStartup]
    public static class MultiplayerCompat
    {
        /// <summary>
        /// Whether RimWorld Multiplayer is loaded. Computed purely from <see cref="ModLister"/> (a Verse type) so
        /// reading it NEVER touches an <c>Multiplayer.API</c> type — the precondition that keeps a non-MP game from
        /// ever resolving the unshipped API assembly. <c>ignorePostfix</c> matches the <c>_steam</c>/<c>_copy</c>
        /// package-id variants.
        /// </summary>
        public static readonly bool Active =
            ModLister.GetActiveModWithIdentifier("rwmt.multiplayer", ignorePostfix: true) != null;

        static MultiplayerCompat()
        {
            if (!Active)
                return;
            // Only reached when MP is present, so the API assembly is loaded and MpHooks.Register can resolve its
            // Multiplayer.API references. Defensive try/catch: a registration fault must never break startup — it
            // degrades to "MP not wired" (single-player-style direct mutation), which at worst desyncs MP, never
            // crashes the game. This is the ONE place we accept catching: a failure here is recoverable and
            // logging it is strictly better than a hard crash on game load.
            try
            {
                MpHooks.Register();
                HDLog.Msg("Multiplayer detected — sync handlers registered; settings are host-authoritative "
                          + "at join (see CONTRIBUTING / mod description).");
            }
            catch (Exception e)
            {
                Log.Warning("[Hauler's Dream] Multiplayer sync registration failed; "
                            + "player-initiated toggles may desync in MP. " + e);
            }
        }

        /// <summary>
        /// True only while an ACTIVE multiplayer session is in progress (not merely "MP is installed"). Used to
        /// gate the mid-game settings guard. Short-circuits on <see cref="Active"/> so <see cref="MpHooks.InMpGame"/>
        /// (which touches an <c>Multiplayer.API</c> type) is only invoked — and thus only JIT'd — when MP is loaded.
        /// In single-player-with-MP-installed this is false, so settings remain freely editable.
        /// </summary>
        public static bool InMultiplayerGame => Active && MpHooks.InMpGame();

        /// <summary>
        /// Whether player-facing feedback (a <see cref="Messages"/> toast, a sound) for a synced action should be
        /// shown on THIS client. Outside MP: always (single-player). Inside MP: only on the client that ISSUED the
        /// command, so a synced action's UI feedback doesn't toast on every player's screen. Vanilla-safe via the
        /// <see cref="Active"/> short-circuit (the MP-typed call is isolated in <see cref="MpHooks"/>).
        /// </summary>
        public static bool ShouldShowLocalFeedback => !Active || MpHooks.IssuedBySelf();

        // ----------------------------------------------------------------------------------------------------
        // Synced methods (player-initiated mutations that bypass the job system). Each is a PLAIN static method
        // whose BODY contains NO Multiplayer.API type — only the [SyncMethod] ATTRIBUTE references MP, and an
        // attribute is inert until reflected (which only happens inside the MP-gated MpHooks.Register). So these
        // run directly in a non-MP game and become synced commands in an MP game. They take a Pawn/Bill (both
        // natively MP-serializable) rather than a ThingComp so the wire form is unambiguous.
        // ----------------------------------------------------------------------------------------------------

        /// <summary>
        /// Set a pawn's per-pawn "auto-haul yields" opt-out. Replaces the gizmo's direct
        /// <c>comp.autoHaulYields = !comp.autoHaulYields</c> flip (a write to a SCRIBED field — synced world state —
        /// that must run on every client, not just the clicker). The current value is read locally in the gizmo
        /// callback and the desired value is passed in, so the command is idempotent across clients.
        /// </summary>
        [SyncMethod]
        public static void SetAutoHaulYields(Pawn pawn, bool value)
        {
            var comp = pawn?.GetComp<CompHauledToInventory>();
            if (comp != null)
                comp.autoHaulYields = value;
        }

        /// <summary>
        /// The "Unload inventory" gizmo action. The vanilla auto-sync only covers <c>TryTakeOrderedJob</c>; this
        /// action assigns jobs via <c>jobQueue.EnqueueFirst</c> / direct inventory adoption
        /// (<see cref="PawnUnloadChecker.CheckIfShouldUnload"/> → <c>RegisterHauledItem</c>, a SCRIBED-set write),
        /// so it is NOT covered and must be synced. Running inside the synced command, the nested job assignment /
        /// inventory writes execute directly (no re-sync, since we're no longer in interface context) and
        /// deterministically on every client. Player-facing toasts inside the called helpers are gated by
        /// <see cref="ShouldShowLocalFeedback"/> at their source so they don't appear on every client.
        /// </summary>
        [SyncMethod]
        public static void UnloadInventoryNow(Pawn pawn)
        {
            if (pawn == null)
                return;
            // Mirrors the original gizmo action: off a home/storage map there's nowhere to unload, so load the
            // nearest pack animal with the carried loot instead; otherwise do the normal storage unload.
            if (pawn.Map != null && !MapGate.ShouldUnloadToStorage(pawn.Map))
                PackAnimalLoad.GizmoLoadNearest(pawn);
            else
                PawnUnloadChecker.CheckIfShouldUnload(pawn, true);
        }

        /// <summary>
        /// Set the per-save batch size for a bill. Replaces the direct <c>GameComponent.SetBatch</c> write from the
        /// batch-size dialog / bill float-menu (a write to the SCRIBED <c>batchBills</c> dictionary). Callers must
        /// invoke this ONCE on commit (dialog close / menu pick), never per-frame, to avoid command spam.
        /// </summary>
        [SyncMethod]
        public static void SetBillBatch(Bill bill, bool on, int size)
        {
            HaulersDreamGameComponent.Instance?.SetBatch(bill, on, size);
        }

        /// <summary>
        /// The single class that touches <c>Multiplayer.API</c> types in its method BODIES. Every member here is
        /// invoked ONLY from behind the <see cref="Active"/> gate, so in a non-MP game these methods are never
        /// called → never JIT'd → the unshipped API assembly is never resolved. Do NOT call any member of this
        /// class without first checking <see cref="Active"/>.
        /// </summary>
        private static class MpHooks
        {
            internal static void Register()
            {
                // Scans this assembly for every [SyncMethod]/[SyncField]/[SyncWorker]-attributed member and wires it
                // up. We use the attribute form (vs explicit RegisterSyncMethod-by-name) so each synced method is
                // self-contained next to the feature it belongs to (the route/craft/batch synced entry points live
                // in their own files) — RegisterAll finds them all in one pass.
                MP.RegisterAll(Assembly.GetExecutingAssembly());
            }

            internal static bool InMpGame() => MP.IsInMultiplayer;

            internal static bool IssuedBySelf() => MP.IsExecutingSyncCommandIssuedBySelf;
        }
    }
}
