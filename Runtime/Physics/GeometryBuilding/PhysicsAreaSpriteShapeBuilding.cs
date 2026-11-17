using Unity.Collections;
using UnityEngine;
using UnityEngine.U2D;

namespace Unity.U2D.Physics
{
    // Copies the controller's collider outline and turns it into shapes. The buffers used here are owned by the component for
    // its whole enabled lifetime, so a rebuild allocates no managed memory; the native arrays a batch create hands back, and
    // the side samples that name each segment's side, are Temp and disposed within the build.
    //
    // This deliberately does not route through ContourGeometry or ContourGroupGeometry: those are managed classes over
    // List<Vector2>, every Transform overload returns a newly allocated one, and their own documentation says to treat them as
    // setup data rather than per-frame data. The span-based factories they themselves call are used directly instead.
    public sealed partial class PhysicsAreaSpriteShape
    {
        #region Building

        // Fill the outline with convex polygons, in one batch.
        private void BuildPolygons(in PhysicsAreaShapeContext context)
        {
            var relative = context.relative;

            // A closed outline can arrive with its first point repeated at the end, and the decomposition wants each corner
            // once, so the outline is welded as a loop first.
            var span = m_Outline.AsArray().AsSpan();
            var keptCount = WeldVertices(span, isLoop: true);
            if (keptCount < 3)
                return;

            var vertices = m_Outline.AsArray().GetSubArray(0, keptCount);

            // Decomposed in the outline's own space and placed afterwards, so the relative transform is applied once per
            // polygon rather than once per vertex.
            NativeArray<PolygonGeometry> polygons;

            using (var composer = PhysicsComposer.Create(Allocator.Temp))
            {
                composer.useDelaunay = true;
                composer.maxPolygonVertices = PhysicsConstants.MaxPolygonVertices;
                composer.AddLayer(vertices.AsReadOnlySpan(), PhysicsTransform.identity);

                polygons = composer.CreatePolygonGeometry(Vector2.one, Allocator.Temp);
            }

            if (!polygons.IsCreated)
                return;

            var geometry = new NativeList<PolygonGeometry>(polygons.Length, Allocator.Temp);

            try
            {
                for (var i = 0; i < polygons.Length; ++i)
                {
                    var placed = polygons[i].Transform(relative, scaleRadius: false);
                    if (placed.isValid)
                        geometry.Add(placed);
                }

                if (geometry.Length == 0)
                    return;

                var created = context.CreateShapeBatch(geometry.AsArray().AsReadOnlySpan());
                Record(context, created);
            }
            finally
            {
                geometry.Dispose();
                polygons.Dispose();
            }
        }

        // Trace the outline as chain segments, in one batch. A closed spline traces a loop, an open one a strip whose ends stop.
        private void BuildSegments(in PhysicsAreaShapeContext context, bool isLoop)
        {
            var relative = context.relative;

            // A loop needs three vertices to enclose anything, and an open strip two to make a single side. The strip is given a
            // ghost vertex past each end below, which brings it up to the four an open chain needs.
            var minimumVertexCount = isLoop ? 3 : 2;

            var vertices = m_Outline.AsArray();
            if (vertices.Length < minimumVertexCount)
                return;

            var span = vertices.AsSpan();

            // Placed before the segments are made, because a chain segment's ghost vertices have to be placed with it. A matrix
            // rather than a PhysicsTransform, because the relative transform carries scale.
            for (var i = 0; i < span.Length; ++i)
                span[i] = relative.MultiplyPoint3x4(span[i]);

            // A chain segment collides on its right, so a clockwise loop faces inward until it is reversed. Checked after the
            // placement, since a mirrored scale on either in-plane axis reverses the winding. An open strip has no winding of
            // its own, so it is reversed whenever the placement mirrors, which keeps the side it collides on.
            // Reverse Winding inverts the result, so the segments collide on the other side.
            var reverse = isLoop
                ? SignedArea(vertices) < 0f
                : relative.m00 * relative.m11 - relative.m01 * relative.m10 < 0f;
            reverse ^= m_BuildSettings.m_ReverseWinding;
            if (reverse)
                Reverse(vertices);

            // Welded after the placement, since it is the placed distances the engine measures.
            var keptCount = WeldVertices(span, isLoop);
            if (keptCount < minimumVertexCount)
                return;

            var chainVertices = isLoop ? span.Slice(0, keptCount) : AddEndGhosts(span.Slice(0, keptCount));

            var segments = ChainSegmentGeometry.CreateSegments(chainVertices, PhysicsTransform.identity, isLoop,
                Allocator.Temp);
            if (!segments.IsCreated)
                return;

            var geometry = new NativeList<ChainSegmentGeometry>(segments.Length, Allocator.Temp);
            var indices = new NativeList<int>(segments.Length, Allocator.Temp);
            var sides = SampleSides(relative, out var sideCount);

            try
            {
                var side = NoSegment;
                var sideDistance = 0f;

                // A degenerate segment is dropped, so each kept segment's side is gathered alongside it.
                for (var i = 0; i < segments.Length; ++i)
                {
                    if (!segments[i].isValid)
                        continue;

                    var segment = segments[i].segment;
                    geometry.Add(segments[i]);

                    side = NearestSide(sides, sideCount, isLoop, (segment.point1 + segment.point2) * 0.5f, side,
                        ref sideDistance);
                    indices.Add(side);
                }

                if (geometry.Length == 0)
                    return;

                var created = context.CreateShapeBatch(geometry.AsArray().AsReadOnlySpan());
                Record(context, created, indices.AsArray());
            }
            finally
            {
                sides.Dispose();
                indices.Dispose();
                geometry.Dispose();
                segments.Dispose();
            }
        }

