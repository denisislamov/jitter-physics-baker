using System.Collections.Generic;
using System.IO;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Editor.Export;
using DataSakura.JitterPhysics.Editor.Install;
using NUnit.Framework;

namespace DataSakura.JitterPhysics.Editor.Tests
{
    /// <summary>
    /// The receipt is what lets the installer tell its own files from the project's, so its
    /// round-trip is asserted rather than assumed: a receipt that loses a hash silently turns
    /// "refuse to overwrite a modified file" into "overwrite everything".
    /// </summary>
    public sealed class JitterPhysicsInstallReceiptTests
    {
        [Test]
        public void RoundTripsThroughJson()
        {
            JitterPhysicsInstallReceipt receipt = JitterPhysicsInstallReceipt.Empty
                .With(new JitterPhysicsInstalledComponent(
                    JitterPhysicsComponentIds.Integration,
                    JitterPhysicsOwnership.Package,
                    "Assets/DataSakura/JitterPhysics/Integration",
                    JitterPhysicsPackage.PackageVersion,
                    "sha256:abc",
                    new List<JitterPhysicsInstalledFile>
                    {
                        new JitterPhysicsInstalledFile("JitterPhysicsWorldBuilder.cs", "0123abcd"),
                        new JitterPhysicsInstalledFile("DataSakura.JitterPhysics.JitterIntegration.asmdef", "4567ef01"),
                    }));

            string path = Path.Combine(Path.GetTempPath(), "jphys-receipt-test.json");
            receipt.Save(path);

            try
            {
                JitterPhysicsInstallReceipt loaded = JitterPhysicsInstallReceipt.Load(path, out string error);

                Assert.That(error, Is.Null.Or.Empty);
                Assert.That(loaded.Components.Count, Is.EqualTo(1));

                JitterPhysicsInstalledComponent component = loaded.Component(JitterPhysicsComponentIds.Integration);
                Assert.That(component, Is.Not.Null);
                Assert.That(component.Ownership, Is.EqualTo(JitterPhysicsOwnership.Package));
                Assert.That(component.Root, Is.EqualTo("Assets/DataSakura/JitterPhysics/Integration"));
                Assert.That(component.SourceHash, Is.EqualTo("sha256:abc"));
                Assert.That(component.Files.Count, Is.EqualTo(2));
                Assert.That(loaded.ToJson(), Is.EqualTo(receipt.ToJson()));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void MissingReceiptIsAnEmptyOneAndNotAnError()
        {
            JitterPhysicsInstallReceipt receipt = JitterPhysicsInstallReceipt.Load(
                Path.Combine(Path.GetTempPath(), "jphys-receipt-that-does-not-exist.json"),
                out string error);

            Assert.That(error, Is.Null.Or.Empty);
            Assert.That(receipt.Components, Is.Empty);
        }

        [Test]
        public void AMalformedReceiptIsReportedInsteadOfTreatedAsEmpty()
        {
            string path = Path.Combine(Path.GetTempPath(), "jphys-receipt-broken.json");
            File.WriteAllText(path, "{ not json");

            try
            {
                JitterPhysicsInstallReceipt.Load(path, out string error);

                // Treating it as empty would make the installer believe it owns nothing and
                // orphan the previous installation forever.
                Assert.That(error, Is.Not.Null.And.Not.Empty);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void ReplacingAComponentDoesNotDuplicateIt()
        {
            JitterPhysicsInstallReceipt receipt = JitterPhysicsInstallReceipt.Empty
                .With(Component("Assets/A"))
                .With(Component("Assets/B"));

            Assert.That(receipt.Components.Count, Is.EqualTo(1));
            Assert.That(receipt.Component(JitterPhysicsComponentIds.Jitter).Root, Is.EqualTo("Assets/B"));
            Assert.That(receipt.Without(JitterPhysicsComponentIds.Jitter).Components, Is.Empty);
        }

        [Test]
        public void VerifyingAProjectionThatIsNotThereIsAnError()
        {
            JitterPhysicsInstallResult result = JitterPhysicsServerProjection.Verify(
                Path.Combine(Path.GetTempPath(), "jphys-no-projection-here"));

            Assert.That(result.Succeeded, Is.False);
        }

        [Test]
        public void GeneratedClassNamesAreValidIdentifiers()
        {
            Assert.That(JitterPhysicsExportDefaults.ClassNameFor("arena"), Is.EqualTo("ArenaArtifact"));
            Assert.That(JitterPhysicsExportDefaults.ClassNameFor("dust_2"), Is.EqualTo("Dust2Artifact"));
            Assert.That(JitterPhysicsExportDefaults.ClassNameFor("2fort"), Is.EqualTo("Level2FortArtifact"));
            Assert.That(JitterPhysicsExportDefaults.ClassNameFor(string.Empty), Is.EqualTo("PhysicsArtifact"));
        }

        private static JitterPhysicsInstalledComponent Component(string root)
        {
            return new JitterPhysicsInstalledComponent(
                JitterPhysicsComponentIds.Jitter,
                JitterPhysicsOwnership.Package,
                root,
                JitterPhysicsPackage.PackageVersion,
                string.Empty,
                new List<JitterPhysicsInstalledFile>());
        }
    }
}

