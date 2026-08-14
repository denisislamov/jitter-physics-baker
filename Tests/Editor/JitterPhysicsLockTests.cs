using System;
using System.IO;
using System.Text;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Editor.Bootstrap;
using NUnit.Framework;

namespace DataSakura.JitterPhysics.Editor.Tests
{
    /// <summary>
    /// The Jitter2 lock, the source hash and the compatibility report.
    /// <para>
    /// The hash is computed by two independent implementations — this one and
    /// <c>tools~/hash-jitter2.py</c> — so the tests pin the rules that let them agree:
    /// which files are selected, in what order, and how the compile profile is serialized.
    /// A divergence here shows up much later as "CI says compatible, the editor says not".
    /// </para>
    /// </summary>
    public sealed class JitterPhysicsLockTests
    {
        [Test]
        public void LockFileParsesAndDeclaresTheJitterAssembly()
        {
            JitterPhysicsLock lockFile = LoadLock();

            Assert.That(lockFile.SchemaVersion, Is.EqualTo(1));
            Assert.That(lockFile.AssemblyName, Is.EqualTo(JitterPhysicsPackage.JitterAssemblyName));
            Assert.That(lockFile.IncludedFiles, Is.Not.Empty);
            Assert.That(lockFile.SourceContentHash, Does.StartWith(JitterPhysicsSourceHasher.HashPrefix));
        }

        /// <summary>
        /// A snapshot is only offered for installation into <c>Assets/</c> once a patch set has
        /// made it compilable by Unity, and the compile profile is where that fact is recorded.
        /// The engine leaves no room here: game assemblies compile at C# 9 with
        /// <c>-langversion</c> in <c>csc.rsp</c> ignored, and the script reference assemblies are
        /// .NET Standard 2.1, which has no <c>System.Runtime.Intrinsics</c>. Installing an
        /// unpatched snapshot therefore does not produce a warning, it produces a project that
        /// no longer compiles at all.
        /// </summary>
        [TestCase("hardware", "none", false)]
        [TestCase("hardware", "unity", false)]
        [TestCase("scalar", "none", false)]
        [TestCase("scalar", "unity", true)]
        public void SnapshotIsOnlyUnitySafeWhenTheProfileDeclaresBothPatches(
            string intrinsics, string polyfill, bool expected)
        {
            JitterPhysicsLock parsed = JitterPhysicsLock.Parse(
                "{\n"
                + "  \"assemblyName\": \"Jitter2.Core\",\n"
                + "  \"compileProfile\": {\n"
                + $"    \"intrinsicsProfile\": \"{intrinsics}\",\n"
                + $"    \"polyfillProfile\": \"{polyfill}\"\n"
                + "  }\n"
                + "}\n");

            Assert.That(parsed.SupportsUnity, Is.EqualTo(expected));
        }

        [Test]
        public void ShippedSnapshotDeclaresTheUnityCompatibleProfileAndIsOffered()
        {
            // Pins the shipped state. Unity cannot compile the snapshot's sources at all - it
            // fixes game assemblies at C# 9 - so the package compiles them itself into a
            // netstandard2.1 assembly, where the missing framework surface is supplied by
            // Jitter2~/Compat. The profile records both halves of that: no hardware intrinsics,
            // and a named polyfill set. Reverting either one must fail this test, because it
            // would mean the install button is offering something that cannot work.
            JitterPhysicsLock lockFile = LoadLock();

            Assert.That(lockFile.IntrinsicsProfile, Is.EqualTo("scalar-shim"));
            Assert.That(lockFile.PolyfillProfile, Is.EqualTo("netstandard21"));
            Assert.That(lockFile.SupportsUnity, Is.True);
        }