        // Fill the copied outline with convex polygons, place them into the composer's space, and add them as one layer.
        // One layer keeps the concave outline a single operand, so every composite operation treats it as one area.
        private void AddOutlineLayer(PhysicsComposer composer, Matrix4x4 areaToComposer, PhysicsTransform extraTransform, LayerSettings composite, int order)
        {
            var span = m_Outline.AsArray().AsSpan();
            var keptCount = WeldVertices(span, isLoop: true);
            if (keptCount < 3)
                return;

            NativeArray<PolygonGeometry> polygons;

            using (var decomposer = PhysicsComposer.Create(Allocator.Temp))
            {
                decomposer.useDelaunay = true;
                decomposer.maxPolygonVertices = PhysicsConstants.MaxPolygonVertices;
                decomposer.AddLayer(m_Outline.AsArray().GetSubArray(0, keptCount).AsReadOnlySpan(), PhysicsTransform.identity);

                polygons = decomposer.CreatePolygonGeometry(Vector2.one, Allocator.Temp);
            }

            if (!polygons.IsCreated)
                return;

            try
            {
                PolygonGeometry.Transform(polygons.AsSpan(), areaToComposer, scaleRadius: false);

                var valid = 0;

                for (var i = 0; i < polygons.Length; ++i)
                {
                    if (polygons[i].isValid)
                        polygons[valid++] = polygons[i];
                }

                if (valid > 0)
                    composer.AddLayer(polygons.AsReadOnlySpan().Slice(0, valid), extraTransform, composite.operation, order, composite.curveStride, composite.reverseWinding);
            }
            finally
            {
                polygons.Dispose();
            }
        }

        // An open chain spends its first and last vertices as ghosts, which would lose the strip's first and last sides, so a
        // ghost is extrapolated past each end. Written to the scratch buffer, so this allocates nothing.
        private System.Span<Vector2> AddEndGhosts(System.ReadOnlySpan<Vector2> strip)
        {
            var last = strip.Length - 1;

            m_Scratch.Clear();

            if (m_Scratch.Capacity < strip.Length + 2)
                m_Scratch.Capacity = strip.Length + 2;

            m_Scratch.Add(strip[0] + (strip[0] - strip[1]));
            for (var i = 0; i <= last; ++i)
                m_Scratch.Add(strip[i]);
            m_Scratch.Add(strip[last] + (strip[last] - strip[last - 1]));

            return m_Scratch.AsArray().AsSpan();
        }

        // Stamp each shape of a freshly created batch with its spline side. Polygon output names no side:
        // the decomposition mixes vertices from anywhere in the outline into one convex piece.
        private static void Record(in PhysicsAreaShapeContext context, NativeArray<PhysicsShape> created,
            NativeArray<int> splineIndices = default)
        {
            if (!created.IsCreated)
                return;

            try
            {
                if (created.Length == 0)
                    return;

                StampSplineIndices(context, created, splineIndices);
            }
            finally
            {
                created.Dispose();
            }
        }

