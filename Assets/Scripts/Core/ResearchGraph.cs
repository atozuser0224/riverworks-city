using System;
using System.Collections.Generic;
using System.Linq;

namespace Riverworks
{
    public readonly struct ResearchEdge : IEquatable<ResearchEdge>
    {
        public ResearchEdge(TechId source, TechId target)
        {
            Source = source;
            Target = target;
        }

        public TechId Source { get; }
        public TechId Target { get; }

        public bool Equals(ResearchEdge other) => Source == other.Source && Target == other.Target;
        public override bool Equals(object obj) => obj is ResearchEdge other && Equals(other);
        public override int GetHashCode() => ((int)Source * 397) ^ (int)Target;
    }

    public sealed class ResearchPlanSummary
    {
        internal ResearchPlanSummary(IReadOnlyList<TechId> technologies, int knowledgeCost, int coinCost, int durationDays)
        {
            Technologies = technologies;
            KnowledgeCost = knowledgeCost;
            CoinCost = coinCost;
            DurationDays = durationDays;
        }

        public IReadOnlyList<TechId> Technologies { get; }
        public int KnowledgeCost { get; }
        public int CoinCost { get; }
        public int DurationDays { get; }
    }

    /// <summary>
    /// Immutable, validated view of the research prerequisite graph for UI and planning.
    /// </summary>
    public sealed class ResearchGraph
    {
        readonly IReadOnlyList<TechSpec> orderedTechnologies;
        readonly IReadOnlyList<ResearchEdge> edges;
        readonly Dictionary<TechId, TechSpec> byId;
        readonly Dictionary<TechId, IReadOnlyList<TechId>> prerequisites;
        readonly Dictionary<TechId, IReadOnlyList<TechId>> successors;
        readonly Dictionary<TechId, int> depths;

        public ResearchGraph(IEnumerable<TechSpec> specs)
        {
            if (specs == null) throw new ArgumentException("Research technology collection cannot be null.", nameof(specs));

            TechSpec[] source = specs.ToArray();
            if (source.Length == 0) throw new ArgumentException("Research technology collection cannot be empty.", nameof(specs));
            if (source.Any(spec => spec == null)) throw new ArgumentException("Research technology collection cannot contain a null technology.", nameof(specs));
            if (source.Any(spec => spec.Id == TechId.None || !Enum.IsDefined(typeof(TechId), spec.Id)))
                throw new ArgumentException("Every research technology must have a known, non-None ID.", nameof(specs));

            TechId duplicateId = source.GroupBy(spec => spec.Id).Where(group => group.Count() > 1).Select(group => group.Key).FirstOrDefault();
            if (duplicateId != TechId.None)
                throw new ArgumentException($"Duplicate research technology ID: {duplicateId}.", nameof(specs));

            byId = source.ToDictionary(spec => spec.Id);
            prerequisites = new Dictionary<TechId, IReadOnlyList<TechId>>();
            var successorLists = source.ToDictionary(spec => spec.Id, _ => new List<TechId>());
            var edgeList = new List<ResearchEdge>();

            foreach (TechSpec spec in source)
            {
                if (spec.Prerequisites == null)
                    throw new ArgumentException($"Technology {spec.Id} has a null prerequisite collection.", nameof(specs));

                TechId[] required = spec.Prerequisites.ToArray();
                if (required.Any(id => id == TechId.None || !Enum.IsDefined(typeof(TechId), id)))
                    throw new ArgumentException($"Technology {spec.Id} has an unknown prerequisite.", nameof(specs));
                if (required.Contains(spec.Id))
                    throw new ArgumentException($"Technology {spec.Id} cannot require itself.", nameof(specs));
                TechId duplicateRequired = required.GroupBy(id => id).Where(group => group.Count() > 1).Select(group => group.Key).FirstOrDefault();
                if (duplicateRequired != TechId.None)
                    throw new ArgumentException($"Technology {spec.Id} repeats prerequisite {duplicateRequired}.", nameof(specs));
                TechId dangling = required.FirstOrDefault(id => !byId.ContainsKey(id));
                if (dangling != TechId.None)
                    throw new ArgumentException($"Technology {spec.Id} references missing prerequisite {dangling}.", nameof(specs));

                prerequisites.Add(spec.Id, Array.AsReadOnly(required));
                foreach (TechId requiredId in required)
                {
                    successorLists[requiredId].Add(spec.Id);
                    edgeList.Add(new ResearchEdge(requiredId, spec.Id));
                }
            }

            TechSpec[] ordered = StableTopologicalOrder(source, prerequisites, successorLists);
            orderedTechnologies = Array.AsReadOnly(ordered);
            edges = edgeList.AsReadOnly();

            var rank = ordered.Select((spec, index) => new { spec.Id, Index = index }).ToDictionary(item => item.Id, item => item.Index);
            successors = successorLists.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<TechId>)Array.AsReadOnly(pair.Value.OrderBy(id => rank[id]).ToArray()));

