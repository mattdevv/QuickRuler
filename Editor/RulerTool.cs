using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.EditorTools;
using Object = UnityEngine.Object;

namespace mattdevv
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
        public override GUIContent toolbarIcon => new GUIContent(
            AssetDatabase.LoadAssetByGUID<Texture>(new GUID("e23c67c3e6dbf794081ed9fc1cdc588f")), "Ruler Tool");

        private bool isDragging;
        private bool foundStart;
        private bool foundEnd;
        private Vector3 startPoint;
        private Vector3 endPoint;

        private bool foundVertex;
        private Vector3 vertexPosition;

        private const float markerSize = 0.05f;
        
        private static readonly List<Object> picks = new (16);
        
        public override void OnActivated()
        {
            ResetRuler();
        }

        private bool ProjectPointInWorld(Vector2 guiPoint, bool tryVertexSnap, ref Vector3 result)
        {
            // Try to project onto a mesh vertex
            if (tryVertexSnap && HandleUtility.FindNearestVertex(guiPoint, out Vector3 vertex))
            {
                foundVertex = true;
                vertexPosition = vertex;
                result = vertex;
                
                return true;
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
                        return true;
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
                return true;
            }

            return false;
        }

        public override void OnToolGUI(EditorWindow window)
        {
            Event e = Event.current;
            
            if (e.type == EventType.Repaint)
            {
                // highlight the current vertex being snapped to
                if (foundVertex)
                {
                    Handles.color = Color.white;
                    Handles.RectangleHandleCap(0, vertexPosition, SceneView.lastActiveSceneView.camera.transform.rotation, markerSize, EventType.Repaint);
                }
                
                // Draw ruler if dragging
                if (isDragging && foundEnd)
                {
                    DrawRuler(startPoint, endPoint);
                }
                // else highlight the position under the cursor
                else if (foundStart)
                {
                    Handles.color = Color.yellow;
                    Handles.DrawWireDisc(startPoint, SceneView.lastActiveSceneView.camera.transform.forward, markerSize * 2f);
                }
            }
            else if (e.type == EventType.Layout)
            {
                // do nothing
            }
            else
            {
                if (isDragging)
                {
                    if (ProjectPointInWorld(e.mousePosition, e.shift, ref endPoint))
                    {
                        foundEnd = true;
                        SceneView.RepaintAll();
                    }
                }
                else
                { 
                    if (ProjectPointInWorld(e.mousePosition, e.shift, ref startPoint))
                    {
                        foundStart = true;
                        SceneView.RepaintAll();
                    }
                }
                
                HandleInput(e);
            }
        }

        private void HandleInput(Event e)
        {
            // check if shift was released
            if (e.type == EventType.KeyUp && (e.keyCode == KeyCode.LeftShift || e.keyCode == KeyCode.RightShift) && !e.shift)
            {
                foundVertex = false;
            }
            else if (isDragging)
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
            }
            else
            {
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    // Lock start point
                    isDragging = true;
                    e.Use();
                }
            }
            
        }

        private void ResetRuler()
        {
            isDragging = false;
            foundEnd = false;
            foundStart = false;
            foundVertex = false;
        }

        private void DrawRulerLine(Vector3 a, Vector3 b, Color colorA, Color colorB, int segments = 1)
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

        private void DrawRuler(Vector3 a, Vector3 b)
        {
            var camera = SceneView.lastActiveSceneView.camera;
            var cameraPos = camera.transform.position;

            // shift render points towards camera by a percentage to prevent z-fighting
            var lineStart = cameraPos + 0.99f * (a - cameraPos);
            var lineEnd = cameraPos + 0.99f * (b - cameraPos);
            
            float separation = Vector3.Distance(lineStart, lineEnd);
            float calcSize = Mathf.Min(separation * 0.3f, markerSize);
            Vector3 lineDir = (b - a).normalized;

            Vector3 handlePosA = lineStart + lineDir * (calcSize * 0.7f);
            Quaternion handleRotA = Quaternion.LookRotation(-lineDir);
            Vector3 handlePosB = lineEnd - lineDir * (calcSize * 0.7f);
            Quaternion handleRotB = Quaternion.LookRotation(lineDir);
            
            lineStart += lineDir * (calcSize * 1.2f);
            lineEnd -= lineDir * (calcSize * 1.2f);

            // change order of drawing so that objects overlap correctly
            if ((handlePosA - cameraPos).sqrMagnitude < (handlePosB - cameraPos).sqrMagnitude)
            {
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.color = Color.red;
                Handles.ConeHandleCap(0, handlePosB, handleRotB, calcSize, EventType.Repaint);
                Handles.DrawLine(lineStart, lineEnd);
                Handles.ConeHandleCap(0, handlePosA, handleRotA, calcSize, EventType.Repaint);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = Color.yellow;
                Handles.ConeHandleCap(0, handlePosB, handleRotB, calcSize, EventType.Repaint);
                DrawRulerLine(lineStart, lineEnd, new Color(1,1,0,1), new Color(0, 0, 0, 1), 20);
                Handles.color = Color.yellow;
                Handles.ConeHandleCap(0, handlePosA, handleRotA, calcSize, EventType.Repaint);
            }
            else
            {
                // draw behind objects
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.color = Color.red;
                Handles.ConeHandleCap(0, handlePosA, handleRotA, calcSize, EventType.Repaint);
                Handles.DrawLine(lineStart, lineEnd);
                Handles.ConeHandleCap(0, handlePosB, handleRotB, calcSize, EventType.Repaint);
                
                // draw in front of objects
                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = Color.yellow;
                Handles.ConeHandleCap(0, handlePosA, handleRotA, calcSize, EventType.Repaint);
                DrawRulerLine(lineStart, lineEnd, new Color(1,1,0,1), new Color(0, 0, 0, 1), 20);
                Handles.color = Color.yellow;
                Handles.ConeHandleCap(0, handlePosB, handleRotB, calcSize, EventType.Repaint);
            }
            // reset for more drawing
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            
            // Distance label in screen space
            float distance = Vector3.Distance(a, b);
            Vector2 guiPoint = (HandleUtility.WorldToGUIPoint(a) + HandleUtility.WorldToGUIPoint(b)) * 0.5f;
            
            var cameraTransform = camera.transform;
            Vector3 cameraDir = cameraTransform.forward;
            Vector3 cameraPlanePoint = cameraTransform.position + cameraDir * SceneView.lastActiveSceneView.camera.nearClipPlane;
            
            // only draw the label if the points are in front of the camera
            if (Vector3.Dot(cameraDir, a - cameraPlanePoint) > 0 && Vector3.Dot(cameraDir, b - cameraPlanePoint) > 0)
            {
                Handles.BeginGUI();
                GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
                {
                    normal = { textColor = Color.white }
                };
                string text = distance.ToString("F3") + " m";
                Vector2 size = style.CalcSize(new GUIContent(text));
                Rect rect = new Rect(guiPoint.x - size.x * 0.5f, guiPoint.y - size.y * 0.5f, size.x, size.y);

                // draw twice for extra darkness
                var roundedStyle = new GUIStyle(GUI.skin.box);
                GUI.color = new Color(0, 0, 0, .75f);
                EditorGUI.LabelField(rect, GUIContent.none, roundedStyle);
                EditorGUI.LabelField(rect, GUIContent.none, roundedStyle);

                GUI.color = Color.white;
                GUI.Label(rect, text, style);

                Handles.EndGUI();
            }
        }
    }
}
