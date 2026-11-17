#if ENABLE_SPRITESHAPE_PHYSICS
using Unity.U2D.Physics;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace UnityEditor.U2D
{
    // Draws a Physics Area Sprite Shape's build settings, showing only the settings in use.
    // Reverse Winding only applies to the Segments output, so it is hidden otherwise.
    [CustomPropertyDrawer(typeof(PhysicsAreaSpriteShape.BuildSettings))]
    internal sealed class PhysicsAreaSpriteShapeBuildSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            var outputType = property.FindPropertyRelative(nameof(PhysicsAreaSpriteShape.BuildSettings.m_OutputType));
            var reverseWinding = new PropertyField(property.FindPropertyRelative(nameof(PhysicsAreaSpriteShape.BuildSettings.m_ReverseWinding)));

            root.Add(new PropertyField(outputType));
            root.Add(reverseWinding);

            Refresh();
            root.TrackPropertyValue(outputType, _ => Refresh());

            return root;

            #region Locals

            // A selection mixing both output types shows every setting either of them uses.
            void Refresh()
            {
                var usesSegments = outputType.hasMultipleDifferentValues || outputType.enumValueIndex == (int)PhysicsAreaSpriteShape.OutputType.Segments;
                reverseWinding.style.display = usesSegments ? DisplayStyle.Flex : DisplayStyle.None;
            }

            #endregion
        }
    }
}
#endif
