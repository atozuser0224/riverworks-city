using UnityEngine;

namespace Riverworks
{
    /// <summary>Identifies the logical factory floor represented by a platform pick surface.</summary>
    public sealed class FactoryFloorPick : MonoBehaviour
    {
        public int X;
        public int Z;
        public int Floor;
        public bool IsPlatform;
    }
}
