using System.Linq;
using UnityEngine;
using UnityEditor;

namespace UnityEditor.U2D
{
    /// <summary>
    /// Upgrades SpriteShape assets authored with a Fill Texture to use a Fill Sprite instead.
    /// The legacy Texture is left serialized, so an asset that cannot be upgraded (for example when its
    /// Texture is not imported as a Sprite) keeps rendering through SpriteShape.resolvedFillTexture.
    /// </summary>
    internal static class SpriteShapeUpgrader
    {
        /// <summary>
        /// Find the Sprite that covers the whole of a Texture, which is the only kind of Sprite that can
        /// tile across the Fill Area. Returns null when the Texture has no such Sprite, or more than one
        /// (a multiple-mode Texture), in which case the choice is ambiguous and is left to the user.
        /// </summary>
        internal static Sprite FindFillSprite(Texture2D texture)
        {
            if (null == texture)
                return null;

            var assetPath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(assetPath))
                return null;

            var sprites = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath).OfType<Sprite>()
                .Where(sprite => sprite.texture == texture).ToList();

            if (sprites.Count != 1)
                return null;

            var candidate = sprites[0];
            var fullRect = new Rect(0, 0, texture.width, texture.height);

            return candidate.rect == fullRect ? candidate : null;
        }

        /// <summary>
        /// Upgrade a single SpriteShape in memory. Returns true when the asset was changed and needs saving.
        /// </summary>
        internal static bool TryUpgrade(UnityEngine.U2D.SpriteShape spriteShape)
        {
            if (null == spriteShape || spriteShape.version >= UnityEngine.U2D.SpriteShape.k_VersionFillSprite)
                return false;

            // Nothing to carry over. Mark as upgraded so the asset is not revisited.
            if (null == spriteShape.legacyFillTexture)
            {
                spriteShape.version = UnityEngine.U2D.SpriteShape.k_VersionFillSprite;
                return true;
            }

            // Already has a Fill Sprite from a previous partial upgrade or manual edit.
            if (null != spriteShape.fillSprite)
            {
                spriteShape.version = UnityEngine.U2D.SpriteShape.k_VersionFillSprite;
                return true;
            }

            var fillSprite = FindFillSprite(spriteShape.legacyFillTexture);
            if (null == fillSprite)
            {
                // Leave the version untouched so that re-importing the Texture as a full-rect Sprite
                // gives this asset another chance to upgrade. The legacy Texture keeps rendering meanwhile.
                return false;
            }

            spriteShape.fillSprite = fillSprite;
            spriteShape.version = UnityEngine.U2D.SpriteShape.k_VersionFillSprite;
            return true;
        }

        /// <summary>
        /// Upgrade a SpriteShape and persist it if it changed.
        /// </summary>
        internal static void UpgradeAndSave(UnityEngine.U2D.SpriteShape spriteShape)
        {
            if (!TryUpgrade(spriteShape))
                return;

            EditorUtility.SetDirty(spriteShape);
            AssetDatabase.SaveAssetIfDirty(spriteShape);
        }

        /// <summary>
        /// Upgrade every SpriteShape asset among the given asset paths.
        /// </summary>
        internal static void UpgradeAssetsAtPaths(string[] assetPaths)
        {
            foreach (var assetPath in assetPaths)
            {
                if (typeof(UnityEngine.U2D.SpriteShape) != AssetDatabase.GetMainAssetTypeAtPath(assetPath))
                    continue;

                var spriteShape = AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteShape>(assetPath);
                UpgradeAndSave(spriteShape);
            }
        }
    }
}
