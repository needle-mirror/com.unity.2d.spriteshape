using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

namespace UnityEditor.U2D
{
    // A Prefab writes any reference to a non-persistent Object as null, so the geometry asset a
    // SpriteShapeGeometryCache creates while editing never reaches the Prefab file. Every import
    // re-bakes each cached SpriteShape from its serialized Spline and adds the result as a
    // sub-asset, which the cache then references like any other asset.
    internal class SpriteShapeGeometryCachePostprocessor : AssetPostprocessor
    {
        // Bump to re-import every Prefab when the baked geometry changes.
        public override uint GetVersion()
        {
            return 1;
        }

        void OnPostprocessPrefab(GameObject root)
        {
            var controllers = root.GetComponentsInChildren<SpriteShapeController>(true);
            var identifiers = new HashSet<string>();
            foreach (var controller in controllers)
            {
                if (!controller.geometryCached || !controller.spriteShapeGeometryCache)
                    continue;

                // Nested Prefab instances included: overrides may have changed their Spline, so
                // the nested Prefab's own sub-asset is not reused.
                var asset = controller.BakeGeometryCacheForImport();
                if (asset == null)
                    continue;

                DeclareDependencies(controller);
                context.AddObjectToAsset(MakeIdentifier(root.transform, controller.transform, identifiers), asset);
            }
        }

        // The geometry follows the profile, its Sprites and any modifiers, none of which the
        // Prefab importer otherwise depends on.
        void DeclareDependencies(SpriteShapeController controller)
        {
            foreach (var dependency in EditorUtility.CollectDependencies(new Object[] { controller }))
            {
                if (dependency == null || dependency is MonoScript)
                    continue;
                var path = AssetDatabase.GetAssetPath(dependency);
                // Built-in resources have a path but no artifact to depend on.
                if (path == assetPath || !(path.StartsWith("Assets/") || path.StartsWith("Packages/")))
                    continue;
                context.DependsOnArtifact(path);
            }
        }

        // Stable across imports, so references from Scenes and other Prefabs survive re-import.
        static string MakeIdentifier(Transform root, Transform transform, HashSet<string> used)
        {
            var path = AnimationUtility.CalculateTransformPath(transform, root);
            var identifier = "SpriteShapeGeometry/" + path;
            for (int i = 1; !used.Add(identifier); ++i)
                identifier = "SpriteShapeGeometry/" + path + "#" + i;
            return identifier;
        }
    }
}
