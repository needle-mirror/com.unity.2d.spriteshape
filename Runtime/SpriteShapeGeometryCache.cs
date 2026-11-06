using System;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;
using Unity.Collections;

[Serializable]
internal struct SpriteShapeGeometryInfo
{
    [SerializeField]
    internal int geomIndex;
    [SerializeField]
    internal int indexCount;
    [SerializeField]
    internal int vertexCount;
    [SerializeField]
    internal int spriteIndex;
}

// Simple Cache for SpriteShape Geometry Data.
//
// The payload is a UnifiedRenderDataAsset: one batch of baked geometry with its per entry
// metadata, bounds and draw ranges, which is exactly what SpriteShapeRenderer now draws. The
// parallel arrays below it are the layout this cache used before that, kept only so upgrading a
// project does not delete anything. See the note on k_Version.
[AddComponentMenu("")]
internal class SpriteShapeGeometryCache : MonoBehaviour
{

    // Cache layout version. Bumped when baked geometry is no longer interchangeable across package
    // versions, so that stale caches are re-baked instead of uploaded.
    // 0 : Fill segment always carries spriteIndex -1 and the Fill Area renders from a Fill Texture.
    // 1 : Fill segment carries the index of the Fill Sprite when the SpriteShape has one.
    // 2 : Geometry lives in a UnifiedRenderDataAsset instead of the parallel arrays below.
    //
    // A cache written by version 0 or 1 is REPORTED AS EMPTY rather than converted: its arrays are
    // staging mesh data in generation order with segment local indices, and re-deriving the entry
    // grouping and the draw list from them would be reimplementing the bake. The controller falls
    // back to a full bake, which produces identical geometry, and the next Editor bake writes a
    // version 2 cache. The old fields are deliberately still declared, so the serialized data
    // survives the upgrade untouched.
    internal const int k_Version = 2;

    // Serialized Data.
    [SerializeField]
    [HideInInspector]
    int m_Version = 0;
    [SerializeField]
    [HideInInspector]
    int m_MaxArrayCount;

    // The baked geometry. A standalone UnityEngine.Object, like a procedural Mesh: a Scene saves it
    // inline, but a Prefab writes any reference to a non-persistent Object as null. Prefabs get it
    // as a sub-asset instead, re-baked at import by SpriteShapeGeometryCachePostprocessor.
    [SerializeField]
    [HideInInspector]
    UnifiedRenderDataAsset m_RenderDataAsset;

    // ---- Version 0/1 layout. Read by nothing; retained so upgrading does not destroy data. ----
#pragma warning disable 0414
    [SerializeField]
    [HideInInspector]
    Vector3[] m_PosArray = null;
    [SerializeField]
    [HideInInspector]
    Vector2[] m_Uv0Array = null;
    [SerializeField]
    [HideInInspector]
    Vector4[] m_TanArray = null;
    [SerializeField]
    [HideInInspector]
    ushort[] m_IndexArray = null;
    [SerializeField]
    [HideInInspector]
    SpriteShapeGeometryInfo[] m_GeomArray = null;
#pragma warning restore 0414

    // Update set.
    bool m_RequiresUpdate = false;
    bool m_RequiresUpload = false;

    // The controller's own render data, recorded by SetGeometryCache and read by
    // UpdateGeometryCache once the producing Job has completed and its write session is closed.
    // Borrowed, never owned: the controller disposes it.
    UnifiedRenderData m_SourceRenderData;

    // The fence covering the Job that fills m_SourceRenderData. Held so that
    // UpdateGeometryCache is self sufficient: it is the one place that has to observe the
    // finished geometry, and it can be called directly rather than through the controller.
    JobHandle m_SourceHandle;

    // The cached index buffer, materialized from the asset. Still ushort[], which is what this
    // accessor has always returned, but the VALUES are now absolute indices into one shared buffer
    // in entry order rather than per segment indices in generation order.
    internal ushort[] indexArray
    {
        get { return ReadIndices(); }
    }

    internal Vector3[] posArray
    {
        get { return ReadPositions(); }
    }

    public Vector4[] tanArray
    {
        get { return ReadTangents(); }
    }

    internal int maxArrayCount
    {
        // A cache baked by an older layout, or one whose asset went missing, is reported as empty
        // so that callers fall back to a full bake.
        get { return (m_Version == k_Version && m_RenderDataAsset != null) ? m_MaxArrayCount : 0; }
    }

    internal UnifiedRenderDataAsset renderDataAsset
    {
        get { return m_RenderDataAsset; }
    }

