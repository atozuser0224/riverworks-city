using UnityEngine;

namespace Riverworks
{
    /// <summary>Generated resident prefabs and humanoid clips used by the presentation layer.</summary>
    [CreateAssetMenu(menuName="Riverworks/Resident Visual Library",fileName="ResidentVisualLibrary")]
    public sealed class ResidentVisualLibrary : ScriptableObject
    {
        public GameObject[] Models;
        public GameObject GuideModel;
        public AnimationClip Idle;
        public AnimationClip Walk;
        public AnimationClip Carry;
        public AnimationClip Chop;
        public AnimationClip Dig;
        public AnimationClip Farm;
        public AnimationClip Knead;
        public AnimationClip Hammer;
        public AnimationClip Read;
        public AnimationClip Talk;
        public AnimationClip Operate;
        public AnimationClip Sit;
        public float[] ModelHeights;
        public float GuideHeight;
    }
}
