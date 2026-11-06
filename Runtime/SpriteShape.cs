using System;
using System.Collections.Generic;

namespace UnityEngine.U2D
{
    /// <summary>
    /// Tangent mode for control points that defines the bezier curve.
    /// </summary>
    public enum ShapeTangentMode
    {
        /// <summary>Linear mode where tangents are zero.</summary>
        Linear = 0,
        /// <summary>Set left and right tangents so that the bezier curve is continuous.</summary>
        Continuous = 1,
        /// <summary>Set custom left and right tangents.</summary>
        Broken = 2,
    };

    /// <summary>
    /// Corner type to assign sprite.
    /// </summary>
    public enum CornerType
    {
        /// <summary>Outer top left Corner.</summary>
        OuterTopLeft,
        /// <summary>Outer top right Corner.</summary>
        OuterTopRight,
        /// <summary>Outer bottom left Corner.</summary>
        OuterBottomLeft,
        /// <summary>Outer bottom right Corner.</summary>
        OuterBottomRight,
        /// <summary>Inner top left Corner.</summary>
        InnerTopLeft,
        /// <summary>Inner top right Corner.</summary>
        InnerTopRight,
        /// <summary>Inner bottom left Corner.</summary>
        InnerBottomLeft,
        /// <summary>Inner bottom right Corner.</summary>
        InnerBottomRight,
    };

    /// <summary>
    /// Level of detail of generated SpriteShape geometry.
    /// </summary>
    public enum QualityDetail
    {
        /// <summary>Highest level of detail (16).</summary>
        High = 16,
        /// <summary>Medium level of detail (8).</summary>
        Mid = 8,
        /// <summary>Low level of detail (4).</summary>
        Low = 4
    }

    /// <summary>
    /// Corner mode that defines how corners are handled when generating SpriteShape geometry.
    /// </summary>
    public enum Corner
    {
        /// <summary>No corners.</summary>
        Disable = 0,
        /// <summary>Automatically use respective corner sprite if 1) angle is within range 2) control point and neighbours are in linear tangent mode and their heights are same.</summary>
        Automatic = 1,
        /// <summary>The sprite at this control point is used to create a curved corner.</summary>
        Stretched = 2,
    }

    /// <summary>
    /// Spline control point that holds information for constructing the Bezier curve and SpriteShape geometry.
    /// </summary>
    [System.Serializable]
    public class SplineControlPoint
    {
        /// <summary>Position of control point.</summary>
        public Vector3 position;
        /// <summary>Left tangent of control point.</summary>
        public Vector3 leftTangent;
        /// <summary>Right tangent of control point.</summary>
        public Vector3 rightTangent;
        /// <summary>Tangent mode of control point.</summary>
        public ShapeTangentMode mode;
        /// <summary>Height of control point used when generating SpriteShape geometry.</summary>
        public float height = 1f;
        /// <summary>Sprite index used for rendering at start of this control point along the edge. </summary>
        public int spriteIndex;
        /// <summary>Enable corners of control point.</summary>
        public bool corner;
        [SerializeField]
        Corner m_CornerMode;

        /// <summary>Corner mode of control point.</summary>
        public Corner cornerMode
        {
            get => m_CornerMode;
            set => m_CornerMode = value;
        }

        /// <summary>
        /// Get hash code for this Spline control point.
        /// </summary>
        /// <returns>Hash code as int.</returns>
        public override int GetHashCode()
        {
            return  ((int)position.x).GetHashCode() ^ ((int)position.y).GetHashCode() ^ position.GetHashCode() ^
                    (leftTangent.GetHashCode() << 2) ^ (rightTangent.GetHashCode() >> 2) ^  ((int)mode).GetHashCode() ^
                    height.GetHashCode() ^ spriteIndex.GetHashCode() ^ corner.GetHashCode() ^ (m_CornerMode.GetHashCode() << 2);
        }
    }

    /// <summary>
    /// Angle Range defines constraints and list of sprites to be used to render edges of SpriteShape.
    /// </summary>
    [System.Serializable]
    public class AngleRange : ICloneable
    {
        /// <summary>Start angle of AngleRange.</summary>
        public float start
        {
            get { return m_Start; }
            set { m_Start = value; }
        }

        /// <summary>End angle of AngleRange. Angles cannot overlap with others.</summary>
        public float end
        {
            get { return m_End; }
            set { m_End = value; }
        }

        /// <summary>Render order for this AngleRange.</summary>
        public int order
        {
            get { return m_Order; }
            set { m_Order = value; }
        }

        /// <summary>List of sprites that are used to render the edge. The sprite index of control point can be used to select the one to be used for rendering.</summary>
        public List<Sprite> sprites
        {
            get { return m_Sprites; }
            set { m_Sprites = value; }
        }

        [SerializeField]
        float m_Start;
        [SerializeField]
        float m_End;
        [SerializeField]
        int m_Order;
        [SerializeField]
        List<Sprite> m_Sprites = new List<Sprite>();
        
