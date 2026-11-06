using System.Collections.Generic;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine.U2D.Common;
using UnityEngine.U2D.Profiling;
#if UNITY_EDITOR
using UnityEditor.U2D;
#endif

namespace UnityEngine.U2D
{
    /// <summary>
    /// SpriteShapeController component contains Spline and SpriteShape Profile information that is used when generating SpriteShape geometry.
    /// </summary>
    [ExecuteInEditMode]
    [RequireComponent(typeof(SpriteShapeRenderer))]
    [DisallowMultipleComponent]
    [HelpURLAttribute("https://docs.unity3d.com/Packages/com.unity.2d.spriteshape@latest/index.html?subfolder=/manual/SSController.html")]
    public class SpriteShapeController : MonoBehaviour
    {
        /// <summary>
        /// Raised when <see cref="physicsShapeData"/> holds a new outline.
        /// </summary>
        public event System.Action<SpriteShapeController> physicsShapeChanged;

        // Internal Dataset.
        const float s_DistanceTolerance = 0.001f;

        // Cached Objects.
        SpriteShape m_ActiveSpriteShape;
        EdgeCollider2D m_EdgeCollider2D;
        PolygonCollider2D m_PolygonCollider2D;
        SpriteShapeRenderer m_SpriteShapeRenderer;
        SpriteShapeGeometryCache m_SpriteShapeGeometryCache;

        // The geometry this controller builds and hands to the renderer. Engine memory behind
        // an IDisposable handle, deliberately not serialized: it is rebuilt from the Spline on
        // demand, and every exit path releases it explicitly rather than leaving it to a
        // finalizer. See ReleaseUnifiedRenderData.
        UnifiedRenderData m_UnifiedRenderData;

        // Guards the write session currently open on m_UnifiedRenderData. Not the same as
        // m_JobHandle, which moves on to the next bake's Jobs.
        JobHandle m_UnifiedWriteFence;

        // Staging geometry that the creator Job and any modifier Jobs write into, before
        // SpriteShapeUnifiedRenderDataJob packs it into m_UnifiedRenderData. Owned here now
        // that the renderer no longer lends out buffers of its own.
        NativeArray<ushort> m_StagingIndices;
        NativeArray<Vector3> m_StagingPositions;
        NativeArray<Vector2> m_StagingTexCoords;
        NativeArray<Vector4> m_StagingTangents;
        NativeArray<SpriteShapeSegment> m_StagingSegments;

        // Scratch for the secondary Texture comparison in ResolveSpriteEntityIds. Static so a
        // per bake, per Sprite pair comparison does not allocate a pair of tiny arrays each
        // time it runs.
        static SecondarySpriteTexture[] s_SecondaryTexturesLhs = new SecondarySpriteTexture[0];
        static SecondarySpriteTexture[] s_SecondaryTexturesRhs = new SecondarySpriteTexture[0];

        Sprite[] m_SpriteArray = new Sprite[0];
        Sprite[] m_EdgeSpriteArray = new Sprite[0];
        Sprite[] m_CornerSpriteArray = new Sprite[0];
        AngleRangeInfo[] m_AngleRangeInfoArray = new AngleRangeInfo[0];

        // Required for Generation.
        NativeArray<float2> m_ColliderData;
        NativeArray<float2> m_ShadowData;
        NativeArray<Vector4> m_TangentData;
        NativeArray<SpriteShapeGeneratorStats> m_Statistics;

        // Renderer Stuff.
        bool m_TessellationNeedsFallback = false;
        bool m_WaitForBake = false;


        // Index of the Fill Sprite in m_SpriteArray. -1 when the Fill Area has no Sprite.
        int m_FillSpriteIndex = -1;

        // Hash Check.
        int m_ActiveSplineHash = 0;
        int m_ActiveSpriteShapeHash = 0;

        int m_CachedChangeHash = -1;
        int m_CachedForChangeIndex = -1;

        EdgeCollider2D m_ActiveEdgeCollider;
        PolygonCollider2D m_ActivePolygonCollider;

        int m_MaxArrayCount = 0;
        JobHandle m_JobHandle;
        SpriteShapeParameters m_ActiveShapeParameters;

        // Serialized Data.
        [SerializeField]
        Spline m_Spline = new Spline();
        [SerializeField]
        SpriteShape m_SpriteShape;

        [SerializeField]
        float m_FillPixelPerUnit = 100.0f;
        [SerializeField]
        float m_StretchTiling = 1.0f;
        [SerializeField]
        int m_SplineDetail;
        [SerializeField]
        bool m_AdaptiveUV;
        [SerializeField]
        bool m_StretchUV;
        [SerializeField]
        bool m_WorldSpaceUV;
        [SerializeField]
        float m_CornerAngleThreshold = 30.0f;
        [SerializeField]
        int m_ColliderDetail;
        [SerializeField, Range(-0.5f, 0.5f)]
        float m_ColliderOffset;
        [SerializeField]
        bool m_UpdateCollider = true;
        [SerializeField]
        bool m_EnableTangents = false;
        [SerializeField]
        [HideInInspector]
        bool m_GeometryCached = false;
        [SerializeField]
        bool m_UTess2D = true;
        [SerializeField]
        bool m_UpdateShadow = false;
        [SerializeField]
        int m_ShadowDetail = (int)QualityDetail.High;
        [SerializeField, Range(-0.5f, 0.5f)]
        float m_ShadowOffset = 0.5f;
        [SerializeField]
        float m_BoundsScale = 2.0f;
        [SerializeField]
        bool m_UpdateGeometry = true;

        [SerializeField]
        SpriteShapeGeometryCreator m_Creator;
        [SerializeField]
        List<SpriteShapeGeometryModifier> m_Modifiers = new List<SpriteShapeGeometryModifier>();
        [SerializeField]
        Vector2[] m_ColliderSegment = new Vector2[0];
        [SerializeField]
        List<Vector2> m_ShadowSegment = new List<Vector2>();

        internal static readonly ProfilerMarker generateGeometry = new ProfilerMarker("SpriteShape.GenerateGeometry");
        internal static readonly ProfilerMarker generateCollider = new ProfilerMarker("SpriteShape.GenerateCollider");

#region GetSet

        internal int maxArrayCount
        {
            get { return m_MaxArrayCount; }
            set { m_MaxArrayCount = value; }
        }

        internal bool geometryCached
        {
            get { return m_GeometryCached; }
            set { m_GeometryCached = value; InvalidateChangeHash(); }
        }

        internal int splineHashCode
        {
            get { return m_ActiveSplineHash; }
        }

        internal Sprite[] spriteArray
        {
            get { return m_SpriteArray; }
        }

        /// <summary>
        /// Index of the Fill Sprite within spriteArray, which is always 0 when a Fill Sprite is set, or -1
        /// when the Fill Area has no Sprite and is rendered through the legacy Fill Texture instead. Edge
        /// and corner Sprites follow the Fill Sprite, so their indices shift by one when it is present.
        /// </summary>
        internal int fillSpriteIndex
        {
            get { return m_FillSpriteIndex; }
        }

        internal SpriteShapeParameters spriteShapeParameters
        {
            get { return m_ActiveShapeParameters; }
        }

