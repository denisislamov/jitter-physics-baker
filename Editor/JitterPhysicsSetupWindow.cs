using System.IO;
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Editor.Bootstrap;
using UnityEditor;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor
{
    /// <summary>
    /// The <c>Setup</c> surface: which Jitter2 the project uses, whether it matches this
    /// package release, and why an operation is blocked.
    /// <para>
    /// The window only reads. Installing, updating or removing anything is a separate,
    /// explicit command, because a window that mutates a project while it is being looked at
    /// is how a consumer's own Jitter2 copy gets overwritten by accident.
    /// </para>
    /// </summary>
    public sealed class JitterPhysicsSetupWindow : EditorWindow
    {
        private const string MenuPath = JitterPhysicsAuthoringConstants.EditorMenuRoot + "Setup";

        private JitterPhysicsCompatibilityReport report;
        private Vector2 scroll;

        [MenuItem(MenuPath, false, 100)]
        private static void Open()
        {
            var window = GetWindow<JitterPhysicsSetupWindow>(false, "Jitter Physics — Setup", true);
            window.minSize = new Vector2(520f, 360f);
            window.Refresh();
            window.Show();
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void Refresh()
        {
            report = JitterPhysicsCompatibilityReport.Create();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("Jitter2 compatibility", EditorStyles.boldLabel);

            if (report == null)
            {
                EditorGUILayout.HelpBox("No report yet.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.HelpBox(report.Message, MessageTypeFor(report.Status));

            EditorGUILayout.LabelField("Status", report.Status.ToString());
            EditorGUILayout.LabelField("Baking allowed", report.CanBake ? "yes" : "no");
            DrawSelectable("Expected source hash", report.ExpectedSourceHash);
            DrawSelectable("Actual source hash", report.ActualSourceHash);
            DrawSelectable("Compile profile id", report.CompileProfileId);
            DrawSelectable("Runtime compatibility id", report.RuntimeCompatibilityId);
            EditorGUILayout.LabelField("Hashed files", report.HashedFileCount.ToString());

            if (report.LockIsPlaceholder)
            {
                EditorGUILayout.HelpBox(
                    "jitter2.lock.json still holds a placeholder hash: the dormant snapshot in "
                    + "Jitter2~/Runtime has not been synced yet. Run tools~/hash-jitter2.py after "
                    + "syncing, otherwise no external Jitter2 can ever be reported as compatible.",
                    MessageType.Warning);
            }

            if (report.JitterDefinitionPaths.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Assembly definitions", EditorStyles.boldLabel);
                for (int i = 0; i < report.JitterDefinitionPaths.Count; i++)
                {
                    EditorGUILayout.SelectableLabel(
                        report.JitterDefinitionPaths[i],
                        GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh"))
                {
                    Refresh();
                }

                if (GUILayout.Button("Copy report JSON"))
                {
                    EditorGUIUtility.systemCopyBuffer = report.ToJson();
                }

                if (GUILayout.Button("Export report..."))
                {
                    ExportReport();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void ExportReport()
        {
            string path = EditorUtility.SaveFilePanel(
                "Export compatibility report",
                string.Empty,
                "jitter-physics-compatibility.json",
                "json");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            File.WriteAllText(path, report.ToJson());
            Debug.Log(JitterPhysicsPackage.LogPrefix + "Compatibility report written to " + path);
        }

        private static void DrawSelectable(string label, string value)
        {
            EditorGUILayout.LabelField(label, string.IsNullOrEmpty(value) ? "—" : string.Empty);
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            EditorGUILayout.SelectableLabel(value, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }

        private static MessageType MessageTypeFor(JitterPhysicsCompatibilityStatus status)
        {
            switch (status)
            {
                case JitterPhysicsCompatibilityStatus.Compatible:
                    return MessageType.Info;
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
    }
}