        // Read back through GetSplineIndex.
        private static void StampSplineIndices(in PhysicsAreaShapeContext context, NativeArray<PhysicsShape> created, NativeArray<int> splineIndices)
        {
            var userDatas = new NativeArray<PhysicsUserData>(created.Length, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);

            try
            {
                // A batch create is free to return fewer shapes than it was given geometry, so the length is checked.
                var hasIndices = splineIndices.IsCreated && splineIndices.Length >= created.Length;

                for (var i = 0; i < created.Length; ++i)
                    userDatas[i] = new PhysicsUserData { intValue = hasIndices ? splineIndices[i] : NoSegment };

                context.SetOwnerUserData(created.AsReadOnlySpan(), userDatas.AsReadOnlySpan());
            }
            finally
            {
                userDatas.Dispose();
            }
        }

        #endregion

        #region Outline

        // Copy the controller's last baked outline into the reused buffer, in this component's local space. Returns false when
        // there is no outline yet.
        private bool TryCopyOutline(out bool isLoop)
        {
            isLoop = false;
            m_Outline.Clear();

            var activeController = controller;
            var spline = activeController == null ? null : activeController.spline;
            if (spline == null)
                return false;

            // The controller keeps its last outline while autoUpdateCollider is off, as its colliders keep theirs.
            var outline = activeController.physicsShapeData;
            if (outline.Length < 2)
                return false;

            isLoop = !spline.isOpenEnded;
            m_Outline.ResizeUninitialized(outline.Length);
            outline.CopyTo(m_Outline.AsArray().AsSpan());

            return true;
        }

        // Twice the enclosed area, signed: positive is anti-clockwise. Only the sign is used.
        private static float SignedArea(NativeArray<Vector2> points)
        {
            var total = 0f;

            for (var i = 0; i < points.Length; ++i)
            {
                var next = points[(i + 1) % points.Length];
                total += points[i].x * next.y - next.x * points[i].y;
            }

            return total;
        }

        private static void Reverse(NativeArray<Vector2> points)
        {
            for (int i = 0, j = points.Length - 1; i < j; ++i, --j)
                (points[i], points[j]) = (points[j], points[i]);
        }

        // Merge vertices closer together than the engine's minimum edge length, keeping the survivors packed at the front and
        // returning how many there are. Mirrors ContourGeometry.WeldVertices, which is internal to the physics package.
        private static int WeldVertices(System.Span<Vector2> vertices, bool isLoop)
        {
            var keptCount = 0;

            for (var i = 0; i < vertices.Length; ++i)
            {
                if (keptCount > 0 && AreTooClose(vertices[keptCount - 1], vertices[i]))
                    continue;

                vertices[keptCount++] = vertices[i];
            }

            // The closing edge runs from the last survivor back to the first, so it has to clear the same bar.
            if (isLoop)
            {
                while (keptCount > 1 && AreTooClose(vertices[keptCount - 1], vertices[0]))
                    --keptCount;
            }

            return keptCount;
        }

        private static bool AreTooClose(Vector2 a, Vector2 b) => PathGeometry.AreTooClose(a, b);

        #endregion

        #region Spline sides

        // How many straight pieces each side of the spline is approximated by when finding the side nearest a segment.
        private const int SideSamples = 16;

        // How many sides either way of the previous segment's side are searched for the next segment's. The outline runs along
        // the spline, so the next segment almost always lies along the same side or a neighbour.
        private const int SideSearchReach = 2;

        // How much farther, squared, a segment may lie from the side found near the previous segment's than the previous
        // segment lay from its own before every side is searched instead. A jump like that is where the outline skips along
        // the spline, such as across an overlap the controller trimmed away, so a nearer side may lie out of reach.
        private const float SideJumpFactor = 4f;
        private const float SideJumpSlack = 0.01f;