        [Test]
        public void CompileProfileIsSerializedTheWayThePythonToolingSerializesIt()
        {
            // A synthetic profile, so the assertion states the serialization *rule* and does
            // not have to be edited whenever the real profile changes. The rule is what the
            // two implementations must share: json.dumps(profile, sort_keys=True,
            // separators=(",", ":"), ensure_ascii=True).
            JitterPhysicsLock parsed = JitterPhysicsLock.Parse(
                "{\n"
                + "  \"assemblyName\": \"Jitter2.Core\",\n"
                + "  \"compileProfile\": {\n"
                + "    \"zebra\": \"last\",\n"
                + "    \"allowUnsafe\": true,\n"
                + "    \"count\": 7,\n"
                + "    \"Apple\": \"uppercase sorts first\",\n"
                + "    \"unicode\": \"\u00fcber\"\n"
                + "  }\n"
                + "}\n");

            Assert.That(
                parsed.CompileProfileText,
                Is.EqualTo(
                    "{\"Apple\":\"uppercase sorts first\","
                    + "\"allowUnsafe\":true,"
                    + "\"count\":7,"
                    + "\"unicode\":\"\\u00fcber\","
                    + "\"zebra\":\"last\"}"));
        }

        [Test]
        public void CompileProfileIdIsDerivedFromTheProfileText()
        {
            JitterPhysicsLock lockFile = LoadLock();

            Assert.That(lockFile.CompileProfileId, Has.Length.EqualTo(64));
            Assert.That(lockFile.CompileProfileText, Does.StartWith("{"));

            // The id is a hash of the text, never a hand-written name, so a changed profile
            // cannot keep an identifier that claims compatibility it no longer has.
            Assert.That(
                lockFile.CompileProfileId,
                Is.EqualTo(ArtifactCodec.JitterPhysicsHash.Sha256HexUtf8(lockFile.CompileProfileText)));
        }

        [Test]
        public void SnapshotHashMatchesTheCommittedLock()
        {
            JitterPhysicsLock lockFile = LoadLock();
            string snapshotRoot = Path.Combine(PackageRoot(), "Jitter2~", "Runtime");

            var inputs = JitterPhysicsSourceHasher.CollectInputs(
                snapshotRoot, lockFile.IncludedFiles, lockFile.ExcludedFiles);

            // An empty snapshot would hash consistently and prove nothing, so the count is
            // asserted before the hash.
            Assert.That(inputs.Count, Is.GreaterThan(0), "The dormant Jitter2 snapshot is empty.");
            Assert.That(lockFile.IsPlaceholder, Is.False, "The lock still carries a placeholder hash.");

            string actual = JitterPhysicsSourceHasher.ComputeSourceContentHash(
                inputs, lockFile.CompileProfileText);

            // This is the cross-tool check: the value in the lock was produced by the Python
            // tool, and it is recomputed here by the C# implementation.
            Assert.That(
                actual,
                Is.EqualTo(lockFile.SourceContentHash),
                "jitter2.lock.json is stale, or the C# and Python hashers disagree. "
                + "Run tools~/verify-jitter2-lock.py to see which.");
        }