        /// <summary>
        /// Clone object.
        /// </summary>
        /// <returns>Cloned Angle Range Object.</returns>
        public object Clone()
        {
            AngleRange clone = this.MemberwiseClone() as AngleRange;
            clone.sprites = new List<Sprite>(clone.sprites);

            return clone;
        }

        /// <summary>
        /// Test for Equality.
        /// </summary>
        /// <param name="obj">Object to test against.</param>
        /// <returns>True if Equal.</returns>
        public override bool Equals(object obj)
        {
            var other = obj as AngleRange;

            if (other == null)
                return false;

            bool equals = start.Equals(other.start) && end.Equals(other.end) && order.Equals(other.order);

            if (!equals)
                return false;

            if (sprites.Count != other.sprites.Count)
                return false;

            for (int i = 0; i < sprites.Count; ++i)
                if (sprites[i] != other.sprites[i])
                    return false;

            return true;
        }
        
        /// <summary>
        /// Get hash code for this AngleRange.
        /// </summary>
        /// <returns>Hash code as int.</returns>
        public override int GetHashCode()
        {
            int hashCode = start.GetHashCode() ^ end.GetHashCode() ^ order.GetHashCode();

            if (sprites != null)
            {
                for (int i = 0; i < sprites.Count; i++)
                {
                    Sprite sprite = sprites[i];
                    if (sprite)
                        hashCode = hashCode * 16777619 ^ (sprite.GetHashCode() + i);
                }
            }

            return hashCode;
        }
    }

    /// <summary>
    /// Corner Sprite used to specify corner type and associated sprites.
    /// </summary>
    [System.Serializable]
    public class CornerSprite : ICloneable
    {

        /// <summary>Type of corner. </summary>
        public CornerType cornerType
        {
            get { return m_CornerType; }
            set { m_CornerType = value; }
        }

        /// <summary>List of sprites associated with this corner. </summary>
        public List<Sprite> sprites
        {
            get { return m_Sprites; }
            set { m_Sprites = value; }
        }

        [SerializeField]
        CornerType m_CornerType;               ///< Set Corner type. enum { OuterTopLeft = 0, OuterTopRight = 1, OuterBottomLeft = 2, OuterBottomRight = 3, InnerTopLeft = 4, InnerTopRight = 5, InnerBottomLeft = 6, InnerBottomRight = 7 }
        [SerializeField]
        List<Sprite> m_Sprites;

        /// <summary>
        /// Clone this object.
        /// </summary>
        /// <returns>A CornerSprite clone.</returns>
        public object Clone()
        {
            CornerSprite clone = this.MemberwiseClone() as CornerSprite;
            clone.sprites = new List<Sprite>(clone.sprites);

            return clone;
        }

        /// <summary>
        /// Test for Equality.
        /// </summary>
        /// <param name="obj">Object to test against</param>
        /// <returns>True if objects are equal.</returns>
        public override bool Equals(object obj)
        {
            var other = obj as CornerSprite;

            if (other == null)
                return false;

            if (!cornerType.Equals(other.cornerType))
                return false;

            if (sprites.Count != other.sprites.Count)
                return false;

            for (int i = 0; i < sprites.Count; ++i)
                if (sprites[i] != other.sprites[i])
                    return false;

            return true;
        }

        /// <summary>
        /// Get hash code for this CornerSprite.
        /// </summary>
        /// <returns>Hash code as int.</returns>
        public override int GetHashCode()
        {
            int hashCode = cornerType.GetHashCode();

            if (sprites != null)
            {
                for (int i = 0; i < sprites.Count; i++)
                {
                    Sprite sprite = sprites[i];
                    if (sprite)
                    {
                        hashCode ^= (i + 1);
                        hashCode ^= sprite.GetHashCode();
                    }
                }
            }

            return hashCode;
        }
    }

    /// <summary>
    /// SpriteShape contains the parameters that define how SpriteShape geometry is generated from a Spline.
    /// </summary>
    [HelpURLAttribute("https://docs.unity3d.com/Packages/com.unity.2d.spriteshape@latest/index.html?subfolder=/manual/SSProfile.html")]
    public class SpriteShape : ScriptableObject
    {
        /// <summary>List of AngleRanges. </summary>
        public List<AngleRange> angleRanges
        {
            get { return m_Angles; }
            set { m_Angles = value; }
        }

        /// <summary>Sprite that fills the enclosed area of a closed shape.</summary>
        /// <remarks>The Sprite must cover its entire Texture and must not be packed into a Sprite Atlas. Set the wrap mode of its Texture to <see cref="TextureWrapMode.Repeat"/> so that Unity can tile the Sprite across the Fill area.</remarks>
        public Sprite fillSprite
        {
            get { return m_FillSprite; }
            set { m_FillSprite = value; }
        }

        /// <summary>Texture that fills the enclosed area of a closed shape.</summary>
        /// <remarks>Set the wrap mode of the Texture to <see cref="TextureWrapMode.Repeat"/> so that Unity can tile it across the Fill area.</remarks>
        [Obsolete("SpriteShape.fillTexture is deprecated and will be removed in a future release. Use SpriteShape.fillSprite instead.", false)]
        public Texture2D fillTexture
        {
            get { return resolvedFillTexture; }
            set
            {
                m_FillTexture = value;
                m_FillSprite = null;
            }
        }

