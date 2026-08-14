using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Editor.Bootstrap;

namespace DataSakura.JitterPhysics.Editor.Install
{
    /// <summary>Who a set of installed files belongs to.</summary>
    public enum JitterPhysicsOwnership
    {
        /// <summary>The package wrote these files and may update or remove them.</summary>
        Package = 0,

        /// <summary>The project had them already; the package only reads them.</summary>
        External,
    }

    /// <summary>Ids of the things the installer can put into a project.</summary>
    public static class JitterPhysicsComponentIds
    {
        /// <summary>The fallback Jitter2 copy.</summary>
        public const string Jitter = "jitter2";

        /// <summary>The Jitter-dependent adapter assembly.</summary>
        public const string Integration = "integration";

        /// <summary>The server runtime source projection.</summary>
        public const string ServerRuntime = "server-runtime";

        /// <summary>The runnable samples.</summary>
        public const string Samples = "samples";
    }

    /// <summary>One installed file and the hash it had when it was written.</summary>
    public sealed class JitterPhysicsInstalledFile
    {
        /// <summary>Path relative to the component root, with forward slashes.</summary>
        public string RelativePath { get; }

        /// <summary>Lowercase hex SHA-256 of the content that was written.</summary>
        public string Hash { get; }

        public JitterPhysicsInstalledFile(string relativePath, string hash)
        {
            RelativePath = relativePath;
            Hash = hash;
        }
    }

    /// <summary>One installed component: what it is, where it went, and what was written.</summary>
    public sealed class JitterPhysicsInstalledComponent
    {
        /// <summary>One of <see cref="JitterPhysicsComponentIds"/>.</summary>
        public string Id { get; }

        /// <summary>Whether the package owns these files.</summary>
        public JitterPhysicsOwnership Ownership { get; }

        /// <summary>Folder the files live in, project-relative for in-project components.</summary>
        public string Root { get; }

        /// <summary>Package version that wrote them.</summary>
        public string PackageVersion { get; }

        /// <summary>Canonical source hash, where the component has one.</summary>
        public string SourceHash { get; }

        /// <summary>The files, ordered by path.</summary>
        public IReadOnlyList<JitterPhysicsInstalledFile> Files { get; }

        public JitterPhysicsInstalledComponent(
            string id,
            JitterPhysicsOwnership ownership,
            string root,
            string packageVersion,
            string sourceHash,
            IReadOnlyList<JitterPhysicsInstalledFile> files)
        {
            Id = id;
            Ownership = ownership;
            Root = root;
            PackageVersion = packageVersion;
            SourceHash = sourceHash ?? string.Empty;
            Files = files ?? Array.Empty<JitterPhysicsInstalledFile>();
        }
    }

    /// <summary>
    /// The record of what the package put into this project.
    /// <para>
    /// Without it an installer has two bad options: overwrite everything it recognises by name,
    /// or never clean up. Both are wrong in the same situation — a consumer who edited an
    /// installed file. With a receipt the installer can tell "this is the file I wrote" from
    /// "this is a file somebody changed", update the first and refuse to touch the second.
    /// </para>
    /// <para>
    /// It is written as deterministic JSON with sorted files, so it diffs cleanly in review and
    /// two identical installations produce identical receipts.
    /// </para>
    /// </summary>
    public sealed class JitterPhysicsInstallReceipt
    {
        /// <summary>Where the receipt lives unless a caller says otherwise.</summary>
        public const string DefaultPath = "Assets/DataSakura/JitterPhysics/InstallationReceipt.json";

        /// <summary>Format version of the receipt itself.</summary>
        public const int SchemaVersion = 1;

        private readonly List<JitterPhysicsInstalledComponent> components;

        /// <summary>Package version that last wrote the receipt.</summary>
        public string PackageVersion { get; }

        /// <summary>Installed components, ordered by id.</summary>
        public IReadOnlyList<JitterPhysicsInstalledComponent> Components => components;

        public JitterPhysicsInstallReceipt(
            string packageVersion,
            IEnumerable<JitterPhysicsInstalledComponent> installedComponents)
        {
            PackageVersion = packageVersion ?? JitterPhysicsPackage.PackageVersion;
            components = new List<JitterPhysicsInstalledComponent>(
                installedComponents ?? Array.Empty<JitterPhysicsInstalledComponent>());
            components.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
        }

        /// <summary>An empty receipt for a project the package has never written to.</summary>
        public static JitterPhysicsInstallReceipt Empty =>
            new JitterPhysicsInstallReceipt(JitterPhysicsPackage.PackageVersion, null);

        /// <summary>The component with this id, or <c>null</c>.</summary>
        public JitterPhysicsInstalledComponent Component(string id)
        {
            for (int i = 0; i < components.Count; i++)
            {
                if (string.Equals(components[i].Id, id, StringComparison.Ordinal))
                {
                    return components[i];
                }
            }

            return null;
        }

