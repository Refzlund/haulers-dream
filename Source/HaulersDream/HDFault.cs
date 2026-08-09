using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// STRUCTURAL fault attribution: who is in an exception's stack, read from the FRAME OBJECTS rather than
    /// from rendered trace TEXT.
    ///
    /// <para><b>Why frames and not text (issue #235, confirming #236).</b> HD used to answer "is HD in this
    /// stack?" by rendering the exception and scanning the string. That probe is unsound for essentially every
    /// player, because the 0Harmony runtime the game actually loads (brrainz.harmony's <c>HarmonyMod.dll</c>,
    /// present in almost every modded install) installs a DEDUPLICATING stack-trace renderer: the SECOND render
    /// of the same exception returns a short "duplicate stacktrace, see ref for original" placeholder instead of
    /// the frames. Any code that renders once to inspect and again to log therefore logs the placeholder — the
    /// lines naming the real source are gone. <see cref="StackFrame"/> objects are not produced by that renderer
    /// and cannot be replaced by it, so every question here is answered from
    /// <c>new StackTrace(ex, false).GetFrames()</c>.</para>
    ///
    /// <para><b>What a stack can and cannot prove.</b> An exception's trace records what the throwing method
    /// CALLED, never who called IT. So a frame's presence is evidence of involvement, but a frame's ABSENCE is
    /// not evidence of innocence — the same contract <see cref="Core.BlamePolicy"/> pins for the universal
    /// breadcrumb. Frames can also fail to resolve entirely (a dynamic method, a stripped/JIT-inlined frame, a
    /// hand-constructed exception that was never thrown), which is a THIRD answer, not a "no":
    /// <see cref="OriginUnknown"/> exists so callers can say "unknown" instead of silently reading it as
    /// "not involved".</para>
    ///
    /// <para><b>Every member is TOTAL — no member may throw.</b> Every caller is an exception handler (a Harmony
    /// finalizer, a catch block, a logger). A helper that faults while reporting a fault destroys the report it
    /// exists to write, and Harmony silently swallows a throw out of a finalizer, so the loss would be invisible.
    /// The catches below are therefore the same sanctioned logger-never-throws boundary
    /// <see cref="HDDebugLog"/> documents, not general exception suppression: nothing here decides gameplay, and
    /// a failed probe degrades to "unknown", which every caller already has to handle.</para>
    /// </summary>
    public static class HDFault
    {
        // A wrapped exception chain (TargetInvocationException, a compat shim's rethrow) is walked
        // innermost-first, but a hand-built exception can be its own InnerException. Bound the walk rather than
        // trust foreign objects not to be cyclic.
        private const int MaxChainDepth = 8;

        // How a real rendered frame line starts, line-anchored so the probe cannot match the same words inside a
        // message. Mono (the runtime RimWorld ships) indents two spaces; the MS CLR indents three. The rebuilt
        // rendering below deliberately emits the Mono form, so it reads identically to a native trace.
        private const string MonoFramePrefix = "\n  at ";
        private const string NetFramePrefix = "\n   at ";

        /// <summary>
        /// True if any resolvable frame belongs to Hauler's Dream — EITHER of its two assemblies. The mod ships
        /// <c>HaulersDream.dll</c> (the game-coupled half) and <c>HaulersDream.Core.dll</c> (the Verse-free policy
        /// half), and a fault can surface with only Core frames when the JIT inlined the HD caller. Asking about
        /// one assembly alone would then classify HD's own bug as foreign and hide it — the exact failure the
        /// no-swallow rule exists to prevent.
        ///
        /// <para>FALSE MEANS "NOT SEEN", NOT "NOT INVOLVED". A false is returned both when HD is genuinely absent
        /// and when nothing in the trace resolves at all — pair it with <see cref="OriginUnknown"/> whenever the
        /// difference matters, and never phrase a false as proof.</para>
        /// </summary>
        /// <param name="ex">The exception to inspect. Null returns false.</param>
        public static bool InvolvesHaulersDream(Exception ex)
        {
            if (ex == null)
                return false;
            var methods = ResolvedMethods(ex);
            for (int i = 0; i < methods.Count; i++)
            {
                if (IsHaulersDream(methods[i].DeclaringType?.Assembly))
                    return true;
            }
            return false;
        }

        /// <summary>True if <paramref name="asm"/> is one of Hauler's Dream's own two assemblies.</summary>
        /// <param name="asm">The assembly to test. Null returns false.</param>
        public static bool IsHaulersDream(Assembly asm)
            => asm != null && (asm == typeof(HDFault).Assembly || asm == CoreAssembly);

        /// <summary>
        /// True when <paramref name="asm"/> is A MOD'S OWN code: some running mod shipped it, it is not the
        /// game's own assembly, and it is not Harmony's shared plumbing. False for vanilla, Harmony, the .NET
        /// runtime, Unity, and anything else nobody in the mod list owns.
        ///
        /// <para>This is the test that keeps HD from naming an innocent party as a culprit. TWO exclusions are
        /// explicit rather than inferred from ownership, for different reasons:
        /// <list type="bullet">
        /// <item><b>Vanilla.</b> <c>ModAssemblyHandler.ReloadAll</c> only ever loads DLLs out of a mod's own
        /// <c>Assemblies/</c> folder, so the game assembly is in nobody's list today — but that is a vanilla
        /// implementation detail, and the guarantee must not depend on it.</item>
        /// <item><b>Harmony.</b> This one is load-bearing, not belt-and-braces: brrainz ships <c>0Harmony.dll</c>
        /// and <c>HarmonyMod.dll</c> INSIDE a mod's <c>Assemblies/</c> folder, so ownership genuinely resolves to
        /// "Harmony". Every patched call in the game runs through that plumbing, so a <c>HarmonyLib</c> frame is
        /// evidence of nothing — yet without this it could be named to the player as the culprit and, worse,
        /// satisfy the quarantine's mod-evidence bar single-handedly.</item>
        /// </list>
        /// The .NET runtime and Unity need no clause: no mod ships them, so ownership already returns null.</para>
        /// </summary>
        /// <param name="asm">The assembly to test. Null returns false.</param>
        public static bool IsModOwned(Assembly asm)
            => asm != null && asm != typeof(Pawn).Assembly && !IsHarmony(asm) && OwningModName(asm) != null;

        /// <summary>
        /// True for Harmony's own assemblies — the patching library every modded call passes through, which is
        /// itself shipped by a mod (see <see cref="IsModOwned"/>). Identity-matched against the runtime HD is
        /// bound to, plus a name check so a second copy loaded from a different path is caught too.
        /// </summary>
        /// <param name="asm">The assembly to test. Null returns false.</param>
        private static bool IsHarmony(Assembly asm)
        {
            if (asm == null)
                return false;
            if (asm == typeof(HarmonyLib.Harmony).Assembly)
                return true;
            string name = SafeAssemblyName(asm);
            return name == "0Harmony" || name == "HarmonyMod";
        }

        // The Verse-free policy assembly, named through a type that only ever lives there.
        private static Assembly CoreAssembly => typeof(Core.BlamePolicy).Assembly;

        /// <summary>
        /// True when the exception carries NO resolvable frames, so nothing can be attributed from it. This is
        /// what separates "absent" from "unknown" for a caller phrasing a blame clause.
        /// </summary>
        /// <param name="ex">The exception to inspect. Null counts as unknown.</param>
        public static bool OriginUnknown(Exception ex) => ex == null || ResolvedMethods(ex).Count == 0;

        /// <summary>
        /// The exception's short type name, read without ever throwing — for a log dedupe key, so that a SECOND,
        /// unrelated fault at the same seam still gets its own full report instead of being hidden by the first
        /// (the same per-(method, exception type) keying the universal breadcrumb uses).
        /// </summary>
        /// <param name="ex">The exception to name. Null returns a placeholder.</param>
        public static string ExceptionTypeName(Exception ex)
        {
            if (ex == null)
                return "none";
            try
            {
                return ex.GetType().Name;
            }
            catch
            {
                return "unknown"; // total-function boundary (see the class doc)
            }
        }

        /// <summary>
        /// The innermost frame that belongs to A MOD other than Hauler's Dream — the most likely foreign origin —
        /// rendered as <c>"&lt;Mod Name&gt; (Namespace.Type.Method)"</c>.
        ///
        /// <para>Only MOD-OWNED frames qualify (<see cref="IsModOwned"/>). This string is shown to the player and
        /// tells them who to report to, so naming <c>mscorlib</c> for a <c>KeyNotFoundException</c> thrown inside
        /// <c>Dictionary`2.get_Item</c>, or a LINQ iterator for a fault in modded LINQ code, is the same
        /// false-blame class issue #236 exists to remove.</para>
        /// </summary>
        /// <param name="ex">The exception to inspect.</param>
        /// <returns>The description, or null when no mod-owned foreign frame is present (no resolvable frames,
        /// or only HD/vanilla/runtime/Harmony frames). A null must be reported as "could not be determined",
        /// never as "no other mod is involved" — see the class doc on what a stack cannot prove.</returns>
        public static string DescribeOrigin(Exception ex)
        {
            if (ex == null)
                return null;
            var methods = ResolvedMethods(ex);
            for (int i = 0; i < methods.Count; i++)
            {
                var type = methods[i].DeclaringType;
                var asm = type?.Assembly;
                if (asm == null || IsHaulersDream(asm) || !IsModOwned(asm))
                    continue;
                return OwningModName(asm) + " (" + type.FullName + "." + methods[i].Name + ")";
            }
            return null;
        }

        /// <summary>
        /// The innermost frame belonging to HAULER'S DREAM ITSELF, rendered as <c>"Namespace.Type.Method"</c> —
        /// the counterpart to <see cref="DescribeOrigin"/>, which skips HD by design so it can answer "who ELSE
        /// is in this stack?".
        ///
        /// <para>It exists so a report can name HD when HD is the one standing in the stack. Without it, a fault
        /// whose only mod-owned frame is HD's reads as "no frame in this stack belongs to a mod" — true of the
        /// FOREIGN search that produced it, and read by anyone else as a denial of the one name that is actually
        /// there. That is the false blame of issue #236 pointed the other way, and it is no more acceptable.</para>
        /// </summary>
        /// <param name="ex">The exception to inspect.</param>
        /// <returns>The frame, or null when no Hauler's Dream frame resolves — which, per the class doc, means
        /// "not seen", never "not involved".</returns>
        public static string DescribeOwnFrame(Exception ex)
        {
            if (ex == null)
                return null;
            var methods = ResolvedMethods(ex);
            for (int i = 0; i < methods.Count; i++)
            {
                var type = methods[i].DeclaringType;
                if (type != null && IsHaulersDream(type.Assembly))
                    return type.FullName + "." + methods[i].Name;
            }
            return null;
        }

        /// <summary>
        /// The innermost frame that resolves AT ALL, whoever owns it, rendered as
        /// <c>"Namespace.Type.Method"</c> — the honest thing to print when <see cref="DescribeOrigin"/> found no
        /// mod to name but the stack is not empty either. Without it a caller has to choose between claiming the
        /// stack holds "only vanilla and HD frames" (false once runtime/Harmony frames are excluded) and saying
        /// nothing.
        /// </summary>
        /// <param name="ex">The exception to inspect.</param>
        /// <returns>The frame, or null when nothing resolves (then <see cref="OriginUnknown"/> is true).</returns>
        public static string DescribeInnermostFrame(Exception ex)
        {
            if (ex == null)
                return null;
            var methods = ResolvedMethods(ex);
            for (int i = 0; i < methods.Count; i++)
            {
                var type = methods[i].DeclaringType;
                if (type != null)
                    return type.FullName + "." + methods[i].Name;
            }
            return null;
        }

        /// <summary>
        /// The player-facing name of the mod that OWNS <paramref name="asm"/>, or null when no running mod does.
        /// The single ownership lookup — <see cref="IsModOwned"/> and <see cref="DescribeOrigin"/> both go
        /// through it, so "is this a mod's code?" is answered one way everywhere.
        ///
        /// <para>Returning NULL rather than a bare assembly name is deliberate: a caller that prints
        /// <c>Assembly-CSharp</c> to a player as if it were a mod to report to is the false-blame class issue
        /// #236 removed. Callers must say "could not be identified" instead.</para>
        /// </summary>
        /// <param name="asm">The assembly to attribute. Null returns null.</param>
        /// <returns>The mod's display name, or null for vanilla / the runtime / Unity / anything unowned. NOTE
        /// that Harmony IS owned (a mod ships it), which is why <see cref="IsModOwned"/> excludes it separately
        /// rather than relying on this.</returns>
        public static string OwningModName(Assembly asm)
        {
            if (asm == null)
                return null;
            try
            {
                var mods = LoadedModManager.RunningModsListForReading;
                if (mods == null)
                    return null;
                for (int i = 0; i < mods.Count; i++)
                {
                    var loaded = mods[i]?.assemblies?.loadedAssemblies;
                    if (loaded == null)
                        continue;
                    for (int j = 0; j < loaded.Count; j++)
                    {
                        if (loaded[j] == asm)
                            return mods[i].Name;
                    }
                }
            }
            catch
            {
                // Total-function boundary (see the class doc): the mod list is read outside HD's control. An
                // unanswerable ownership question degrades to "unowned", which every caller already handles.
            }
            return null;
        }


        /// <summary>
        /// The innermost frame whose declaring type derives from <paramref name="baseType"/> — how a caller asks
        /// "which WorkGiver threw?" without parsing text or trusting a parameter that may not be in scope.
        /// </summary>
        /// <param name="ex">The exception to inspect.</param>
        /// <param name="baseType">The base type/interface to match, e.g. <c>typeof(WorkGiver)</c>.</param>
        /// <returns>The concrete declaring type, or null when no frame matches or nothing resolves.</returns>
        public static Type FirstFrameTypeAssignableTo(Exception ex, Type baseType)
        {
            if (ex == null || baseType == null)
                return null;
            var methods = ResolvedMethods(ex);
            for (int i = 0; i < methods.Count; i++)
            {
                var type = methods[i].DeclaringType;
                if (type != null && baseType.IsAssignableFrom(type))
                    return type;
            }
            return null;
        }

        /// <summary>
        /// Render an exception for a log line in a way a hijacked or deduplicating <c>ToString()</c> cannot
        /// silence: the type and message are always emitted, the frames come from <c>ToString()</c> only when it
        /// actually produced frames, and otherwise are rebuilt from the <see cref="StackFrame"/> objects. The
        /// inner-exception chain is included either way.
        /// </summary>
        /// <param name="ex">The exception to render. Null renders a placeholder rather than throwing.</param>
        public static string Render(Exception ex)
        {
            if (ex == null)
                return "<no exception>";

            // A working ToString() already carries type + message + frames + the whole inner chain, and it is
            // the format players and maintainers recognise — prefer it, but only once we have EVIDENCE it
            // rendered frames rather than Harmony's "duplicate stacktrace" placeholder (or a foreign override's
            // one-liner). The test is LINE-ANCHORED on purpose: a bare " at " also matches ordinary prose in the
            // MESSAGE ("could not find file at path ..."), and a false positive here returns the placeholder
            // instead of the rebuilt frames — reintroducing the exact #236 failure inside its own fix.
            string full = SafeToString(ex);
            if (full != null && (full.IndexOf(MonoFramePrefix, StringComparison.Ordinal) >= 0
                || full.IndexOf(NetFramePrefix, StringComparison.Ordinal) >= 0))
                return full;

            var sb = new StringBuilder();
            RenderRebuilt(sb, ex, 0);
            return sb.ToString();
        }

        // The fallback rendering: type + message from the members that cannot be intercepted by a trace
        // renderer, then the frame list rebuilt from objects, then the same for each inner exception.
        private static void RenderRebuilt(StringBuilder sb, Exception ex, int depth)
        {
            if (ex == null || depth > MaxChainDepth)
                return;

            sb.Append(SafeTypeName(ex)).Append(": ").Append(SafeMessage(ex));

            var frames = FramesOf(ex);
            if (frames != null)
            {
                for (int i = 0; i < frames.Length; i++)
                {
                    var method = MethodOf(frames[i]);
                    if (method == null)
                    {
                        // A frame the runtime cannot name — in practice a Harmony-patched method, which runs as
                        // a DynamicMethod (Mono prints these as "at (wrapper dynamic-method) …_Patch1"). This
                        // rendering is used EXACTLY when Harmony's dedupe placeholder suppressed ToString(), so
                        // silently skipping them would delete the one signal that a method was patched, with no
                        // sign anything was dropped. Emit a placeholder so depth and ordering survive.
                        sb.Append(MonoFramePrefix).Append("(wrapper dynamic-method) <unresolvable frame, "
                            + "typically a Harmony-patched method>");
                        continue;
                    }
                    sb.Append(MonoFramePrefix).Append(method.DeclaringType?.FullName ?? "<unknown type>")
                        .Append('.').Append(method.Name).Append(" ()");
                }
            }

            var inner = InnerOf(ex);
            if (inner == null || ReferenceEquals(inner, ex))
                return;
            sb.Append("\n ---> inner exception: ");
            RenderRebuilt(sb, inner, depth + 1);
        }

        // Every resolvable frame method of `ex` and its inner-exception chain, INNERMOST FIRST: the deepest
        // inner exception's frames come before the outer ones, and within one exception frame 0 (the throw
        // site) comes first. That ordering is what makes "the first foreign frame" mean "the origin".
        // Returns an empty list (never null) when nothing resolves.
        private static List<MethodBase> ResolvedMethods(Exception ex)
        {
            var methods = new List<MethodBase>();
            Collect(ex, methods, 0);
            return methods;
        }

        private static void Collect(Exception ex, List<MethodBase> into, int depth)
        {
            if (ex == null || depth > MaxChainDepth)
                return;

            var inner = InnerOf(ex);
            if (inner != null && !ReferenceEquals(inner, ex))
                Collect(inner, into, depth + 1);

            var frames = FramesOf(ex);
            if (frames == null)
                return;
            for (int i = 0; i < frames.Length; i++)
            {
                var method = MethodOf(frames[i]);
                if (method != null)
                    into.Add(method);
            }
        }

        // --- the total wrappers around everything that can fault on a foreign exception object ---

        private static StackFrame[] FramesOf(Exception ex)
        {
            try
            {
                return new StackTrace(ex, false).GetFrames(); // null when the exception carries no frames
            }
            catch
            {
                return null; // total-function boundary; the caller reads this as "unknown", never as "absent"
            }
        }

        private static MethodBase MethodOf(StackFrame frame)
        {
            try
            {
                return frame?.GetMethod(); // can be null for a dynamic/stripped frame
            }
            catch
            {
                return null; // total-function boundary (see the class doc)
            }
        }

        private static Exception InnerOf(Exception ex)
        {
            try
            {
                return ex.InnerException; // a foreign override could fault here
            }
            catch
            {
                return null; // total-function boundary (see the class doc)
            }
        }

        private static string SafeToString(Exception ex)
        {
            try
            {
                return ex.ToString();
            }
            catch
            {
                return null; // an overridden ToString() threw -> fall back to the rebuilt rendering
            }
        }

        private static string SafeTypeName(Exception ex)
        {
            try
            {
                return ex.GetType().FullName;
            }
            catch
            {
                return "<unknown exception type>"; // total-function boundary (see the class doc)
            }
        }

        /// <summary>
        /// An exception's message, read without ever throwing — the message counterpart to
        /// <see cref="ExceptionTypeName"/>, and the single copy of that boundary. A foreign <c>Message</c>
        /// override CAN throw, and callers here run inside Harmony finalizers, where a throw is SWALLOWED — so
        /// an unguarded read makes the very line that reports the fault disappear with no trace of why.
        /// </summary>
        /// <param name="ex">The exception to read. Null returns a placeholder.</param>
        /// <returns>The message, or a bracketed placeholder naming what went wrong. Never null.</returns>
        internal static string SafeMessage(Exception ex)
        {
            if (ex == null)
                return "<no exception>";
            try
            {
                return ex.Message;
            }
            catch (Exception messageFailed)
            {
                // A type name cannot throw, so name what failed instead of losing the whole line.
                return "<Message threw " + messageFailed.GetType().Name + ">";
            }
        }

        private static string SafeAssemblyName(Assembly asm)
        {
            try
            {
                return asm.GetName().Name;
            }
            catch
            {
                return "an unidentified assembly"; // total-function boundary (see the class doc)
            }
        }
    }
}