            depths = new Dictionary<TechId, int>();
            foreach (TechSpec spec in ordered)
                depths[spec.Id] = prerequisites[spec.Id].Count == 0 ? 0 : prerequisites[spec.Id].Max(id => depths[id]) + 1;
        }

        public IReadOnlyList<TechSpec> OrderedTechnologies => orderedTechnologies;
        public IReadOnlyList<ResearchEdge> Edges => edges;

        public IReadOnlyList<TechId> Ancestors(TechId id)
        {
            RequireKnown(id, nameof(id));
            var found = new HashSet<TechId>();
            CollectAncestors(id, found);
            return InTopologicalOrder(found);
        }

        public IReadOnlyList<TechId> Descendants(TechId id)
        {
            RequireKnown(id, nameof(id));
            var found = new HashSet<TechId>();
            CollectDescendants(id, found);
            return InTopologicalOrder(found);
        }

        public IReadOnlyList<TechId> PlanTo(TechId target, IEnumerable<TechId> completed)
        {
            RequireKnown(target, nameof(target));
            if (completed == null) throw new ArgumentNullException(nameof(completed));
            var completedSet = new HashSet<TechId>();
            foreach (TechId id in completed)
            {
                RequireKnown(id, nameof(completed));
                completedSet.Add(id);
            }

            var needed = new HashSet<TechId>(Ancestors(target)) { target };
            needed.ExceptWith(completedSet);
            return InTopologicalOrder(needed);
        }

        public ResearchPlanSummary PlanSummaryTo(TechId target, IEnumerable<TechId> completed)
        {
            IReadOnlyList<TechId> plan = PlanTo(target, completed);
            return new ResearchPlanSummary(
                plan,
                plan.Sum(id => byId[id].ResearchCost),
                plan.Sum(id => byId[id].CoinCost),
                plan.Sum(id => byId[id].DurationDays));
        }

        public int Depth(TechId id)
        {
            RequireKnown(id, nameof(id));
            return depths[id];
        }

        public IReadOnlyList<TechId> DirectSuccessors(TechId id)
        {
            RequireKnown(id, nameof(id));
            return successors[id];
        }

        static TechSpec[] StableTopologicalOrder(
            TechSpec[] source,
            IReadOnlyDictionary<TechId, IReadOnlyList<TechId>> prerequisites,
            IReadOnlyDictionary<TechId, List<TechId>> successors)
        {
            var sourceIndex = source.Select((spec, index) => new { spec.Id, Index = index }).ToDictionary(item => item.Id, item => item.Index);
            var indegree = source.ToDictionary(spec => spec.Id, spec => prerequisites[spec.Id].Count);
            var ready = new SortedSet<int>(source.Where(spec => indegree[spec.Id] == 0).Select(spec => sourceIndex[spec.Id]));
            var ordered = new List<TechSpec>(source.Length);

            while (ready.Count > 0)
            {
                int index = ready.Min;
                ready.Remove(index);
                TechSpec spec = source[index];
                ordered.Add(spec);
                foreach (TechId successor in successors[spec.Id])
                {
                    indegree[successor]--;
                    if (indegree[successor] == 0) ready.Add(sourceIndex[successor]);
                }
            }

            if (ordered.Count != source.Length)
                throw new ArgumentException("Research technology prerequisites contain a cycle.", nameof(source));
            return ordered.ToArray();
        }

        void CollectAncestors(TechId id, HashSet<TechId> found)
        {
            foreach (TechId prerequisite in prerequisites[id])
                if (found.Add(prerequisite)) CollectAncestors(prerequisite, found);
        }

        void CollectDescendants(TechId id, HashSet<TechId> found)
        {
            foreach (TechId successor in successors[id])
                if (found.Add(successor)) CollectDescendants(successor, found);
        }

        IReadOnlyList<TechId> InTopologicalOrder(HashSet<TechId> ids) =>
            Array.AsReadOnly(orderedTechnologies.Where(spec => ids.Contains(spec.Id)).Select(spec => spec.Id).ToArray());

        void RequireKnown(TechId id, string parameterName)
        {
            if (!byId.ContainsKey(id)) throw new ArgumentException($"Unknown research technology ID: {id}.", parameterName);
        }
    }
}