    internal bool requiresUpdate
    {
        get { return m_RequiresUpdate; }
    }

    internal bool requiresUpload
    {
        get { return m_RequiresUpload; }
    }

    void OnEnable()
    {
        m_RequiresUpload = true;
        m_RequiresUpdate = false;
    }

    /// <summary>
    /// Records the render data a bake just produced. The copy itself happens in UpdateGeometryCache.
    /// </summary>
    /// <remarks>
    /// Nothing is read here: at this point the producing Job is still in flight and its write
    /// session is still open. Both are settled in UpdateGeometryCache.
    /// </remarks>
    internal void SetGeometryCache(UnifiedRenderData renderData, JobHandle handle)
    {
        m_SourceRenderData = renderData;
        m_SourceHandle = handle;
        m_RequiresUpdate = (null != renderData);
    }

    // Drops the reference to the current asset so the next UpdateGeometryCache writes a new one.
    // A Prefab being imported may still reference the sub-asset of a nested Prefab, and writing
    // into that would change another Prefab's geometry.
    internal void DetachRenderDataAsset()
    {
        m_RenderDataAsset = null;
    }

    // Update GeometryCache.
    internal void UpdateGeometryCache()
    {
        if (!m_RequiresUpdate || null == m_SourceRenderData || !m_SourceRenderData.isValid)
            return;

        // The producing Job writes straight into these buffers, so there is nothing to read until
        // it has finished and the session has been closed. Normally the renderer closes it at
        // prepare time; this is the one caller that needs the geometry before then, and EndWrite
        // is idempotent so the renderer's later call is a no-op.
        m_SourceHandle.Complete();
        if (m_SourceRenderData.isWriteSessionOpen)
            m_SourceRenderData.EndWrite();

#if UNITY_EDITOR
        // A persistent asset is a Prefab's sub-asset, shared by every instance of that Prefab, so
        // it is never written to here. An instance whose geometry still matches keeps sharing it;
        // one that differs, through Spline overrides for instance, gets a private asset instead.
        if (null != m_RenderDataAsset && UnityEditor.EditorUtility.IsPersistent(m_RenderDataAsset))
        {
            if (m_Version == k_Version && MatchesRenderDataAsset(m_SourceRenderData))
            {
                m_RequiresUpdate = false;
                return;
            }
            m_RenderDataAsset = null;
        }
#endif

        if (null == m_RenderDataAsset)
        {
            m_RenderDataAsset = new UnifiedRenderDataAsset();
            m_RenderDataAsset.name = "SpriteShape Geometry";
        }

        var destination = m_RenderDataAsset.GetRenderData();
        if (null == destination)
            return;

        // GetRenderData hands out a handle with a reference of its own; the asset keeps the
        // geometry once this one is released.
        using (destination)
            CopyRenderData(m_SourceRenderData, destination);

        m_MaxArrayCount = Mathf.Max(m_SourceRenderData.vertexCount, m_SourceRenderData.indexCount);
        m_Version = k_Version;
        m_RequiresUpdate = false;
    }

