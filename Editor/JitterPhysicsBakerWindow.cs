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
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
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
            Overview = 0,
            Sources,
            Bake,
            Tools,
            Setup,
            Artifacts,
        }

        private static readonly string[] TabNames =
        {
            "Overview",
            "Sources",
            "Bake",
            "Tools",
            "Setup",
            "Artifacts",
        };

        [SerializeField]
        private Tab tab;
        [SerializeField]
        private Vector2 scroll;

        [SerializeField]
        private JitterPhysicsLevel level;
        private JitterPhysicsIssueLog issues;
        private JitterPhysicsIssueLog validationIssues;
        private DateTime validationTime;
        private bool validationStale;
        private string bakeSummary;
        private bool lastActionFailed;

        private JitterPhysicsArtifactAsset[] artifacts = Array.Empty<JitterPhysicsArtifactAsset>();
        private int selectedArtifact = -1;
        private string exportNamespace = JitterPhysicsExportDefaults.GeneratedNamespace;

        private string diagnosticsReport;

        /// <summary>Opens the package's main authoring window.</summary>
        [MenuItem(MenuPath, false, 1)]
        public static void Open()
        {
            var window = GetWindow<JitterPhysicsBakerWindow>();
            window.titleContent = new GUIContent("Jitter Physics");
            window.minSize = new Vector2(560f, 440f);
            window.TrySelectLevelFromContext();
            window.RefreshArtifacts();
            window.Show();
        }

        /// <summary>Opens the main authoring window on the setup tab.</summary>
        public static void OpenSetupTab()
        {
            Open();
            GetWindow<JitterPhysicsBakerWindow>().tab = Tab.Setup;
        }

        /// <summary>Opens the main authoring window on the artifacts tab.</summary>
        public static void OpenArtifactsTab()
        {
            Open();
            GetWindow<JitterPhysicsBakerWindow>().tab = Tab.Artifacts;
        }

        private void OnEnable()
        {
            Selection.selectionChanged += OnSelectionChanged;
            TrySelectLevelFromContext();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawStatusBar();

            DrawTabSelector();
            EditorGUILayout.Space(6f);

            bool levelRequired = tab == Tab.Overview
                                 || tab == Tab.Sources
                                 || tab == Tab.Bake
                                 || tab == Tab.Tools;
            if (levelRequired)
            {
                DrawLevelSelector();
                if (level == null)
                {
                    DrawEmptyState();
                    return;
                }
            }

            using var scope = new EditorGUILayout.ScrollViewScope(scroll);
            scroll = scope.scrollPosition;

            switch (tab)
            {
                case Tab.Sources:
                    DrawSourcesTab();
                    break;

                case Tab.Bake:
                    DrawBakeTab();
                    break;

                case Tab.Tools:
                    DrawDiagnosticsTab();
                    break;

                case Tab.Setup:
                    DrawSetupTab();
                    break;

                case Tab.Artifacts:
                    DrawArtifactsTab();
                    break;

                default:
                    DrawOverviewTab();
                    break;
            }
        }

        private static void DrawHeader()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Jitter Physics Authoring", EditorStyles.largeLabel);
            EditorGUILayout.LabelField(
                "Deterministic static-geometry baking for the Unity client and .NET server.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4f);
        }

        private void DrawTabSelector()
        {
            if (EditorGUIUtility.currentViewWidth < 520f)
            {
                tab = (Tab)EditorGUILayout.Popup("Section", (int)tab, TabNames);
                return;
            }

            tab = (Tab)GUILayout.Toolbar((int)tab, TabNames);
        }

        /// <summary>
        /// Draws only the cached result of an explicit validation. Merely opening or repainting
        /// the window never scans geometry or changes the project.
        /// </summary>
        private void DrawStatusBar()
        {
            bool compact = EditorGUIUtility.currentViewWidth < 360f;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var style = new GUIStyle(EditorStyles.boldLabel) { wordWrap = true };
                    EditorGUILayout.LabelField(DescribeValidationStatus(), style);
                    if (!compact)
                    {
                        GUILayout.FlexibleSpace();
                        DrawStatusValidateButton(GUILayout.Width(80f), GUILayout.Height(22f));
                    }
                }

                if (compact)
                {
                    DrawStatusValidateButton(GUILayout.Height(22f));
                }
            }
        }

        private void DrawStatusValidateButton(params GUILayoutOption[] options)
        {
            using (new EditorGUI.DisabledScope(level == null))
            {
                if (GUILayout.Button(
                        new GUIContent(
                            "Validate",
                            "A one-shot level check. Nothing runs in the background."),
                        options))
                {
                    Validate();
                }
            }
        }

        private GUIContent DescribeValidationStatus()
        {
            if (level == null)
            {
                return new GUIContent("[ ]  No level selected");
            }

            if (validationIssues == null)
            {
                return new GUIContent(
                    "[ ]  Not validated - press Validate",
                    "Validation runs manually and automatically before a bake.");
            }

            string checkedAt = validationTime == default ? string.Empty :
                "   ·   checked at " + validationTime.ToString("HH:mm:ss");
            string stale = validationStale ? "   (data changed)" : string.Empty;
            if (validationIssues.HasErrors)
            {
                return new GUIContent(
                    $"[X]  {validationIssues.ErrorCount} errors - bake blocked{checkedAt}{stale}");
            }

            if (validationIssues.WarningCount > 0)
            {
                return new GUIContent(
                    $"[!]  {validationIssues.WarningCount} warnings{checkedAt}{stale}");
            }

            return new GUIContent($"[v]  Ready to bake{checkedAt}{stale}");
        }

        private void DrawLevelSelector()
        {
            EditorGUI.BeginChangeCheck();
            JitterPhysicsLevel next = (JitterPhysicsLevel)EditorGUILayout.ObjectField(
                new GUIContent(
                    "Jitter Physics Level",
                    "The JitterPhysicsLevel in the scene edited by this window."),
                level,
                typeof(JitterPhysicsLevel),
                true);
            if (EditorGUI.EndChangeCheck())
            {
                level = next;
                ClearValidation();
            }

            EditorGUILayout.Space(4f);
        }

        private void DrawEmptyState()
        {
            EditorGUILayout.HelpBox(
                "This scene has no selected Jitter Physics Level. Create one to define explicit "
                + "static-body sources, shared world settings and the artifact output.",
                MessageType.Info);

            if (GUILayout.Button("Create Jitter Physics Level Setup", GUILayout.Height(34f)))
            {
                CreateLevelSetup();
            }

            JitterPhysicsLevel[] sceneLevels = FindSceneLevels();
            if (sceneLevels.Length == 0)
            {
                return;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Existing levels", EditorStyles.boldLabel);
            for (int i = 0; i < sceneLevels.Length; i++)
            {
                if (GUILayout.Button(sceneLevels[i].name, EditorStyles.miniButton))
                {
                    level = sceneLevels[i];
                    Selection.activeObject = level.gameObject;
                    ClearValidation();
                }
            }
        }

        // --------------------------------------------------------------- Overview

        private void DrawOverviewTab()
        {
            EditorGUILayout.LabelField("Level summary", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawLevelSummary();
            }

            EditorGUILayout.Space(8f);
            DrawLevelSettings();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
            if (validationIssues == null)
            {
                EditorGUILayout.HelpBox(
                    "The level has not been validated in this session. Validation does not run "
                    + "on selection or scene changes - press Validate, or just Bake (it validates first).",
                    MessageType.None);
            }
            else if (validationIssues.Issues.Count == 0)
            {
                EditorGUILayout.HelpBox("The authoring setup is valid for baking.", MessageType.Info);
            }
            else
            {
                DrawIssues(validationIssues);
            }

            EditorGUILayout.Space(8f);
            DrawBuildStatus();
        }

        private void DrawLevelSummary()
        {
            IReadOnlyList<JitterStaticBodySource> sources = level.CollectSources();

            DrawSummaryRow("Level ID", level.LevelId);
            DrawSummaryRow(
                "Geometry root",
                level.GeometryRoot != null ? level.GeometryRoot.name : "<the level object>");
            DrawSummaryRow(
                "World profile",
                level.WorldProfile != null ? level.WorldProfile.name : "<none — bake will refuse>");
            DrawSummaryRow("Static body sources", sources.Count.ToString());
            DrawSummaryRow("Output folder", level.GeneratedFolder);
            DrawSummaryRow(
                "Last artifact",
                string.IsNullOrEmpty(level.LastArtifactHash) ? "Not baked" : Short(level.LastArtifactHash));
        }

        private void DrawLevelSettings()
        {
            EditorGUILayout.LabelField("Level setup", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Only explicitly marked static-body sources become part of the artifact. The "
                + "same world profile and exact artifact bytes are consumed by Unity and .NET.",
                MessageType.Info);

            var serialized = new SerializedObject(level);
            serialized.Update();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serialized.FindProperty("levelId"));
            EditorGUILayout.PropertyField(serialized.FindProperty("geometryRoot"));
            EditorGUILayout.PropertyField(serialized.FindProperty("worldProfile"));
            EditorGUILayout.PropertyField(serialized.FindProperty("generatedFolder"));
            if (EditorGUI.EndChangeCheck())
            {
                serialized.ApplyModifiedProperties();
                MarkSceneChanged();
                MarkValidationStale();
            }

            if (!level.HasCanonicalLevelId)
            {
                EditorGUILayout.HelpBox(
                    "The level id is not canonical. It is part of every artifact name and of the "
                    + "handshake, so it is sanitized once and then kept.",
                    MessageType.Warning);
            }

            if (level.WorldProfile == null)
            {
                EditorGUILayout.HelpBox(
                    "No world profile is assigned. Create one with Assets/Create/Jitter Physics/World "
                    + "Profile, then assign it here. Baking stays blocked until the shared settings exist.",
                    MessageType.Warning);
            }
        }

        // ---------------------------------------------------------------- Sources

        private void DrawSourcesTab()
        {
            IReadOnlyList<JitterStaticBodySource> sources = level.CollectSources();
            EditorGUILayout.LabelField("Explicit static-body sources", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "A source groups one or more Unity colliders into one deterministic static body. "
                + "Unmarked colliders are ignored, so adding scenery cannot silently change physics.",
                MessageType.Info);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawSummaryRow("Marked sources", sources.Count.ToString());
                DrawSummaryRow(
                    "Geometry root",
                    level.GeometryRoot != null ? level.GeometryRoot.name : "Entire scene");
            }

            GameObject selected = Selection.activeGameObject;
            bool canAdd = selected != null
                          && selected.scene.IsValid()
                          && IsInsideLevelScope(selected)
                          && selected.GetComponent<JitterStaticBodySource>() == null;
            using (new EditorGUI.DisabledScope(!canAdd))
            {
                string label = selected == null
                    ? "Select a GameObject to add a source"
                    : "Add Source to " + selected.name;
                if (GUILayout.Button(label, GUILayout.Height(30f)))
                {
                    AddSource(selected);
                }
            }

            if (selected != null && selected.scene.IsValid() && !IsInsideLevelScope(selected))
            {
                EditorGUILayout.HelpBox(
                    "The selected object is outside this level's geometry root and would not be "
                    + "collected. Select an object inside the geometry root or change it on Overview.",
                    MessageType.Warning);
            }

            if (sources.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No static bodies are marked yet. Select a collider root in the Hierarchy and "
                    + "add a source, or add JitterStaticBodySource from the Inspector.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField($"Sources ({sources.Count})", EditorStyles.boldLabel);
            for (int i = 0; i < sources.Count; i++)
            {
                DrawSource(sources[i]);
            }
        }

        private void DrawSource(JitterStaticBodySource source)
        {
            if (source == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField(source, typeof(JitterStaticBodySource), true);
                    if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(54f)))
                    {
                        Selection.activeObject = source.gameObject;
                        EditorGUIUtility.PingObject(source.gameObject);
                    }

                    if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(60f)))
                    {
                        Undo.DestroyObjectImmediate(source);
                        MarkSceneChanged();
                        MarkValidationStale();
                        GUIUtility.ExitGUI();
                    }
                }

                var serialized = new SerializedObject(source);
                serialized.Update();
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(serialized.FindProperty("sourceId"));
                EditorGUILayout.PropertyField(serialized.FindProperty("includeChildren"));
                EditorGUILayout.PropertyField(serialized.FindProperty("friction"));
                EditorGUILayout.PropertyField(serialized.FindProperty("restitution"));
                if (EditorGUI.EndChangeCheck())
                {
                    serialized.ApplyModifiedProperties();
                    MarkSceneChanged();
                    MarkValidationStale();
                }
            }
        }

        private void AddSource(GameObject target)
        {
            JitterStaticBodySource source = Undo.AddComponent<JitterStaticBodySource>(target);
            EditorUtility.SetDirty(source);
            MarkSceneChanged();
            MarkValidationStale();
            Selection.activeObject = target;
        }

        private bool IsInsideLevelScope(GameObject target)
        {
            if (target == null || level == null || target.scene != level.gameObject.scene)
            {
                return false;
            }

            return level.GeometryRoot == null || target.transform.IsChildOf(level.GeometryRoot);
        }

        // ------------------------------------------------------------------ Bake

        private void DrawBakeTab()
        {
            EditorGUILayout.LabelField("World shared by client and server", EditorStyles.boldLabel);
            if (level.WorldProfile == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a Jitter Physics World Profile on the Overview tab before baking.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                UnityEditor.Editor profileEditor = UnityEditor.Editor.CreateEditor(level.WorldProfile);
                if (profileEditor != null)
                {
                    profileEditor.OnInspectorGUI();
                    DestroyImmediate(profileEditor);
                }

                if (EditorGUI.EndChangeCheck())
                {
                    MarkValidationStale();
                }
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Bake pipeline", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Bake validates first, writes one deterministic artifact, and leaves the previous "
                + "artifact untouched if anything fails.",
                MessageType.None);
            DrawBakeButtons();
            EditorGUILayout.Space(8f);
            DrawIssues(issues);

            if (!string.IsNullOrEmpty(bakeSummary))
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(bakeSummary, lastActionFailed ? MessageType.Error : MessageType.Info);
            }

            EditorGUILayout.Space(8f);
            DrawBuildStatus();
        }

        private void DrawBuildStatus()
        {
            EditorGUILayout.LabelField("Build status", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawSummaryRow(
                    "Last artifact",
                    string.IsNullOrEmpty(level.LastArtifactHash) ? "Not baked" : Short(level.LastArtifactHash));
                DrawSummaryRow("Output folder", level.GeneratedFolder);
                DrawSummaryRow("Artifacts in project", artifacts.Length.ToString());
            }

            if (string.IsNullOrEmpty(level.LastArtifactHash))
            {
                EditorGUILayout.HelpBox(
                    "This level has not produced an artifact yet. Open the Bake tab when the setup is ready.",
                    MessageType.None);
            }
        }

        private static void DrawSummaryRow(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(150f));
                EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
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
            validationIssues = result.Issues;
            validationTime = DateTime.Now;
            validationStale = false;
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
            validationIssues = result.Issues;
            validationTime = DateTime.Now;
            validationStale = false;
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

        private static void DrawIssues(JitterPhysicsIssueLog issueLog)
        {
            if (issueLog == null || issueLog.Issues.Count == 0)
            {
                return;
            }

            EditorGUILayout.LabelField(
                $"Issues ({issueLog.ErrorCount} errors, {issueLog.WarningCount} warnings)",
                EditorStyles.boldLabel);

            for (int i = 0; i < issueLog.Issues.Count; i++)
            {
                JitterPhysicsIssue issue = issueLog.Issues[i];

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

        // ------------------------------------------------------------------- Setup

        private void DrawSetupTab()
        {
            JitterPhysicsCompatibilityReport report = JitterPhysicsCompatibilityReport.Create();

            EditorGUILayout.LabelField("Jitter2 compatibility", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(report.Message, MessageTypeFor(report.Status));
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawSummaryRow("Status", report.Status.ToString());
                DrawSummaryRow("Baking allowed", report.CanBake ? "Yes" : "No");
                DrawSummaryRow("Hashed files", report.HashedFileCount.ToString());
                DrawSummaryRow(
                    "Runtime ID",
                    string.IsNullOrEmpty(report.RuntimeCompatibilityId)
                        ? "Not available"
                        : Short(report.RuntimeCompatibilityId));
            }

            EditorGUILayout.HelpBox(
                "Installation is always explicit. The package never copies, moves or edits an "
                + "external Jitter2 merely because this window was opened.",
                MessageType.None);

            if (EditorGUIUtility.currentViewWidth < 420f)
            {
                if (GUILayout.Button("Open installation details", GUILayout.Height(30f)))
                {
                    JitterPhysicsSetupWindow.OpenWindow();
                }

                if (GUILayout.Button("Copy compatibility JSON", GUILayout.Height(30f)))
                {
                    EditorGUIUtility.systemCopyBuffer = report.ToJson();
                }
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open installation details", GUILayout.Height(30f)))
                    {
                        JitterPhysicsSetupWindow.OpenWindow();
                    }

                    if (GUILayout.Button("Copy compatibility JSON", GUILayout.Height(30f)))
                    {
                        EditorGUIUtility.systemCopyBuffer = report.ToJson();
                    }
                }
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Package", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawSummaryRow("Package", JitterPhysicsPackage.PackageName);
                DrawSummaryRow("Version", JitterPhysicsPackage.PackageVersion);
                DrawSummaryRow("Artifact schema", JitterPhysicsPackage.ArtifactSchemaVersion.ToString());
            }

            if (GUILayout.Button("About package", EditorStyles.miniButton))
            {
                JitterPhysicsAboutWindow.OpenWindow();
            }
        }

        private static MessageType MessageTypeFor(JitterPhysicsCompatibilityStatus status)
        {
            switch (status)
            {
                case JitterPhysicsCompatibilityStatus.Compatible:
                case JitterPhysicsCompatibilityStatus.Missing:
                    return MessageType.Info;
                case JitterPhysicsCompatibilityStatus.Incompatible:
                case JitterPhysicsCompatibilityStatus.Duplicate:
                case JitterPhysicsCompatibilityStatus.UnsupportedPlugin:
                    return MessageType.Error;
                default:
                    return MessageType.Warning;
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

            tab = Tab.Bake;
        }

        private void VerifyArtifact(JitterPhysicsArtifactAsset asset)
        {
            PhysicsArtifactResult result = JitterPhysicsArtifactLoader.Load(asset);

            lastActionFailed = !result.Succeeded;
            bakeSummary = result.Succeeded
                ? $"'{asset.LevelId}' re-hashes and decodes cleanly: {result.Artifact.Bodies.Count} bodies, "
                    + $"{result.Artifact.ShapeCount} shapes."
                : "Artifact is not loadable: " + result.Error;

            tab = Tab.Bake;
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

        private void CreateLevelSetup()
        {
            const string settingsFolder = "Assets/JitterPhysics/Generated/Settings";
            EnsureAssetFolder(settingsFolder);

            Scene scene = SceneManager.GetActiveScene();
            string sceneName = string.IsNullOrWhiteSpace(scene.name) ? "UnsavedScene" : scene.name;
            string sceneKey = JitterPhysicsIdUtility.Sanitize(sceneName, "level");

            var root = new GameObject("Jitter Physics Level");
            Undo.RegisterCreatedObjectUndo(root, "Create Jitter Physics Level Setup");
            JitterPhysicsLevel createdLevel = Undo.AddComponent<JitterPhysicsLevel>(root);
            createdLevel.EnsureLevelId();

            string profilePath = AssetDatabase.GenerateUniqueAssetPath(
                $"{settingsFolder}/{sceneKey}_WorldProfile.asset");
            var profile = CreateInstance<JitterPhysicsWorldProfile>();
            profile.name = Path.GetFileNameWithoutExtension(profilePath);
            AssetDatabase.CreateAsset(profile, profilePath);

            var serialized = new SerializedObject(createdLevel);
            serialized.Update();
            serialized.FindProperty("worldProfile").objectReferenceValue = profile;
            serialized.ApplyModifiedProperties();

            EditorUtility.SetDirty(createdLevel);
            AssetDatabase.SaveAssets();
            level = createdLevel;
            Selection.activeGameObject = root;
            MarkSceneChanged();
            ClearValidation();
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private void TrySelectLevelFromContext()
        {
            if (Selection.activeGameObject != null)
            {
                JitterPhysicsLevel selected =
                    Selection.activeGameObject.GetComponentInParent<JitterPhysicsLevel>();
                if (selected != null)
                {
                    level = selected;
                    return;
                }
            }

            JitterPhysicsLevel[] levels = FindSceneLevels();
            if (level == null && levels.Length == 1)
            {
                level = levels[0];
            }
        }

        private static JitterPhysicsLevel[] FindSceneLevels()
        {
            JitterPhysicsLevel[] all = Resources.FindObjectsOfTypeAll<JitterPhysicsLevel>();
            var result = new List<JitterPhysicsLevel>();
            for (int i = 0; i < all.Length; i++)
            {
                JitterPhysicsLevel candidate = all[i];
                if (candidate != null
                    && !EditorUtility.IsPersistent(candidate)
                    && candidate.gameObject.scene.IsValid()
                    && candidate.gameObject.scene.isLoaded)
                {
                    result.Add(candidate);
                }
            }

            return result.ToArray();
        }

        private void OnSelectionChanged()
        {
            JitterPhysicsLevel previous = level;
            TrySelectLevelFromContext();
            if (previous != level)
            {
                ClearValidation();
            }

            Repaint();
        }

        private void ClearValidation()
        {
            validationIssues = null;
            issues = null;
            validationTime = default;
            validationStale = false;
            bakeSummary = string.Empty;
        }

        private void MarkValidationStale()
        {
            validationStale = validationIssues != null;
        }

        private void MarkSceneChanged()
        {
            if (level != null && level.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(level.gameObject.scene);
            }
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
