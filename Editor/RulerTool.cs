using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.ShortcutManagement;
using Object = UnityEngine.Object;

namespace mattdevv.Editor
{
    internal static class InternalEditorTools
    {
        private static MethodInfo meth_IntersectRayMesh;
        
        [InitializeOnLoadMethod]
        private static void Init() 
        {
            meth_IntersectRayMesh = typeof(HandleUtility).GetMethod(
                "IntersectRayMesh",BindingFlags.Static | BindingFlags.NonPublic);
        }
	    
        public static bool IntersectRayMesh(Ray ray, MeshFilter meshFilter, out RaycastHit hit)
        {
            return IntersectRayMesh(ray, meshFilter.sharedMesh, meshFilter.transform.localToWorldMatrix, out hit);
        }

        public static bool IntersectRayMesh(Ray ray, Mesh mesh, Matrix4x4 matrix, out RaycastHit hit)
        {
            var parameters = new object[] {ray, mesh, matrix, null};
            
            bool result = (bool) meth_IntersectRayMesh.Invoke(null, parameters);
            hit = (RaycastHit) parameters[3];
            
            return result;
        }
    }

    [EditorTool("Ruler Tool")]
    public class RulerTool : EditorTool
    {
        [Shortcut("Activate QuickRuler", KeyCode.Q, ShortcutModifiers.Control)]
        static void ToolShortcut()
        {
            ToolManager.SetActiveTool<RulerTool>();
        }
        
        public override GUIContent toolbarIcon => new GUIContent(
            AssetDatabase.LoadAssetByGUID<Texture>(new GUID("e23c67c3e6dbf794081ed9fc1cdc588f")), "Ruler Tool");

        private List<Vector3> nodes = new ();
        private Vector3 mouseNode;

        private bool foundVertex;
        private Vector3 vertexPosition;

        private const float markerSize = 0.05f;
        
        private static readonly List<Object> picks = new (16);

        private GUIStyle boxStyle;
        private GUIStyle textStyle;

        private void ProjectPointInWorld(Vector2 guiPoint, bool tryVertexSnap, out Vector3 result)
        {
            // Try to project onto a mesh vertex
            if (tryVertexSnap && HandleUtility.FindNearestVertex(guiPoint, out Vector3 vertex))
            {
                const float pixelRadius = 50;
                Vector2 point = HandleUtility.WorldToGUIPoint(vertex);
                if (Vector2.SqrMagnitude(point - guiPoint) < pixelRadius * pixelRadius)
                {
                    foundVertex = true;
                    vertexPosition = vertex;
                    result = vertex;

                    return;
                }
            }

            // store that the latest projection was not to a vertex
            foundVertex = false;
            
            // Try to project onto a opaque surface
            HandleUtility.PickAllObjects(guiPoint, picks);
            for (int i = 0; i < picks.Count; i++)
            {
                var pickObject = picks[i] as GameObject;
                if (pickObject != null && pickObject.TryGetComponent(out MeshFilter meshFilter))
                {
                    if (InternalEditorTools.IntersectRayMesh(HandleUtility.GUIPointToWorldRay(guiPoint), meshFilter, out RaycastHit hit))
                    {
                        result = hit.point;
                        return;
                    }
                }
            }

            var sceneView = SceneView.lastActiveSceneView;
            Vector3 cameraForward = sceneView.camera.transform.forward;
            Ray cameraRay = HandleUtility.GUIPointToWorldRay(guiPoint);
            
            // project on to the camera plane at the distance of the pivot
            Plane pivotPlane = new Plane(cameraForward, sceneView.pivot);
            if (pivotPlane.Raycast(cameraRay, out float d))
            {
                result = cameraRay.GetPoint(d);
                return;
            }

            result = cameraRay.origin;
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (boxStyle == null)
            {
                boxStyle = new GUIStyle(GUI.skin.box);
                textStyle = new GUIStyle(EditorStyles.boldLabel) {
                    normal = { textColor = Color.white }
                }; 
            
            }
            
            Event e = Event.current;
            
            if (e.type == EventType.Repaint)
            {
                DrawRulers();
                
                // highlight the current vertex being snapped to
                if (foundVertex)
                {
                    Handles.color = Color.white;
                    Handles.RectangleHandleCap(0, vertexPosition, SceneView.lastActiveSceneView.camera.transform.rotation, markerSize, EventType.Repaint);
                }
            }
            else if (e.type == EventType.Layout)
            {
                // do nothing
            }
            else
            {
                ProjectPointInWorld(e.mousePosition, e.shift, out mouseNode);
                SceneView.RepaintAll();
                
                HandleInput(e);
            }
            
            Window();
        }
        