    /// <summary>
    /// Hands the cached geometry straight to the renderer, skipping the bake entirely.
    /// </summary>
    /// <param name="sr">The renderer to draw the cached geometry.</param>
    /// <param name="sc">The controller the cache belongs to.</param>
    /// <returns>An already completed JobHandle; nothing is scheduled on this path.</returns>
    internal JobHandle Upload(SpriteShapeRenderer sr, SpriteShapeController sc)
    {
        JobHandle jobHandle = (default);
        if (!m_RequiresUpload || 0 == maxArrayCount)
            return jobHandle;

        var renderData = m_RenderDataAsset.GetRenderData();
        if (null != renderData)
        {
            // SHARED ownership: SetUnifiedRenderData takes a reference of its own, so releasing
            // this handle leaves the renderer and the asset holding the same geometry.
            using (renderData)
            {
                // As in ScheduleBake: a pre-fillSprite profile's Fill Area has no Sprite to
                // carry its Texture, and the cached geometry does not carry one either.
                sr.SetFillTexture(sc.spriteShapeParameters.fillTexture);
                sr.SetUnifiedRenderData(renderData);
            }
            m_RequiresUpload = false;
        }
        return jobHandle;
    }

#if UNITY_EDITOR
    // True if the cached asset holds exactly what CopyRenderData would write from source: the
    // channel layout, every channel, the indices, the entries, the draw ranges and the bounds.
    // Instance overrides reach all of these -- the Spline moves vertices, enableTangents adds a
    // channel, a different profile changes the Sprites -- so a partial match is not enough.
    bool MatchesRenderDataAsset(UnifiedRenderData source)
    {
        var cached = m_RenderDataAsset.GetRenderData();
        if (null == cached)
            return false;

        using (cached)
        {
            if (cached.channelMask != source.channelMask)
                return false;
            if (cached.vertexCount != source.vertexCount || cached.indexCount != source.indexCount)
                return false;
            if (cached.bounds != source.bounds)
                return false;

            // The masks are equal, so each optional channel is present on both sides or neither.
            if (!ChannelEquals(cached.GetPositions(), source.GetPositions()))
                return false;
            if (source.HasVertexAttribute(VertexAttribute.TexCoord0) && !ChannelEquals(cached.GetTexCoords(), source.GetTexCoords()))
                return false;
            if (source.HasVertexAttribute(VertexAttribute.Normal) && !ChannelEquals(cached.GetNormals(), source.GetNormals()))
                return false;
            if (source.HasVertexAttribute(VertexAttribute.Tangent) && !ChannelEquals(cached.GetTangents(), source.GetTangents()))
                return false;
            if (source.HasVertexAttribute(VertexAttribute.Color) && !ColorsEqual(cached.GetColors(), source.GetColors()))
                return false;

            var cachedIndices = cached.GetIndices();
            var sourceIndices = source.GetIndices();
            if (cachedIndices.Length != sourceIndices.Length)
                return false;
            for (int i = 0; i < sourceIndices.Length; ++i)
                if (cachedIndices[i] != sourceIndices[i])
                    return false;

            if (!EntitiesEqual(cached, source) || !DrawRangesEqual(cached.GetDrawRanges(), source.GetDrawRanges()))
                return false;
        }
        return true;
    }

    // Only the fields CopyRenderData carries over; the offsets it recomputes follow the counts.
    static bool EntitiesEqual(UnifiedRenderData a, UnifiedRenderData b)
    {
        var entitiesA = a.GetEntities(Allocator.Temp);
        var entitiesB = b.GetEntities(Allocator.Temp);
        var equal = entitiesA.Length == entitiesB.Length;
        for (int i = 0; equal && i < entitiesA.Length; ++i)
        {
            var x = entitiesA[i];
            var y = entitiesB[i];
            equal = x.spriteId.Equals(y.spriteId)
                && x.instances == y.instances
                && x.vertexCount == y.vertexCount
                && x.indexCount == y.indexCount
                && x.bounds == y.bounds
                && x.spriteProps == y.spriteProps
                && x.spriteColor == y.spriteColor;
        }
        entitiesA.Dispose();
        entitiesB.Dispose();
        return equal;
    }

    static bool DrawRangesEqual(NativeArray<DrawRange> a, NativeArray<DrawRange> b)
    {
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; ++i)
        {
            if (a[i].firstIndex != b[i].firstIndex || a[i].indexCount != b[i].indexCount
                || a[i].firstVertex != b[i].firstVertex || a[i].vertexCount != b[i].vertexCount
                || a[i].entityIndex != b[i].entityIndex)
                return false;
        }
        return true;
    }

    static bool ChannelEquals<T>(NativeSlice<T> a, NativeSlice<T> b) where T : struct, IEquatable<T>
    {
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; ++i)
            if (!a[i].Equals(b[i]))
                return false;
        return true;
    }

    // Color32 is not IEquatable, and the generic path would box every vertex.
    static bool ColorsEqual(NativeSlice<Color32> a, NativeSlice<Color32> b)
    {
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; ++i)
            if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a)
                return false;
        return true;
    }
