using System;

using Unity.Collections;
using UnityEngine;
using UnityEngine.U2D;

namespace Unity.U2D.Physics
{
    /// <summary>
    /// Creates physics shapes from the Sprite Shape on this GameObject.
    /// </summary>
    [AddComponentMenu("Physics 2D (Core)/Physics Area Sprite Shape")]
    [Icon("Packages/com.unity.2d.spriteshape/Editor/Icons/PhysicsAreaSpriteShape.asset")]
    [RequireComponent(typeof(SpriteShapeController))]
    [PhysicsCustomProperties]
    public sealed partial class PhysicsAreaSpriteShape : PhysicsArea
    {
        /// <summary>
        /// The kind of shapes to create.
        /// </summary>
        public enum OutputType
        {
            /// <summary>
            /// Solid shapes that fill the outline. An open spline is closed to fill it.
            /// </summary>
            Polygons,

            /// <summary>
            /// Hollow edges along the outline. Use this for open splines.
            /// </summary>
            Segments
        }

        // The settings that decide how the shapes are built, kept together so the Inspector draws them as one group that shows only the settings in use.
        // Only this area reads and writes them, through its public properties.
        [Serializable]
        internal struct BuildSettings
        {
            // The settings a new or reset area starts with: segments, with the outline's winding as it is.
            internal static BuildSettings defaultSettings => new()
            {
                m_OutputType = OutputType.Segments,
                m_ReverseWinding = false
            };

            [Tooltip("The kind of shapes to create.")]
            [SerializeField]
            internal OutputType m_OutputType;

            [Tooltip("Whether the outline's winding is reversed, flipping the side the one-sided segments make contact on.")]
            [SerializeField]
            internal bool m_ReverseWinding;
        }

        /// <summary>
        /// Returned by <see cref="GetSplineIndex"/> when a shape has no spline side.
        /// </summary>
        public const int noSplineIndex = NoSegment;

        /// <summary>
        /// The kind of shapes to create.
        /// </summary>
        public OutputType outputType
        {
            get => m_BuildSettings.m_OutputType;
            set
            {
                if (m_BuildSettings.m_OutputType == value)
                    return;

                m_BuildSettings.m_OutputType = value;
                ApplyGeometry();
            }
        }

        /// <summary>
        /// Whether the outline's winding is reversed, which flips the side the one-sided segments make contact on.
        /// </summary>
        /// <remarks>
        /// When the value is true, the segments of a closed spline make contact on its inside.
        /// Otherwise, they make contact on its outside.
        /// For an open spline, a value of true moves contact to the opposite side of the strip.
        /// This property only affects the <see cref="OutputType.Segments"/> output type and has no effect on polygons.
        /// Changing the value rebuilds the shapes.
        /// </remarks>
        public bool reverseWinding
        {
            get => m_BuildSettings.m_ReverseWinding;
            set
            {
                if (m_BuildSettings.m_ReverseWinding == value)
                    return;

                m_BuildSettings.m_ReverseWinding = value;
                ApplyGeometry();
            }
        }

        /// <summary>
        /// The Sprite Shape Controller on this GameObject.
        /// </summary>
        public SpriteShapeController controller
        {
            get
            {
                // Cached, since this is read on every build. Compared through the Unity operator, so a destroyed controller
                // is looked up again rather than handed back as a live reference.
                if (m_Controller == null)
                    TryGetComponent(out m_Controller);

                return m_Controller;
            }
        }

        /// <summary>
        /// Rebuilds the shapes from the current spline.
        /// </summary>
        /// <remarks>
        /// Call this after changing the spline from a script while the Sprite Shape is off-screen. A Sprite Shape only
        /// updates its outline while it is visible, so the shapes would otherwise wait until it is.
        /// </remarks>
        public void RebuildFromSpline()
        {
            BakeOutline();
            RequestRebuild();
        }

