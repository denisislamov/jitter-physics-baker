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
    /// package release, why an operation is blocked, and the installation actions.
    /// <para>
    /// The report itself only reads. Every action that changes the project is a button below it
    /// and nothing runs on import, on selection or on opening the window: a tool that mutates a
    /// project while it is being looked at is how a consumer's own Jitter2 copy gets overwritten
    /// by accident.
    /// </para>
    /// </summary>
    public sealed class JitterPhysicsSetupWindow : EditorWindow
    {
        private const string MenuPath = JitterPhysicsAuthoringConstants.EditorMenuRoot + "Setup";

        private JitterPhysicsCompatibilityReport report;
        private Vector2 scroll;
        private string installLog;
        private bool snapshotSupportsUnity;

        [MenuItem(MenuPath, false, 100)]
        private static void OpenFromMenu()
        {
            JitterPhysicsBakerWindow.OpenSetupTab();
        }

        /// <summary>Opens the detailed installation and compatibility report.</summary>
        internal static void OpenWindow()
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
            snapshotSupportsUnity = ReadSnapshotSupportsUnity();
        }

        /// <summary>
        /// Whether the shipped snapshot could be compiled by Unity if it were installed. Read
        /// here so the button can be disabled with a reason, rather than letting the user find
        /// out from a project that no longer compiles.
        /// </summary>
        private static bool ReadSnapshotSupportsUnity()
        {
            string packageRoot = JitterPhysicsCompatibilityReport.ResolvePackageRootPath();
            if (string.IsNullOrEmpty(packageRoot))
            {
                return false;
            }

            try
            {
                return JitterPhysicsLock.Load(packageRoot).SupportsUnity;
            }
            catch (System.Exception)
            {
                // An unreadable lock is already reported by the compatibility report above; for
                // the button the safe reading is "do not offer an install we cannot vouch for".
                return false;
            }
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

            DrawInstallActions();

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// The actions that change the project. They live below the read-only report on purpose:
        /// the window is opened to find out what is wrong far more often than to install
        /// anything, and an install button under the cursor is an install button somebody presses
        /// by accident.
        /// </summary>
        private void DrawInstallActions()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Installation", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Nothing here runs on import or on selection. An external Jitter2 is never copied, "
                + "moved or edited, and package-owned files that were modified locally are reported "
                + "instead of overwritten.",
                MessageType.Info);

            if (!snapshotSupportsUnity)
            {
                EditorGUILayout.HelpBox(
                    "\"Install Jitter2\" is unavailable in this package release: its compile "
                    + "profile does not declare a Unity-compatible build, so there is no assembly "
                    + "to install. Add a Jitter2 to the project yourself — baking uses whichever "
                    + "copy is present.",
                    MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate installation"))
                {
                    Run(Install.JitterPhysicsInstaller.Validate());
                }

                using (new EditorGUI.DisabledScope(
                    !snapshotSupportsUnity
                    || report.Status != JitterPhysicsCompatibilityStatus.Missing))
                {
                    if (GUILayout.Button("Install Jitter2"))
                    {
                        Run(Install.JitterPhysicsInstaller.InstallJitter());
                    }
                }

                using (new EditorGUI.DisabledScope(
                    report.Status == JitterPhysicsCompatibilityStatus.Missing))
                {
                    if (GUILayout.Button("Install/update integration"))
                    {
                        Run(Install.JitterPhysicsInstaller.InstallIntegration());
                    }
                }

                // Last, and after the integration button, because that is the order the samples
                // depend on: they reference the adapter by name and cannot compile without it.
                using (new EditorGUI.DisabledScope(
                    report.Status == JitterPhysicsCompatibilityStatus.Missing))
                {
                    if (GUILayout.Button("Install/update samples"))
                    {
                        Run(Install.JitterPhysicsInstaller.InstallSamples());
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Install server runtime sources..."))
                {
                    string folder = EditorUtility.SaveFolderPanel(
                        "Install server runtime sources into", string.Empty, "JitterPhysics");

                    if (!string.IsNullOrEmpty(folder))
                    {
                        Run(Install.JitterPhysicsServerProjection.Install(folder));
                    }
                }

                if (GUILayout.Button("Remove package-owned installation"))
                {
                    if (EditorUtility.DisplayDialog(
                        "Remove installation",
                        "Files this package installed and that have not been modified since will be "
                        + "deleted. Anything you changed is kept.",
                        "Remove",
                        "Cancel"))
                    {
                        // One combined operation, one log: two separate uninstalls would leave the
                        // second, empty report on screen and scroll the "kept a modified file"
                        // warning out of sight.
                        Run(Install.JitterPhysicsInstaller.UninstallAll(
                            Install.JitterPhysicsComponentIds.Integration,
                            Install.JitterPhysicsComponentIds.Jitter));
                    }
                }
            }

            if (!string.IsNullOrEmpty(installLog))
            {
                EditorGUILayout.SelectableLabel(
                    installLog, EditorStyles.textArea, GUILayout.MinHeight(80f));
            }
        }

        private void Run(Install.JitterPhysicsInstallResult result)
        {
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < result.Issues.Issues.Count; i++)
            {
                Baking.JitterPhysicsIssue issue = result.Issues.Issues[i];
                builder.Append(issue.IsError ? "ERROR  " : "note   ").Append(issue.Message).Append('\n');
            }

            if (result.Succeeded && result.Files.Count > 0)
            {
                builder.Append(result.Files.Count).Append(" file(s):\n");
                for (int i = 0; i < result.Files.Count; i++)
                {
                    builder.Append("  ").Append(result.Files[i]).Append('\n');
                }
            }

            installLog = builder.ToString();
            Refresh();
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