        // The generator's own output, before SpriteShapeUnifiedRenderDataJob groups it into
        // entries: one segment per spline segment in spline order, with segment local indices.
        // This used to be reachable through SpriteShapeRenderer.GetSegments/GetChannels, which
        // no longer describe what is drawn now that the renderer owns no geometry.
        internal NativeArray<SpriteShapeSegment> stagedSegments
        {
            get { return m_StagingSegments; }
        }

        internal NativeArray<ushort> stagedIndices
        {
            get { return m_StagingIndices; }
        }

        internal NativeSlice<Vector3> stagedPositions
        {
            get { return m_StagingPositions.IsCreated ? new NativeSlice<Vector3>(m_StagingPositions) : default; }
        }

        internal NativeSlice<Vector2> stagedTexCoords
        {
            get { return m_StagingTexCoords.IsCreated ? new NativeSlice<Vector2>(m_StagingTexCoords) : default; }
        }

        internal NativeSlice<Vector4> stagedTangents
        {
            get { return m_StagingTangents.IsCreated ? new NativeSlice<Vector4>(m_StagingTangents) : default; }
        }

        /// <summary>
        /// The geometry this controller last built, with the bake completed and its write session
        /// closed, so it can be read without waiting for a render.
        /// </summary>
        /// <remarks>
        /// The renderer normally closes the session at prepare time. EndWrite is idempotent, so
        /// closing it early only moves the truncation forward; it does not take the geometry away
        /// from the renderer, which holds a reference of its own.
        /// </remarks>
        internal UnifiedRenderData CompleteUnifiedRenderData()
        {
            m_JobHandle.Complete();
            if (null != m_UnifiedRenderData && m_UnifiedRenderData.isValid && m_UnifiedRenderData.isWriteSessionOpen)
            {
                // The session's own fence, which m_JobHandle only happens to match while the
                // pack Job is the last thing scheduled.
                m_UnifiedWriteFence.Complete();
                m_UnifiedRenderData.EndWrite();
            }
            return m_UnifiedRenderData;
        }

        internal SpriteShapeGeometryCache spriteShapeGeometryCache
        {
            get
            {
                if (!m_SpriteShapeGeometryCache)
                {
                    bool b = TryGetComponent(typeof(SpriteShapeGeometryCache), out Component comp);
                    m_SpriteShapeGeometryCache = b ? (comp as SpriteShapeGeometryCache) : null;
                }
                return m_SpriteShapeGeometryCache;
            }
        }

        internal Sprite[] cornerSpriteArray
        {
            get { return m_CornerSpriteArray; }
        }

        internal Sprite[] edgeSpriteArray
        {
            get { return m_EdgeSpriteArray; }
        }

        internal NativeArray<float2> shadowData
        {
            get { return m_ShadowData; }
        }

        /// <summary> Angle Ranges </summary>
        public AngleRangeInfo[] angleRangeInfoArray
        {
            get { return m_AngleRangeInfoArray; }
        }

        /// <summary>Get/Set SpriteShape Geometry Creator. </summary>
        public SpriteShapeGeometryCreator spriteShapeCreator
        {
            get
            {
                if (m_Creator == null)
                {
#if UNITY_EDITOR
                    m_Creator = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Packages/com.unity.2d.spriteshape/Runtime/DefaultSpriteShapeCreator.asset") as SpriteShapeGeometryCreator;
                    if (m_Creator == null)
#endif
                        m_Creator = SpriteShapeDefaultCreator.defaultInstance;
                }
                return m_Creator;
            }
            set
            {
                if (value != null)
                {
                    m_Creator = value;
                    InvalidateChangeHash();
                }
            }
        }

        /// <summary>Get a list of Modifiers. </summary>
        public List<SpriteShapeGeometryModifier> modifiers
        {
            get { return m_Modifiers; }
        }

        /// <summary>Hash code for SpriteShape used to check for changes. </summary>
        public int spriteShapeHashCode
        {
            get { return m_ActiveSpriteShapeHash; }
        }

        /// <summary>Defines whether UV for fill geometry uses local or global space. </summary>
        public bool worldSpaceUVs
        {
            get { return m_WorldSpaceUV; }
            set { m_WorldSpaceUV = value; InvalidateChangeHash(); }
        }

        /// <summary>Defines pixel per unit for fill geometry UV generation. </summary>
        public float fillPixelsPerUnit
        {
            get { return m_FillPixelPerUnit; }
            set { m_FillPixelPerUnit = value; }
        }

        /// <summary>Enable tangent channel when generating SpriteShape geometry (used in Shaders) </summary>
        public bool enableTangents
        {
            get { return m_EnableTangents; }
            set { m_EnableTangents = value; InvalidateChangeHash(); }
        }

        /// <summary>Stretch tiling for inner fill geometry UV generation. </summary>
        public float stretchTiling
        {
            get { return m_StretchTiling; }
            set { m_StretchTiling = value; InvalidateChangeHash(); }
        }

        /// <summary>Level of detail for generated geometry. </summary>
        public int splineDetail
        {
            get { return m_SplineDetail; }
            set { m_SplineDetail = Mathf.Max(0, value); InvalidateChangeHash(); }
        }

        /// <summary>Level of detail for geometry generated for colliders. </summary>
        public int colliderDetail
        {
            get { return m_ColliderDetail; }
            set { m_ColliderDetail = Mathf.Max(0, value); InvalidateChangeHash(); }
        }

        /// <summary>Offset for colliders. </summary>
        public float colliderOffset
        {
            get { return m_ColliderOffset; }
            set { m_ColliderOffset = value; InvalidateChangeHash(); }
        }

        /// <summary>Angle threshold within which corners are enabled. </summary>
        public float cornerAngleThreshold
        {
            get { return m_CornerAngleThreshold; }
            set { m_CornerAngleThreshold = value; InvalidateChangeHash(); }
        }

        /// <summary>Auto update colliders on any change to SpriteShape geometry. </summary>
        public bool autoUpdateCollider
        {
            get { return m_UpdateCollider; }
            set { m_UpdateCollider = value; InvalidateChangeHash(); }
        }

        /// <summary>Optimize generated collider geometry. </summary>
        public bool optimizeCollider
        {
            get { return true; }
        }

        /// <summary>Optimize generated SpriteShape geometry. </summary>
        public bool optimizeGeometry
        {
            get { return true; }
        }

        /// <summary>Does this SpriteShapeController object has colliders ?</summary>
        public bool hasCollider
        {
            get { return (edgeCollider != null) || (polygonCollider != null); }
        }

        /// <summary>Spline object that has data to create the Bezier curve of this SpriteShape Controller. </summary>
        public Spline spline
        {
            get { return m_Spline; }
        }

        /// <summary>Scale the Bounding Box of SpriteShape Geometry so its not culled out when scripting or dyanmically modifying Splines. </summary>
        public float boundsScale
        {
            get { return m_BoundsScale; }
            set { m_BoundsScale = value; InitBounds(); }
        }

        /// <summary>SpriteShape Profile asset that contains information on how to generate/render SpriteShapes. </summary>
        public SpriteShape spriteShape
        {
            get { return m_SpriteShape; }
            set { m_SpriteShape = value; }
        }

        /// <summary>EdgeCollider2D component attached to this Object.</summary>
        public EdgeCollider2D edgeCollider
        {
            get
            {
                if (!m_EdgeCollider2D)
                {
                    bool b = TryGetComponent(typeof(EdgeCollider2D), out Component comp);
                    m_EdgeCollider2D = b ? (comp as EdgeCollider2D) : null;
                }
                return m_EdgeCollider2D;
            }
        }