        [Test]
        public void HashChangesWhenAnyCompileRelevantInputChanges()
        {
            JitterPhysicsLock lockFile = LoadLock();
            string root = CreateTempSourceTree(
                ("Core/World.cs", "namespace Jitter2 { }\n"),
                ("Core/csc.rsp", "-unsafe\n"));

            try
            {
                string baseline = JitterPhysicsSourceHasher.ComputeSourceContentHash(root, lockFile);

                File.WriteAllText(Path.Combine(root, "Core", "World.cs"), "namespace Jitter2 { class A { } }\n");
                string afterEdit = JitterPhysicsSourceHasher.ComputeSourceContentHash(root, lockFile);

                Assert.That(afterEdit, Is.Not.EqualTo(baseline));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void HashIgnoresLineEndingsAndConsumerSpecificFiles()
        {
            JitterPhysicsLock lockFile = LoadLock();
            string lf = CreateTempSourceTree(("Core/World.cs", "using System;\nclass A { }\n"));
            string crlf = CreateTempSourceTree(("Core/World.cs", "using System;\r\nclass A { }\r\n"));

            try
            {
                string lfHash = JitterPhysicsSourceHasher.ComputeSourceContentHash(lf, lockFile);
                string crlfHash = JitterPhysicsSourceHasher.ComputeSourceContentHash(crlf, lockFile);

                // A Windows checkout must not look like a different Jitter2 revision.
                Assert.That(crlfHash, Is.EqualTo(lfHash));

                // A consumer's own asmdef and Unity's .meta files describe where the sources
                // live, not what they are, so they must not affect source identity.
                File.WriteAllText(Path.Combine(lf, "Core", "Jitter2.Core.asmdef"), "{\"name\":\"Jitter2.Core\"}\n");
                File.WriteAllText(Path.Combine(lf, "Core", "World.cs.meta"), "guid: 0123456789\n");

                Assert.That(
                    JitterPhysicsSourceHasher.ComputeSourceContentHash(lf, lockFile),
                    Is.EqualTo(lfHash));
            }
            finally
            {
                Directory.Delete(lf, true);
                Directory.Delete(crlf, true);
            }
        }

        [TestCase("x.cs", "**/*.cs", true)]
        [TestCase("a/x.cs", "**/*.cs", true)]
        [TestCase("a/b/x.cs", "**/*.cs", true)]
        [TestCase("README.md", "**/*.cs", false)]
        [TestCase("a/x.csx", "**/*.cs", false)]
        [TestCase("csc.rsp", "**/csc.rsp", true)]
        [TestCase("Runtime/csc.rsp", "**/csc.rsp", true)]
        [TestCase("x.meta", "**/*.meta", true)]
        [TestCase("bin/x.cs", "**/bin/**", true)]
        [TestCase("a/bin/x.cs", "**/bin/**", true)]
        [TestCase("a/binary/x.cs", "**/bin/**", false)]
        public void GlobRulesMatchThePythonImplementation(string path, string pattern, bool expected)
        {
            Assert.That(JitterPhysicsSourceHasher.GlobMatches(path, pattern), Is.EqualTo(expected));
        }

        [Test]
        public void CompatibilityReportIsProducedAndIsSerializable()
        {
            JitterPhysicsCompatibilityReport report = JitterPhysicsCompatibilityReport.Create();

            Assert.That(report, Is.Not.Null);
            Assert.That(report.Status, Is.Not.EqualTo(JitterPhysicsCompatibilityStatus.Unknown), report.Message);
            Assert.That(report.Message, Is.Not.Empty);

            string json = report.ToJson();
            Assert.That(json, Does.Contain("\"status\": \"" + report.Status + "\""));
            Assert.That(json, Does.Contain("\"canBake\": " + (report.CanBake ? "true" : "false")));
        }

        [Test]
        public void CompatibilityReportBlocksBakingWithoutJitter()
        {
            JitterPhysicsCompatibilityReport report = JitterPhysicsCompatibilityReport.Create();

            // This project intentionally has no Jitter2 yet: a clean import must be a valid,
            // non-failing state that simply cannot bake.
            if (report.Status == JitterPhysicsCompatibilityStatus.Missing)
            {
                Assert.That(report.CanBake, Is.False);
                Assert.That(report.ActualSourceHash, Is.Null);
            }
            else
            {
                Assert.That(
                    new[]
                    {
                        JitterPhysicsCompatibilityStatus.Compatible,
                        JitterPhysicsCompatibilityStatus.Incompatible,
                        JitterPhysicsCompatibilityStatus.Duplicate,
                        JitterPhysicsCompatibilityStatus.UnsupportedPlugin,
                    },
                    Does.Contain(report.Status));
            }
        }

        private static JitterPhysicsLock LoadLock()
        {
            return JitterPhysicsLock.Load(PackageRoot());
        }

        private static string PackageRoot()
        {
            string root = JitterPhysicsCompatibilityReport.ResolvePackageRootPath();
            Assert.That(root, Is.Not.Null, "The package is not resolved by the Package Manager.");
            return root;
        }

        private static string CreateTempSourceTree(params (string RelativePath, string Content)[] files)
        {
            string root = Path.Combine(Path.GetTempPath(), "jitter-physics-hash-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            foreach ((string relativePath, string content) in files)
            {
                string absolute = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(absolute) ?? root);
                File.WriteAllBytes(absolute, new UTF8Encoding(false).GetBytes(content));
            }

            return root;
        }
    }
}







