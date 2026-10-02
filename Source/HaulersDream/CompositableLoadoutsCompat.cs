using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Compositable Loadouts integration, without a hard assembly reference. Bill-menu composition retains
    /// CL's actual GetOptions actions, including recipe guards and target/repeat/includeEquipped setup.
    /// Missing contributions and IT's inadequate proxies are handled by the scoped menu composer; CL's
    /// inventory keep policy below remains independent of that menu integration.
    /// </summary>
    public static class CompositableLoadoutsCompat
    {
        // --- #200 inventory-KEEP API (a separate CL surface from the bill menu above). CL assigns each pawn a
        // loadout of items with desired counts and a think-node re-fetches any shortfall; HD's "adopt all surplus"
        // would ship those items to storage and CL re-fetches them (the unload<->pickup loop). KeepCount below sums
        // the loadout's desired count per def into HD's keep so a loadout item CL WOULD RE-FETCH is never counted as
        // surplus — and, per #233, no more than that: apparel (which CL satisfies by WEARING, off the map) keeps
        // nothing, and a copy the pawn already WIELDS discharges its own entry. See KeepCount's doc for both. Member
        // names are from CL's OPEN SOURCE (simplyWiri/Loadout-Compositing, namespace Inventory) but are NOT
        // decompile-verified (CL isn't installed here), so they are resolved reflectively + guarded: a rename
        // degrades to "keep nothing extra" with a logged warning, never a crash. Resolved lazily + independently of
        // the bill-menu method (a CL build could expose one and not the other).
        private static bool keepApiInitialized;
        private static bool keepApiOk;
        private static Type loadoutComponentType;   // Inventory.LoadoutComponent (a ThingComp on the pawn)
        private static MethodInfo loadoutGetter;     // LoadoutComponent.Loadout getter -> Inventory.Loadout
        private static MethodInfo itemsGetter;       // Loadout.Items getter -> IEnumerable<Inventory.Item>
        private static MethodInfo itemDefGetter;     // Item.Def getter -> ThingDef
        private static MethodInfo itemQtyGetter;     // Item.Quantity getter -> int

        /// <summary>Whether the registered CL menu provider and its action contract are ready.</summary>
        public static bool IsActive => BillRepeatMenuProviders.Get(BillRepeatMenuKind.Loadouts) != null;

        /// <summary>Merge the authoritative CL contribution without duplicating an existing adequate entry.</summary>
        public static void TryInsertModes(List<FloatMenuOption> options, Bill_Production bill) =>
            BillRepeatMenuComposition.MergeProvider(options, bill, BillRepeatMenuKind.Loadouts);

        /// <summary>
        /// How many of <paramref name="def"/> the pawn's Compositable Loadouts loadout needs HD to keep in INVENTORY —
        /// summed into HD's keep-count (see <see cref="InventorySurplus.KeepCountOf"/>) so "adopt all surplus" never
        /// ships a loadout item to storage that CL would immediately re-fetch (the #200 unload↔pickup loop). Returns 0
        /// when CL is absent, the pawn has no loadout, the item isn't in it, or the keep API didn't resolve.
        ///
        /// THE INVARIANT, and the whole of issue #233: HD's keep must equal the units CL would RE-FETCH — no more. A
        /// unit HD keeps that CL would never re-fetch is not protected, it is STRANDED: HD's bulk-load put it in the
        /// pack, HD is the only thing that would take it out, and HD has just refused to. Two rules follow, both
        /// decided by the unit-tested <see cref="CompositableLoadoutKeepPolicy"/>:
        ///
        /// (a) APPAREL IS NEVER KEPT (<see cref="CompositableLoadoutKeepPolicy.ShieldsDef"/>). CL EXCLUDES apparel
        /// from the loadout items it fetches into inventory, at two sites: <c>Loadout.DesiredItems</c> opens with
        /// <c>Items.Where(t =&gt; !t.Def.IsApparel)</c>, and <c>ThinkNode_LoadoutRealisation.SatisfyLoadoutItemsJob</c>
        /// iterates <c>loadout.Items.Where(item =&gt; !item.Def.IsApparel)</c>. Apparel is satisfied by a SEPARATE path,
        /// <c>SatisfyLoadoutClothingJob</c> — a modified <c>JobGiver_OptimizeApparel</c> that issues
        /// <c>JobDefOf.Wear</c> against garments lying ON THE MAP. So there is nothing to protect: a duster in the pack
        /// is not a duster CL is about to re-fetch, and CL in fact wants it GONE
        /// (<c>FirstUnloadableThing_Patch.ShouldDropThing</c> surrenders <c>currentQuantity - desiredQuantity</c> — a
        /// vanilla-unload hook HD's own unload driver never consults). And HD CREATED the situation: CL alone never
        /// puts apparel into <c>innerContainer</c>, HD's bulk-load does — the reporter confirmed the strand disappears
        /// when HD's bulk loading is disabled. Reported symptom: a worn helmet/pants/duster loadout, three EXTRA
        /// dusters bulk-hauled into the pack and never unloaded; removing "duster" from the loadout released all three
        /// at once.
        ///
        /// (b) A WIELDED LOADOUT WEAPON DISCHARGES ITS ENTRY (<see cref="CompositableLoadoutKeepPolicy.ContributedKeep"/>).
        /// CL's own gear list is <c>Utility.InventoryAndEquipment(pawn)</c> = the pawn's inventory CONCAT
        /// <c>equipment.AllEquipmentListForReading</c>, but the "have" side of HD's subtraction in
        /// <see cref="InventorySurplus.SurplusOf"/> counts <c>innerContainer</c> only — so an equipped loadout weapon
        /// never discharged its entry and a hauled spare of that def was pinned forever. Same bug class as (a), different
        /// item class, and live for this reporter: their log shows CL + Combat Extended but NO Simple Sidearms, so the
        /// SS branch that would otherwise intercept every weapon before this keep is inert. The correction is applied
        /// INSIDE this term, not on the shared "have" side: the comparison is
        /// <c>have_inventory - (K_drug + K_stock + K_CE + K_ItemPolicy + K_CL)</c>, so counting equipment for the CL
        /// term alone is algebraically identical to replacing <c>K_CL</c> with <c>max(0, K_CL - equipped)</c>. One term,
        /// no new reflection (<c>pawn.equipment</c> is vanilla), <see cref="InventorySurplus.KeepCountOf"/>'s signature
        /// untouched, and no other keeper's contribution disturbed. It mirrors <see cref="SidearmKeepMath.KeepForPair"/>,
        /// documented at <c>InventorySurplus.cs</c>'s Simple Sidearms branch as the fix for exactly this bug.
        ///
        /// DELIBERATELY NOT gated on <c>(def, stuff)</c> equality — and this is REQUIRED, not merely defensible. Both
        /// other sides of the comparison are def-only in this branch: <c>wanted</c> comes from a def-only entry match
        /// (see <see cref="LoadoutWantedUnits"/>) and <c>have</c> from a def-only inventory count
        /// (<c>YieldRouter.InventoryCountOfDef</c>). A stuff-qualified <c>equipped</c> would therefore be an
        /// ASYMMETRIC comparison, and the asymmetry lands on the COMMON case: a permissive CL filter, a steel longsword
        /// wielded, a plasteel longsword hauled → no subtraction → the spare is pinned FOREVER, to cover the rarer case
        /// of an entry whose <c>Filter</c> genuinely names one material. Do not "harden" this later. Making the whole
        /// branch stuff-aware on all three sides at once is a different, larger change and needs CL's <c>Filter</c>
        /// (see the residual below).
        ///
        /// EVIDENCE GRADE: every claim about CL's own internals above is SOURCE-READ from
        /// github.com/simplyWiri/Loadout-Compositing and is NOT decompile-verified — CL is not installed on this
        /// machine. The design is safe even if those reads are wrong: the worst case is that HD unloads a piece CL
        /// wanted, CL re-fetches it once, and the player still has the per-def unload rules and the per-pawn keep pin —
        /// versus today's GUARANTEED permanent strand.
        ///
        /// KNOWN RESIDUAL of (b), and its COST DEPENDS ON THE "unload all surplus" SETTING — say the whole thing or the
        /// next reader under-rates it. The match is def-only and ignores CL's per-entry <c>Filter</c>, so a
        /// filter-restricted entry plus a def-matching but filter-FAILING wielded weapon under-keeps by one: CL wants
        /// 1 × longsword restricted to plasteel, the pawn wields a STEEL longsword, and both <c>wanted</c> and
        /// <c>equipped</c> match on def alone → keep 0 → the plasteel spare unloads. Then:
        /// <list type="bullet">
        /// <item>At the DEFAULT (<c>unloadAllSurplus</c> OFF) it costs ONE churn trip and stops there.
        /// <c>CompHauledToInventory</c>'s self-heal builds its <c>liveDefs</c> only from CURRENTLY TAGGED things
        /// (<c>CompHauledToInventory.cs:124-128,182-190</c>), so once HD's last tag of that def has left the pack it
        /// cannot re-adopt the foreign copy CL fetched back.</item>
        /// <item>With <c>unloadAllSurplus</c> ON it is a REPEATING LOOP, not a one-off: CL re-fetches the spare
        /// UNTAGGED, and <c>PawnUnloadChecker.cs:110-112</c> calls <c>AdoptSurplusInventory(pawn, comp, adoptAll: true)</c>,
        /// which re-tags every stack with <c>SurplusOf &gt; 0</c> and a destination (<c>:297-340</c>; the Simple
        /// Sidearms / Grab Your Tool guards at <c>:327-329</c> are inert for a CL-only user) → unloaded again →
        /// forever, until the entry's <c>Filter</c> is honoured.</item>
        /// </list>
        /// Still strictly better than the pre-fix state for the same player (a churn loop self-corrects the moment the
        /// filter is honoured or the weapon is swapped; a strand never does), but it is a loop, not a trip. Reading
        /// <c>Filter.Allows</c> / <c>Item.CountIn</c> is the follow-up, deferred because neither can be verified here.
        ///
        /// Defensively try/caught — UNLIKE the compile-verified <see cref="CECompat.LoadoutKeepCount"/> — precisely
        /// because CL's members are resolved reflectively from its source names and were NOT decompile-verified against
        /// a running CL here (CL wasn't installed): a wrong/renamed member must degrade to "keep nothing" + one report,
        /// never crash HD's unload path. Both VANILLA reads (<c>def.IsApparel</c> and the equipment walk) sit OUTSIDE
        /// that try on purpose, so an HD bug in either is never misreported as a CL API fault and can never latch the
        /// CL shield off.
        ///
        /// SCOPE: the apparel carve-out is justified by CL's OWN source and only CL's. Do NOT generalise it to
        /// <see cref="CECompat.LoadoutKeepCount"/> or <see cref="ItemPolicyCompat.KeepCount"/> — CE loadouts and Item
        /// Policy stock legitimately hold apparel and DO re-fetch it.
        /// </summary>
        public static int KeepCount(Pawn pawn, ThingDef def)
        {
            if (pawn == null || def == null)
                return 0;
            // (a) #233. Ahead of InitKeepApi and the keepApiOk latch on purpose: this is a VANILLA read and a
            // decision about HD's own model, so it must hold identically whether or not CL's API ever resolves,
            // and must never be attributable to a CL fault. Also why CompositableLoadoutKeepPolicy.ShieldsDef
            // takes a bool rather than a def — the IsApparel read stays here, ahead of the reflection latch.
            if (!CompositableLoadoutKeepPolicy.ShieldsDef(def.IsApparel))
                return 0;
            if (!keepApiInitialized)
                InitKeepApi();
            if (!keepApiOk)
                return 0;
            var comp = FindLoadoutComp(pawn);
            if (comp == null)
                return 0; // this pawn has no CL loadout component -> nothing to keep
            int wanted;
            try
            {
                wanted = LoadoutWantedUnits(comp, def);
            }
            catch (Exception e)
            {
                // Stop probing a broken/renamed API for the rest of the session and report once (the loop may recur,
                // but HD's unload never crashes on CL). This is RECOVER + REPORT, not a silent swallow.
                keepApiOk = false;
                HDLog.ErrOnce("Compositable Loadouts keep-count read threw for " + (pawn.def?.defName ?? "a pawn")
                    + "; HD is standing down its CL loadout shield (an unload↔re-fetch loop may recur). Please report "
                    + "it (issue #200).\n" + e, 0x20C10AD7);
                return 0;
            }
            // (b) #233. Vanilla-only read, outside the try for the same reason as (a).
            return CompositableLoadoutKeepPolicy.ContributedKeep(wanted, EquippedCountOfDef(pawn, def));
        }

        // The units this pawn's CL loadout asks for of one def, summed across its entries. Every read here is
        // REFLECTED into CL, which is precisely why the caller wraps it (and only it) in the API-fault catch.
        // A def may legitimately appear in several entries — Loadout.Items is a SelectMany over the pawn's active
        // tags — so duplicates SUM; EntryUnits floors each at >= 0 so one bad entry cannot cancel a good one.
        private static int LoadoutWantedUnits(ThingComp comp, ThingDef def)
        {
            var loadout = loadoutGetter.Invoke(comp, null);
            if (loadout == null)
                return 0;
            if (!(itemsGetter.Invoke(loadout, null) is System.Collections.IEnumerable items))
                return 0;
            int wanted = 0;
            foreach (var item in items)
            {
                if (item == null || (itemDefGetter.Invoke(item, null) as ThingDef) != def)
                    continue; // def-matched entries only; a generic filter entry (null Def) isn't shielded here
                wanted += CompositableLoadoutKeepPolicy.EntryUnits((int)itemQtyGetter.Invoke(item, null));
            }
            return wanted;
        }

        // Units of a def the pawn already holds in its EQUIPMENT slots — the half of CL's own
        // Utility.InventoryAndEquipment gear list that HD's inventory-side "have" cannot see. Vanilla only (no
        // reflection), so it stays outside the CL API try; a pawn with no equipment tracker (an animal, a mech
        // without weapons) contributes 0. Private to this shim by design: every OTHER keeper's term has its own
        // "have" semantics, and CE's in particular already models its loadout's equipment slots itself.
        //
        // DO NOT extend this to pawn.apparel.WornApparel to "also cover clothing": rule (a) above already returns
        // 0 for every apparel def before this method is reached, so a worn-apparel branch here would be dead code
        // that merely looks like it is doing something. (Written down because it is the obvious next "completion"
        // a future reader would make.)
        private static int EquippedCountOfDef(Pawn pawn, ThingDef def)
        {
            var equipment = pawn.equipment?.AllEquipmentListForReading;
            if (equipment == null)
                return 0;
            int count = 0;
            for (int i = 0; i < equipment.Count; i++)
            {
                var eq = equipment[i];
                if (eq != null && eq.def == def)
                    count += eq.stackCount;
            }
            return count;
        }

        // Resolve CL's loadout keep API lazily + independently of bill-menu binding (a CL build could expose one and
        // not the other). Every member is guarded; keepApiOk is set only when the full read path resolved, and a
        // partial resolve is surfaced once so a silent CL rename doesn't just re-open the unload loop unnoticed.
        private static void InitKeepApi()
        {
            keepApiInitialized = true;
            loadoutComponentType = AccessTools.TypeByName("Inventory.LoadoutComponent");
            if (loadoutComponentType == null)
                return; // CL not loaded (or the component was renamed) -> keep nothing extra, no warning (absent is normal).
            loadoutGetter = AccessTools.PropertyGetter(loadoutComponentType, "Loadout");
            var loadoutType = AccessTools.TypeByName("Inventory.Loadout");
            itemsGetter = loadoutType != null ? AccessTools.PropertyGetter(loadoutType, "Items") : null;
            var itemType = AccessTools.TypeByName("Inventory.Item");
            itemDefGetter = itemType != null ? AccessTools.PropertyGetter(itemType, "Def") : null;
            itemQtyGetter = itemType != null ? AccessTools.PropertyGetter(itemType, "Quantity") : null;
            keepApiOk = loadoutGetter != null && itemsGetter != null && itemDefGetter != null && itemQtyGetter != null;
            // The success line mirrors ItemPolicyCompat's detection line. Success used to be SILENT, which made "was
            // the CL keep shield live in this session?" unanswerable from a bug report — the exact question #233
            // turned on. NOTE the timing: KeepCount now short-circuits APPAREL before this initializer runs, so this
            // line first appears on the first NON-apparel keep query and can therefore land LATER in a session than
            // the cooperative bill-menu detection line. Absence of this line does not mean CL went undetected.
            if (keepApiOk)
                HDLog.Msg("Compositable Loadouts loadout keep API resolved — a pawn's loadout items are kept during "
                          + "surplus unload (so HD won't fight CL's re-fetch). Apparel is deliberately not kept.");
            else
                HDLog.Warn("Compositable Loadouts present but its per-pawn loadout keep API (LoadoutComponent.Loadout "
                           + "/ Loadout.Items / Item.Def|Quantity) did not fully resolve — a CL version/rename likely. "
                           + "HD cannot shield CL-loadout items from 'adopt all surplus', so an unload↔re-fetch loop "
                           + "may recur. Please report it (issue #200). HD continues.");
        }

        // The pawn's Inventory.LoadoutComponent, matched on AllComps by the reflected type (TryGetComp<T> is generic,
        // and the concrete type is only known reflectively here).
        private static ThingComp FindLoadoutComp(Pawn pawn)
        {
            var comps = pawn.AllComps;
            if (comps == null)
                return null;
            for (int i = 0; i < comps.Count; i++)
                if (comps[i] != null && loadoutComponentType.IsInstanceOfType(comps[i]))
                    return comps[i];
            return null;
        }
    }
}
