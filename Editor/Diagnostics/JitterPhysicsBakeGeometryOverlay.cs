using System;
using System.Collections.Generic;
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Editor.Baking;
using DataSakura.JitterPhysics.UnityArtifact;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DataSakura.JitterPhysics.Editor.Diagnostics
{
    /// <summary>
    /// Scene View overlay of the last baked snapshot and geometry authored since that bake.
    /// <para>
    /// Green always means the immutable artifact, including geometry since deleted from the
    /// scene. Red always means current geometry that is absent from or differs from that
    /// artifact. Drawing both snapshots is what makes moves and deletions diagnosable instead
    /// of merely saying that the level hash is stale.
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    internal static class JitterPhysicsBakeGeometryOverlay
    {
        private const string MenuPath =
            JitterPhysicsAuthoringConstants.EditorMenuRoot + "Show Baked Geometry Overlay";
        private const string PreferenceKey =
            "DataSakura.JitterPhysics.Editor.ShowBakedGeometryOverlay";

        private static readonly Color BakedColor = new Color(0.25f, 1f, 0.42f, 0.82f);
        private static readonly Color CurrentColor = new Color(1f, 0.25f, 0.22f, 0.88f);

        private static readonly Dictionary<string, CachedArtifact> ArtifactCache =
            new Dictionary<string, CachedArtifact>(StringComparer.Ordinal);

        private static bool enabled;

        static JitterPhysicsBakeGeometryOverlay()
        {
            enabled = EditorPrefs.GetBool(PreferenceKey, false);
            SceneView.duringSceneGui -= DuringSceneGui;
            SceneView.duringSceneGui += DuringSceneGui;
        }

        private static bool Enabled => enabled;

        [MenuItem(MenuPath, false, 12)]
        private static void Toggle()
        {
            enabled = !enabled;
            EditorPrefs.SetBool(PreferenceKey, enabled);
            Menu.SetChecked(MenuPath, enabled);
            SceneView.RepaintAll();
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateToggle()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        private static void DuringSceneGui(SceneView sceneView)
        {
            if (!Enabled || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            JitterPhysicsLevel[] levels = UnityEngine.Object.FindObjectsByType<JitterPhysicsLevel>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            var summaries = new List<LevelSummary>(levels.Length);

            Color previousColor = Handles.color;
            Matrix4x4 previousMatrix = Handles.matrix;
            CompareFunction previousZTest = Handles.zTest;

            try
            {
                // An x-ray overlay is deliberate: hidden collision behind a renderer is exactly
                // the geometry an author needs this mode to reveal.
                Handles.zTest = CompareFunction.Always;

                for (int i = 0; i < levels.Length; i++)
                {
                    JitterPhysicsLevel level = levels[i];
                    if (level == null || !level.gameObject.scene.IsValid() || !level.gameObject.scene.isLoaded)
                    {
                        continue;
                    }

                    summaries.Add(DrawLevel(level));
                }
            }
            finally
            {
                Handles.color = previousColor;
                Handles.matrix = previousMatrix;
                Handles.zTest = previousZTest;
            }

            DrawLegend(sceneView, summaries);
        }

        private static LevelSummary DrawLevel(JitterPhysicsLevel level)
        {
            PhysicsArtifact artifact = LoadArtifact(level, out string artifactError);
            var bakedBodies = new Dictionary<string, PhysicsBodyRecord>(StringComparer.Ordinal);

            if (artifact != null)
            {
                for (int i = 0; i < artifact.Bodies.Count; i++)
                {
                    PhysicsBodyRecord body = artifact.Bodies[i];
                    bakedBodies[body.SourceId] = body;

                    for (int shapeIndex = 0; shapeIndex < body.Shapes.Count; shapeIndex++)
                    {
                        DrawShape(body, body.Shapes[shapeIndex], BakedColor);
                    }
                }
            }

            var covered = new HashSet<Collider>();
            IReadOnlyList<JitterStaticBodySource> sources = level.CollectSources();

            int matching = 0;
            int currentOnly = 0;

            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                JitterStaticBodySource source = sources[sourceIndex];
                if (source == null)
                {
                    continue;
                }

                bakedBodies.TryGetValue(source.SourceId, out PhysicsBodyRecord bakedBody);
                bool bodyMatches = TryBodyPoseMatches(bakedBody, source.transform);

                var colliders = new List<Collider>();
                if (source.IncludeChildren)
                {
                    source.transform.GetComponentsInChildren(
                        JitterStaticBodySource.IncludeInactiveChildren, colliders);
                }
                else
                {
                    colliders.AddRange(source.GetComponents<Collider>());
                }

                for (int colliderIndex = 0; colliderIndex < colliders.Count; colliderIndex++)
                {
                    Collider collider = colliders[colliderIndex];
                    if (!IsCurrentGeometry(collider))
                    {
                        continue;
                    }

                    covered.Add(collider);
                    string shapeKey = JitterPhysicsColliderKey.Build(source.transform, collider);
                    JitterPhysicsConversionResult conversion = TryConvert(
                        source.transform, collider, shapeKey);

                    PhysicsShapeRecord bakedShape = FindShape(bakedBody, shapeKey);
                    if (conversion.Succeeded
                        && bodyMatches
                        && JitterPhysicsGeometryComparer.ShapesMatch(bakedShape, conversion.Shape))
                    {
                        matching++;
                        continue;
                    }

                    currentOnly++;
                    DrawCurrent(source.transform, collider, conversion);
                }
            }

            var inScope = new List<Collider>();
            if (level.GeometryRoot != null)
            {
                level.GeometryRoot.GetComponentsInChildren(false, inScope);
            }
            else
            {
                foreach (GameObject root in level.gameObject.scene.GetRootGameObjects())
                {
                    root.GetComponentsInChildren(false, inScope);
                }
            }

            // Colliders not claimed by a source can never enter the artifact. Showing them red
            // makes a missing JitterStaticBodySource visible before somebody assumes Bake saw it.
            for (int i = 0; i < inScope.Count; i++)
            {
                Collider collider = inScope[i];
                if (!IsCurrentGeometry(collider) || covered.Contains(collider))
                {
                    continue;
                }

                currentOnly++;
                JitterPhysicsConversionResult conversion = TryConvert(
                    collider.transform, collider, "unbaked");
                DrawCurrent(collider.transform, collider, conversion);
            }

            return new LevelSummary(
                string.IsNullOrEmpty(level.LevelId) ? level.name : level.LevelId,
                artifact?.ShapeCount ?? 0,
                matching,
                currentOnly,
                artifactError);
        }

        private static bool IsCurrentGeometry(Collider collider)
        {
            return collider != null
                && collider.enabled
                && collider.gameObject.activeInHierarchy;
        }

        private static bool TryBodyPoseMatches(PhysicsBodyRecord bakedBody, Transform transform)
        {
            try
            {
                return JitterPhysicsGeometryComparer.BodyPoseMatches(bakedBody, transform);
            }
            catch (ArgumentException)
            {
                // A non-finite or zero quaternion is invalid authoring. It belongs in red and
                // the regular validation command will provide the actionable error.
                return false;
            }
        }

        private static JitterPhysicsConversionResult TryConvert(
            Transform bodyRoot,
            Collider collider,
            string shapeKey)
        {
            try
            {
                return JitterPhysicsColliderConverter.Convert(bodyRoot, collider, shapeKey);
            }
            catch (Exception exception)
            {
                return JitterPhysicsConversionResult.Failure(
                    JitterPhysicsConversionStatus.NotFinite,
                    exception.Message);
            }
        }

        private static PhysicsShapeRecord FindShape(PhysicsBodyRecord body, string shapeKey)
        {
            if (body == null)
            {
                return null;
            }

            for (int i = 0; i < body.Shapes.Count; i++)
            {
                PhysicsShapeRecord candidate = body.Shapes[i];
                if (string.Equals(candidate.ShapeKey, shapeKey, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void DrawCurrent(
            Transform bodyRoot,
            Collider collider,
            JitterPhysicsConversionResult conversion)
        {
            if (conversion.Succeeded)
            {
                Vector3 position = bodyRoot.position;
                Quaternion orientation = bodyRoot.rotation;

                var body = new PhysicsBodyRecord(
                    "current",
                    new PhysicsVector3(position.x, position.y, position.z).Canonical(),
                    new PhysicsQuaternion(
                        orientation.x, orientation.y, orientation.z, orientation.w).Canonical(),
                    0f,
                    0f,
                    new[] { conversion.Shape });

                DrawShape(body, conversion.Shape, CurrentColor);
                return;
            }

            DrawBounds(collider.bounds, CurrentColor);
        }

        private static void DrawShape(
            PhysicsBodyRecord body,
            PhysicsShapeRecord shape,
            Color color)
        {
            Matrix4x4 previousMatrix = Handles.matrix;
            Color previousColor = Handles.color;

            try
            {
                Matrix4x4 bodyMatrix = Matrix4x4.TRS(
                    ToVector(body.Position),
                    ToQuaternion(body.Orientation),
                    Vector3.one);
                Matrix4x4 localMatrix = Matrix4x4.TRS(
                    ToVector(shape.LocalPosition),
                    ToQuaternion(shape.LocalRotation),
                    Vector3.one);

                Handles.matrix = bodyMatrix * localMatrix;
                Handles.color = color;

                switch (shape.ShapeType)
                {
                    case PhysicsShapeType.Box:
                        Handles.DrawWireCube(Vector3.zero, ToVector(shape.Size));
                        break;

                    case PhysicsShapeType.Sphere:
                        DrawSphere(shape.Radius);
                        break;

                    case PhysicsShapeType.Capsule:
                        DrawCapsule(shape.Radius, shape.Length);
                        break;

                    case PhysicsShapeType.Mesh:
                        DrawMesh(shape);
                        break;
                }
            }
            finally
            {
                Handles.matrix = previousMatrix;
                Handles.color = previousColor;
            }
        }

        private static void DrawSphere(float radius)
        {
            Handles.DrawWireDisc(Vector3.zero, Vector3.right, radius);
            Handles.DrawWireDisc(Vector3.zero, Vector3.up, radius);
            Handles.DrawWireDisc(Vector3.zero, Vector3.forward, radius);
        }

        private static void DrawCapsule(float radius, float length)
        {
            float halfLength = length * 0.5f;
            Vector3 top = Vector3.up * halfLength;
            Vector3 bottom = Vector3.down * halfLength;

            // Full circles deliberately show through the x-ray pass and make the cap radius
            // readable from any camera angle; the four rails communicate the cylinder length.
            Handles.DrawWireDisc(top, Vector3.right, radius);
            Handles.DrawWireDisc(top, Vector3.forward, radius);
            Handles.DrawWireDisc(bottom, Vector3.right, radius);
            Handles.DrawWireDisc(bottom, Vector3.forward, radius);

            Handles.DrawLine(top + Vector3.right * radius, bottom + Vector3.right * radius);
            Handles.DrawLine(top + Vector3.left * radius, bottom + Vector3.left * radius);
            Handles.DrawLine(top + Vector3.forward * radius, bottom + Vector3.forward * radius);
            Handles.DrawLine(top + Vector3.back * radius, bottom + Vector3.back * radius);
        }

        private static void DrawMesh(PhysicsShapeRecord shape)
        {
            for (int i = 0; i < shape.Indices.Length; i += 3)
            {
                Vector3 a = ToVector(shape.Vertices[shape.Indices[i]]);
                Vector3 b = ToVector(shape.Vertices[shape.Indices[i + 1]]);
                Vector3 c = ToVector(shape.Vertices[shape.Indices[i + 2]]);

                Handles.DrawLine(a, b);
                Handles.DrawLine(b, c);
                Handles.DrawLine(c, a);
            }
        }

        private static void DrawBounds(Bounds bounds, Color color)
        {
            Matrix4x4 previousMatrix = Handles.matrix;
            Color previousColor = Handles.color;

            Handles.matrix = Matrix4x4.identity;
            Handles.color = color;
            Handles.DrawWireCube(bounds.center, bounds.size);

            Handles.matrix = previousMatrix;
            Handles.color = previousColor;
        }

        private static PhysicsArtifact LoadArtifact(
            JitterPhysicsLevel level,
            out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(level.LevelId))
            {
                error = "level id is empty";
                return null;
            }

            string assetPath = JitterPhysicsArtifactPaths.ArtifactAssetPath(
                level.GeneratedFolder, level.LevelId);
            var asset = AssetDatabase.LoadAssetAtPath<JitterPhysicsArtifactAsset>(assetPath);

            if (asset == null)
            {
                error = "no baked artifact";
                return null;
            }

            string payloadPath = AssetDatabase.GetAssetPath(asset.Payload);
            string dependencyHash = string.IsNullOrEmpty(payloadPath)
                ? "<missing>"
                : AssetDatabase.GetAssetDependencyHash(payloadPath).ToString();
            string signature = asset.ArtifactHash + ":" + dependencyHash;

            if (ArtifactCache.TryGetValue(assetPath, out CachedArtifact cached)
                && string.Equals(cached.Signature, signature, StringComparison.Ordinal))
            {
                error = cached.Error;
                return cached.Artifact;
            }

            PhysicsArtifactResult result = JitterPhysicsArtifactLoader.Load(asset);
            var replacement = result.Succeeded
                ? new CachedArtifact(signature, result.Artifact, null)
                : new CachedArtifact(signature, null, result.Error.ToString());

            ArtifactCache[assetPath] = replacement;
            error = replacement.Error;
            return replacement.Artifact;
        }

        private static void DrawLegend(SceneView sceneView, IReadOnlyList<LevelSummary> summaries)
        {
            Handles.BeginGUI();
            try
            {
                float height = 58f + (Mathf.Max(1, summaries.Count) * 20f);
                var area = new Rect(12f, sceneView.position.height - height - 28f, 410f, height);
                GUI.Box(area, GUIContent.none, GUI.skin.window);

                float left = area.x + 10f;
                float top = area.y + 8f;
                GUI.Label(
                    new Rect(left, top, area.width - 20f, 18f),
                    "Jitter Physics — baked geometry",
                    EditorStyles.boldLabel);

                top += 20f;
                DrawSwatch(new Rect(left, top + 3f, 12f, 12f), BakedColor);
                GUI.Label(new Rect(left + 17f, top, 175f, 18f), "green: last baked snapshot");
                DrawSwatch(new Rect(left + 200f, top + 3f, 12f, 12f), CurrentColor);
                GUI.Label(new Rect(left + 217f, top, 175f, 18f), "red: new or changed now");

                top += 20f;
                if (summaries.Count == 0)
                {
                    GUI.Label(
                        new Rect(left, top, area.width - 20f, 18f),
                        "No active JitterPhysicsLevel in loaded scenes.");
                }
                else
                {
                    for (int i = 0; i < summaries.Count; i++)
                    {
                        LevelSummary summary = summaries[i];
                        string line = summary.LevelId
                            + $": baked {summary.Baked}, matching {summary.Matching}, red {summary.CurrentOnly}";

                        if (!string.IsNullOrEmpty(summary.Error))
                        {
                            line += " — " + summary.Error;
                        }

                        GUI.Label(new Rect(left, top, area.width - 20f, 18f), line);
                        top += 20f;
                    }
                }

            }
            finally
            {
                Handles.EndGUI();
            }
        }

        private static void DrawSwatch(Rect rect, Color color)
        {
            EditorGUI.DrawRect(rect, color);
        }

        private static Vector3 ToVector(PhysicsVector3 value) =>
            new Vector3(value.X, value.Y, value.Z);

        private static Quaternion ToQuaternion(PhysicsQuaternion value) =>
            new Quaternion(value.X, value.Y, value.Z, value.W);

        private sealed class CachedArtifact
        {
            internal CachedArtifact(string signature, PhysicsArtifact artifact, string error)
            {
                Signature = signature;
                Artifact = artifact;
                Error = error;
            }

            internal string Signature { get; }

            internal PhysicsArtifact Artifact { get; }

            internal string Error { get; }
        }

        private readonly struct LevelSummary
        {
            internal LevelSummary(
                string levelId,
                int baked,
                int matching,
                int currentOnly,
                string error)
            {
                LevelId = levelId;
                Baked = baked;
                Matching = matching;
                CurrentOnly = currentOnly;
                Error = error;
            }

            internal string LevelId { get; }

            internal int Baked { get; }

            internal int Matching { get; }

            internal int CurrentOnly { get; }

            internal string Error { get; }
        }
    }
}