        /// <summary>A copy of this receipt with <paramref name="component"/> added or replaced.</summary>
        public JitterPhysicsInstallReceipt With(JitterPhysicsInstalledComponent component)
        {
            var next = new List<JitterPhysicsInstalledComponent>(components.Count + 1);
            for (int i = 0; i < components.Count; i++)
            {
                if (!string.Equals(components[i].Id, component.Id, StringComparison.Ordinal))
                {
                    next.Add(components[i]);
                }
            }

            next.Add(component);
            return new JitterPhysicsInstallReceipt(JitterPhysicsPackage.PackageVersion, next);
        }

        /// <summary>A copy of this receipt without the component with this id.</summary>
        public JitterPhysicsInstallReceipt Without(string id)
        {
            var next = new List<JitterPhysicsInstalledComponent>(components.Count);
            for (int i = 0; i < components.Count; i++)
            {
                if (!string.Equals(components[i].Id, id, StringComparison.Ordinal))
                {
                    next.Add(components[i]);
                }
            }

            return new JitterPhysicsInstallReceipt(JitterPhysicsPackage.PackageVersion, next);
        }

        /// <summary>
        /// Reads a receipt, or returns an empty one when there is none. A malformed receipt is
        /// reported rather than ignored: silently treating it as empty would make the installer
        /// believe it owns nothing and leave the previous installation behind forever.
        /// </summary>
        public static JitterPhysicsInstallReceipt Load(string path, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return Empty;
            }

            JitterPhysicsJsonValue root;
            try
            {
                root = JitterPhysicsJsonValue.Parse(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                error = $"Installation receipt '{path}' could not be parsed: {exception.Message}";
                return Empty;
            }

            var parsed = new List<JitterPhysicsInstalledComponent>();
            JitterPhysicsJsonValue componentsValue = root.Member("components");

            if (componentsValue != null)
            {
                IReadOnlyList<JitterPhysicsJsonValue> items = componentsValue.Items;
                for (int i = 0; i < items.Count; i++)
                {
                    JitterPhysicsJsonValue item = items[i];
                    var files = new List<JitterPhysicsInstalledFile>();
                    JitterPhysicsJsonValue filesValue = item.Member("files");

                    if (filesValue != null)
                    {
                        IReadOnlyList<JitterPhysicsJsonValue> fileItems = filesValue.Items;
                        for (int f = 0; f < fileItems.Count; f++)
                        {
                            files.Add(new JitterPhysicsInstalledFile(
                                fileItems[f].StringMember("path", string.Empty),
                                fileItems[f].StringMember("hash", string.Empty)));
                        }
                    }

                    parsed.Add(new JitterPhysicsInstalledComponent(
                        item.StringMember("id", string.Empty),
                        string.Equals(item.StringMember("ownership", "package"), "external", StringComparison.Ordinal)
                            ? JitterPhysicsOwnership.External
                            : JitterPhysicsOwnership.Package,
                        item.StringMember("root", string.Empty),
                        item.StringMember("packageVersion", string.Empty),
                        item.StringMember("sourceHash", string.Empty),
                        files));
                }
            }

            return new JitterPhysicsInstallReceipt(
                root.StringMember("packageVersion", JitterPhysicsPackage.PackageVersion), parsed);
        }

        /// <summary>Serializes the receipt as deterministic JSON with LF line endings.</summary>
        public string ToJson()
        {
            var builder = new StringBuilder(1024);
            builder.Append("{\n");
            builder.Append("  \"schemaVersion\": ")
                .Append(SchemaVersion.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            builder.Append("  \"packageVersion\": \"").Append(Escape(PackageVersion)).Append("\",\n");
            builder.Append("  \"components\": [\n");

            for (int i = 0; i < components.Count; i++)
            {
                JitterPhysicsInstalledComponent component = components[i];
                builder.Append("    {\n");
                builder.Append("      \"id\": \"").Append(Escape(component.Id)).Append("\",\n");
                builder.Append("      \"ownership\": \"")
                    .Append(component.Ownership == JitterPhysicsOwnership.External ? "external" : "package")
                    .Append("\",\n");
                builder.Append("      \"root\": \"").Append(Escape(component.Root)).Append("\",\n");
                builder.Append("      \"packageVersion\": \"")
                    .Append(Escape(component.PackageVersion)).Append("\",\n");
                builder.Append("      \"sourceHash\": \"").Append(Escape(component.SourceHash)).Append("\",\n");
                builder.Append("      \"files\": [\n");

                for (int f = 0; f < component.Files.Count; f++)
                {
                    JitterPhysicsInstalledFile file = component.Files[f];
                    builder.Append("        { \"path\": \"").Append(Escape(file.RelativePath))
                        .Append("\", \"hash\": \"").Append(Escape(file.Hash)).Append("\" }")
                        .Append(f == component.Files.Count - 1 ? "\n" : ",\n");
                }

                builder.Append("      ]\n");
                builder.Append(i == components.Count - 1 ? "    }\n" : "    },\n");
            }

            builder.Append("  ]\n");
            builder.Append("}\n");
            return builder.ToString();
        }

        /// <summary>Writes the receipt, creating its folder if needed.</summary>
        public void Save(string path)
        {
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(path, ToJson(), new UTF8Encoding(false));
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}