        /// <summary>
        /// Gets the side of the spline that a shape lies along.
        /// </summary>
        /// <remarks>
        /// Use this in a contact callback to find which part of the Sprite Shape was hit. Side <c>i</c> runs from control
        /// point <c>i</c> to the next one.
        /// Only <see cref="OutputType.Segments"/> shapes have a side. To get a callback for every segment that is touched, set
        /// the area's Callback Source to <see cref="PhysicsArea.CallbackSourceType.Shapes"/>.
        /// </remarks>
        /// <param name="shape">A shape from a contact or trigger event.</param>
        /// <returns>The index of the control point that starts the side, or <see cref="noSplineIndex"/> if the shape has no
        /// side or doesn't belong to this area.</returns>
        public int GetSplineIndex(PhysicsShape shape)
        {
            // The owner is stamped as the shapes are created, so this is constant time. A destroyed shape answers false, since
            // an event can outlive the shape it names.
            if (!shape.isValid || !ReferenceEquals(shape.owner, this))
                return noSplineIndex;

            return shape.ownerUserData.intValue;
        }

        /// <summary>
        /// Whether this area always produces exactly one shape.
        /// </summary>
        /// <remarks>
        /// Returns false, because the outline can produce any number of shapes.
        /// The inspector reports the shapes as a set instead of showing a single shape.
        /// </remarks>
        protected override bool producesSingleShape => false;

        /// <summary>
        /// Whether this area's geometry has a radius that the Transform scale can affect.
        /// </summary>
        /// <remarks>
        /// Returns false, because neither the polygons that fill the outline nor the chain segments that trace it have a radius.
        /// The inspector does not offer the Scale Radius option.
        /// </remarks>
        protected override bool usesScaleRadius => false;

        /// <summary>
        /// Whether this area can be used as a Pose layer of a composite.
        /// </summary>
        /// <remarks>
        /// Returns true for both output types, because the outline always encloses an area for the composite to fill.
        /// See <see cref="PhysicsAreaComposite"/>.
        /// </remarks>
        protected override bool canContributeToComposer => true;

        /// <summary>
        /// Prepares this area to hold the Sprite Shape outline when the component is enabled.
        /// </summary>
        /// <remarks>
        /// Paired with <see cref="OnProviderDisable"/>.
        /// </remarks>
        protected override void OnProviderEnable()
        {
            base.OnProviderEnable();

            // Allocated for the whole enabled lifetime, so a rebuild allocates nothing.
            if (!m_Outline.IsCreated)
                m_Outline = new NativeList<Vector2>(InitialVertexCapacity, Allocator.Persistent);

            if (!m_Scratch.IsCreated)
                m_Scratch = new NativeList<Vector2>(InitialVertexCapacity, Allocator.Persistent);
        }

        /// <summary>
        /// Releases what the area prepared for holding the Sprite Shape outline when the component is disabled.
        /// </summary>
        /// <remarks>
        /// Paired with <see cref="OnProviderEnable"/>.
        /// </remarks>
        protected override void OnProviderDisable()
        {
            base.OnProviderDisable();

            if (m_Outline.IsCreated)
                m_Outline.Dispose();

            if (m_Scratch.IsCreated)
                m_Scratch.Dispose();
        }

        /// <summary>
        /// Takes the current outline from the Sprite Shape Controller and starts following its changes.
        /// </summary>
        /// <remarks>
        /// Runs when the area is enabled and again after an undo, but not before a rebuild.
        /// The outline is baked here even when the Sprite Shape is not rendered, so an off-screen Sprite Shape still gets shapes.
        /// Paired with <see cref="OnAreaDisable"/>.
        /// </remarks>
        protected override void OnAreaEnable()
        {
            // Baked before subscribing, so the report the bake raises doesn't come straight back as a rebuild request.
            BakeOutline();

            // Remembered rather than read back from the controller property, which looks the component up again once its
            // cached reference dies and would leave this subscribed to a controller it can no longer name.
            m_SubscribedController = controller;
            if (m_SubscribedController != null)
                m_SubscribedController.physicsShapeChanged += m_OnSpriteShapeChangedHandler ??= OnSpriteShapeChanged;
        }

