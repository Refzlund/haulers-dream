using System;
using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>Saved player intent and load-session provenance; live custody is supplied by the game layer.</summary>
    public sealed class TransporterUnloadState
    {
        private readonly HashSet<int> flags;
        private readonly HashSet<int> sessions;

        public TransporterUnloadState(IEnumerable<int> flagged = null, IEnumerable<int> loading = null)
        {
            flags = flagged == null ? new HashSet<int>() : new HashSet<int>(flagged);
            sessions = loading == null ? new HashSet<int>() : new HashSet<int>(loading);
            flags.RemoveWhere(id => id <= 0);
            sessions.RemoveWhere(id => id < 0);
        }

        public bool IsFlagged(int thingId) => flags.Contains(thingId);
        public bool HasRecords => flags.Count != 0 || sessions.Count != 0;
        // Disabled flags are dormant. They never block loading or authorize an unload.
        public bool IsActive(int thingId, bool enabled) => enabled && IsFlagged(thingId);
        public bool SetFlag(int thingId, bool on)
            => thingId > 0 && (on ? flags.Add(thingId) : flags.Remove(thingId));
        public bool HasSession(int groupId) => groupId >= 0 && sessions.Contains(groupId);

        public bool AcceptLoad(bool accepted, int groupId, IEnumerable<int> members)
        {
            if (!accepted || groupId < 0) return false;
            bool changed = sessions.Add(groupId);
            if (members != null)
                foreach (int id in members) changed |= flags.Remove(id);
            return changed;
        }

        public bool EndLoad(int groupId) => groupId >= 0 && sessions.Remove(groupId);

        // Missing history in an old save cannot prove a pending manifest abandoned. Treat any current
        // manifest conservatively as loading until fulfilled or explicitly cancelled. A drained manifest
        // still cannot release a courier or boarding lord with actual custody.
        public bool LoadOwns(int groupId, bool hasManifest, bool hasLiveCustody)
            => hasLiveCustody || hasManifest;

        public bool Prune(Predicate<int> transporterExists, Predicate<int> groupExists)
        {
            int removed = flags.RemoveWhere(id => !transporterExists(id));
            removed += sessions.RemoveWhere(id => !groupExists(id));
            return removed != 0;
        }

        public List<int> CaptureFlags() { var result = new List<int>(flags); result.Sort(); return result; }
        public List<int> CaptureSessions() { var result = new List<int>(sessions); result.Sort(); return result; }
    }
}
