using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Riverworks
{
    /// <summary>Allocation-free-between-changes uGUI renderer for a routed research edge.</summary>
    public sealed class ResearchConnectionGraphic : MaskableGraphic
    {
        const float MinimumStroke = .5f;
        const int MaximumDashPieces = 256;

        Vector2[] points = Array.Empty<Vector2>();
        float strokeWidth = 2f;
        bool isDashed;

        public IReadOnlyList<Vector2> Points => points;
        public float StrokeWidth => strokeWidth;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        public void SetPath(IReadOnlyList<Vector2> points, Color color, float thickness = 2f, bool dashed = false)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            this.points = new Vector2[points.Count];
            for (int index = 0; index < points.Count; index++) this.points[index] = points[index];
            this.color = color;
            strokeWidth = Mathf.Max(MinimumStroke, thickness);
            isDashed = dashed;
            raycastTarget = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (points.Length < 2) return;

            int dashPieces = 0;
            for (int index = 0; index < points.Length - 1; index++)
            {
                Vector2 from = ToLocal(points[index]);
                Vector2 to = ToLocal(points[index + 1]);
                if (isDashed) AddDashedSegment(vertexHelper, from, to, ref dashPieces);
                else AddQuad(vertexHelper, from, to);
            }

            Vector2 tip = ToLocal(points[points.Length - 1]);
            Vector2 previous = tip;
            for (int index = points.Length - 2; index >= 0 && (previous - tip).sqrMagnitude < .0001f; index--)
                previous = ToLocal(points[index]);
            if ((tip - previous).sqrMagnitude > .0001f) AddArrow(vertexHelper, previous, tip);
        }

        Vector2 ToLocal(Vector2 point)
        {
            Rect bounds = rectTransform.rect;
            return new Vector2(bounds.xMin + point.x, bounds.yMax - point.y);
        }

        void AddDashedSegment(VertexHelper vertexHelper, Vector2 from, Vector2 to, ref int pieces)
        {
            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length < .001f || pieces >= MaximumDashPieces) return;
            Vector2 direction = delta / length;
            float dashLength = Mathf.Max(5f, strokeWidth * 3f);
            float step = dashLength * 1.75f;
            for (float distance = 0f; distance < length && pieces < MaximumDashPieces; distance += step, pieces++)
            {
                float end = Mathf.Min(distance + dashLength, length);
                AddQuad(vertexHelper, from + direction * distance, from + direction * end);
            }
        }

        void AddQuad(VertexHelper vertexHelper, Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            if (delta.sqrMagnitude < .0001f) return;
            Vector2 normal = new Vector2(-delta.y, delta.x).normalized * (strokeWidth * .5f);
            int start = vertexHelper.currentVertCount;
            AddVertex(vertexHelper, from - normal);
            AddVertex(vertexHelper, from + normal);
            AddVertex(vertexHelper, to + normal);
            AddVertex(vertexHelper, to - normal);
            vertexHelper.AddTriangle(start, start + 1, start + 2);
            vertexHelper.AddTriangle(start, start + 2, start + 3);
        }

        void AddArrow(VertexHelper vertexHelper, Vector2 previous, Vector2 tip)
        {
            Vector2 direction = (tip - previous).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x);
            float length = Mathf.Max(8f, strokeWidth * 4f);
            float halfWidth = Mathf.Max(5f, strokeWidth * 2.5f);
            Vector2 baseCenter = tip - direction * length;
            int start = vertexHelper.currentVertCount;
            AddVertex(vertexHelper, tip);
            AddVertex(vertexHelper, baseCenter + normal * halfWidth);
            AddVertex(vertexHelper, baseCenter - normal * halfWidth);
            vertexHelper.AddTriangle(start, start + 1, start + 2);
        }

        void AddVertex(VertexHelper vertexHelper, Vector2 position)
        {
            vertexHelper.AddVert(position, color, Vector2.zero);
        }
    }
}
