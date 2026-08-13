using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Editor.Baking;
using DataSakura.JitterPhysics.Editor.Bootstrap;
using DataSakura.JitterPhysics.Editor.Export;
using DataSakura.JitterPhysics.UnityArtifact;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DataSakura.JitterPhysics.Editor
{
    /// <summary>
    /// The window an author works in: pick a level, see what is wrong with it, bake it, and
    /// look at what came out.
    /// <para>
    /// It is deliberately built out of the same commands a script would call —
    /// <see cref="JitterPhysicsBakeCommand"/>, <see cref="JitterPhysicsArtifactExporter"/> — and
    /// contains no baking logic of its own. A window that reimplements part of the pipeline is
    /// a second pipeline, and the second one is the one nobody tests.
    /// </para>
    /// <para>
    /// Every mutation is an explicit button. Nothing is baked, written or deleted because a tab
    /// was opened or a selection changed: an editor tool that writes while being looked at is
    /// how a project loses work it never asked to change.
    /// </para>
    /// </summary>
    public sealed class JitterPhysicsBakerWindow : EditorWindow
    {
        private const string MenuPath = JitterPhysicsAuthoringConstants.EditorMenuRoot + "Physics Baker";

        private enum Tab
        {
            Level = 0,
            Artifacts,
            Diagnostics,
        }

        private static readonly string[] TabNames = { "Level && Bake", "Artifacts", "Diagnostics" };

        private Tab tab;
        private Vector2 scroll;

        private JitterPhysicsLevel level;
        private JitterPhysicsIssueLog issues;
        private string bakeSummary;
        private bool lastActionFailed;

        private JitterPhysicsArtifactAsset[] artifacts = Array.Empty<JitterPhysicsArtifactAsset>();
        private int selectedArtifact = -1;
        private string exportNamespace = JitterPhysicsExportDefaults.GeneratedNamespace;

        private string diagnosticsReport;

        [MenuItem(MenuPath, false, 1)]
        private static void Open()
        {
            var window = GetWindow<JitterPhysicsBakerWindow>(false, "Jitter Physics — Baker", true);
            window.minSize = new Vector2(560f, 420f);
            window.FindLevel();
            window.RefreshArtifacts();
            window.Show();
        }

        private void OnGUI()
        {
            tab = (Tab)GUILayout.Toolbar((int)tab, TabNames);
            EditorGUILayout.Space();

            using var scope = new EditorGUILayout.ScrollViewScope(scroll);
            scroll = scope.scrollPosition;

            switch (tab)
            {
                case Tab.Level:
                    DrawLevelTab();
                    break;

                case Tab.Artifacts:
                    DrawArtifactsTab();
                    break;

                case Tab.Diagnostics:
                    DrawDiagnosticsTab();
                    break;
            }
        }

        // ---------------------------------------------------------------- Level & Bake

        private void DrawLevelTab()
        {
            EditorGUILayout.LabelField("Level", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                level = (JitterPhysicsLevel)EditorGUILayout.ObjectField(
                    "Level", level, typeof(JitterPhysicsLevel), true);

                if (GUILayout.Button("Find in scene", GUILayout.Width(110f)))
                {
                    FindLevel();
                }
            }

            if (level == null)
            {
                EditorGUILayout.HelpBox(
                    "No JitterPhysicsLevel selected. Add one to the scene that owns the static "
                    + "geometry, or drag it in here.",
                    MessageType.Info);
                return;
            }

            DrawLevelSummary();
            EditorGUILayout.Space();
            DrawBakeButtons();
            EditorGUILayout.Space();
            DrawIssues();

            if (!string.IsNullOrEmpty(bakeSummary))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(bakeSummary, lastActionFailed ? MessageType.Error : MessageType.Info);
            }
        }

        private void DrawLevelSummary()
        {
            IReadOnlyList<JitterStaticBodySource> sources = level.CollectSources();

            EditorGUILayout.LabelField("Level id", level.LevelId);
            EditorGUILayout.LabelField(
                "Geometry root",
                level.GeometryRoot != null ? level.GeometryRoot.name : "<the level object>");
            EditorGUILayout.LabelField(
                "World profile",
                level.WorldProfile != null ? level.WorldProfile.name : "<none — bake will refuse>");
            EditorGUILayout.LabelField("Marked sources", sources.Count.ToString());
            EditorGUILayout.LabelField("Output folder", level.GeneratedFolder);

            if (!level.HasCanonicalLevelId)
            {
                EditorGUILayout.HelpBox(
                    "The level id is not canonical. It is part of every artifact name and of the "
                    + "handshake, so it is sanitized once and then kept.",
                    MessageType.Warning);
            }

            if (sources.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Nothing is marked for baking. Static geometry is collected only from objects "
                    + "carrying JitterStaticBodySource, never from every collider in the scene: "
                    + "otherwise adding scenery would silently change the level.",
                    MessageType.Warning);
            }
        }

        private void DrawBakeButtons()
        {
            JitterPhysicsCompatibilityReport report = JitterPhysicsCompatibilityReport.Create();

            if (!report.CanBake)
            {
                EditorGUILayout.HelpBox(
                    "Baking is blocked: " + report.Message
                    + "\n\nValidation still works, and its findings are worth fixing first.",
                    MessageType.Error);
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox(
                    "Play Mode: the scene state belongs to the simulation, not to the author, so "
                    + "baking is refused. Validation is still available.",
                    MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate"))
                {
                    Validate();
                }

                using (new EditorGUI.DisabledScope(
                    !report.CanBake || EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button("Validate and bake"))
                    {
                        Bake();
                    }
                }
            }
        }

        private void Validate()
        {
            var stopwatch = Stopwatch.StartNew();
            JitterPhysicsBuildResult result = JitterPhysicsBakeCommand.Validate(level);
            stopwatch.Stop();

            issues = result.Issues;
            lastActionFailed = result.Issues.HasErrors;
            bakeSummary = result.Issues.HasErrors
                ? $"Validation found {result.Issues.ErrorCount} error(s). Nothing was written."
                : $"Ready to bake: {result.Artifact.Bodies.Count} bodies, {result.Artifact.ShapeCount} shapes, "
                    + $"{result.Artifact.TriangleCount} triangles, checked in {stopwatch.ElapsedMilliseconds} ms.";
        }

        private void Bake()
        {
            var stopwatch = Stopwatch.StartNew();
            JitterPhysicsBakeResult result = JitterPhysicsBakeCommand.Execute(level);
            stopwatch.Stop();

            issues = result.Issues;
            lastActionFailed = !result.Succeeded;

            if (!result.Succeeded)
            {
                bakeSummary = "Bake failed; the previously baked artifact was left untouched.";
                return;
            }

            JitterPhysicsBakeOutput output = result.Output;
            bakeSummary =
                $"Baked '{output.Manifest.LevelId}'\n"
                + $"bodies {output.Manifest.BodyCount}, shapes {output.Manifest.ShapeCount}, "
                + $"triangles {output.Manifest.TriangleCount}\n"
                + $"{output.PayloadSize} bytes in {stopwatch.ElapsedMilliseconds} ms\n"
                + $"hash {output.ArtifactHash}\n"
                + output.AssetPath;

            RefreshArtifacts();
        }

        private void DrawIssues()
        {
            if (issues == null || issues.Issues.Count == 0)
            {
                return;
            }

            EditorGUILayout.LabelField(
                $"Issues ({issues.ErrorCount} errors, {issues.WarningCount} warnings)",
                EditorStyles.boldLabel);

            for (int i = 0; i < issues.Issues.Count; i++)
            {
                JitterPhysicsIssue issue = issues.Issues[i];

                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(
                        (issue.IsError ? "ERROR  " : "warning  ") + issue.Message,
                        EditorStyles.wordWrappedLabel);

                    // The object that caused the problem, not just its name: a hierarchy path in
                    // a log is something an author has to go and find by hand.
                    using (new EditorGUI.DisabledScope(issue.Context == null))
                    {
                        if (GUILayout.Button("Select", GUILayout.Width(60f)))
                        {
                            Selection.activeObject = issue.Context;
                            EditorGUIUtility.PingObject(issue.Context);
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------- Artifacts

        private void DrawArtifactsTab()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Baked artifacts", EditorStyles.boldLabel);
                if (GUILayout.Button("Refresh", GUILayout.Width(80f)))
                {
                    RefreshArtifacts();
                }
            }

            if (artifacts.Length == 0)
            {
                EditorGUILayout.HelpBox("No baked artifacts in this project yet.", MessageType.Info);
                return;
            }

            var names = new string[artifacts.Length];
            for (int i = 0; i < artifacts.Length; i++)
            {
                names[i] = artifacts[i] != null
                    ? artifacts[i].LevelId + "  " + artifacts[i].ShortHash
                    : "<missing>";
            }

            selectedArtifact = EditorGUILayout.Popup("Artifact", Mathf.Max(0, selectedArtifact), names);
            JitterPhysicsArtifactAsset asset = artifacts[selectedArtifact];
            if (asset == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Level id", asset.LevelId);
            EditorGUILayout.LabelField("Schema", asset.SchemaVersion.ToString());
            EditorGUILayout.LabelField("Tick rate", asset.TickRate.ToString());
            EditorGUILayout.LabelField(
                "Contents",
                $"{asset.BodyCount} bodies, {asset.ShapeCount} shapes, {asset.TriangleCount} triangles");
            EditorGUILayout.LabelField("Generated by", asset.GeneratorVersion);
            DrawSelectable("Artifact hash", asset.ArtifactHash);
            DrawSelectable("Runtime id", asset.RuntimeCompatibilityId);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select asset"))
                {
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }

                if (GUILayout.Button("Copy hash"))
                {
                    EditorGUIUtility.systemCopyBuffer = asset.ArtifactHash;
                }

                if (GUILayout.Button("Verify"))
                {
                    VerifyArtifact(asset);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Export", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Exports copy the artifact that already exists. Neither of them re-bakes: the "
                + "server has to run the exact bytes this project verified.",
                MessageType.Info);

            if (GUILayout.Button("Export payload and manifest..."))
            {
                string folder = EditorUtility.SaveFolderPanel("Export artifact to", string.Empty, string.Empty);
                if (!string.IsNullOrEmpty(folder))
                {
                    ShowExport(JitterPhysicsArtifactExporter.ExportBinary(asset, folder));
                }
            }

            exportNamespace = EditorGUILayout.TextField("Generated namespace", exportNamespace);

            if (GUILayout.Button("Export embedded provider (.g.cs)..."))
            {
                ExportEmbedded(asset);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Danger zone", EditorStyles.boldLabel);
            if (GUILayout.Button("Delete this artifact"))
            {
                DeleteArtifact(asset);
            }
        }

        private void ExportEmbedded(JitterPhysicsArtifactAsset asset)
        {
            string folder = EditorUtility.SaveFolderPanel(
                "Export generated provider to", string.Empty, string.Empty);

            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            EmbeddedArtifactSourceOptions options;
            try
            {
                options = new EmbeddedArtifactSourceOptions(
                    exportNamespace, JitterPhysicsExportDefaults.ClassNameFor(asset.LevelId));
            }
            catch (ArgumentException exception)
            {
                lastActionFailed = true;
                bakeSummary = exception.Message;
                return;
            }

            ShowExport(JitterPhysicsArtifactExporter.ExportEmbedded(asset, folder, options));
        }

        private void ShowExport(JitterPhysicsExportResult result)
        {
            issues = result.Issues;
            lastActionFailed = !result.Succeeded;
            bakeSummary = result.Succeeded
                ? "Exported:\n" + string.Join("\n", result.Files)
                : "Export failed; nothing was written.";

            for (int i = 0; i < result.Issues.Issues.Count; i++)
            {
                JitterPhysicsIssue issue = result.Issues.Issues[i];
                if (issue.IsError)
                {
                    Debug.LogError(JitterPhysicsPackage.LogPrefix + issue.Message, issue.Context);
                }
            }

            tab = Tab.Level;
        }

        private void VerifyArtifact(JitterPhysicsArtifactAsset asset)
        {
            PhysicsArtifactResult result = JitterPhysicsArtifactLoader.Load(asset);

            lastActionFailed = !result.Succeeded;
            bakeSummary = result.Succeeded
                ? $"'{asset.LevelId}' re-hashes and decodes cleanly: {result.Artifact.Bodies.Count} bodies, "
                    + $"{result.Artifact.ShapeCount} shapes."
                : "Artifact is not loadable: " + result.Error;

            tab = Tab.Level;
        }

        private void DeleteArtifact(JitterPhysicsArtifactAsset asset)
        {
            string assetPath = AssetDatabase.GetAssetPath(asset);
            string payloadPath = asset.Payload != null ? AssetDatabase.GetAssetPath(asset.Payload) : null;
            string manifestPath = null;

            if (!string.IsNullOrEmpty(payloadPath))
            {
                string folder = Path.GetDirectoryName(payloadPath);
                if (!string.IsNullOrEmpty(folder))
                {
                    manifestPath = Path.Combine(
                        folder,
                        JitterPhysicsArtifactNaming.ManifestFileName(asset.LevelId, asset.ArtifactHash))
                        .Replace('\\', '/');
                }
            }

            // Only the files of the artifact that was explicitly chosen, listed by name in the
            // prompt. "Clean up generated artifacts" is how somebody deletes the level a
            // colleague is about to ship.
            string message = "These files will be deleted:\n\n" + assetPath;
            if (!string.IsNullOrEmpty(payloadPath))
            {
                message += "\n" + payloadPath;
            }

            if (!string.IsNullOrEmpty(manifestPath))
            {
                message += "\n" + manifestPath;
            }

            if (!EditorUtility.DisplayDialog("Delete artifact", message, "Delete", "Cancel"))
            {
                return;
            }

            if (!string.IsNullOrEmpty(payloadPath))
            {
                AssetDatabase.DeleteAsset(payloadPath);
            }

            if (!string.IsNullOrEmpty(manifestPath) && File.Exists(manifestPath))
            {
                AssetDatabase.DeleteAsset(manifestPath);
            }

            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.Refresh();
            RefreshArtifacts();
        }

        // ----------------------------------------------------------------- Diagnostics

        private void DrawDiagnosticsTab()
        {
            EditorGUILayout.LabelField("Diagnostics", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "These answer the questions that otherwise get answered by starting a match: does "
                + "this project bake the same bytes twice, does an artifact decode, and is it the "
                + "artifact this build can run.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(level == null))
            {
                if (GUILayout.Button("Repeat-bake determinism check"))
                {
                    RunDeterminismCheck();
                }
            }

            using (new EditorGUI.DisabledScope(artifacts.Length == 0))
            {
                if (GUILayout.Button("Codec round-trip of every baked artifact"))
                {
                    RunRoundTripCheck();
                }

                if (GUILayout.Button("Runtime compatibility of every baked artifact"))
                {
                    RunCompatibilityCheck();
                }
            }

            if (!string.IsNullOrEmpty(diagnosticsReport))
            {
                EditorGUILayout.Space();
                EditorGUILayout.SelectableLabel(
                    diagnosticsReport,
                    EditorStyles.textArea,
                    GUILayout.MinHeight(160f));

                if (GUILayout.Button("Copy report"))
                {
                    EditorGUIUtility.systemCopyBuffer = diagnosticsReport;
                }
            }
        }

        private void RunDeterminismCheck()
        {
            JitterPhysicsCompatibilityReport report = JitterPhysicsCompatibilityReport.Create();

            JitterPhysicsBuildResult first = JitterPhysicsArtifactBuilder.Build(
                level, report.RuntimeCompatibilityId);

            JitterPhysicsBuildResult second = JitterPhysicsArtifactBuilder.Build(
                level, report.RuntimeCompatibilityId);

            if (!first.Succeeded || !second.Succeeded)
            {
                diagnosticsReport = "The level does not build, so determinism cannot be judged. "
                    + "Fix the validation errors on the first tab.";
                return;
            }

            // Compared as bytes rather than as records: byte equality is the property the whole
            // format exists to provide, and two record graphs can look equal while encoding
            // differently.
            byte[] a = PhysicsArtifactWriter.Write(first.Artifact);
            byte[] b = PhysicsArtifactWriter.Write(second.Artifact);

            string hashA = JitterPhysicsHash.Sha256Hex(a);
            string hashB = JitterPhysicsHash.Sha256Hex(b);

            diagnosticsReport = hashA == hashB
                ? $"Deterministic: two bakes of '{level.LevelId}' produced {a.Length} identical bytes, "
                    + $"hash {hashA}."
                : $"NOT deterministic: {hashA} then {hashB}. Two bakes of an unchanged scene must be "
                    + "byte-identical; this is a bug worth reporting with the scene attached.";
        }

        private void RunRoundTripCheck()
        {
            var builder = new System.Text.StringBuilder();

            for (int i = 0; i < artifacts.Length; i++)
            {
                JitterPhysicsArtifactAsset asset = artifacts[i];
                if (asset == null)
                {
                    continue;
                }

                PhysicsArtifactResult result = JitterPhysicsArtifactLoader.Load(asset);
                builder.Append(result.Succeeded ? "OK    " : "FAIL  ")
                    .Append(asset.LevelId).Append(' ').Append(asset.ShortHash);

                if (!result.Succeeded)
                {
                    builder.Append("  ").Append(result.Error);
                }
                else
                {
                    byte[] rewritten = PhysicsArtifactWriter.Write(result.Artifact);
                    string hash = JitterPhysicsHash.Sha256Hex(rewritten);
                    builder.Append(JitterPhysicsHash.HexEquals(hash, asset.ArtifactHash)
                        ? "  re-encodes identically"
                        : "  DECODES BUT RE-ENCODES DIFFERENTLY — the codec is not canonical");
                }

                builder.Append('\n');
            }

            diagnosticsReport = builder.ToString();
        }

        private void RunCompatibilityCheck()
        {
            JitterPhysicsCompatibilityReport report = JitterPhysicsCompatibilityReport.Create();
            var builder = new System.Text.StringBuilder();

            builder.Append("This build runs artifacts of runtime ")
                .Append(string.IsNullOrEmpty(report.RuntimeCompatibilityId)
                    ? "<unknown: Jitter2 is missing or incompatible>"
                    : report.RuntimeCompatibilityId)
                .Append("\n\n");

            for (int i = 0; i < artifacts.Length; i++)
            {
                JitterPhysicsArtifactAsset asset = artifacts[i];
                if (asset == null)
                {
                    continue;
                }

                bool compatible = JitterPhysicsHash.HexEquals(
                    asset.RuntimeCompatibilityId, report.RuntimeCompatibilityId);

                builder.Append(compatible ? "OK    " : "STALE ")
                    .Append(asset.LevelId).Append(' ').Append(asset.ShortHash)
                    .Append("  baked for ")
                    .Append(Short(asset.RuntimeCompatibilityId))
                    .Append('\n');
            }

            builder.Append(
                "\nA stale artifact is not corrupt — it was baked for different runtime semantics "
                + "and has to be re-baked before a client and a server can agree on it.");

            diagnosticsReport = builder.ToString();
        }

        // ---------------------------------------------------------------------- Helpers

        private void FindLevel()
        {
            level = FindFirstObjectByType<JitterPhysicsLevel>(FindObjectsInactive.Include);
        }

        private void RefreshArtifacts()
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(JitterPhysicsArtifactAsset));
            var found = new List<JitterPhysicsArtifactAsset>(guids.Length);

            for (int i = 0; i < guids.Length; i++)
            {
                var asset = AssetDatabase.LoadAssetAtPath<JitterPhysicsArtifactAsset>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));

                if (asset != null)
                {
                    found.Add(asset);
                }
            }

            found.Sort((left, right) => string.CompareOrdinal(left.LevelId, right.LevelId));
            artifacts = found.ToArray();
            selectedArtifact = artifacts.Length == 0 ? -1 : Mathf.Clamp(selectedArtifact, 0, artifacts.Length - 1);
        }

        private static void DrawSelectable(string label, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                EditorGUILayout.SelectableLabel(value, EditorStyles.textField, GUILayout.Height(18f));
            }
        }

        private static string Short(string hash)
        {
            if (string.IsNullOrEmpty(hash))
            {
                return "<none>";
            }

            return hash.Length >= JitterPhysicsArtifactNaming.ShortHashLength
                ? hash.Substring(0, JitterPhysicsArtifactNaming.ShortHashLength)
                : hash;
        }
    }
}

