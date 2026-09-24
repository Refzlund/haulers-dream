using System;
using System.Collections.Generic;
using HaulersDream.Core;

namespace HaulersDream
{
    internal sealed class ProjectionSubjectStop : Exception
    {
        internal ProjectionSubjectResult Result { get; }
        internal ProjectionSubjectStop(ProjectionSubjectResult result) { Result = result; }
    }

    // State consists only of reference identities and primitive integer values.
    // Never dispatch Equals/GetHashCode on a mod-defined Thing, comp or owner.
    internal sealed class ProjectionSubjectState
    {
        private readonly object[] references;
        private readonly long[] values;
        private readonly ISubjectListSeal[] lists;
        internal ProjectionSubjectState(object[] references, long[] values, ISubjectListSeal[] lists)
        { this.references = references; this.values = values; this.lists = lists; }
        internal void CheckLists(ProjectionWorkBudget work)
        {
            SubjectStateBuilder.Charge(work, ProjectionWorkKind.GuardChecks, lists.Length);
            foreach (var list in lists)
                if (!list.Matches()) throw new ProjectionSubjectStop(ProjectionSubjectResult.Changed);
        }
        internal bool Matches(ProjectionSubjectState other, ProjectionWorkBudget work)
        {
            SubjectStateBuilder.Charge(work, ProjectionWorkKind.GuardChecks, 2L + references.Length + values.Length);
            if (references.Length != other.references.Length || values.Length != other.values.Length) return false;
            for (int i = 0; i < references.Length; i++)
                if (!ReferenceEquals(references[i], other.references[i])) return false;
            for (int i = 0; i < values.Length; i++) if (values[i] != other.values[i]) return false;
            return true;
        }
    }

    internal interface ISubjectListSeal { bool Matches(); }
    internal sealed class SubjectListSeal<T> : ISubjectListSeal
    {
        private readonly List<T> list;
        private readonly ProjectionListGuard<T> guard;
        internal SubjectListSeal(List<T> list) { this.list = list; guard = new ProjectionListGuard<T>(list); }
        public bool Matches() => guard.Matches(list);
    }

    internal sealed class SubjectStateBuilder
    {
        private readonly ProjectionWorkBudget work;
        private readonly int limit;
        private readonly List<object> references = new List<object>();
        private readonly List<long> values = new List<long>();
        private readonly List<ISubjectListSeal> lists = new List<ISubjectListSeal>();
        private int records;
        internal SubjectStateBuilder(ProjectionWorkBudget work, int limit) { this.work = work; this.limit = limit; }
        internal static void Charge(ProjectionWorkBudget work, ProjectionWorkKind kind, long count)
        {
            if (work == null || !work.TryCharge(ProjectionWork.Cost(kind, count)))
                throw new ProjectionSubjectStop(ProjectionSubjectResult.WorkLimit);
        }
        internal void Guards(long count) => Charge(work, ProjectionWorkKind.GuardChecks, count);
        internal void Record()
        {
            if (records >= limit) throw new ProjectionSubjectStop(ProjectionSubjectResult.StateLimit);
            Charge(work, ProjectionWorkKind.OutputRecords, 1); records++;
        }
        internal void Ref(object value) { Record(); references.Add(value); }
        internal void Value(long value) { Record(); values.Add(value); }
        internal void List<T>(List<T> list, int maximum)
        {
            Guards(3); Ref(list); Value(list == null ? -1 : list.Count);
            if (list == null) return;
            if (list.Count > maximum) throw new ProjectionSubjectStop(ProjectionSubjectResult.StateLimit);
            Record(); lists.Add(new SubjectListSeal<T>(list));
        }
        internal ProjectionSubjectState Finish()
        {
            Record();
            Guards(3L + references.Count + values.Count + lists.Count);
            var result = new ProjectionSubjectState(references.ToArray(), values.ToArray(), lists.ToArray());
            result.CheckLists(work);
            return result;
        }
    }
}