        private Rect windowRect = new Rect(20, 20, 260, 140);
        private void Window()
        {
            Handles.BeginGUI();

            windowRect = GUI.Window(
                123456,
                windowRect,
                DrawWindow,
                "My Tool Settings"
            );

            Handles.EndGUI();
        }
        
        void DrawWindow(int id)
        {
            EditorGUILayout.FloatField("Radius", 0);
            EditorGUILayout.Toggle("Advanced", false);
            GUILayout.Button("Reset");

            GUI.DragWindow();
        }

        private void HandleInput(Event e)
        {
            // check if (final) shift key was released
            if (e.type == EventType.KeyUp && (e.keyCode == KeyCode.LeftShift || e.keyCode == KeyCode.RightShift) && !e.shift)
            {
                foundVertex = false;
            }
            else if (nodes.Count > 0)
            {
                if (e.type == EventType.MouseLeaveWindow)
                {
                    // Finish dragging, reset to start selection
                    e.Use();
                    
                    ResetRuler();
                }
                else if (e.type == EventType.MouseUp && e.button == 0)
                {
                    // Finish dragging, reset to start selection
                    e.Use();
                    
                    ResetRuler();
                }
                else if (e.type == EventType.MouseDown && e.button == 1)
                {
                    e.Use();
                    
                    // check that ruler has length
                    if (Vector3.SqrMagnitude(mouseNode - nodes[^1]) > 0.00005f)
                        nodes.Add(mouseNode);
                }
            }
            else
            {
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    e.Use();
                    
                    // Lock start point
                    nodes.Add(mouseNode);
                }
            }
            
        }

        private void ResetRuler()
        {
            nodes.Clear();
            foundVertex = false;
        }

        private void DrawSegmentLine(Vector3 a, Vector3 b, int segments, Color colorA, Color colorB)
        {
            float segmentCount = segments; //5 * Mathf.Log((a - b).magnitude + 1) + 3;
            Vector3 segment = (b - a) * (1f / segmentCount);
            
            Vector3 start = a;

            bool color = false;
            while (segmentCount > 0)
            {
                float length = Mathf.Min(segmentCount, 1);
                segmentCount -= length;
                
                Vector3 end = start + segment * length;
                
                Handles.color = color ? colorB : colorA;
                color = !color;
                
                Handles.DrawLine(start, end);
                start = end;
            }
        }

        private void DrawRulers()
        {
            var camera = SceneView.lastActiveSceneView.camera;
            var cameraPos = camera.transform.position;
            var cameraDir = camera.transform.forward;

            if (nodes.Count > 0)
            {
                for (int i = 1; i < nodes.Count; i++)
                {
                    if (i == 1)
                        DrawRuler(nodes[i - 1], nodes[i], cameraPos, cameraDir, true, false);
                    else
                        DrawRuler(nodes[i - 1], nodes[i], cameraPos, cameraDir, false, false);
                }

                if (Vector3.SqrMagnitude(nodes[^1] - mouseNode) > 0.00005f)
                {
                        DrawRuler(nodes[^1], mouseNode, cameraPos, cameraDir, nodes.Count < 2, true);
                }
                else if (nodes.Count == 1)
                {
                    Handles.color = Color.yellow;
                    Handles.SphereHandleCap(0, mouseNode, Quaternion.identity, markerSize, EventType.Repaint);
                }

                if (nodes.Count > 1)
                {
                    float totalLength = Vector3.Distance(nodes[^1], mouseNode);
                    for (int i = 1; i < nodes.Count; i++)
                        totalLength += Vector3.Distance(nodes[i - 1], nodes[i]);
                    
                    DrawText("Total: " + totalLength.ToString("F3") + " m", Event.current.mousePosition + new Vector2(0, -15));
                }
            }
            else
            {
                Handles.SphereHandleCap(0, mouseNode, Quaternion.identity, markerSize, EventType.Repaint);
            }
        }