        /// <summary>
        /// Texture used to render the Fill Area. Resolved from fillSprite when set, else from the
        /// legacy serialized fillTexture so that assets authored before fillSprite keep rendering.
        /// </summary>
        internal Texture2D resolvedFillTexture
        {
            get { return m_FillSprite != null ? m_FillSprite.texture : m_FillTexture; }
        }

        /// <summary>Legacy serialized Fill Texture, without any fillSprite resolution. Used by asset upgrade. </summary>
        internal Texture2D legacyFillTexture
        {
            get { return m_FillTexture; }
        }

        /// <summary>Serialized version of this asset, used to drive one-time upgrades. </summary>
        internal int version
        {
            get { return m_Version; }
            set { m_Version = value; }
        }

        /// <summary>Sprites to be used for corners. </summary>
        public List<CornerSprite> cornerSprites
        {
            get { return m_CornerSprites; }
            set { m_CornerSprites = value; }
        }

        /// <summary>Fill offset for the closed shape. </summary>
        public float fillOffset
        {
            get { return m_FillOffset; }
            set { m_FillOffset = value; }
        }

        /// <summary>Use borders of sprites when generating edge geometry. </summary>
        public bool useSpriteBorders
        {
            get { return m_UseSpriteBorders; }
            set { m_UseSpriteBorders = value; }
        }

        // Asset version before fillSprite was introduced. Assets at this version still carry m_FillTexture only.
        internal const int k_VersionFillTexture = 0;
        // Asset version where the Fill Area is authored as a Sprite.
        internal const int k_VersionFillSprite = 1;

        [SerializeField]
        List<AngleRange> m_Angles = new List<AngleRange>();
        // Kept serialized for backwards compatibility with assets authored before m_FillSprite. Do not remove.
        [SerializeField]
        Texture2D m_FillTexture;
        [SerializeField]
        Sprite m_FillSprite;
        [SerializeField]
        List<CornerSprite> m_CornerSprites = new List<CornerSprite>();
        [SerializeField]
        float m_FillOffset;

        [SerializeField]
        bool m_UseSpriteBorders = true;
        [SerializeField]
        int m_Version = k_VersionFillTexture;
        
#if UNITY_EDITOR        
        internal static event Action<SpriteShape> onReset = null;
#endif        
        private CornerSprite GetCornerSprite(CornerType cornerType)
        {
            var cornerSprite = new CornerSprite();
            cornerSprite.cornerType = cornerType;
            cornerSprite.sprites = new List<Sprite>();
            cornerSprite.sprites.Insert(0, null);
            return cornerSprite;
        }

        void ResetCornerList()
        {
            m_CornerSprites.Clear();
            m_CornerSprites.Insert(0, GetCornerSprite(CornerType.OuterTopLeft));
            m_CornerSprites.Insert(1, GetCornerSprite(CornerType.OuterTopRight));
            m_CornerSprites.Insert(2, GetCornerSprite(CornerType.OuterBottomLeft));
            m_CornerSprites.Insert(3, GetCornerSprite(CornerType.OuterBottomRight));
            m_CornerSprites.Insert(4, GetCornerSprite(CornerType.InnerTopLeft));
            m_CornerSprites.Insert(5, GetCornerSprite(CornerType.InnerTopRight));
            m_CornerSprites.Insert(6, GetCornerSprite(CornerType.InnerBottomLeft));
            m_CornerSprites.Insert(7, GetCornerSprite(CornerType.InnerBottomRight));
        }

        void OnValidate()
        {
            if (m_CornerSprites.Count != 8)
                ResetCornerList();
        }

        void Reset()
        {
            m_Angles.Clear();
            ResetCornerList();
            // Newly created assets are authored with fillSprite and need no upgrade.
            m_Version = k_VersionFillSprite;
#if UNITY_EDITOR
            onReset?.Invoke(this);
#endif            
        }

        internal static int GetSpriteShapeHashCode(SpriteShape spriteShape)
        {
            // useSpriteBorders, fillOffset and fillTexture are hashChecked elsewhere.

            unchecked
            {
                int hashCode = (int)2166136261;

                if (spriteShape.fillSprite)
                    hashCode = hashCode * 16777619 ^ spriteShape.fillSprite.GetHashCode();

                hashCode = hashCode * 16777619 ^ spriteShape.angleRanges.Count;

                for (int i = 0; i < spriteShape.angleRanges.Count; ++i)
                {
                    hashCode = hashCode * 16777619 ^ (spriteShape.angleRanges[i].GetHashCode() + i);
                }

                hashCode = hashCode * 16777619 ^ spriteShape.cornerSprites.Count;

                for (int i = 0; i < spriteShape.cornerSprites.Count; ++i)
                {
                    hashCode = hashCode * 16777619 ^ (spriteShape.cornerSprites[i].GetHashCode() + i);
                }

                return hashCode;
            }
        }

    }
}