#endif

    // Copies one batch of geometry between two UnifiedRenderData instances.
    //
    // Through a write session rather than AddSprite per entry: a session reserves once and the
    // entry records, including the draw ranges, are written verbatim. Draw ranges are INTENT --
    // which entries the producer decided could share a draw -- and re-deriving them would lose the
    // fill/edge split at range 0 that SpriteShapeRenderer depends on.
    static void CopyRenderData(UnifiedRenderData source, UnifiedRenderData destination)
    {
        var entities = source.GetEntities(Allocator.Temp);
        var ranges = source.GetDrawRanges();
        var positions = source.GetPositions();
        var indices = source.GetIndices();

        var hasTexCoords = source.HasVertexAttribute(VertexAttribute.TexCoord0);
        var hasNormals = source.HasVertexAttribute(VertexAttribute.Normal);
        var hasTangents = source.HasVertexAttribute(VertexAttribute.Tangent);
        var hasColors = source.HasVertexAttribute(VertexAttribute.Color);

        var writer = destination.BeginWrite(positions.Length, indices.Length, entities.Length, source.channelMask);
        if (writer.isCreated)
        {
            CopyChannel(positions, writer.positions);
            if (hasTexCoords)
                CopyChannel(source.GetTexCoords(), writer.texCoords);
            if (hasNormals)
                CopyChannel(source.GetNormals(), writer.normals);
            if (hasTangents)
                CopyChannel(source.GetTangents(), writer.tangents);
            if (hasColors)
                CopyChannel(source.GetColors(), writer.colors);

            NativeArray<uint>.Copy(indices, writer.indices, indices.Length);

            // PerEntityRenderData reports counts but not offsets. Baked entries tile the shared
            // buffers gap free in entry order, so the offsets are the running sums of the counts.
            var destinationEntries = writer.entries;
            var vertexOffset = 0;
            var indexOffset = 0;
            for (int i = 0; i < entities.Length; ++i)
            {
                var entity = entities[i];
                destinationEntries[i] = new SpriteBatchEntry
                {
                    spriteId = entity.spriteId,
                    instances = entity.instances,
                    vertexOffset = vertexOffset,
                    vertexCount = entity.vertexCount,
                    indexOffset = indexOffset,
                    indexCount = entity.indexCount,
                    bounds = entity.bounds,
                    spriteProps = entity.spriteProps,
                    spriteColor = entity.spriteColor
                };
                vertexOffset += entity.vertexCount;
                indexOffset += entity.indexCount;
            }

            var rangeCount = Mathf.Min(ranges.Length, writer.drawRangeCapacity);
            NativeArray<DrawRange>.Copy(ranges, writer.drawRanges, rangeCount);

            writer.vertexCount = positions.Length;
            writer.indexCount = indices.Length;
            writer.entryCount = entities.Length;
            writer.drawRangeCount = rangeCount;
        }

        // Nothing is in flight: the copy above ran on this thread.
        destination.EndWrite();
        destination.bounds = source.bounds;

        entities.Dispose();
    }

    // The writer's views span the session's whole reserved capacity, so the copy targets
    // only the leading `source.Length` elements. Both sides are strided views into
    // interleaved vertex data; CopyFrom handles the strides.
    static void CopyChannel<T>(NativeSlice<T> source, NativeSlice<T> destination) where T : struct
    {
        destination.Slice(0, source.Length).CopyFrom(source);
    }

    ushort[] ReadIndices()
    {
        var renderData = (0 != maxArrayCount) ? m_RenderDataAsset.GetRenderData() : null;
        if (null == renderData)
            return new ushort[0];

        using (renderData)
        {
            var indices = renderData.GetIndices();
            var result = new ushort[indices.Length];
            for (int i = 0; i < indices.Length; ++i)
            {
                // Absolute indices into one shared 32-bit buffer, so this narrowing is lossy
                // above 65535 rather than merely legacy.
                if (indices[i] > ushort.MaxValue)
                    throw new OverflowException("SpriteShape geometry has more than 65535 vertices and cannot be read as 16-bit indices.");
                result[i] = (ushort)indices[i];
            }
            return result;
        }
    }

    Vector3[] ReadPositions()
    {
        var renderData = (0 != maxArrayCount) ? m_RenderDataAsset.GetRenderData() : null;
        if (null == renderData)
            return new Vector3[0];

        using (renderData)
        {
            var positions = renderData.GetPositions();
            var result = new Vector3[positions.Length];
            for (int i = 0; i < positions.Length; ++i)
                result[i] = positions[i];
            return result;
        }
    }

    Vector4[] ReadTangents()
    {
        var renderData = (0 != maxArrayCount) ? m_RenderDataAsset.GetRenderData() : null;
        if (null == renderData)
            return new Vector4[0];

        using (renderData)
        {
            // A one element stub is what the old layout stored when Tangents were disabled, and
            // what callers test for with Length > 1.
            if (!renderData.HasVertexAttribute(VertexAttribute.Tangent))
                return new Vector4[1];

            var tangents = renderData.GetTangents();
            var result = new Vector4[tangents.Length];
            for (int i = 0; i < tangents.Length; ++i)
                result[i] = tangents[i];
            return result;
        }
    }

}
