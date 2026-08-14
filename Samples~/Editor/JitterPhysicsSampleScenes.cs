using System.Collections.Generic;
using System.IO;
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Editor.Baking;
using DataSakura.JitterPhysics.UnityArtifact;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DataSakura.JitterPhysics.Samples.Editor
{
    /// <summary>
    /// Builds the sample scenes, bakes them and wires the runtime components to the artifact.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scenes are generated from code rather than shipped as <c>.unity</c> files. A committed
    /// scene is a wall of GUIDs that nobody reads in review, and it drifts from the sample scripts
    /// without anyone noticing. Generated scenes also make a point the package cares about:
    /// building the same scene twice must produce the same artifact hash, and here that is a menu
    /// item rather than a claim.
    /// </para>
    /// <para>
    /// Nothing here runs on import or on selection. Every entry writes to the project only when it
    /// is chosen from the menu.
    /// </para>
    /// </remarks>
    public static class JitterPhysicsSampleScenes
    {
        private const string MenuRoot = "Tools/DataSakura/Jitter Physics/Samples/";

        private const string BouncingBallLevelId = "sample_bouncing_ball";
        private const string ShooterLevelId = "sample_fps_shooter";

        [MenuItem(MenuRoot + "Build and bake: Bouncing Ball", false, 100)]
        public static void BuildBouncingBall()
        {
            if (!ConfirmSceneChange())
            {
                return;
            }

            Scene scene = NewScene();
            Transform geometry = BuildBouncingBallGeometry();
            JitterPhysicsLevel level = CreateLevel(BouncingBallLevelId, geometry, CreateProfile());

            if (!BakeAndSave(scene, level, "SampleBouncingBall", out JitterPhysicsArtifactAsset artifact))
            {
                return;
            }

            GameObject runtime = new GameObject("Jitter Physics Runtime");
            runtime.AddComponent<JitterPhysicsSampleWorld>();
            runtime.AddComponent<JitterPhysicsBodyViews>();
            runtime.AddComponent<JitterPhysicsBouncingBallSample>();
            runtime.AddComponent<JitterPhysicsArtifactVerificationSample>();

            AssignArtifact(runtime, artifact);
            AddCamera(new Vector3(0f, 8f, -22f), new Vector3(12f, 0f, 0f));

            SaveScene(scene, "SampleBouncingBall");
            Debug.Log("[JitterPhysics] Bouncing Ball sample is ready. Press Play, then Space to drop a ball.");
        }

        [MenuItem(MenuRoot + "Build and bake: FPS Shooter", false, 101)]
        public static void BuildFpsShooter()
        {
            if (!ConfirmSceneChange())
            {
                return;
            }

            Scene scene = NewScene();
            Transform geometry = BuildShooterGeometry();
            JitterPhysicsLevel level = CreateLevel(ShooterLevelId, geometry, CreateProfile());

            if (!BakeAndSave(scene, level, "SampleFpsShooter", out JitterPhysicsArtifactAsset artifact))
            {
                return;
            }

            GameObject runtime = new GameObject("Jitter Physics Runtime");
            runtime.AddComponent<JitterPhysicsSampleWorld>();
            runtime.AddComponent<JitterPhysicsBodyViews>();
            runtime.AddComponent<JitterPhysicsFpsShooterSample>();
            runtime.AddComponent<JitterPhysicsArtifactVerificationSample>();

            AssignArtifact(runtime, artifact);
            AddCamera(new Vector3(0f, 2f, -18f), Vector3.zero);

            SaveScene(scene, "SampleFpsShooter");
            Debug.Log("[JitterPhysics] FPS Shooter sample is ready. Press Play: WASD to move, LMB to fire.");
        }

        [MenuItem(MenuRoot + "Bake level in the open scene", false, 200)]
        public static void BakeOpenScene()
        {
            JitterPhysicsLevel level = FindLevel();
            if (level == null)
            {
                EditorUtility.DisplayDialog(
                    "Jitter Physics",
                    "No JitterPhysicsLevel in the open scene. Build a sample scene first.",
                    "OK");
                return;
            }

            JitterPhysicsBakeResult result = JitterPhysicsBakeCommand.Execute(level);
            Report(result);
        }

        /// <summary>
        /// Bakes the open level twice and compares the two hashes.
        /// </summary>
        /// <remarks>
        /// This is the check the whole format exists for. A baker that emitted a dictionary in hash
        /// order, or wrote a timestamp, would still produce a level that loads and plays correctly
        /// - and a client and a server built minutes apart would quietly disagree about it.
        /// </remarks>
        [MenuItem(MenuRoot + "Verify determinism: bake the open level twice", false, 201)]
        public static void VerifyDeterminism()
        {
            JitterPhysicsLevel level = FindLevel();
            if (level == null)
            {
                EditorUtility.DisplayDialog(
                    "Jitter Physics",
                    "No JitterPhysicsLevel in the open scene. Build a sample scene first.",
                    "OK");
                return;
            }

            JitterPhysicsBakeResult first = JitterPhysicsBakeCommand.Execute(level);
            if (!first.Succeeded)
            {
                Report(first);
                return;
            }

            JitterPhysicsBakeResult second = JitterPhysicsBakeCommand.Execute(level);
            if (!second.Succeeded)
            {
                Report(second);
                return;
            }

            bool identical = string.Equals(
                first.Output.ArtifactHash, second.Output.ArtifactHash, System.StringComparison.Ordinal);

            string message = identical
                ? $"Both bakes produced the same artifact:\n{first.Output.ArtifactHash}"
                : "The two bakes DIFFER, which means the bake is not deterministic:\n"
                  + $"first  {first.Output.ArtifactHash}\nsecond {second.Output.ArtifactHash}";

            if (identical)
            {
                Debug.Log($"[JitterPhysics] determinism check passed. {message}");
            }
            else
            {
                Debug.LogError($"[JitterPhysics] determinism check FAILED. {message}");
            }

            EditorUtility.DisplayDialog("Jitter Physics determinism", message, "OK");
        }

        [MenuItem(MenuRoot + "Validate level in the open scene", false, 202)]
        public static void ValidateOpenScene()
        {
            JitterPhysicsLevel level = FindLevel();
            if (level == null)
            {
                EditorUtility.DisplayDialog(
                    "Jitter Physics",
                    "No JitterPhysicsLevel in the open scene. Build a sample scene first.",
                    "OK");
                return;
            }

            JitterPhysicsBuildResult result = JitterPhysicsBakeCommand.Validate(level);
            string text = result.Issues == null || result.Issues.Issues.Count == 0
                ? "No issues found; this level is ready to bake."
                : Describe(result.Issues);

            Debug.Log($"[JitterPhysics] validation: {(result.Succeeded ? "ok" : "blocked")}\n{text}");
            EditorUtility.DisplayDialog("Jitter Physics validation", text, "OK");
        }

        private static Transform BuildBouncingBallGeometry()
        {
            var root = new GameObject("Baked Geometry").transform;

            // A floor to land on, a ramp to roll down and a bowl of walls so nothing escapes the
            // camera. Between them they cover the three collider conversions a level usually needs.
            AddBox(root, "Floor", new Vector3(0f, -0.5f, 0f), Quaternion.identity, new Vector3(40f, 1f, 40f));
            AddBox(root, "Ramp", new Vector3(-8f, 2f, 0f), Quaternion.Euler(0f, 0f, -22f), new Vector3(14f, 0.6f, 12f));
            AddBox(root, "Step", new Vector3(7f, 0.75f, 3f), Quaternion.identity, new Vector3(6f, 1.5f, 6f));

            AddBox(root, "Wall North", new Vector3(0f, 2f, 20f), Quaternion.identity, new Vector3(40f, 4f, 1f));
            AddBox(root, "Wall South", new Vector3(0f, 2f, -20f), Quaternion.identity, new Vector3(40f, 4f, 1f));
            AddBox(root, "Wall East", new Vector3(20f, 2f, 0f), Quaternion.identity, new Vector3(1f, 4f, 40f));
            AddBox(root, "Wall West", new Vector3(-20f, 2f, 0f), Quaternion.identity, new Vector3(1f, 4f, 40f));

            AddSphere(root, "Boulder", new Vector3(10f, 1.5f, -8f), 1.5f);
            AddCapsule(root, "Pillar", new Vector3(4f, 2f, 10f), 0.6f, 4f);

            MarkAsSources(root);
            return root;
        }

        private static Transform BuildShooterGeometry()
        {
            var root = new GameObject("Baked Geometry").transform;

            AddBox(root, "Floor", new Vector3(0f, -0.5f, 0f), Quaternion.identity, new Vector3(60f, 1f, 60f));

            AddBox(root, "Wall North", new Vector3(0f, 3f, 30f), Quaternion.identity, new Vector3(60f, 6f, 1f));
            AddBox(root, "Wall South", new Vector3(0f, 3f, -30f), Quaternion.identity, new Vector3(60f, 6f, 1f));
            AddBox(root, "Wall East", new Vector3(30f, 3f, 0f), Quaternion.identity, new Vector3(1f, 6f, 60f));
            AddBox(root, "Wall West", new Vector3(-30f, 3f, 0f), Quaternion.identity, new Vector3(1f, 6f, 60f));

            // Cover to shoot at and hide behind, at heights that make the difference between a
            // hitscan that clears an obstacle and one that does not.
            for (int i = 0; i < 5; i++)
            {
                float x = -16f + i * 8f;
                AddBox(root, $"Cover {i}", new Vector3(x, 0.75f, 4f + (i % 2) * 6f),
                    Quaternion.Euler(0f, i * 15f, 0f), new Vector3(3f, 1.5f, 1f));
            }

            AddBox(root, "Platform", new Vector3(12f, 1.5f, -12f), Quaternion.identity, new Vector3(8f, 3f, 8f));
            AddBox(root, "Ramp", new Vector3(5f, 0.9f, -12f), Quaternion.Euler(0f, 0f, -14f), new Vector3(9f, 0.5f, 8f));

            AddCapsule(root, "Column A", new Vector3(-10f, 3f, -8f), 0.7f, 6f);
            AddCapsule(root, "Column B", new Vector3(10f, 3f, 12f), 0.7f, 6f);
            AddSphere(root, "Dome", new Vector3(-16f, 1.2f, 14f), 2.4f);

            MarkAsSources(root);
            return root;
        }

        private static void MarkAsSources(Transform root)
        {
            // Only components that carry a source are baked. Marking each child, rather than the
            // root alone, keeps one collider per body and makes the source ids read like the scene.
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                var source = child.gameObject.AddComponent<JitterStaticBodySource>();

                var serialized = new SerializedObject(source);
                serialized.FindProperty("sourceId").stringValue = Sanitize(child.name);
                serialized.FindProperty("includeChildren").boolValue = true;
                serialized.FindProperty("friction").floatValue = 0.4f;
                serialized.FindProperty("restitution").floatValue = 0.1f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static JitterPhysicsLevel CreateLevel(
            string levelId, Transform geometry, JitterPhysicsWorldProfile profile)
        {
            var host = new GameObject("Jitter Physics Level");
            var level = host.AddComponent<JitterPhysicsLevel>();

            var serialized = new SerializedObject(level);
            serialized.FindProperty("levelId").stringValue = levelId;
            serialized.FindProperty("geometryRoot").objectReferenceValue = geometry;
            serialized.FindProperty("worldProfile").objectReferenceValue = profile;
            serialized.FindProperty("generatedFolder").stringValue = GeneratedFolder();
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return level;
        }

        private static JitterPhysicsWorldProfile CreateProfile()
        {
            string folder = SampleRoot();
            string path = $"{folder}/SampleWorldProfile.asset";

            var existing = AssetDatabase.LoadAssetAtPath<JitterPhysicsWorldProfile>(path);
            if (existing != null)
            {
                return existing;
            }

            var profile = ScriptableObject.CreateInstance<JitterPhysicsWorldProfile>();

            var serialized = new SerializedObject(profile);
            serialized.FindProperty("gravity").vector3Value = new Vector3(0f, -9.81f, 0f);
            serialized.FindProperty("tickRate").intValue = 60;
            serialized.FindProperty("substepCount").intValue = 1;
            serialized.FindProperty("solverIterations").intValue = 6;
            serialized.FindProperty("relaxationIterations").intValue = 4;
            serialized.FindProperty("allowDeactivation").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }

        private static bool BakeAndSave(
            Scene scene, JitterPhysicsLevel level, string sceneName, out JitterPhysicsArtifactAsset artifact)
        {
            artifact = null;

            // Saved before baking, because the bake reads the scene from disk-backed asset paths
            // and a never-saved scene has none.
            SaveScene(scene, sceneName);

            JitterPhysicsBakeResult result = JitterPhysicsBakeCommand.Execute(level);
            Report(result);

            if (!result.Succeeded)
            {
                return false;
            }

            artifact = AssetDatabase.LoadAssetAtPath<JitterPhysicsArtifactAsset>(result.Output.AssetPath);

            if (artifact == null)
            {
                Debug.LogError(
                    $"[JitterPhysics] the bake reported success but no artifact asset is at "
                    + $"'{result.Output.AssetPath}'.");
                return false;
            }

            return true;
        }

        private static void AssignArtifact(GameObject runtime, JitterPhysicsArtifactAsset artifact)
        {
            foreach (MonoBehaviour behaviour in runtime.GetComponents<MonoBehaviour>())
            {
                var serialized = new SerializedObject(behaviour);
                SerializedProperty property = serialized.FindProperty("artifact");

                if (property != null)
                {
                    property.objectReferenceValue = artifact;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        private static void AddCamera(Vector3 position, Vector3 eulerAngles)
        {
            var camera = new GameObject("Main Camera", typeof(Camera));
            camera.tag = "MainCamera";
            camera.transform.SetPositionAndRotation(position, Quaternion.Euler(eulerAngles));

            var light = new GameObject("Directional Light", typeof(Light));
            light.GetComponent<Light>().type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static GameObject AddPrimitive(
            Transform parent, PrimitiveType type, string name, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            GameObject instance = GameObject.CreatePrimitive(type);
            instance.name = name;
            instance.transform.SetParent(parent, worldPositionStays: false);
            instance.transform.SetLocalPositionAndRotation(position, rotation);
            instance.transform.localScale = scale;
            return instance;
        }

        private static void AddBox(Transform parent, string name, Vector3 position, Quaternion rotation, Vector3 size) =>
            AddPrimitive(parent, PrimitiveType.Cube, name, position, rotation, size);

        private static void AddSphere(Transform parent, string name, Vector3 position, float radius) =>
            AddPrimitive(parent, PrimitiveType.Sphere, name, position, Quaternion.identity, Vector3.one * (radius * 2f));

        private static void AddCapsule(Transform parent, string name, Vector3 position, float radius, float height) =>
            AddPrimitive(parent, PrimitiveType.Capsule, name, position, Quaternion.identity,
                new Vector3(radius * 2f, height * 0.5f, radius * 2f));

        private static Scene NewScene() =>
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        private static void SaveScene(Scene scene, string sceneName)
        {
            string folder = $"{SampleRoot()}/Scenes";
            EnsureFolder(folder);
            EditorSceneManager.SaveScene(scene, $"{folder}/{sceneName}.unity");
        }

        private static bool ConfirmSceneChange() =>
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        private static JitterPhysicsLevel FindLevel() =>
            Object.FindFirstObjectByType<JitterPhysicsLevel>();

        private static void Report(JitterPhysicsBakeResult result)
        {
            string issues = Describe(result.Issues);

            if (result.Succeeded)
            {
                Debug.Log(
                    $"[JitterPhysics] baked '{result.Output.Manifest.LevelId}' "
                    + $"hash={result.Output.ArtifactHash} bytes={result.Output.PayloadSize} "
                    + $"bodies={result.Output.Manifest.BodyCount} shapes={result.Output.Manifest.ShapeCount} "
                    + $"triangles={result.Output.Manifest.TriangleCount}\n{issues}");
                return;
            }

            Debug.LogError($"[JitterPhysics] bake did not complete.\n{issues}");
        }

        private static string Describe(JitterPhysicsIssueLog log)
        {
            if (log == null || log.Issues.Count == 0)
            {
                return "no issues";
            }

            var lines = new List<string>(log.Issues.Count);

            for (int i = 0; i < log.Issues.Count; i++)
            {
                JitterPhysicsIssue issue = log.Issues[i];
                lines.Add($"  [{(issue.IsError ? "error" : "warning")}] {issue.Message}");
            }

            return string.Join("\n", lines);
        }

        private static string GeneratedFolder()
        {
            string folder = $"{SampleRoot()}/Generated";
            EnsureFolder(folder);
            return folder;
        }

        /// <summary>
        /// The folder the samples were installed into, found from this script rather than hardcoded.
        /// </summary>
        /// <remarks>
        /// The installer lets the target folder be chosen, so a fixed path would work only for
        /// whoever accepted the default.
        /// </remarks>
        private static string SampleRoot()
        {
            string[] guids = AssetDatabase.FindAssets($"{nameof(JitterPhysicsSampleScenes)} t:MonoScript");

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);

                if (path.EndsWith($"/{nameof(JitterPhysicsSampleScenes)}.cs", System.StringComparison.Ordinal))
                {
                    // <root>/Editor/JitterPhysicsSampleScenes.cs -> <root>
                    return Path.GetDirectoryName(Path.GetDirectoryName(path)).Replace('\\', '/');
                }
            }

            return "Assets";
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        private static string Sanitize(string value)
        {
            var text = new System.Text.StringBuilder(value.Length);

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                text.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_');
            }

            return text.ToString();
        }
    }
}



