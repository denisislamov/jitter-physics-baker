using System.Collections.Generic;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor.Baking
{
    /// <summary>How badly a validation issue affects the bake.</summary>
    public enum JitterPhysicsIssueSeverity
    {
        /// <summary>The bake continues; the result may differ from what the author expected.</summary>
        Warning = 0,

        /// <summary>The bake is refused.</summary>
        Error,
    }

    /// <summary>
    /// One problem found while validating or baking, with enough context to act on it.
    /// <para>
    /// The offending object is carried along so that the editor can select and ping it. A
    /// validation message that only names a problem, without saying which object has it,
    /// is close to useless in a level with hundreds of colliders.
    /// </para>
    /// </summary>
    public sealed class JitterPhysicsIssue
    {
        /// <summary>Severity of the issue.</summary>
        public JitterPhysicsIssueSeverity Severity { get; }

        /// <summary>What is wrong and what to do about it.</summary>
        public string Message { get; }

        /// <summary>Object the issue belongs to, or <c>null</c> when it is level-wide.</summary>
        public Object Context { get; }

        /// <summary>Hierarchy path of <see cref="Context"/>, kept for logs and reports.</summary>
        public string ContextPath { get; }

        public JitterPhysicsIssue(JitterPhysicsIssueSeverity severity, string message, Object context = null)
        {
            Severity = severity;
            Message = message ?? string.Empty;
            Context = context;
            ContextPath = BuildPath(context);
        }

        /// <summary>True when this issue blocks the bake.</summary>
        public bool IsError => Severity == JitterPhysicsIssueSeverity.Error;

        public override string ToString()
        {
            return string.IsNullOrEmpty(ContextPath)
                ? $"{Severity}: {Message}"
                : $"{Severity}: {Message} ({ContextPath})";
        }

        private static string BuildPath(Object context)
        {
            GameObject gameObject = context switch
            {
                GameObject value => value,
                Component component => component.gameObject,
                _ => null,
            };

            if (gameObject == null)
            {
                return context != null ? context.name : string.Empty;
            }

            var path = new System.Text.StringBuilder(gameObject.name);
            for (Transform parent = gameObject.transform.parent; parent != null; parent = parent.parent)
            {
                path.Insert(0, parent.name + "/");
            }

            return path.ToString();
        }
    }

    /// <summary>A list of issues plus the convenience of asking whether anything blocks a bake.</summary>
    public sealed class JitterPhysicsIssueLog
    {
        private readonly List<JitterPhysicsIssue> issues = new List<JitterPhysicsIssue>();

        /// <summary>Every recorded issue, in the order it was found.</summary>
        public IReadOnlyList<JitterPhysicsIssue> Issues => issues;

        /// <summary>True when at least one issue blocks the bake.</summary>
        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < issues.Count; i++)
                {
                    if (issues[i].IsError)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Number of recorded errors.</summary>
        public int ErrorCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < issues.Count; i++)
                {
                    if (issues[i].IsError)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Number of recorded warnings.</summary>
        public int WarningCount => issues.Count - ErrorCount;

        /// <summary>Records a blocking issue.</summary>
        public void Error(string message, Object context = null)
        {
            issues.Add(new JitterPhysicsIssue(JitterPhysicsIssueSeverity.Error, message, context));
        }

        /// <summary>Records a non-blocking issue.</summary>
        public void Warning(string message, Object context = null)
        {
            issues.Add(new JitterPhysicsIssue(JitterPhysicsIssueSeverity.Warning, message, context));
        }

        /// <summary>Formats every issue as one line each, for a log or a report file.</summary>
        public string Format()
        {
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < issues.Count; i++)
            {
                builder.AppendLine(issues[i].ToString());
            }

            return builder.ToString();
        }
    }
}