        /// <summary>
        /// Stops following changes to the Sprite Shape Controller.
        /// </summary>
        /// <remarks>
        /// Paired with <see cref="OnAreaEnable"/>.
        /// </remarks>
        protected override void OnAreaDisable()
        {
            if (m_SubscribedController != null)
                m_SubscribedController.physicsShapeChanged -= m_OnSpriteShapeChangedHandler;

            m_SubscribedController = null;
        }

        /// <summary>
        /// Builds this area's shapes from the Sprite Shape outline.
        /// </summary>
        /// <remarks>
        /// The Polygons output fills the outline with convex polygons, and the Segments output traces it with chain segments.
        /// Builds nothing when the Sprite Shape has no outline yet.
        /// See <see cref="outputType"/>.
        /// </remarks>
        /// <param name="context">The body, placement and definition to build with, and the calls that create the shapes.</param>
        protected override void GenerateShapes(in PhysicsAreaShapeContext context)
        {
            // Editor tooling can ask for shapes while this is disabled, before the buffers exist.
            if (!m_Outline.IsCreated)
                return;

            if (!TryCopyOutline(out var isLoop))
                return;

            if (m_BuildSettings.m_OutputType == OutputType.Segments)
                BuildSegments(context, isLoop);
            else
                BuildPolygons(context);
        }

        /// <summary>
        /// Adds the Sprite Shape outline to a composer as one closed layer.
        /// </summary>
        /// <remarks>
        /// An open spline is closed to fill it, as the Polygons output does.
        /// Adds nothing when the Sprite Shape has no outline yet.
        /// </remarks>
        /// <param name="composer">Composer to add the layer to.</param>
        /// <param name="areaToComposer">Matrix mapping this area's geometry into the composite's pose space.</param>
        /// <param name="extraTransform">That layer's own placement, applied on top of <paramref name="areaToComposer"/>.</param>
        /// <param name="composite">That layer's composite arguments.</param>
        /// <param name="order">That layer's order among the composite's other layers.</param>
        protected override void AddCompositeLayers(PhysicsComposer composer, Matrix4x4 areaToComposer, PhysicsTransform extraTransform, LayerSettings composite, int order)
        {
            if (!m_Outline.IsCreated || !TryCopyOutline(out _))
                return;

            AddOutlineLayer(composer, areaToComposer, extraTransform, composite, order);
        }

        void OnSpriteShapeChanged(SpriteShapeController changed)
        {
            if (!m_Baking)
                RequestRebuild();
        }

        // The controller only bakes while it is rendered, so it is asked to bake now. The report the bake raises is ignored,
        // since every caller rebuilds afterwards anyway.
        void BakeOutline()
        {
            var activeController = controller;
            if (activeController == null)
                return;

            m_Baking = true;
            try
            {
                activeController.BakeMesh().Complete();
                activeController.BakeCollider();
            }
            finally
            {
                m_Baking = false;
            }
        }

        // How many vertices the reused storage starts out able to hold, before it has to grow.
        const int InitialVertexCapacity = 256;

        // The stamp meaning no spline side is named for the shape. Negative, so it can never collide with a real index.
        internal const int NoSegment = -1;

        // The build settings, drawn in the Inspector by their own drawer.
        [SerializeField]
        BuildSettings m_BuildSettings = BuildSettings.defaultSettings;

        // Persist for the enabled lifetime and are refilled by each build, so a rebuild allocates nothing.
        NativeList<Vector2> m_Outline;
        NativeList<Vector2> m_Scratch;

        // Set while this component itself asks the controller to bake. See BakeOutline.
        bool m_Baking;

        SpriteShapeController m_Controller;
        SpriteShapeController m_SubscribedController;

        // Created once on first subscribe, since a method-group subscribe allocates a fresh delegate per call.
        System.Action<SpriteShapeController> m_OnSpriteShapeChangedHandler;
    }
}
