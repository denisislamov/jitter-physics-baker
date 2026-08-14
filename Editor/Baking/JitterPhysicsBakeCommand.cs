using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Editor.Bootstrap;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor.Baking
{
    /// <summary>
    /// The entry point a menu item, a window or a build script calls to bake a level.
    /// <para>
    /// It exists to make one rule impossible to bypass: the runtime compatibility id always
    /// comes from the compatibility report, never from a caller. If baking took an id as an
    /// argument, some caller would eventually pass a constant to get past a red Setup window,
    /// and the resulting artifact would claim compatibility it does not have. That artifact
    /// would then be accepted by a peer and diverge silently, which is the most expensive
    /// failure this package exists to prevent.
    /// </para>
    /// </summary>
    public static class JitterPhysicsBakeCommand
    {
        /// <summary>
        /// Validates the project and the level, then bakes. Returns the bake result; the
        /// caller decides how to present it.
        /// </summary>
        public static JitterPhysicsBakeResult Execute(JitterPhysicsLevel level)
        {
            var issues = new JitterPhysicsIssueLog();

            JitterPhysicsCompatibilityReport report = JitterPhysicsCompatibilityReport.Create();
            if (!report.CanBake)
            {
                issues.Error(DescribeBlockedSetup(report), level);
                return new JitterPhysicsBakeResult(null, issues);
            }

            return JitterPhysicsBaker.Bake(level, report.RuntimeCompatibilityId);
        }

        /// <summary>
        /// Validates without writing anything, so an author can check a scene at any time.
        /// </summary>
        public static JitterPhysicsBuildResult Validate(JitterPhysicsLevel level)
        {
            JitterPhysicsCompatibilityReport report = JitterPhysicsCompatibilityReport.Create();

            // Validation still runs when the setup is wrong, because the authoring problems it
            // reports are worth seeing before the Jitter situation is resolved. The bake itself
            // remains blocked: `Build` refuses an absent runtime id.
            JitterPhysicsBuildResult result = JitterPhysicsArtifactBuilder.Build(
                level, report.RuntimeCompatibilityId);

            if (!report.CanBake)
            {
                result.Issues.Error(DescribeBlockedSetup(report), level);
            }

            return result;
        }

        /// <summary>True when the project is currently able to bake.</summary>
        public static bool CanBake => JitterPhysicsCompatibilityReport.Create().CanBake;

        private static string DescribeBlockedSetup(JitterPhysicsCompatibilityReport report)
        {
            switch (report.Status)
            {
                case JitterPhysicsCompatibilityStatus.Missing:
                    return "Baking requires a Jitter2 copy in this project. " + report.Message;

                case JitterPhysicsCompatibilityStatus.Incompatible:
                    return "The Jitter2 sources in this project do not match the ones this package "
                        + "release supports, so an artifact baked now would rebuild a different "
                        + "world on a peer. " + report.Message;

                case JitterPhysicsCompatibilityStatus.Duplicate:
                    return "More than one Jitter2.Core exists in this project, so there is no single "
                        + "set of sources to bake against. " + report.Message;

                case JitterPhysicsCompatibilityStatus.UnsupportedPlugin:
                    return "Jitter2 is present as a precompiled plugin, so its sources cannot be "
                        + "hashed and compatibility cannot be proven. " + report.Message;

                default:
                    return "Baking is blocked: " + report.Message;
            }
        }
    }

    /// <summary>Menu entries for the bake commands.</summary>
    internal static class JitterPhysicsBakeMenu
    {
        private const string BakeMenuPath = JitterPhysicsAuthoringConstants.EditorMenuRoot + "Bake Selected Level";
        private const string ValidateMenuPath =
            JitterPhysicsAuthoringConstants.EditorMenuRoot + "Validate Selected Level";

        [UnityEditor.MenuItem(BakeMenuPath, false, 10)]
        private static void BakeSelected()
        {
            JitterPhysicsLevel level = FindSelectedLevel();
            if (level == null)
            {
                return;
            }

            JitterPhysicsBakeResult result = JitterPhysicsBakeCommand.Execute(level);
            Report(result.Issues);

            if (result.Succeeded)
            {
                Debug.Log(
                    $"{Contracts.JitterPhysicsPackage.LogPrefix}Baked '{result.Output.Manifest.LevelId}': "
                    + $"{result.Output.Manifest.BodyCount} bodies, {result.Output.Manifest.ShapeCount} shapes, "
                    + $"{result.Output.PayloadSize} bytes, hash {result.Output.ArtifactHash}",
                    UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(result.Output.AssetPath));
            }
            else
            {
                Debug.LogError(
                    Contracts.JitterPhysicsPackage.LogPrefix
                    + "Bake failed; nothing was written. See the preceding [JitterPhysics] "
                    + "validation errors; click an error to select its object.",
                    level);
            }
        }

        [UnityEditor.MenuItem(ValidateMenuPath, false, 11)]
        private static void ValidateSelected()
        {
            JitterPhysicsLevel level = FindSelectedLevel();
            if (level == null)
            {
                return;
            }

            JitterPhysicsBuildResult result = JitterPhysicsBakeCommand.Validate(level);
            Report(result.Issues);

            if (!result.Issues.HasErrors)
            {
                Debug.Log(
                    Contracts.JitterPhysicsPackage.LogPrefix
                    + $"'{level.LevelId}' is ready to bake ({result.Issues.WarningCount} warnings).",
                    level);
            }
        }

        private static JitterPhysicsLevel FindSelectedLevel()
        {
            var level = UnityEditor.Selection.activeGameObject != null
                ? UnityEditor.Selection.activeGameObject.GetComponentInParent<JitterPhysicsLevel>()
                : null;

            if (level == null)
            {
                Debug.LogError(
                    Contracts.JitterPhysicsPackage.LogPrefix
                    + "Select a GameObject with a JitterPhysicsLevel component first.");
            }

            return level;
        }

        /// <summary>
        /// Logs each issue against its own object, so that clicking a message selects the
        /// collider that caused it rather than the level root.
        /// </summary>
        private static void Report(JitterPhysicsIssueLog issues)
        {
            for (int i = 0; i < issues.Issues.Count; i++)
            {
                JitterPhysicsIssue issue = issues.Issues[i];
                string message = Contracts.JitterPhysicsPackage.LogPrefix + issue;

                if (issue.IsError)
                {
                    Debug.LogError(message, issue.Context);
                }
                else
                {
                    Debug.LogWarning(message, issue.Context);
                }
            }
        }
    }
}
