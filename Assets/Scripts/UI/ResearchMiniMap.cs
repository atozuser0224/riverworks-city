using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Riverworks
{
    /// <summary>A clickable overview of the entire tree with the visible map rectangle.</summary>
    public sealed class ResearchMiniMap : MaskableGraphic, IPointerClickHandler
    {
        ResearchTreeView tree;
        public void Bind(ResearchTreeView owner) { tree = owner; raycastTarget = true; Repaint(); }
        public void Repaint() => SetVerticesDirty();

        float Scale => tree == null ? 1 : Mathf.Min((rectTransform.rect.width - 12) / tree.Layout.ContentSize.x,
            (rectTransform.rect.height - 12) / tree.Layout.ContentSize.y);

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (tree == null) return;
            Rect frame = rectTransform.rect;
            AddRect(mesh, frame, new Color(HudStyle.Surface.r, HudStyle.Surface.g, HudStyle.Surface.b, .96f));
            float scale = Scale;
            Vector2 origin = new Vector2(frame.xMin + 6, frame.yMax - 6);
            foreach (ResearchEdge edge in tree.Graph.Edges)
            {
                var points = tree.Layout.EdgePoints(edge);
                for (int i = 1; i < points.Count; i++)
                {
                    Vector2 a = origin + new Vector2(points[i - 1].x, -points[i - 1].y) * scale;
                    Vector2 b = origin + new Vector2(points[i].x, -points[i].y) * scale;
                    AddRect(mesh, new Rect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(1, Mathf.Abs(a.x - b.x)),
                        Mathf.Max(1, Mathf.Abs(a.y - b.y))), HudStyle.SurfaceRaised);
                }
            }
            foreach (TechSpec spec in tree.Graph.OrderedTechnologies)
            {
                Rect node = tree.Layout.NodeRect(spec.Id);
                AddRect(mesh, new Rect(origin.x + node.x * scale, origin.y - node.yMax * scale,
                    node.width * scale, node.height * scale), tree.MiniMapNodeColor(spec.Id));
            }
            Vector2 top = new Vector2(-tree.GraphContent.anchoredPosition.x, tree.GraphContent.anchoredPosition.y) / tree.ZoomLevel;
            Vector2 size = Vector2.Min(tree.Layout.ContentSize, tree.GraphViewport.rect.size / tree.ZoomLevel);
            top = Vector2.Min(Vector2.Max(Vector2.zero, top), tree.Layout.ContentSize - size);
            Rect visible = new Rect(origin.x + top.x * scale, origin.y - (top.y + size.y) * scale, size.x * scale, size.y * scale);
            Color outline = HudStyle.Accent;
            AddRect(mesh, new Rect(visible.xMin, visible.yMin, visible.width, 1.5f), outline);
            AddRect(mesh, new Rect(visible.xMin, visible.yMax - 1.5f, visible.width, 1.5f), outline);
            AddRect(mesh, new Rect(visible.xMin, visible.yMin, 1.5f, visible.height), outline);
            AddRect(mesh, new Rect(visible.xMax - 1.5f, visible.yMin, 1.5f, visible.height), outline);
        }

        public void OnPointerClick(PointerEventData data)
        {
            if (tree == null || data.button != PointerEventData.InputButton.Left) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, data.position, data.pressEventCamera, out Vector2 local))
            {
                Rect frame = rectTransform.rect;
                Vector2 point = new Vector2(local.x - frame.xMin - 6, frame.yMax - 6 - local.y) / Scale;
                tree.CenterOnGraphPoint(point);
                data.Use();
            }
        }

        static void AddRect(VertexHelper mesh, Rect rect, Color color)
        {
            int first = mesh.currentVertCount;
            mesh.AddVert(new Vector3(rect.xMin, rect.yMin), color, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMin, rect.yMax), color, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMax, rect.yMax), color, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMax, rect.yMin), color, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first, first + 2, first + 3);
        }
    }
}
