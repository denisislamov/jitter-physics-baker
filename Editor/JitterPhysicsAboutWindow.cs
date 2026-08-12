using System.Collections.Generic;
using System.Text;
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Editor.Bootstrap;
using UnityEditor;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor
{
    /// <summary>
    /// The <c>About</c> surface of the package: versions, the state of the always-compiled
    /// assemblies and whether a Jitter2 core is present in this project.
    /// <para>
    /// It exists from the very first version because "does the package import cleanly into a
    /// project that has no Jitter2 yet" is a shipping requirement, and this window is how a
    /// human answers that question without reading the console.
    /// </para>
    /// </summary>
    public sealed class JitterPhysicsAboutWindow : EditorWindow
    {
        private const string MenuPath = JitterPhysicsAuthoringConstants.EditorMenuRoot + "About";

        private Vector2 scroll;

        [MenuItem(MenuPath, false, 200)]
        private static void Open()
        {
            var window = GetWindow<JitterPhysicsAboutWindow>(true, "Jitter Physics — About", true);
            window.minSize = new Vector2(460f, 320f);
            window.Show();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField(JitterPhysicsPackage.DisplayName, EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(
                JitterPhysicsPackage.PackageName + " " + JitterPhysicsPackage.PackageVersion,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.LabelField(
                "Artifact schema version",
                JitterPhysicsPackage.ArtifactSchemaVersion.ToString());

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Package assemblies", EditorStyles.boldLabel);
            IReadOnlyList<JitterPhysicsAssemblyInfo> bootstrap =
                JitterPhysicsAssemblyProbe.ProbeBootstrapAssemblies();
            for (int i = 0; i < bootstrap.Count; i++)
            {
                DrawAssembly(bootstrap[i]);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Jitter2", EditorStyles.boldLabel);
            JitterPhysicsAssemblyInfo jitter = JitterPhysicsAssemblyProbe.ProbeJitter();
            DrawAssembly(jitter);
            DrawJitterHint(jitter);

            EditorGUILayout.EndScrollView();
        }

        private static void DrawAssembly(JitterPhysicsAssemblyInfo assembly)
        {
            string state = assembly.Exists ? "compiled" : "not present";
            if (assembly.IsDuplicated)
            {
                state += " (DUPLICATE)";
            }

            EditorGUILayout.LabelField(assembly.Name, state);
            for (int i = 0; i < assembly.DefinitionPaths.Count; i++)
            {
                EditorGUILayout.SelectableLabel(
                    "    " + assembly.DefinitionPaths[i],
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        private static void DrawJitterHint(JitterPhysicsAssemblyInfo jitter)
        {
            if (jitter.IsDuplicated)
            {
                var message = new StringBuilder();
                message.AppendLine(
                    "More than one assembly definition declares 'Jitter2.Core'. Two compiled "
                    + "copies of Jitter2 cannot coexist: duplicate types break every consumer.");
                message.Append("Remove one of the copies listed above before installing anything.");
                EditorGUILayout.HelpBox(message.ToString(), MessageType.Error);
                return;
            }

            if (!jitter.Exists)
            {
                EditorGUILayout.HelpBox(
                    "This project has no 'Jitter2.Core' assembly. That is a valid state: the "
                    + "package compiles without Jitter and never installs anything implicitly. "
                    + "Baking and world building become available after Jitter2 is installed "
                    + "or provided by the project.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(
                "A 'Jitter2.Core' assembly is present. The package references it by assembly "
                + "name and will not copy, move or modify it.",
                MessageType.Info);
        }
    }
}
