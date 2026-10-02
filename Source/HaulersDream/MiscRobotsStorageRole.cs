using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace HaulersDream
{
    internal enum MiscRobotStorageRole
    {
        Unrelated,
        Assigned,
        Unassigned,
        Unsupported
    }

    /// <summary>A robot-role answer only. It does not grant ownership, control, capacity or permission to haul.</summary>
    internal readonly struct MiscRobotStorageRoleResult
    {
        internal readonly MiscRobotStorageRole Role;
        internal readonly int Priority;
        internal readonly string Diagnostic;

        internal MiscRobotStorageRoleResult(MiscRobotStorageRole role, int priority = 0, string diagnostic = null)
        {
            Role = role;
            Priority = priority;
            Diagnostic = diagnostic;
        }
    }

    /// <summary>
    /// Soft dependency on Misc. Robots' actual storage-work authority. X2_AIRobot.GetPriority reads def2,
    /// then returns the FIRST robotWorkTypes entry whose workTypeDef is the requested live reference.
    /// Its simulated Pawn_WorkSettings and mechEnabledWorkTypes do not express that permission.
    ///
    /// Only metadata is cached, keyed by the actual robot base Type. Every query reads the live def2/list/row;
    /// no per-pawn, per-def or per-tick role result is retained. This also avoids binding a different loaded
    /// assembly generation via a global type-name search. No optional-mod method or property is invoked.
    /// Call only at unrelated storage-intake admission, never at a shared yield or cargo-recovery gate.
    /// </summary>
    internal static class MiscRobotsStorageRole
    {
        private const string PawnTypeName = "AIRobot.X2_AIRobot";
        private const string DefTypeName = "AIRobot.X2_ThingDef_AIRobot";
        private const string RowTypeName = "AIRobot.X2_ThingDef_AIRobot+RobotWorkTypes";
        private const BindingFlags Fields = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        private sealed class Binding
        {
            internal Type DefType;
            internal Type ListType;
            internal Type RowType;
            internal FieldInfo Def;
            internal FieldInfo WorkTypes;
            internal FieldInfo WorkType;
            internal FieldInfo Priority;
            internal string Problem;
        }

        private static readonly ConcurrentDictionary<Type, Binding> bindings = new ConcurrentDictionary<Type, Binding>();
        private static readonly Func<Type, Binding> bind = Bind;
        private static readonly ConcurrentDictionary<string, byte> warned = new ConcurrentDictionary<string, byte>();

        /// <summary>
        /// Returns Unrelated when neither pawn nor current def belongs to the recognized class families. A null def2 is
        /// Unassigned, matching native GetPriority's zero return. A recognized but unreadable contract is
        /// Unsupported with a diagnostic, never silently treated as an unrelated pawn or a positive role.
        /// The caller still owns all ordinary command and feature checks. A null pawn is Unrelated, not valid.
        /// </summary>
        internal static MiscRobotStorageRoleResult Query(Pawn pawn)
        {
            Type robotType = FindType(pawn?.GetType(), PawnTypeName);
            if (robotType == null)
                return FindType(pawn?.def?.GetType(), DefTypeName) == null
                    ? new MiscRobotStorageRoleResult(MiscRobotStorageRole.Unrelated)
                    : Unsupported("A recognized robot definition is attached to a pawn outside the X2_AIRobot class hierarchy.");

            try
            {
                var api = bindings.GetOrAdd(robotType, bind);
                if (api.Problem != null)
                    return Unsupported(api.Problem);
                var haul = WorkTypeDefOf.Hauling;
                if (haul == null)
                    return Unsupported("The native Hauling work type is not initialized.");
                object definition = api.Def.GetValue(pawn);
                if (definition == null)
                    return new MiscRobotStorageRoleResult(MiscRobotStorageRole.Unassigned);
                if (!api.DefType.IsInstanceOfType(definition))
                    return Unsupported("X2_AIRobot.def2 is not the bound robot definition type.");
                object roles = api.WorkTypes.GetValue(definition);
                if (roles == null)
                    return Unsupported("X2_ThingDef_AIRobot.robotWorkTypes is null.");
                // Admit the shipped closed BCL List exactly, so enumeration cannot dispatch a custom
                // collection's code. Individual row subclasses still use the bound base fields, as native does.
                if (roles.GetType() != api.ListType)
                    return Unsupported("robotWorkTypes is not the bound List<RobotWorkTypes> runtime type.");

                // List's enumerator is finite and detects mutation during traversal like native foreach.
                // Return immediately on the first match: a later duplicate must not override a zero/negative
                // priority, and an unreadable later row must not invalidate an earlier native return.
                foreach (object row in (IEnumerable)roles)
                {
                    if (row == null || !api.RowType.IsInstanceOfType(row))
                        return Unsupported("robotWorkTypes contains an unreadable row before the Hauling match.");
                    if (!ReferenceEquals(api.WorkType.GetValue(row), haul))
                        continue;
                    int priority = (int)api.Priority.GetValue(row);
                    return new MiscRobotStorageRoleResult(priority > 0
                        ? MiscRobotStorageRole.Assigned : MiscRobotStorageRole.Unassigned, priority);
                }
                return new MiscRobotStorageRoleResult(MiscRobotStorageRole.Unassigned);
            }
            catch (Exception ex) when (IsContractFailure(ex))
            {
                return Unsupported("The robot storage-role fields could not be read (" + ex.GetType().Name + ").");
            }
        }

        /// <summary>
        /// Adds only this optional integration's restriction. Unrelated pawns retain their caller's existing
        /// rules; a recognized unassigned/unsupported robot receives no new unrelated storage pickup.
        /// Existing loads and work yields must not pass through this method.
        /// </summary>
        internal static bool AllowsNewStorageIntake(Pawn pawn)
        {
            if (pawn == null)
                return false;
            var result = Query(pawn);
            if (result.Role == MiscRobotStorageRole.Unsupported && warned.TryAdd(result.Diagnostic, 0))
                HDLog.Warn("Misc. Robots storage hauling is unavailable for the encountered robot API: "
                    + result.Diagnostic + " New storage pickups are declined; existing cargo recovery and own-work yields retain their separate paths.");
            return result.Role == MiscRobotStorageRole.Unrelated || result.Role == MiscRobotStorageRole.Assigned;
        }

        private static Type FindType(Type type, string fullName)
        {
            for (var current = type; current != null; current = current.BaseType)
                if (current.FullName == fullName)
                    return current;
            return null;
        }

        private static Binding Bind(Type robotType)
        {
            // These types must belong to the same actual assembly as the recognized pawn base. No hash or
            // version pin excludes a compatible optional-mod update; the concrete field contract is checked.
            var assembly = robotType.Assembly;
            var api = new Binding
            {
                DefType = assembly.GetType(DefTypeName, throwOnError: false),
                RowType = assembly.GetType(RowTypeName, throwOnError: false)
            };
            if (!typeof(Pawn).IsAssignableFrom(robotType)
                || api.DefType == null || api.DefType.Assembly != assembly || !typeof(ThingDef).IsAssignableFrom(api.DefType)
                || api.RowType == null || api.RowType.Assembly != assembly
                || !api.RowType.IsClass || api.RowType.DeclaringType != api.DefType)
            {
                api.Problem = "The recognized X2_AIRobot/robot definition/RobotWorkTypes type contract is unavailable.";
                return api;
            }
            api.ListType = typeof(List<>).MakeGenericType(api.RowType);
            api.Def = robotType.GetField("def2", Fields);
            api.WorkTypes = api.DefType.GetField("robotWorkTypes", Fields);
            api.WorkType = api.RowType.GetField("workTypeDef", Fields);
            api.Priority = api.RowType.GetField("priority", Fields);
            if (!HasType(api.Def, api.DefType) || !HasType(api.WorkTypes, api.ListType)
                || !HasType(api.WorkType, typeof(WorkTypeDef)) || !HasType(api.Priority, typeof(int)))
                api.Problem = "Expected public instance fields def2, robotWorkTypes, workTypeDef and int priority did not bind exactly.";
            return api;
        }

        private static bool HasType(FieldInfo field, Type expected)
            => field != null && !field.IsStatic && field.FieldType == expected;

        private static MiscRobotStorageRoleResult Unsupported(string reason)
            => new MiscRobotStorageRoleResult(MiscRobotStorageRole.Unsupported, diagnostic: reason);

        private static bool IsContractFailure(Exception ex)
            => ex is ArgumentException || ex is MemberAccessException || ex is TargetException
                || ex is TypeLoadException || ex is NotSupportedException || ex is InvalidOperationException
                || ex is InvalidCastException;
    }
}