        /// <summary>PolygonCollider2D component attached to this Object.</summary>
        public PolygonCollider2D polygonCollider
        {
            get
            {
                if (!m_PolygonCollider2D)
                {
                    bool b = TryGetComponent(typeof(PolygonCollider2D), out Component comp);
                    m_PolygonCollider2D = b ? (comp as PolygonCollider2D) : null;
                }
                return m_PolygonCollider2D;
            }
        }

        /// <summary>SpriteShapeRenderer component of this Object. </summary>
        public SpriteShapeRenderer spriteShapeRenderer
        {
            get
            {
                if (!m_SpriteShapeRenderer)
                    m_SpriteShapeRenderer = GetComponent<SpriteShapeRenderer>();
                return m_SpriteShapeRenderer;
            }
        }

        internal bool updateShadow
        {
            get { return m_UpdateShadow; }
            set { m_UpdateShadow = value; InvalidateChangeHash(); }
        }

        internal int shadowDetail
        {
            get { return m_ShadowDetail; }
            set { m_ShadowDetail = value; InvalidateChangeHash(); }
        }

        internal float shadowOffset
        {
            get { return m_ShadowOffset; }
            set { m_ShadowOffset = value; InvalidateChangeHash(); }
        }

        internal List<Vector2> shadowSegment
        {
            get { return m_ShadowSegment; }
        }

        internal NativeArray<SpriteShapeGeneratorStats> stats
        {
            get
            {
                if (!m_Statistics.IsCreated)
                    m_Statistics = new NativeArray<SpriteShapeGeneratorStats>(1, Allocator.Persistent);
                return m_Statistics;
            }
        }

        /// <summary>
        /// Force the controller to wait for the bake job to complete. Normally
        /// the controller just starts the job and returns immediately. Setting this
        /// to true will wait for the job to complete before returning, impacting performance.
        /// </summary>
        public bool WaitForBake
        {
            get => m_WaitForBake;
            set => m_WaitForBake = value;
        }

        /// <summary>Auto update Geometry. If disabled, will not automatically generate geometry on changes</summary>
        internal bool autoUpdateGeometry
        {
            get { return m_UpdateGeometry; }
            set
            {
                if (value != m_UpdateGeometry)
                {
                    if (value == false)
                        BakeMesh();
                    m_UpdateGeometry = value;
                }
            }
        }

        /// <summary>
        /// The shape of the collision outline.
        /// </summary>
        public System.ReadOnlySpan<Vector2> physicsShapeData => m_ColliderSegment;

#endregion

#region EventHandles.

        void DisposeInternal()
        {
            m_JobHandle.Complete();
            ReleaseUnifiedRenderData();
            if (m_ColliderData.IsCreated)
                m_ColliderData.Dispose();
            if (m_ShadowData.IsCreated)
                m_ShadowData.Dispose();
            if (m_TangentData.IsCreated)
                m_TangentData.Dispose();
            if (m_Statistics.IsCreated)
                m_Statistics.Dispose();
            DisposeStagingGeometry();
        }

        /// <summary>
        /// Releases the UnifiedRenderData this controller owns.
        /// </summary>
        /// <remarks>
        /// Ownership is SHARED with the renderer: SetUnifiedRenderData takes a reference of its
        /// own, so releasing this handle never stops a draw that is already in flight, and the
        /// renderer keeps drawing the last geometry it was given.
        ///
        /// The write session is closed FIRST. The renderer normally closes it at prepare time,
        /// but it may never get there -- the component can be disabled, destroyed or reloaded
        /// between the bake and the next render -- and destroying render data while a session
        /// is open frees buffers a Job still names.
        /// </remarks>
        void ReleaseUnifiedRenderData()
        {
            if (null == m_UnifiedRenderData)
                return;

            if (m_UnifiedRenderData.isValid)
            {
                if (m_UnifiedRenderData.isWriteSessionOpen)
                {
                    // EndWrite truncates the buffers the pack Job writes through, so its fence
                    // has to be settled first.
                    m_UnifiedWriteFence.Complete();
                    m_UnifiedRenderData.EndWrite();
                }
                m_UnifiedRenderData.Dispose();
            }
            m_UnifiedRenderData = null;
        }

        void DisposeStagingGeometry()
        {
            if (m_StagingIndices.IsCreated)
                m_StagingIndices.Dispose();
            if (m_StagingPositions.IsCreated)
                m_StagingPositions.Dispose();
            if (m_StagingTexCoords.IsCreated)
                m_StagingTexCoords.Dispose();
            if (m_StagingTangents.IsCreated)
                m_StagingTangents.Dispose();
            if (m_StagingSegments.IsCreated)
                m_StagingSegments.Dispose();
        }

        void OnApplicationQuit()
        {
            DisposeInternal();
        }

        void OnEnable()
        {
            InitBounds();
            UpdateSpriteData();
        }

        void OnDisable()
        {
            UpdateGeometryCache();
            DisposeInternal();
        }

        void OnDestroy()
        {
            // BakeMesh only checks enabled, so an explicit bake on an inactive GameObject allocates
            // after OnDisable has run, and destroying that GameObject does not call OnDisable again.
            DisposeInternal();
        }

        void OnValidate()
        {
            m_Spline?.MarkDirty();
            InvalidateChangeHash();
        }

        void OnDidApplyAnimationProperties()
        {
            m_Spline?.MarkDirty();
            InvalidateChangeHash();
        }

        void Reset()
        {
            m_SplineDetail = (int)QualityDetail.High;
            m_AdaptiveUV = true;
            m_StretchUV = false;
            m_FillPixelPerUnit = 100f;

            m_ColliderDetail = (int)QualityDetail.High;
            m_ShadowDetail = (int)QualityDetail.High;
            m_StretchTiling = 1.0f;
            m_WorldSpaceUV = false;
            m_CornerAngleThreshold = 30.0f;
            m_ColliderOffset = 0;
            m_ShadowOffset = 0.5f;
            m_UpdateCollider = true;
            m_EnableTangents = false;

            spline.Clear();
            spline.InsertPointAt(0, Vector2.left + Vector2.down);
            spline.InsertPointAt(1, Vector2.left + Vector2.up);
            spline.InsertPointAt(2, Vector2.right + Vector2.up);
            spline.InsertPointAt(3, Vector2.right + Vector2.down);

            // The settings above were written straight to the fields, bypassing the setters.
            InvalidateChangeHash();
        }

        static void SmartDestroy(UnityEngine.Object o)
        {
            if (o == null)
                return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(o);
            else
#endif
                Destroy(o);
        }

#endregion

#region HashAndDataCheck

        internal Bounds InitBounds()
        {
            var pointCount = spline.GetPointCount();
            if (pointCount > 1)
            {
                Bounds bounds = new Bounds(spline.GetPosition(0), Vector3.zero);
                for (int i = 1; i < pointCount; ++i)
                    bounds.Encapsulate(spline.GetPosition(i));
                bounds.size = bounds.size * m_BoundsScale;
                bounds.Encapsulate(spriteShapeRenderer.localBounds);
                spriteShapeRenderer.SetLocalAABB(bounds);
                return bounds;
            }
            // No Spline at all. Use reasonable default.
            Bounds defaultBounds = new Bounds(gameObject.transform.position, Vector3.one);
            spriteShapeRenderer.bounds = defaultBounds;
            return defaultBounds;
        }

