using Unity.Profiling;

namespace UnityEngine.U2D.Profiling
{
    internal static class PerformanceMarkers
    {
        public static readonly ProfilerMarker lateUpdateMarker = new ProfilerMarker("SpriteShapeController.LateUpdate");
        public static readonly ProfilerMarker bakeColliderMarker = new ProfilerMarker("SpriteShapeController.BakeCollider");
        public static readonly ProfilerMarker tryUpdateCollidersMarker = new ProfilerMarker("SpriteShapeController.TryUpdateColliders");

    }
}