        private void DrawRuler(in Vector3 a, in Vector3 b, in Vector3 cameraPos, in Vector3 cameraDir, bool startArrow, bool endArrow)
        {
            // shift render points towards camera by a percentage to prevent z-fighting
            var lineStart = cameraPos + 0.99f * (a - cameraPos);
            var lineEnd = cameraPos + 0.99f * (b - cameraPos);
            
            float separation = Vector3.Distance(lineStart, lineEnd);
            float calcSize = Mathf.Min(separation * 0.3f, markerSize);
            Vector3 lineDir = (b - a).normalized;

            Vector3 handlePosA = lineStart + lineDir * (calcSize * 0.7f);
            Quaternion handleRotA = Quaternion.LookRotation(-lineDir, cameraDir);
            Vector3 handlePosB = lineEnd - lineDir * (calcSize * 0.7f);
            Quaternion handleRotB = Quaternion.LookRotation(lineDir, cameraDir);
            
            lineStart += lineDir * (startArrow ? calcSize * 1.2f : calcSize * 0.5f);
            lineEnd -= lineDir * (endArrow ? calcSize * 1.2f : calcSize * 0.5f);

            // change order of drawing so that objects overlap correctly
            if ((handlePosA - cameraPos).sqrMagnitude < (handlePosB - cameraPos).sqrMagnitude)
            {
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.color = Color.red;
                if (endArrow)
                    Handles.ConeHandleCap(0, handlePosB, handleRotB, calcSize, EventType.Repaint);
                else                    
                    Handles.SphereHandleCap(0, b, handleRotB, calcSize, EventType.Repaint);
                Handles.DrawLine(lineStart, lineEnd);
                if (startArrow)
                    Handles.ConeHandleCap(0, handlePosA, handleRotA, calcSize, EventType.Repaint);
                else
                    Handles.SphereHandleCap(0, a, handleRotA, calcSize, EventType.Repaint);
                
                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = Color.yellow;
                if (endArrow)
                    Handles.ConeHandleCap(0, handlePosB, handleRotB, calcSize, EventType.Repaint);
                else                    
                    Handles.SphereHandleCap(0, b, handleRotB, calcSize, EventType.Repaint);
                DrawSegmentLine(lineStart, lineEnd, 20, new Color(1,1,0,1), new Color(0, 0, 0, 1));
                Handles.color = Color.yellow;
                if (startArrow)
                    Handles.ConeHandleCap(0, handlePosA, handleRotA, calcSize, EventType.Repaint);
                else
                    Handles.SphereHandleCap(0, a, handleRotA, calcSize, EventType.Repaint);
            }
            else
            {
                // draw behind objects
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.color = Color.red;
                if (startArrow)
                    Handles.ConeHandleCap(0, handlePosA, handleRotA, calcSize, EventType.Repaint);
                else
                    Handles.SphereHandleCap(0, a, handleRotA, calcSize, EventType.Repaint);
                Handles.DrawLine(lineStart, lineEnd);
                if (endArrow)
                    Handles.ConeHandleCap(0, handlePosB, handleRotB, calcSize, EventType.Repaint);
                else                    
                    Handles.SphereHandleCap(0, b, handleRotB, calcSize, EventType.Repaint);
                
                // draw in front of objects
                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = Color.yellow;
                if (startArrow)
                    Handles.ConeHandleCap(0, handlePosA, handleRotA, calcSize, EventType.Repaint);
                else
                    Handles.SphereHandleCap(0, a, handleRotA, calcSize, EventType.Repaint);
                DrawSegmentLine(lineStart, lineEnd, 20, new Color(1,1,0,1), new Color(0, 0, 0, 1));
                Handles.color = Color.yellow;
                if (endArrow)
                    Handles.ConeHandleCap(0, handlePosB, handleRotB, calcSize, EventType.Repaint);
                else                    
                    Handles.SphereHandleCap(0, b, handleRotB, calcSize, EventType.Repaint);
            }
            
            // reset for more drawing
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            
            
            Vector3 cameraPlanePoint = cameraPos + cameraDir * SceneView.lastActiveSceneView.camera.nearClipPlane;
            
            // only draw the label if the points are in front of the camera
            if (Vector3.Dot(cameraDir, a - cameraPlanePoint) > 0 && Vector3.Dot(cameraDir, b - cameraPlanePoint) > 0)
            {
                // Distance label in screen space
                Vector2 guiPoint = (HandleUtility.WorldToGUIPoint(a) + HandleUtility.WorldToGUIPoint(b)) * 0.5f;
                
                float distance = Vector3.Distance(a, b);
                string text = distance.ToString("F3") + " m";
                
                DrawText(text, guiPoint);
            }
        }

        public void DrawText(in string text, in Vector2 center)
        {
            Vector2 size = textStyle.CalcSize(new GUIContent(text));
            Rect rect = new Rect(center.x - size.x * 0.5f, center.y - size.y * 0.5f, size.x, size.y);
                
            Handles.BeginGUI();
            {
                // draw twice for extra darkness
                GUI.color = new Color(0, 0, 0, .75f);
                EditorGUI.LabelField(rect, GUIContent.none, boxStyle);
                EditorGUI.LabelField(rect, GUIContent.none, boxStyle);

                GUI.color = Color.white;
                EditorGUI.LabelField(rect, text, textStyle);
            }
            Handles.EndGUI(); 
        }
    }
}