        /// <summary>
        /// Refresh SpriteShape Hash so its force generated again on the next frame if its visible.
        /// </summary>
        public void RefreshSpriteShape()
        {
            // This is only needed for Playmode.
            m_ActiveSplineHash = 0;
            InvalidateChangeHash();
        }

        // Ensure Neighbor points are not too close to each other.
        // FIX for SpatialAccel tests: Reduced distance tolerance and added closed-loop check
        bool ValidateSpline()
        {
            int pointCount = spline.GetPointCount();
            if (pointCount < 2)
                return false;
            for (int i = 0; i < pointCount - 1; ++i)
            {
                var vec = spline.GetPosition(i) - spline.GetPosition(i + 1);
                if (vec.sqrMagnitude < s_DistanceTolerance)
                {
                    Debug.LogWarningFormat(gameObject, "[SpriteShape] Control points {0} & {1} are too close. SpriteShape will not be generated for < {2} >.", i, i + 1, gameObject.name);
                    return false;
                }
            }
            // For closed shapes, also check wrap-around (last point to first point)
            if (!spline.isOpenEnded && pointCount >= 2)
            {
                var vec = spline.GetPosition(pointCount - 1) - spline.GetPosition(0);
                if (vec.sqrMagnitude < s_DistanceTolerance)
                {
                    Debug.LogWarningFormat(gameObject, "[SpriteShape] Control points {0} & {1} are too close. SpriteShape will not be generated for < {2} >.", pointCount - 1, 0, gameObject.name);
                    return false;
                }
            }
            return true;
        }

        // Ensure SpriteShape is valid if not
        bool ValidateSpriteShapeTexture()
        {
            bool valid = false;

            // Check if SpriteShape Profile is valid.
            if (spriteShape != null)
            {
                // Open ended and no valid Sprites set. Check if it has a valid fill texture.
                if (!spline.isOpenEnded)
                {
                    valid = (spriteShape.resolvedFillTexture != null);
                }
            }
            else
            {
                // Warn that no SpriteShape is set.
                Debug.LogWarningFormat(gameObject, "[SpriteShape] A valid SpriteShape profile has not been set for gameObject < {0} >.", gameObject.name);
#if UNITY_EDITOR
                // We allow null SpriteShape for rapid prototyping in Editor.
                valid = true;
#endif
            }
            return valid;
        }

        internal bool ValidateUTess2D()
        {
            bool uTess2D = m_UTess2D;
            // Check for all properties that can create overlaps/intersections.
            if (m_UTess2D && null != spriteShape)
            {
                uTess2D = (spriteShape.fillOffset == 0);
            }
            return uTess2D && !m_TessellationNeedsFallback;
        }

        bool HasSpriteShapeChanged()
        {
            bool changed = (m_ActiveSpriteShape != spriteShape);
            if (changed)
                m_ActiveSpriteShape = spriteShape;
            return changed;
        }

        bool HasSpriteShapeDataChanged()
        {
            bool updateSprites = HasSpriteShapeChanged();
            if (spriteShape)
            {
                var hashCode = SpriteShape.GetSpriteShapeHashCode(spriteShape);
                if (spriteShapeHashCode != hashCode)
                {
                    m_ActiveSpriteShapeHash = hashCode;
                    updateSprites = true;
                }
            }
            return updateSprites;
        }

        int GetCustomScriptHashCode()
        {

            int hashCode = 0;

            unchecked
            {
                hashCode = (int)2166136261 ^ spriteShapeCreator.GetVersion();
                for (int i = 0; i < m_Modifiers.Count; ++i)
                {
                    var mod = m_Modifiers[i];
                    if (null != mod)
                        hashCode = hashCode * 16777619 ^ mod.GetVersion();
                }
            }

            return hashCode;

        }

        internal void InvalidateChangeHash()
        {
            m_CachedForChangeIndex = -1;
        }

        internal int GetSplineDataHashCode()
        {
            var changeIndex = spline.GetChangeIndex();

            // Everything except the custom scripts only changes through a Spline mutator (bumping
            // the change index) or a setter/OnValidate (resetting m_CachedForChangeIndex to -1), so
            // that part of the fold is cached across calls.
            if (m_CachedForChangeIndex != changeIndex)
            {
                unchecked
                {
                    int cacheable = (int)2166136261;
                    cacheable = cacheable * 16777619 ^ changeIndex;

                    // Local Stuff.
                    cacheable = cacheable * 16777619 ^ (m_UTess2D ? 1 : 0);
                    cacheable = cacheable * 16777619 ^ (m_WorldSpaceUV ? 1 : 0);
                    cacheable = cacheable * 16777619 ^ (m_EnableTangents ? 1 : 0);
                    cacheable = cacheable * 16777619 ^ (m_GeometryCached ? 1 : 0);
                    cacheable = cacheable * 16777619 ^ (m_UpdateShadow ? 1 : 0);
                    cacheable = cacheable * 16777619 ^ (m_UpdateCollider ? 1 : 0);
                    cacheable = cacheable * 16777619 ^ (m_StretchTiling.GetHashCode());
                    cacheable = cacheable * 16777619 ^ (m_ColliderOffset.GetHashCode());
                    cacheable = cacheable * 16777619 ^ (m_ColliderDetail.GetHashCode());
                    cacheable = cacheable * 16777619 ^ (m_ShadowOffset.GetHashCode());
                    cacheable = cacheable * 16777619 ^ (m_ShadowDetail.GetHashCode());
                    cacheable = cacheable * 16777619 ^ (m_SplineDetail.GetHashCode());
                    cacheable = cacheable * 16777619 ^ (m_CornerAngleThreshold.GetHashCode());

                    m_CachedChangeHash = cacheable;
                    m_CachedForChangeIndex = changeIndex;
                }
            }

            unchecked
            {
                var hashCode = m_CachedChangeHash * 16777619 ^ GetCustomScriptHashCode();
                return (0 == hashCode) ? 1 : hashCode;
            }
        }

        bool HasSplineDataChanged()
        {
            var hashCode = GetSplineDataHashCode();
            if (splineHashCode == hashCode)
                return false;
            m_ActiveSplineHash = hashCode;
            return true;
        }

        void OnBecameInvisible()
        {
            InitBounds();
        }

        void LateUpdate()
        {
            using var lateUpdateScope = PerformanceMarkers.lateUpdateMarker.Auto();

            BakeCollider();

            if (autoUpdateCollider)
                TryUpdateColliders();
        }

        void OnWillRenderObject()
        {
            if (autoUpdateGeometry || 0 == m_ActiveSplineHash)
            {
                var jh = BakeMesh();
                if (m_WaitForBake)
                    jh.Complete();
            }
        }