        // Every side of the spline as a polyline, placed like the segments so the two compare directly. Side i occupies
        // SideSamples + 1 points from i * (SideSamples + 1).
        private NativeArray<Vector2> SampleSides(Matrix4x4 relative, out int sideCount)
        {
            sideCount = 0;

            var spline = controller == null ? null : controller.spline;
            var points = spline == null ? 0 : spline.GetPointCount();
            if (points < 2)
                return new NativeArray<Vector2>(0, Allocator.Temp);

            sideCount = spline.isOpenEnded ? points - 1 : points;

            var samples = new NativeArray<Vector2>(sideCount * (SideSamples + 1), Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);

            for (var side = 0; side < sideCount; ++side)
            {
                var next = (side + 1) % points;
                var start = spline.GetPosition(side);
                var end = spline.GetPosition(next);
                var startTangent = start + spline.GetRightTangent(side);
                var endTangent = end + spline.GetLeftTangent(next);

                for (var step = 0; step <= SideSamples; ++step)
                {
                    var point = BezierUtility.BezierPoint(startTangent, start, end, endTangent, step / (float)SideSamples);
                    samples[side * (SideSamples + 1) + step] = relative.MultiplyPoint3x4(point);
                }
            }

            return samples;
        }

        // The side whose polyline passes closest to the point, or NoSegment when there are no sides. Searched outward from the
        // previous segment's side, so a build costs in proportion to the segments rather than to segments times sides, and from
        // every side for the first segment and wherever the outline jumps. The distance carries the squared distance from one
        // call to the next.
        private static int NearestSide(NativeArray<Vector2> samples, int sideCount, bool isLoop, Vector2 point,
            int previousSide, ref float distance)
        {
            // A window as wide as the spline already searches every side.
            if (previousSide != NoSegment && sideCount > SideSearchReach * 2 + 1)
            {
                var previousDistance = distance;
                var side = NearestSideNear(samples, sideCount, isLoop, point, previousSide, out distance);
                if (distance <= previousDistance * SideJumpFactor + SideJumpSlack)
                    return side;
            }

            return NearestSideOfAll(samples, sideCount, point, out distance);
        }

        // Search the sides within reach of a starting side, and move the window on while the nearest one is at its edge, which
        // follows the outline past sides too short to hold a segment of their own.
        private static int NearestSideNear(NativeArray<Vector2> samples, int sideCount, bool isLoop, Vector2 point, int start,
            out float distance)
        {
            var centre = start;
            distance = DistanceSquaredToSide(samples, centre, point);

            for (var moves = 0; moves < sideCount; ++moves)
            {
                var nearest = centre;
                var nearestOffset = 0;

                for (var offset = -SideSearchReach; offset <= SideSearchReach; ++offset)
                {
                    if (offset == 0)
                        continue;

                    var side = centre + offset;
                    if (isLoop)
                        side = (side + sideCount) % sideCount;
                    else if (side < 0 || side >= sideCount)
                        continue;

                    var sideDistance = DistanceSquaredToSide(samples, side, point);
                    if (sideDistance < distance)
                    {
                        distance = sideDistance;
                        nearest = side;
                        nearestOffset = offset;
                    }
                }

                if (Mathf.Abs(nearestOffset) < SideSearchReach)
                    return nearest;

                centre = nearest;
            }

            return centre;
        }

        private static int NearestSideOfAll(NativeArray<Vector2> samples, int sideCount, Vector2 point, out float distance)
        {
            var nearest = NoSegment;
            distance = float.MaxValue;

            for (var side = 0; side < sideCount; ++side)
            {
                var sideDistance = DistanceSquaredToSide(samples, side, point);
                if (sideDistance < distance)
                {
                    distance = sideDistance;
                    nearest = side;
                }
            }

            return nearest;
        }

        private static float DistanceSquaredToSide(NativeArray<Vector2> samples, int side, Vector2 point)
        {
            var first = side * (SideSamples + 1);
            var nearest = float.MaxValue;

            for (var step = 0; step < SideSamples; ++step)
                nearest = Mathf.Min(nearest, DistanceSquaredToSegment(point, samples[first + step], samples[first + step + 1]));

            return nearest;
        }

        private static float DistanceSquaredToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var lengthSquared = ab.sqrMagnitude;
            var t = lengthSquared > 0f ? Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared) : 0f;
            return (a + ab * t - point).sqrMagnitude;
        }

        #endregion
    }
}
