using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.U2D;

namespace UnityEditor.U2D
{
    public class SpriteShapeToolEditor
    {
        private struct ShapeSegment
        {
            public int start;
            public int end;
            public int angleRange;
        };

        private struct ShapeAngleRange
        {
            public float start;
            public float end;
            public int order;
            public int index;
        };

        private static class Contents
        {
            public static readonly GUIContent tangentStraightIcon = SpriteShapeEditorGUI.IconContent("TangentStraight", "Straight line from point to point.");
            public static readonly GUIContent tangentCurvedIcon = SpriteShapeEditorGUI.IconContent("TangentCurved", "Tangents mirror each others angle.");
            public static readonly GUIContent tangentAsymmetricIcon = SpriteShapeEditorGUI.IconContent("TangentAssymetric", "Tangents are not linked.");
            public static readonly GUIContent tangentStraightIconPro = SpriteShapeEditorGUI.IconContent("TangentStraightPro", "Straight line from point to point.");
            public static readonly GUIContent tangentCurvedIconPro = SpriteShapeEditorGUI.IconContent("TangentCurvedPro", "Tangents mirror each others angle.");
            public static readonly GUIContent tangentAsymmetricIconPro = SpriteShapeEditorGUI.IconContent("TangentAssymetricPro", "Tangents are not linked.");
            public static readonly GUIContent positionLabel = new GUIContent("Position", "Position of the Control Point");
            public static readonly GUIContent leftTangentLabel = new GUIContent("Left Tangent", "Left Tangent end point.");
            public static readonly GUIContent rightTangentLabel = new GUIContent("Right Tangent", "Right Tangent end point.");
            public static readonly GUIContent enableSnapLabel = new GUIContent("Snapping", "Snap points using the snap settings");
            public static readonly GUIContent pointModeLabel = new GUIContent("Mode");
            public static readonly GUIContent invalidSpriteLabel = new GUIContent("No sprite defined");

            public static readonly GUIContent heightLabel = new GUIContent("Height", "Height override for control point.");
            public static readonly GUIContent spriteIndexLabel = new GUIContent("Sprite Variant", "Index of the sprite variant at this control point");
            public static readonly GUIContent cornerLabel = new GUIContent("Corner", "Set if Corner is automatic or disabled.");
            public static readonly GUIContent pointLabel = new GUIContent("Point");

            public static readonly int[] cornerValues = { 0, 1 };
            public static readonly GUIContent[] cornerOptions = { new GUIContent("Disabled"), new GUIContent("Automatic") };
            public static readonly GUIContent xLabel = new GUIContent("X");
            public static readonly GUIContent yLabel = new GUIContent("Y");
            public static readonly GUIContent zLabel = new GUIContent("Z");
        }

        Spline m_Spline;
        int m_SelectedPoint = -1;
        int m_SelectedAngleRange = -1;
        int m_SpriteShapeHashCode = 0;
        List<ShapeSegment> m_ShapeSegments = new List<ShapeSegment>();
        SpriteSelector spriteSelector = new SpriteSelector();

        public Spline spline
        {
            get { return m_Spline; }
        }

        public SpriteShapeToolEditor()
        {
            RegisterCallbacks();
        }

        public void OnDisable()
        {
            UnregisterCallbacks();
        }