        /// <summary>
        /// Generate geometry on a Job.
        /// </summary>
        /// <returns>JobHandle for the SpriteShape geometry generation job.</returns>
        public JobHandle BakeMesh()
        {
            JobHandle jobHandle = default;

#if !UNITY_EDITOR
            if (spriteShapeGeometryCache)
            {
                // If SpriteShapeGeometry has already been uploaded, don't bother checking further.
                if (0 != m_ActiveSplineHash && 0 != spriteShapeGeometryCache.maxArrayCount)
                    return jobHandle;
            }
#endif

            bool valid = ValidateSpline();

            if (valid)
            {
                bool splineChanged = HasSplineDataChanged();
                bool spriteShapeChanged = HasSpriteShapeDataChanged();
                bool spriteShapeParametersChanged = UpdateSpriteShapeParameters();

                if (splineChanged || spriteShapeChanged || spriteShapeParametersChanged || m_TessellationNeedsFallback)
                {
                    if (spriteShapeChanged)
                    {
                        UpdateSpriteData();
                    }
                    jobHandle = ScheduleBake();

#if UNITY_EDITOR
                    UpdateGeometryCache();
#endif
                }

            }
            return jobHandle;
        }

#endregion

#region UpdateData

        /// <summary>
        /// Update Cache.
        /// </summary>
        internal void UpdateGeometryCache()
        {
            if (spriteShapeGeometryCache && geometryCached)
            {
                // The renderer normally closes the write session at prepare time, but the cache
                // has to READ the finished geometry now, so close it here instead.
                CompleteUnifiedRenderData();
                spriteShapeGeometryCache.UpdateGeometryCache();
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Bakes this controller into a new geometry cache asset, for a Prefab being imported.
        /// </summary>
        /// <returns>The asset the geometry was written to, or null if nothing was cached.</returns>
        /// <remarks>
        /// Objects loaded for import never receive OnEnable or OnDisable, so this sets up and
        /// releases the bake state those normally handle. Returned as Object because the Editor
        /// assembly cannot see UnifiedRenderDataAsset.
        /// </remarks>
        internal Object BakeGeometryCacheForImport()
        {
            var cache = spriteShapeGeometryCache;
            if (!cache || !geometryCached)
                return null;

            cache.DetachRenderDataAsset();
            InitBounds();
            UpdateSpriteData();
            RefreshSpriteShape();
            BakeMesh().Complete();
            DisposeInternal();
            return cache.renderDataAsset;
        }
#endif

        /// <summary>
        /// Force update SpriteShape parameters.
        /// </summary>
        /// <returns>Returns true if there are changes</returns>
        public bool UpdateSpriteShapeParameters()
        {
            bool carpet = !spline.isOpenEnded;
            bool smartSprite = true;
            bool adaptiveUV = m_AdaptiveUV;
            bool stretchUV = m_StretchUV;
            bool spriteBorders = false;
            uint fillScale = 0;
            uint splineDetail = (uint)m_SplineDetail;
            float borderPivot = 0f;
            float angleThreshold = (m_CornerAngleThreshold >= 0 && m_CornerAngleThreshold < 90) ? m_CornerAngleThreshold : 89.9999f;
            Texture2D fillTexture = null;
            Matrix4x4 transformMatrix = Matrix4x4.identity;

            if (spriteShape)
            {
                if (worldSpaceUVs)
                    transformMatrix = transform.localToWorldMatrix;

                fillTexture = spriteShape.resolvedFillTexture;
                fillScale = stretchUV ? (uint)stretchTiling : (uint)fillPixelsPerUnit;
                borderPivot = spriteShape.fillOffset;
                spriteBorders = spriteShape.useSpriteBorders;
                // If Corners are enabled, set smart-sprite to false.
                if (spriteShape.cornerSprites.Count > 0)
                    smartSprite = false;
            }
            else
            {
#if UNITY_EDITOR
                fillTexture = UnityEditor.EditorGUIUtility.whiteTexture;
                fillScale = 100;
#endif
            }

            bool changed = m_ActiveShapeParameters.adaptiveUV != adaptiveUV ||
                m_ActiveShapeParameters.angleThreshold != angleThreshold ||
                m_ActiveShapeParameters.borderPivot != borderPivot ||
                m_ActiveShapeParameters.carpet != carpet ||
                m_ActiveShapeParameters.fillScale != fillScale ||
                m_ActiveShapeParameters.fillTexture != fillTexture ||
                m_ActiveShapeParameters.smartSprite != smartSprite ||
                m_ActiveShapeParameters.splineDetail != splineDetail ||
                m_ActiveShapeParameters.spriteBorders != spriteBorders ||
                m_ActiveShapeParameters.transform != transformMatrix ||
                m_ActiveShapeParameters.stretchUV != stretchUV;

            m_ActiveShapeParameters.adaptiveUV = adaptiveUV;
            m_ActiveShapeParameters.stretchUV = stretchUV;
            m_ActiveShapeParameters.angleThreshold = angleThreshold;
            m_ActiveShapeParameters.borderPivot = borderPivot;
            m_ActiveShapeParameters.carpet = carpet;
            m_ActiveShapeParameters.fillScale = fillScale;
            m_ActiveShapeParameters.fillTexture = fillTexture;
            m_ActiveShapeParameters.smartSprite = smartSprite;
            m_ActiveShapeParameters.splineDetail = splineDetail;
            m_ActiveShapeParameters.spriteBorders = spriteBorders;
            m_ActiveShapeParameters.transform = transformMatrix;

            return changed;
        }

        void UpdateSpriteData()
        {
            if (spriteShape)
            {
                List<Sprite> edgeSpriteList = new List<Sprite>();
                List<Sprite> cornerSpriteList = new List<Sprite>();
                List<AngleRangeInfo> angleRangeInfoList = new List<AngleRangeInfo>();

                List<AngleRange> sortedAngleRanges = new List<AngleRange>(spriteShape.angleRanges);
                sortedAngleRanges.Sort((a, b) => a.order.CompareTo(b.order));

                for (int i = 0; i < sortedAngleRanges.Count; i++)
                {
                    bool validSpritesFound = false;
                    AngleRange angleRange = sortedAngleRanges[i];
                    foreach (Sprite edgeSprite in angleRange.sprites)
                    {
                        if (edgeSprite != null)
                        {
                            validSpritesFound = true;
                            break;
                        }
                    }

                    if (validSpritesFound)
                    {
                        AngleRangeInfo angleRangeInfo = new AngleRangeInfo();
                        angleRangeInfo.start = angleRange.start;
                        angleRangeInfo.end = angleRange.end;
                        angleRangeInfo.order = (uint)angleRange.order;
                        List<int> spriteIndices = new List<int>();
                        foreach (Sprite edgeSprite in angleRange.sprites)
                        {
                            edgeSpriteList.Add(edgeSprite);
                            spriteIndices.Add(edgeSpriteList.Count - 1);
                        }
                        angleRangeInfo.sprites = spriteIndices.ToArray();
                        angleRangeInfoList.Add(angleRangeInfo);
                    }
                }

                bool validCornerSpritesFound = false;
                foreach (CornerSprite cornerSprite in spriteShape.cornerSprites)
                {
                    if (cornerSprite.sprites[0] != null)
                    {
                        validCornerSpritesFound = true;
                        break;
                    }
                }

                if (validCornerSpritesFound)
                {
                    for (int i = 0; i < spriteShape.cornerSprites.Count; i++)
                    {
                        CornerSprite cornerSprite = spriteShape.cornerSprites[i];
                        cornerSpriteList.Add(cornerSprite.sprites[0]);
                    }
                }

                m_EdgeSpriteArray = edgeSpriteList.ToArray();
                m_CornerSpriteArray = cornerSpriteList.ToArray();
                m_AngleRangeInfoArray = angleRangeInfoList.ToArray();

                List<Sprite> spriteList = new List<Sprite>();

                // The Fill Sprite takes the first slot, which makes the Fill Area a regular Sprite segment
                // and is what binds its secondary Textures. Assets still on the legacy Fill Texture leave
                // the index at -1 and keep rendering from SpriteShapeParameters.fillTexture.
                m_FillSpriteIndex = -1;
                if (spriteShape.fillSprite != null)
                {
                    spriteList.Add(spriteShape.fillSprite);
                    m_FillSpriteIndex = 0;
                }

                spriteList.AddRange(m_EdgeSpriteArray);
                spriteList.AddRange(m_CornerSpriteArray);
                m_SpriteArray = spriteList.ToArray();
            }
            else
            {
                m_SpriteArray = new Sprite[0];
                m_EdgeSpriteArray = new Sprite[0];
                m_CornerSpriteArray = new Sprite[0];
                m_AngleRangeInfoArray = new AngleRangeInfo[0];
                m_FillSpriteIndex = -1;
            }
        }

        internal NativeArray<ShapeControlPoint> GetShapeControlPoints()
        {
            int pointCount = spline.GetPointCount();
            NativeArray<ShapeControlPoint> shapePoints = new NativeArray<ShapeControlPoint>(pointCount, Allocator.Temp);
            for (int i = 0; i < pointCount; ++i)
            {
                ShapeControlPoint shapeControlPoint;
                shapeControlPoint.position = spline.GetPosition(i);
                shapeControlPoint.leftTangent = spline.GetLeftTangent(i);
                shapeControlPoint.rightTangent = spline.GetRightTangent(i);
                shapeControlPoint.mode = (int)spline.GetTangentMode(i);
                shapePoints[i] = shapeControlPoint;
            }
            return shapePoints;
        }

        internal NativeArray<SplinePointMetaData> GetSplinePointMetaData()
        {
            int pointCount = spline.GetPointCount();
            NativeArray<SplinePointMetaData> shapeMetaData = new NativeArray<SplinePointMetaData>(pointCount, Allocator.Temp);
            for (int i = 0; i < pointCount; ++i)
            {
                SplinePointMetaData metaData;
                metaData.height = m_Spline.GetHeight(i);
                metaData.spriteIndex = (uint)m_Spline.GetSpriteIndex(i);
                metaData.cornerMode = (int)m_Spline.GetCornerMode(i);
                shapeMetaData[i] = metaData;
            }
            return shapeMetaData;
        }

        internal int CalculateMaxArrayCount(NativeArray<ShapeControlPoint> shapePoints)
        {
            int maxVertexCount = 1024 * 64;
            bool hasSprites = false;
            float smallestWidth = 99999.0f;

            if (null != spriteArray)
            {
                for (int i = 0; i < m_SpriteArray.Length; ++i)
                {
                    // The Fill Sprite is not laid out along the spline, so its width must not drive the
                    // segment count. Excluding it keeps the vertex estimate identical to a Fill Texture.
                    if (i == m_FillSpriteIndex)
                        continue;

                    var sprite = m_SpriteArray[i];
                    if (sprite != null)
                    {
                        hasSprites = true;
                        float pixelWidth = BezierUtility.GetSpritePixelWidth(sprite);
                        smallestWidth = (smallestWidth > pixelWidth) ? pixelWidth : smallestWidth;
                    }
                }
            }

            // Approximate vertex Array Count. Include Corners and Wide Sprites into account.
            float smallestSegment = smallestWidth;
            float shapeLength = BezierUtility.BezierLength(shapePoints, splineDetail, ref smallestSegment) * 4.0f;
            int adjustShape = shapePoints.Length * 5 * splineDetail;
            int adjustWidth = hasSprites ? ((int)(shapeLength / smallestSegment) * splineDetail) + adjustShape : 0;
            adjustShape = optimizeGeometry ? (adjustShape) : (adjustShape * 2);
            adjustShape = ValidateSpriteShapeTexture() ? adjustShape : 0;
            maxArrayCount = adjustShape + adjustWidth;
            maxArrayCount = math.min(maxArrayCount, maxVertexCount);
            return maxArrayCount;
        }

#endregion

#region ScheduleAndGenerate

        unsafe JobHandle ScheduleBake()
        {
            JobHandle jobHandle = default;

            bool staticUpload = Application.isPlaying;
#if !UNITY_EDITOR
            staticUpload = true;
#endif
            if (staticUpload && geometryCached)
            {
                if (spriteShapeGeometryCache)
                    if (spriteShapeGeometryCache.maxArrayCount != 0)
                    {
#if ENABLE_SPRITESHAPE_POLYSPATIAL_SERIALIZATION
                        InternalEngineBridge.MarkDirty(this);
#endif
                        return spriteShapeGeometryCache.Upload(spriteShapeRenderer, this);
                    }
            }
            maxArrayCount = spriteShapeCreator.GetVertexArrayCount(this);

            if (maxArrayCount > 0 && enabled)
            {
                // Complate previos
                m_JobHandle.Complete();

                // Collider Data
                if (m_ColliderData.IsCreated)
                    m_ColliderData.Dispose();
                m_ColliderData = new NativeArray<float2>(maxArrayCount, Allocator.Persistent);
                if (m_ShadowData.IsCreated)
                    m_ShadowData.Dispose();
                m_ShadowData = new NativeArray<float2>(maxArrayCount, Allocator.Persistent);

                // Tangent Data
                if (!m_TangentData.IsCreated)
                    m_TangentData = new NativeArray<Vector4>(1, Allocator.Persistent);

                NativeArray<ushort> indexArray;
                NativeSlice<Vector3> posArray;
                NativeSlice<Vector2> uv0Array;
                NativeSlice<Vector4> tanArray;
                NativeArray<SpriteShapeSegment> geomArray;
                PrepareStagingGeometry(maxArrayCount, spline.GetPointCount() * 8, out indexArray, out posArray, out uv0Array, out tanArray, out geomArray);

                m_JobHandle = jobHandle = spriteShapeCreator.MakeCreatorJob(this, indexArray, posArray, uv0Array, tanArray, geomArray, m_ColliderData);
                foreach (var geomMod in m_Modifiers)
                    if (null != geomMod)
                        m_JobHandle = geomMod.MakeModifierJob(m_JobHandle, this, indexArray, posArray, uv0Array, tanArray, geomArray, m_ColliderData);

                // Pack the staged geometry into this controller's own UnifiedRenderData and hand
                // it over WITHOUT completing: the renderer syncs the fence and closes the write
                // session at prepare time, which is what keeps the rebuild off the main thread.
                bool packScheduled;
                m_JobHandle = ScheduleUnifiedRenderDataBuild(m_JobHandle, indexArray, posArray, uv0Array, tanArray, geomArray, out packScheduled);
                if (packScheduled)
                {
                    // The Fill Area of a profile authored before fillSprite has no Sprite to
                    // carry its Texture, so entry 0 resolves it from the renderer instead.
                    spriteShapeRenderer.SetFillTexture(m_ActiveShapeParameters.fillTexture);
                    spriteShapeRenderer.SetUnifiedRenderData(m_UnifiedRenderData, m_JobHandle);
                }
                jobHandle = m_JobHandle;
                m_TessellationNeedsFallback = false;

#if UNITY_EDITOR
                if (spriteShapeGeometryCache && geometryCached)
                    spriteShapeGeometryCache.SetGeometryCache(m_UnifiedRenderData, m_JobHandle);
#endif

                JobHandle.ScheduleBatchedJobs();
#if ENABLE_SPRITESHAPE_POLYSPATIAL_SERIALIZATION
                InternalEngineBridge.MarkDirty(this);
#endif
            }

            return jobHandle;
        }

        /// <summary>
        /// Allocates (or resizes) the staging buffers the creator and modifier Jobs write into.
        /// </summary>
        /// <remarks>
        /// These used to be the renderer's own arrays, lent out through GetChannels/GetSegments.
        /// The renderer no longer owns geometry, so the controller does. Sized EXACTLY rather
        /// than grown, to keep the generator's internal limits and the write session's reserved
        /// capacity describing the same number of vertices.
        /// </remarks>
        void PrepareStagingGeometry(int vertexCount, int segmentCount, out NativeArray<ushort> indexArray,
            out NativeSlice<Vector3> posArray, out NativeSlice<Vector2> uv0Array, out NativeSlice<Vector4> tanArray,
            out NativeArray<SpriteShapeSegment> geomArray)
        {
            ResizeStagingArray(ref m_StagingIndices, vertexCount);
            ResizeStagingArray(ref m_StagingPositions, vertexCount);
            ResizeStagingArray(ref m_StagingTexCoords, vertexCount);
            ResizeStagingArray(ref m_StagingSegments, segmentCount);

            // Tangents are optional. The generator tests m_TanArray.Length > 1 to decide whether
            // to write them, so the disabled case stays the one element stub it has always been.
            if (m_EnableTangents)
                ResizeStagingArray(ref m_StagingTangents, vertexCount);
            else if (m_StagingTangents.IsCreated)
                m_StagingTangents.Dispose();

            indexArray = m_StagingIndices;
            posArray = new NativeSlice<Vector3>(m_StagingPositions);
            uv0Array = new NativeSlice<Vector2>(m_StagingTexCoords);
            tanArray = m_EnableTangents ? new NativeSlice<Vector4>(m_StagingTangents) : new NativeSlice<Vector4>(m_TangentData);
            geomArray = m_StagingSegments;
        }

        static void ResizeStagingArray<T>(ref NativeArray<T> array, int count) where T : struct
        {
            if (array.IsCreated && array.Length == count)
                return;

            if (array.IsCreated)
                array.Dispose();
            array = new NativeArray<T>(count, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        }

        /// <summary>
        /// Opens a write session on this controller's UnifiedRenderData and schedules the Job
        /// that packs the staged geometry into it.
        /// </summary>
        /// <remarks>
        /// The three bounds are CONSERVATIVE upper bounds, the same ones the staging buffers use.
        /// The Job reports what it actually produced and the engine truncates to that at
        /// EndWrite, so over reserving costs a resize and never correctness.
        /// </remarks>
        JobHandle ScheduleUnifiedRenderDataBuild(JobHandle dependency, NativeArray<ushort> indexArray,
            NativeSlice<Vector3> posArray, NativeSlice<Vector2> uv0Array, NativeSlice<Vector4> tanArray,
            NativeArray<SpriteShapeSegment> geomArray, out bool scheduled)
        {
            scheduled = false;
            if (null == m_UnifiedRenderData || !m_UnifiedRenderData.isValid)
                m_UnifiedRenderData = new UnifiedRenderData();

            // A second BeginWrite without an intervening prepare throws. BakeMesh can run again
            // before the renderer ever prepared -- a second camera, a forced refresh, an editor
            // regeneration -- so close the stale session instead of letting it throw. The fence
            // to wait on is the previous pack Job's, not this bake's freshly scheduled chain.
            if (m_UnifiedRenderData.isWriteSessionOpen)
            {
                m_UnifiedWriteFence.Complete();
                m_UnifiedRenderData.EndWrite();
            }

            var channels = (1 << (int)Rendering.VertexAttribute.Position) | (1 << (int)Rendering.VertexAttribute.TexCoord0);
            if (m_EnableTangents)
                channels |= 1 << (int)Rendering.VertexAttribute.Tangent;

            // One entry per staged segment is the worst case, when no two segments share a
            // Sprite. BeginWrite bounds the draw ranges by the same number.
            var writer = m_UnifiedRenderData.BeginWrite(maxArrayCount, maxArrayCount, geomArray.Length, channels);
            if (!writer.isCreated)
                return dependency;   // scheduled stays false: BeginWrite has already Cleared, so the
                                      // caller must not hand an emptied instance to the renderer.

            NativeArray<EntityId> spriteIds;
            NativeArray<EntityId> textureIds;
            ResolveSpriteEntityIds(out spriteIds, out textureIds);

            var packJob = new SpriteShapeUnifiedRenderDataJob
            {
                segments = geomArray,
                indices = indexArray,
                positions = posArray,
                texCoords = uv0Array,
                tangents = tanArray,
                spriteIds = spriteIds,
                textureIds = textureIds,
                writer = writer,
                writeTangents = m_EnableTangents ? 1 : 0,
                spriteProps = UnifiedRenderData.defaultSpriteProps,
                // Neutral, not the renderer's tint: SpriteShapeRenderer applies its colour live at
                // draw time, so a baked tint would go stale on the next colour change.
                spriteColor = (Vector4)Color.white
            };
            m_UnifiedWriteFence = packJob.Schedule(dependency);
            scheduled = true;
            return m_UnifiedWriteFence;
        }

        /// <summary>
        /// Resolves the Sprite and Texture EntityId of every Sprite in spriteArray, indexed the
        /// same way SpriteShapeSegment.spriteIndex is.
        /// </summary>
        /// <remarks>
        /// MAIN THREAD, and that is the point: with both resolved up front the packing Job can
        /// group entries and merge draw ranges by comparing integers, and never dereferences a
        /// Sprite.
        ///
        /// The Texture id is a MERGE KEY rather than a plain Texture reference. Two Sprites can
        /// share a main Texture and still bind different secondary Textures, and a merged draw
        /// range binds all of them from a single representative entry, so a Sprite whose
        /// secondary set does not match gets a key of its own and cannot merge.
        /// </remarks>
        void ResolveSpriteEntityIds(out NativeArray<EntityId> spriteIds, out NativeArray<EntityId> textureIds)
        {
            var count = m_SpriteArray.Length;
            spriteIds = new NativeArray<EntityId>(count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            textureIds = new NativeArray<EntityId>(count, Allocator.TempJob, NativeArrayOptions.ClearMemory);

            for (int i = 0; i < count; ++i)
            {
                var sprite = m_SpriteArray[i];
                if (sprite == null)
                    continue;

                spriteIds[i] = sprite.GetEntityId();

                var texture = sprite.texture;
                if (texture == null)
                    continue;

                var key = texture.GetEntityId();
                for (int j = 0; j < i; ++j)
                {
                    var other = m_SpriteArray[j];
                    if (other == null || other.texture != texture)
                        continue;

                    key = HasMatchingSecondaryTextures(other, sprite) ? textureIds[j] : spriteIds[i];
                    break;
                }
                textureIds[i] = key;
            }
        }

        // True when two Sprites bind an identical SET of secondary Textures, and so may share a
        // draw. Order insensitive: the order comes from how the asset was authored, and two
        // Sprites listing the same maps in the opposite order are genuinely mergeable.
        static bool HasMatchingSecondaryTextures(Sprite lhs, Sprite rhs)
        {
            var lhsCount = lhs.GetSecondaryTextureCount();
            var rhsCount = rhs.GetSecondaryTextureCount();
            if (lhsCount != rhsCount)
                return false;
            if (0 == lhsCount)
                return true;

            if (s_SecondaryTexturesLhs.Length < lhsCount)
                s_SecondaryTexturesLhs = new SecondarySpriteTexture[lhsCount];
            if (s_SecondaryTexturesRhs.Length < rhsCount)
                s_SecondaryTexturesRhs = new SecondarySpriteTexture[rhsCount];
            lhs.GetSecondaryTextures(s_SecondaryTexturesLhs);
            rhs.GetSecondaryTextures(s_SecondaryTexturesRhs);

            for (int i = 0; i < lhsCount; ++i)
            {
                var found = false;
                for (int j = 0; j < rhsCount && !found; ++j)
                    found = s_SecondaryTexturesLhs[i].name == s_SecondaryTexturesRhs[j].name
                        && s_SecondaryTexturesLhs[i].texture == s_SecondaryTexturesRhs[j].texture;
                if (!found)
                    return false;
            }
            return true;
        }

        internal void BakeShadow()
        {
            if (m_ShadowData.IsCreated)
            {
                if (updateShadow)
                {
                    int maxCount = short.MaxValue - 1;
                    float2 last = (float2)0;

                    m_ShadowSegment.Clear();
                    for (int i = 0; i < maxCount; ++i)
                    {
                        float2 now = m_ShadowData[i];
                        if (!math.any(last) && !math.any(now))
                        {
                            if ((i + 1) < maxCount)
                            {
                                float2 next = m_ShadowData[i + 1];
                                if (!math.any(next) && !math.any(next))
                                    break;
                            }
                            else
                                break;
                        }
                        m_ShadowSegment.Add(new Vector2(now.x, now.y));
                    }

#if UNITY_EDITOR
                    UnityEditor.SceneView.RepaintAll();
#endif
                }

                // Dispose Collider as its no longer needed.
                m_ShadowData.Dispose();
            }
        }

        /// <summary>
        /// Update Collider of this Object.
        /// </summary>
        public void BakeCollider()
        {
            using var bakeColliderScope = PerformanceMarkers.bakeColliderMarker.Auto();

            // Previously this must be explicitly called if using BakeMesh.
            // But now we do it internally. BakeCollider_CanBeCalledMultipleTimesWithoutJobComplete
            m_JobHandle.Complete();
            BakeShadow();

            var physicsShapeUpdated = false;
            if (m_ColliderData.IsCreated)
            {
                if (autoUpdateCollider)
                {
                    m_ActiveEdgeCollider = null;
                    m_ActivePolygonCollider = null;

                    var count = CountPointsBeforeSentinel(m_ColliderData);
                    if (m_ColliderSegment.Length != count)
                        m_ColliderSegment = new Vector2[count];
                    m_ColliderData.Reinterpret<Vector2>().AsReadOnlySpan().Slice(0, count).CopyTo(m_ColliderSegment);

                    TryUpdateColliders();

                    physicsShapeUpdated = true;
#if UNITY_EDITOR
                    UnityEditor.SceneView.RepaintAll();
#endif
                }

                // Dispose Collider as its no longer needed.
                m_ColliderData.Dispose();

                // Print Once.
                if (m_Statistics.IsCreated)
                {
                    var stats = m_Statistics[0];
                    switch (stats.status)
                    {
                        case SpriteShapeGeneratorResult.ErrorNativeDataOverflow:
                            Debug.LogWarningFormat(gameObject, "NativeArray access not within range. Please submit a bug report.");
                            break;
                        case SpriteShapeGeneratorResult.ErrorSpritesTightPacked:
                            Debug.LogWarningFormat(gameObject, "Sprites used in SpriteShape profile must use FullRect.");
                            break;
                        case SpriteShapeGeneratorResult.ErrorSpritesWrongBorder:
                            Debug.LogWarningFormat(gameObject, "Sprites used in SpriteShape profile have invalid borders. Please check SpriteShape profile.");
                            break;
                        case SpriteShapeGeneratorResult.ErrorVertexLimitReached:
                            Debug.LogWarningFormat(gameObject, "Mesh data has reached Limits. Please try dividing shape into smaller blocks.");
                            break;
                        case SpriteShapeGeneratorResult.ErrorDefaultQuadCreated:
                            if (m_UTess2D)
                            {
                                m_TessellationNeedsFallback = true;
                                Debug.LogWarningFormat(gameObject, "Fill tessellation (C# Job) encountered errors. Please avoid overlaps or close points in the input Spline. Falling back to default generator.");
                                break;
                            }
                            Debug.LogWarningFormat(gameObject, "Fill tessellation encountered errors. Please avoid overlaps or close points in the input spline.");
                            break;
                    }
                }
            }

            if (physicsShapeUpdated)
                physicsShapeChanged?.Invoke(this);
        }

        static int CountPointsBeforeSentinel(NativeArray<float2> points)
        {
            int maxCount = math.min(points.Length, short.MaxValue - 1);
            for (int i = 0; i < maxCount; ++i)
            {
                if (math.any(points[i]))
                    continue;

                if ((i + 1) >= maxCount || !math.any(points[i + 1]))
                    return i;
            }

            return maxCount;
        }

        void TryUpdateColliders()
        {
            using var scope = PerformanceMarkers.tryUpdateCollidersMarker.Auto();

            var edge = edgeCollider;
            if (!ReferenceEquals(edge, m_ActiveEdgeCollider))
            {
                if (edge != null)
                    edge.points = m_ColliderSegment;
                m_ActiveEdgeCollider = edge;
            }

            var polygon = polygonCollider;
            if (!ReferenceEquals(polygon, m_ActivePolygonCollider))
            {
                if (polygon != null)
                    polygon.points = m_ColliderSegment;
                m_ActivePolygonCollider = polygon;
            }
        }

        internal void BakeMeshForced()
        {
            if (spriteShapeRenderer != null)
            {
                var hasSplineChanged = HasSplineDataChanged();
                if (hasSplineChanged)
                {
                    BakeMesh();
                    Rendering.CommandBuffer rc = new Rendering.CommandBuffer();
                    rc.GetTemporaryRT(0, 256, 256, 0);
                    rc.SetRenderTarget(0);
                    rc.DrawRenderer(spriteShapeRenderer, spriteShapeRenderer.sharedMaterial);
                    rc.ReleaseTemporaryRT(0);
                    Graphics.ExecuteCommandBuffer(rc);
                }
            }
        }

#endregion

        internal void ForceShadowShapeUpdate(bool forceUpdate)
        {
            m_UpdateShadow = forceUpdate;
            InvalidateChangeHash();
        }

        internal NativeArray<float2> GetShadowShapeData()
        {
            if (m_ShadowData.IsCreated)
            {
                JobHandle handle = BakeMesh();
                handle.Complete();
                BakeCollider();
            }

            NativeArray<float2> retNativeArray = new NativeArray<float2>(m_ShadowSegment.Count, Allocator.Temp);
            for (int i = 0; i < retNativeArray.Length; i++)
                retNativeArray[i] = m_ShadowSegment[i];

            return retNativeArray;
        }
    }
}
