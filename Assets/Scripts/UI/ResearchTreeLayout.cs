using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Riverworks
{
    public sealed class ResearchEraBand
    {
        public ResearchEraBand(Era era, float minX, float maxX)
        {
            Era = era;
            MinX = minX;
            MaxX = maxX;
        }

        public Era Era { get; }
        public float MinX { get; }
        public float MaxX { get; }
    }

    /// <summary>
    /// Deterministic, positive-Y-down geometry for the scrollable research canvas.
    /// Nodes occupy topological columns; edges use only column gaps and row corridors.
    /// </summary>
    public sealed class ResearchTreeLayout
    {
        public const float NodeWidth = 224f;
        public const float NodeHeight = 156f;
        public const float ColumnGap = 80f;
        public const float RowGap = 24f;
        public const float LeftMargin = 24f;
        public const float TopMargin = 64f;

        const float RightMargin = 24f;
        const float BottomMargin = 24f;
        const float CorridorInset = 5f;

        readonly Dictionary<TechId, Rect> nodeRects = new Dictionary<TechId, Rect>();
        readonly Dictionary<ResearchEdge, IReadOnlyList<Vector2>> edgePoints = new Dictionary<ResearchEdge, IReadOnlyList<Vector2>>();
        readonly IReadOnlyList<ResearchEraBand> eraBands;

        public ResearchTreeLayout(ResearchGraph graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            TechSpec[] technologies = graph.OrderedTechnologies.ToArray();
            if (technologies.Length == 0) throw new ArgumentException("The research graph cannot be empty.", nameof(graph));

            int maximumDepth = technologies.Max(technology => graph.Depth(technology.Id));
            var sourceOrder = technologies.Select((technology, index) => new { technology.Id, Index = index })
                .ToDictionary(item => item.Id, item => item.Index);
            var columns = new List<TechId>[maximumDepth + 1];
            for (int depth = 0; depth <= maximumDepth; depth++)
                columns[depth] = technologies.Where(technology => graph.Depth(technology.Id) == depth)
                    .Select(technology => technology.Id).ToList();

            OrderRows(graph, columns, sourceOrder);
            int maximumRows = columns.Max(column => column.Count);
            for (int depth = 0; depth < columns.Length; depth++)
            for (int row = 0; row < columns[depth].Count; row++)
            {
                float x = LeftMargin + depth * (NodeWidth + ColumnGap);
                float y = TopMargin + row * (NodeHeight + RowGap);
                nodeRects.Add(columns[depth][row], new Rect(x, y, NodeWidth, NodeHeight));
            }

            float width = LeftMargin + columns.Length * NodeWidth + Math.Max(0, columns.Length - 1) * ColumnGap + RightMargin;
            float height = TopMargin + maximumRows * NodeHeight + Math.Max(0, maximumRows - 1) * RowGap + BottomMargin;
            ContentSize = new Vector2(width, height);
            eraBands = BuildEraBands(technologies);
            BuildEdgePaths(graph, maximumRows);
        }

        public Vector2 ContentSize { get; }
        public IReadOnlyList<ResearchEraBand> EraBands => eraBands;

        public Rect NodeRect(TechId id)
        {
            if (!nodeRects.TryGetValue(id, out Rect rect))
                throw new ArgumentException("Unknown research technology ID: " + id + ".", nameof(id));
            return rect;
        }

        public IReadOnlyList<Vector2> EdgePoints(ResearchEdge edge)
        {
            if (!edgePoints.TryGetValue(edge, out IReadOnlyList<Vector2> points))
                throw new ArgumentException("Unknown research edge: " + edge.Source + " -> " + edge.Target + ".", nameof(edge));
            return points;
        }

        static void OrderRows(ResearchGraph graph, IList<List<TechId>> columns, IReadOnlyDictionary<TechId, int> sourceOrder)
        {
            var rows = new Dictionary<TechId, float>();
            for (int depth = 0; depth < columns.Count; depth++)
            for (int row = 0; row < columns[depth].Count; row++) rows[columns[depth][row]] = row;

            // Alternating barycentric sweeps reduce crossings while the original topological
            // index provides a stable tie break. The two roots consequently remain rows 0 and 1.
            for (int pass = 0; pass < 4; pass++)
            {
                for (int depth = 1; depth < columns.Count; depth++)
                    SortByNeighbours(columns[depth], id => graph.Ancestors(id).Where(rows.ContainsKey), rows, sourceOrder);
                RefreshRows(columns, rows);
                for (int depth = columns.Count - 2; depth >= 1; depth--)
                    SortByNeighbours(columns[depth], id => graph.Descendants(id).Where(rows.ContainsKey), rows, sourceOrder);
                RefreshRows(columns, rows);
            }
        }

        static void SortByNeighbours(List<TechId> column, Func<TechId, IEnumerable<TechId>> neighbours,
            IReadOnlyDictionary<TechId, float> rows, IReadOnlyDictionary<TechId, int> sourceOrder)
        {
            column.Sort((first, second) =>
            {
                float a = AverageRow(neighbours(first), rows, sourceOrder[first]);
                float b = AverageRow(neighbours(second), rows, sourceOrder[second]);
                int comparison = a.CompareTo(b);
                return comparison != 0 ? comparison : sourceOrder[first].CompareTo(sourceOrder[second]);
            });
        }

        static float AverageRow(IEnumerable<TechId> ids, IReadOnlyDictionary<TechId, float> rows, int fallback)
        {
            float total = 0f;
            int count = 0;
            foreach (TechId id in ids)
            {
                if (!rows.TryGetValue(id, out float row)) continue;
                total += row;
                count++;
            }
            return count == 0 ? fallback : total / count;
        }

        static void RefreshRows(IEnumerable<List<TechId>> columns, IDictionary<TechId, float> rows)
        {
            foreach (List<TechId> column in columns)
            for (int row = 0; row < column.Count; row++) rows[column[row]] = row;
        }

        IReadOnlyList<ResearchEraBand> BuildEraBands(IEnumerable<TechSpec> technologies)
        {
            var bands = new List<ResearchEraBand>();
            foreach (IGrouping<Era, TechSpec> group in technologies.GroupBy(technology => technology.Era).OrderBy(group => group.Min(item => nodeRects[item.Id].xMin)))
            {
                float min = group.Min(item => nodeRects[item.Id].xMin);
                float max = group.Max(item => nodeRects[item.Id].xMax);
                bands.Add(new ResearchEraBand(group.Key, min, max));
            }
            return bands.AsReadOnly();
        }

        void BuildEdgePaths(ResearchGraph graph, int maximumRows)
        {
            ResearchEdge[] orderedEdges = graph.Edges
                .OrderBy(edge => graph.Depth(edge.Source)).ThenBy(edge => graph.Depth(edge.Target))
                .ThenBy(edge => (int)edge.Source).ThenBy(edge => (int)edge.Target).ToArray();
            var gapCounts = new Dictionary<int, int>();
            var corridorCounts = new Dictionary<int, int>();

            foreach (ResearchEdge edge in orderedEdges)
            {
                Rect source = nodeRects[edge.Source];
                Rect target = nodeRects[edge.Target];
                int sourceDepth = graph.Depth(edge.Source);
                int targetDepth = graph.Depth(edge.Target);
                Vector2 start = new Vector2(source.xMax, source.center.y);
                Vector2 end = new Vector2(target.xMin, target.center.y);

                if (targetDepth == sourceDepth + 1)
                {
                    int lane = TakeLane(gapCounts, sourceDepth);
                    float channelX = source.xMax + LaneOffset(lane, ColumnGap);
                    edgePoints.Add(edge, ReadOnlyPath(start, new Vector2(channelX, start.y), new Vector2(channelX, end.y), end));
                    continue;
                }

                int preferredCorridor = maximumRows > 1
                    ? Mathf.Clamp(Mathf.RoundToInt(((start.y + end.y) * .5f - TopMargin) / (NodeHeight + RowGap)), 1, maximumRows - 1)
                    : 0;
                int corridorLane = TakeLane(corridorCounts, preferredCorridor);
                float corridorY = CorridorY(preferredCorridor, maximumRows) +
                    PackedOffset(corridorLane, preferredCorridor == 0 ? 0f : RowGap - CorridorInset * 2f);
                int outgoingLane = TakeLane(gapCounts, sourceDepth);
                int incomingGap = targetDepth - 1;
                int incomingLane = TakeLane(gapCounts, incomingGap);
                float outgoingX = source.xMax + LaneOffset(outgoingLane, ColumnGap);
                float incomingX = target.xMin - LaneOffset(incomingLane, ColumnGap);
                edgePoints.Add(edge, ReadOnlyPath(start, new Vector2(outgoingX, start.y), new Vector2(outgoingX, corridorY),
                    new Vector2(incomingX, corridorY), new Vector2(incomingX, end.y), end));
            }
        }

        static int TakeLane(IDictionary<int, int> counts, int key)
        {
            counts.TryGetValue(key, out int lane);
            counts[key] = lane + 1;
            return lane;
        }

        static float LaneOffset(int lane, float span)
        {
            // A low-discrepancy sequence distributes any number of edges across the open gap.
            float fraction = ((lane * 0.61803398875f) % 1f) * .72f + .14f;
            return span * fraction;
        }

        static float PackedOffset(int lane, float available)
        {
            if (lane == 0) return 0f;
            int magnitude = (lane + 1) / 2;
            float offset = magnitude * 2f * (lane % 2 == 1 ? 1f : -1f);
            return Mathf.Clamp(offset, -available * .5f, available * .5f);
        }

        static float CorridorY(int boundary, int maximumRows)
        {
            if (boundary <= 0) return TopMargin - CorridorInset;
            if (boundary >= maximumRows)
                return TopMargin + maximumRows * NodeHeight + Math.Max(0, maximumRows - 1) * RowGap + CorridorInset;
            return TopMargin + boundary * NodeHeight + (boundary - .5f) * RowGap;
        }

        static IReadOnlyList<Vector2> ReadOnlyPath(params Vector2[] points)
        {
            var compact = new List<Vector2>(points.Length);
            foreach (Vector2 point in points)
                if (compact.Count == 0 || (compact[compact.Count - 1] - point).sqrMagnitude > .0001f) compact.Add(point);
            return compact.AsReadOnly();
        }
    }
}
