using UnityEngine;
using UnityEngine.U2D;
using UnityEditor;

namespace UnityEditor.U2D
{
    internal class SpriteShapeAssetPostProcessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (importedAssets.Length > 0)
            {
                // Upgrade outside of the import callback so that saving the asset does not re-enter it.
                EditorApplication.delayCall += () => SpriteShapeUpgrader.UpgradeAssetsAtPaths(importedAssets);

                GameObject[] allGOs = UnityEngine.Object.FindObjectsByType<GameObject>();
                foreach (GameObject go in allGOs)
                {
                    if (!go.activeInHierarchy)
                        continue;
                    SpriteShapeController sc = go.GetComponent<SpriteShapeController>();
                    if (sc != null)
                        sc.RefreshSpriteShape();
                }
            }
        }
    }
}
