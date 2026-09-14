using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Riverworks
{
    public sealed class ResearchMapScrollRect : ScrollRect
    {
        public ResearchTreeView Owner { get; set; }

        public override void OnScroll(PointerEventData data)
        {
            if (Owner == null) { base.OnScroll(data); return; }
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                Owner.SetZoom(Owner.ZoomLevel + (data.scrollDelta.y > 0 ? 1 : -1));
            else if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                Owner.PanBy(new Vector2(0, -data.scrollDelta.y * 48));
            else Owner.PanBy(new Vector2(data.scrollDelta.y * 48, -data.scrollDelta.x * 48));
            data.Use();
        }
    }
}