        private void RegisterCallbacks()
        {
            UnregisterCallbacks();

            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void UnregisterCallbacks()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void RegisterUndo(string name)
        {
            Debug.Assert(SpriteShapeTool.instance.isActive);
            Undo.RegisterCompleteObjectUndo(SplineEditorCache.GetTarget(), name);
        }

        private void SetDirty()
        {
            Debug.Assert(SpriteShapeTool.instance.isActive);
            EditorUtility.SetDirty(SplineEditorCache.GetTarget());
        }

        private void OnUndoRedo()
        {
            SceneView.RepaintAll();
        }

        public void OnInspectorGUI(Spline spline)
        {
            m_Spline = spline;

            if (SpriteShapeTool.instance != null)
                if (SpriteShapeTool.instance.splineEditor != null)
                    SpriteShapeTool.instance.splineEditor.GetAngleRange = GetAngleRange;

            EditorGUI.BeginChangeCheck();
           
            if (GUI.enabled && SplineEditorCache.GetSelection().Count > 0)
            {
                
                EditorGUILayout.LabelField(Contents.pointLabel, EditorStyles.boldLabel);

                DoTangentGUI();
                DoPointInspector();
                SnappingUtility.enabled = EditorGUILayout.Toggle(Contents.enableSnapLabel, SnappingUtility.enabled);
            }                

            if (EditorGUI.EndChangeCheck())
                SetDirty();
        }

        private void DoTangentGUI()
        {
            ISelection selection = SplineEditorCache.GetSelection();

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.PrefixLabel(Contents.pointModeLabel);

            ShapeTangentMode? tangentMode = null;

            if (selection.single != -1)
                tangentMode = m_Spline.GetTangentMode(selection.single);
            else
            {
                foreach (int index in selection)
                {
                    if (tangentMode == null)
                        tangentMode = m_Spline.GetTangentMode(index);
                    else
                    {
                        if (tangentMode != m_Spline.GetTangentMode(index))
                        {
                            tangentMode = null;
                            break;
                        }
                    }
                }

            }

            ShapeTangentMode? prevTangentMode = tangentMode;

            GUIContent tangentStraightIcon = Contents.tangentStraightIcon;
            GUIContent tangentCurvedIcon = Contents.tangentCurvedIcon;
            GUIContent tangentAsymmetricIcon = Contents.tangentAsymmetricIcon;

            if (EditorGUIUtility.isProSkin)
            {
                tangentStraightIcon = Contents.tangentStraightIconPro;
                tangentCurvedIcon = Contents.tangentCurvedIconPro;
                tangentAsymmetricIcon = Contents.tangentAsymmetricIconPro;
            }

            if (selection.single != -1)
            {
                if (GUILayout.Toggle(tangentMode == ShapeTangentMode.Linear, tangentStraightIcon, new GUIStyle("Button"), GUILayout.Height(23), GUILayout.Width(29)))
                    tangentMode = ShapeTangentMode.Linear;

                if (GUILayout.Toggle(tangentMode == ShapeTangentMode.Continuous, tangentCurvedIcon, new GUIStyle("Button"), GUILayout.Height(23), GUILayout.Width(29)))
                    tangentMode = ShapeTangentMode.Continuous;

                if (GUILayout.Toggle(tangentMode == ShapeTangentMode.Broken, tangentAsymmetricIcon, new GUIStyle("Button"), GUILayout.Height(23), GUILayout.Width(29)))
                    tangentMode = ShapeTangentMode.Broken;

                if (tangentMode.HasValue && prevTangentMode.HasValue && tangentMode.Value != prevTangentMode.Value)
                {
                    RegisterUndo("Edit Tangent Mode");
                    SpriteShapeTool.instance.SetTangentMode(selection.single, tangentMode.Value);
                }
            }
            else
            {
                EditorGUI.BeginChangeCheck();

                if (GUILayout.Toggle(tangentMode == ShapeTangentMode.Linear, tangentStraightIcon, new GUIStyle("Button"), GUILayout.Height(23), GUILayout.Width(29)))
                    tangentMode = ShapeTangentMode.Linear;

                if (GUILayout.Toggle(tangentMode == ShapeTangentMode.Continuous, tangentCurvedIcon, new GUIStyle("Button"), GUILayout.Height(23), GUILayout.Width(29)))
                    tangentMode = ShapeTangentMode.Continuous;

                if (GUILayout.Toggle(tangentMode == ShapeTangentMode.Broken, tangentAsymmetricIcon, new GUIStyle("Button"), GUILayout.Height(23), GUILayout.Width(29)))
                    tangentMode = ShapeTangentMode.Broken;

                if (EditorGUI.EndChangeCheck())
                {
                    if (tangentMode.HasValue && prevTangentMode.HasValue && tangentMode.Value != prevTangentMode.Value)
                    {
                        RegisterUndo("Edit Tangent Mode");
                        
                        foreach (int index in selection)
                            SpriteShapeTool.instance.SetTangentMode(index, tangentMode.Value);
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        static bool WithinRange(ShapeAngleRange angleRange, float inputAngle)
        {
            float range = angleRange.end - angleRange.start;
            float angle = Mathf.Repeat(inputAngle - angleRange.start, 360f);
            angle = (angle == 360.0f) ? 0 : angle;
            return (angle >= 0f && angle <= range);
        }

        static int RangeFromAngle(List<ShapeAngleRange> angleRanges, float angle)
        {
            foreach (var range in angleRanges)
            {
                if (WithinRange(range, angle))
                    return range.index;
            }

            return -1;
        }

        private void GenerateSegments(SpriteShapeController sc, List<ShapeAngleRange> angleRanges)
        {
            var controlPointCount = sc.spline.GetPointCount();
            var angleRangeIndices = new int[controlPointCount];
            ShapeSegment activeSegment = new ShapeSegment() { start = -1, end = -1, angleRange = -1 };
            m_ShapeSegments.Clear();

            for (int i = 0; i < controlPointCount; ++i)
            {
                var actv = i;
                var next = SplineUtility.NextIndex(actv, controlPointCount);
                var pos1 = sc.spline.GetPosition(actv);
                var pos2 = sc.spline.GetPosition(next);
                bool continueStrip = (sc.spline.GetTangentMode(actv) == ShapeTangentMode.Continuous), edgeUpdated = false;
                float angle = 0;
                if (false == continueStrip || activeSegment.start == -1)
                    angle = SplineUtility.SlopeAngle(pos1, pos2) + 90.0f;

                next = (!sc.spline.isOpenEnded && next == 0) ? (actv + 1) : next;
                int mn = (actv < next) ? actv : next;
                int mx = (actv > next) ? actv : next;

                var anglerange = RangeFromAngle(angleRanges, angle);
                angleRangeIndices[actv] = anglerange;
                if (anglerange == -1)
                {
                    activeSegment = new ShapeSegment() { start = mn, end = mx, angleRange = anglerange };
                    m_ShapeSegments.Add(activeSegment);
                    continue;
                }

                // Check for Segments. Also check if the Segment Start has been resolved. Otherwise simply start with the next one
                if (activeSegment.start != -1)
                    continueStrip = continueStrip && (angleRangeIndices[activeSegment.start] != -1);

                bool canContinue = (actv != (controlPointCount - 1)) || (!sc.spline.isOpenEnded && (actv == (controlPointCount - 1)));
                if (continueStrip && canContinue)
                {
                    for (int s = 0; s < m_ShapeSegments.Count; ++s)
                    {
                        activeSegment = m_ShapeSegments[s];
                        if (activeSegment.start - mn == 1)
                        {
                            edgeUpdated = true;
                            activeSegment.start = mn;
                            m_ShapeSegments[s] = activeSegment;
                            break;
                        }
                        if (mx - activeSegment.end == 1)
                        {
                            edgeUpdated = true;
                            activeSegment.end = mx;
                            m_ShapeSegments[s] = activeSegment;
                            break;
                        }
                    }
                }

                if (!edgeUpdated)
                {
                    activeSegment.start = mn;
                    activeSegment.end = mx;
                    activeSegment.angleRange = anglerange;
                    m_ShapeSegments.Add(activeSegment);
                }

            }        
        }

        private int GetAngleRange(SpriteShapeController sc, int point, ref int startPoint)
        {
            int angleRange = -1;
            startPoint = point;
            for (int i = 0; i < m_ShapeSegments.Count; ++i)
            {
                if (point >= m_ShapeSegments[i].start && point < m_ShapeSegments[i].end)
                {
                    angleRange = m_ShapeSegments[i].angleRange;
                    // startPoint = m_ShapeSegments[i].start;   // As variants inside a continous segment is allowed, we just set each points as it is.
                    if (angleRange >= sc.spriteShape.angleRanges.Count)
                        angleRange = 0;
                    break;
                }
            }
            return angleRange;
        }

        private List<ShapeAngleRange> GetAngleRangeSorted(SpriteShape ss)
        {
            List <ShapeAngleRange> angleRanges = new List<ShapeAngleRange>();
            int i = 0;
            foreach ( var angleRange in ss.angleRanges)
            {
                ShapeAngleRange sar = new ShapeAngleRange() { start = angleRange.start, end = angleRange.end, order = angleRange.order, index = i };
                angleRanges.Add(sar);
                i++;
            }
            angleRanges.Sort((a, b) => a.order.CompareTo(b.order));
            return angleRanges;
        }

        private int ResolveSpriteIndex(List<int> spriteIndices, ISelection selection, ref List<int> startPoints)
        {
            var spriteIndexValue = spriteIndices.FirstOrDefault();
            SpriteShapeController sc = SplineEditorCache.GetTarget();

            if (sc == null || sc.spriteShape == null)
                return -1;

            // Either SpriteShape Asset or SpriteShape Data has changed. 
            List<ShapeAngleRange> angleRanges = GetAngleRangeSorted(sc.spriteShape);
            if (m_SpriteShapeHashCode != sc.spriteShapeHashCode)
            {
                GenerateSegments(sc, angleRanges);
                m_SpriteShapeHashCode = sc.spriteShapeHashCode;
                m_SelectedPoint = -1;
            }

            if (sc.spriteShape != null)
            { 
                if (selection.single != -1)
                {
                    m_SelectedAngleRange = GetAngleRange(sc, selection.single, ref m_SelectedPoint);
                    startPoints.Add(m_SelectedPoint);
                    spriteIndexValue = m_Spline.GetSpriteIndex(m_SelectedPoint);
                }
                else
                {
                    m_SelectedAngleRange = -1;
                    foreach (var index in selection)
                    {
                        int startPoint = index;
                        int angleRange = GetAngleRange(sc, index, ref startPoint);
                        if (m_SelectedAngleRange != -1 && angleRange != m_SelectedAngleRange)
                        {
                            m_SelectedAngleRange = -1;
                            break;
                        }
                        startPoints.Add(startPoint);
                        m_SelectedAngleRange = angleRange;
                    }
                }
            }

            if (m_SelectedAngleRange != -1)
                spriteSelector.UpdateSprites(sc.spriteShape.angleRanges[m_SelectedAngleRange].sprites.ToArray());
            else
                spriteIndexValue = -1;
            return spriteIndexValue;
        }

        private int GetAngleRange(int index)
        {
            int startPoint = 0;
            SpriteShapeController sc = SplineEditorCache.GetTarget();
            return GetAngleRange(sc, index, ref startPoint);
        }

        private void DoPointInspector()
        {
            var selection = SplineEditorCache.GetSelection();
            var positions = new List<Vector3>();
            var heights = new List<float>();
            var spriteIndices = new List<int>();
            var corners = new List<bool>();

            foreach (int index in selection)
            {
                positions.Add(m_Spline.GetPosition(index));
                heights.Add(m_Spline.GetHeight(index));
                spriteIndices.Add(m_Spline.GetSpriteIndex(index));
                corners.Add(m_Spline.GetCorner(index));
            }

            EditorGUIUtility.wideMode = true;

            EditorGUI.BeginChangeCheck();

            positions = MultiVector2Field(Contents.positionLabel, positions, 1.5f);

            if (EditorGUI.EndChangeCheck())
            {
                RegisterUndo("Inspector");

                for (int index = 0; index < positions.Count; index++)
                    m_Spline.SetPosition(selection.ElementAt(index), positions[index]);
                SceneView.RepaintAll();
            }

            EditorGUIUtility.wideMode = false;

            bool mixedValue = EditorGUI.showMixedValue;

            EditorGUI.BeginChangeCheck();
            
            var heightValue = heights.FirstOrDefault();
            EditorGUI.showMixedValue = heights.All( h => Mathf.Approximately(h, heightValue) ) == false;

            heightValue = EditorGUILayout.Slider(Contents.heightLabel, heightValue, 0.1f, 4.0f);

            if (EditorGUI.EndChangeCheck())
            {
                RegisterUndo("Inspector");

                foreach (var index in selection)
                    m_Spline.SetHeight(index, heightValue);
            }

            List<int> startPoints = new List<int>();
            var spriteIndexValue = ResolveSpriteIndex(spriteIndices, selection, ref startPoints);

            if (spriteIndexValue != -1)
            { 
                EditorGUI.BeginChangeCheck();

                spriteSelector.ShowGUI(spriteIndexValue);

                if (EditorGUI.EndChangeCheck())
                {
                    RegisterUndo("Inspector");
                    foreach (var index in startPoints)
                        m_Spline.SetSpriteIndex(index, spriteSelector.selectedIndex);
                }
            }
            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();

            var cornerValue = corners.FirstOrDefault();
            EditorGUI.showMixedValue = corners.All( v => (v == cornerValue) ) == false;

            cornerValue = EditorGUILayout.IntPopup(Contents.cornerLabel, cornerValue ? 1 : 0, Contents.cornerOptions, Contents.cornerValues) > 0;

            if (EditorGUI.EndChangeCheck())
            {
                RegisterUndo("Inspector");

                foreach (var index in selection)
                    m_Spline.SetCorner(index, cornerValue);
            }

            EditorGUI.showMixedValue = mixedValue;
        }

        private List<Vector3> MultiVector2Field(GUIContent label, List<Vector3> values, float floatWidth)
        {
            float kSpacingSubLabel = 2.0f;
            float kMiniLabelW = 13;

            if (!values.Any())
                return values;

            Rect position = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            int id = GUIUtility.GetControlID("Vector2Field".GetHashCode(), FocusType.Passive, position);
            position = SpriteShapeEditorGUI.MultiFieldPrefixLabel(position, id, label, 2);
            position.height = EditorGUIUtility.singleLineHeight;

            float w = (position.width - kSpacingSubLabel) / floatWidth;
            Rect nr = new Rect(position);
            nr.width = w;
            float t = EditorGUIUtility.labelWidth;
            int l = EditorGUI.indentLevel;
            EditorGUIUtility.labelWidth = kMiniLabelW;
            EditorGUI.indentLevel = 0;
            bool mixedValue = EditorGUI.showMixedValue;

            bool equalX = values.All(v => Mathf.Approximately(v.x, values.First().x));
            bool equalY = values.All(v => Mathf.Approximately(v.y, values.First().y));

            EditorGUI.showMixedValue = !equalX;
            EditorGUI.BeginChangeCheck();
            float x = EditorGUI.FloatField(nr, Contents.xLabel, values[0].x);
            if (EditorGUI.EndChangeCheck())
                for (int i = 0; i < values.Count; i++)
                    values[i] = new Vector3(x, values[i].y, values[i].z);

            nr.x += w + kSpacingSubLabel;

            EditorGUI.showMixedValue = !equalY;
            EditorGUI.BeginChangeCheck();
            float y = EditorGUI.FloatField(nr, Contents.yLabel, values[0].y);
            if (EditorGUI.EndChangeCheck())
                for (int i = 0; i < values.Count; i++)
                    values[i] = new Vector3(values[i].x, y, values[i].z);

            EditorGUI.showMixedValue = mixedValue;
            EditorGUIUtility.labelWidth = t;
            EditorGUI.indentLevel = l;

            return values;
        }
    }
}
