using System.Collections.Generic;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Editor.Baking;
using NUnit.Framework;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor.Tests
{
    /// <summary>
    /// The bake pipeline: what gets collected, how colliders are converted, and whether two
    /// runs agree.
    /// <para>
    /// Determinism is the property everything else rests on, so it is tested directly rather
    /// than assumed: the same scene must produce the same bytes, and a scene that differs
    /// only in hierarchy order must not produce different ones.
    /// </para>
    /// </summary>
    public sealed class JitterPhysicsBakeTests
    {
        private const string RuntimeId =
            "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

        private readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < spawned.Count; i++)
            {
                if (spawned[i] != null)
                {
                    Object.DestroyImmediate(spawned[i]);
                }
            }

            spawned.Clear();
        }

        [Test]
        public void BakingAnUnchangedSceneTwiceProducesIdenticalBytes()
        {
            JitterPhysicsLevel level = CreateLevelWithGround();

            byte[] first = WriteArtifact(level);
            byte[] second = WriteArtifact(level);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(
                JitterPhysicsHash.Sha256Hex(second),
                Is.EqualTo(JitterPhysicsHash.Sha256Hex(first)));
        }

        [Test]
        public void RecordOrderFollowsAuthoredIdsRatherThanHierarchyOrder()
        {
            JitterPhysicsLevel level = CreateLevel(out Transform root);

            // Created in one order, named in the opposite one: if collection order leaked
            // into the artifact, the two bakes below would differ.
            JitterStaticBodySource second = CreateSource(root, "b_second", new Vector3(3f, 0f, 0f));
            JitterStaticBodySource first = CreateSource(root, "a_first", new Vector3(-3f, 0f, 0f));

            PhysicsArtifact artifact = BuildArtifact(level);

            Assert.That(artifact.Bodies[0].SourceId, Is.EqualTo("a_first"));
            Assert.That(artifact.Bodies[1].SourceId, Is.EqualTo("b_second"));

            byte[] before = PhysicsArtifactWriter.Write(artifact);

            // Reordering siblings changes nothing semantic, so it must not change the bytes.
            first.transform.SetSiblingIndex(1);
            second.transform.SetSiblingIndex(0);

            Assert.That(PhysicsArtifactWriter.Write(BuildArtifact(level)), Is.EqualTo(before));
        }

        [Test]
        public void BoxColliderKeepsItsFullSizeAndScale()
        {
            JitterPhysicsLevel level = CreateLevel(out Transform root);
            JitterStaticBodySource source = CreateSource(root, "ground", Vector3.zero);
            source.transform.localScale = new Vector3(2f, 3f, 4f);

            BoxCollider box = source.gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(1f, 2f, 0.5f);

            PhysicsShapeRecord shape = SingleShape(level);

            Assert.That(shape.ShapeType, Is.EqualTo(PhysicsShapeType.Box));
            Assert.That(shape.Size.X, Is.EqualTo(2f).Within(1e-5f));
            Assert.That(shape.Size.Y, Is.EqualTo(6f).Within(1e-5f));
            Assert.That(shape.Size.Z, Is.EqualTo(2f).Within(1e-5f));
        }

        [Test]
        public void SphereUnderNonUniformScaleUsesTheLargestAxisAndWarns()
        {
            JitterPhysicsLevel level = CreateLevel(out Transform root);
            JitterStaticBodySource source = CreateSource(root, "ball", Vector3.zero);
            source.transform.localScale = new Vector3(1f, 3f, 2f);

            SphereCollider sphere = source.gameObject.AddComponent<SphereCollider>();
            sphere.radius = 0.5f;

            JitterPhysicsBuildResult result = JitterPhysicsArtifactBuilder.Build(level, RuntimeId);

            Assert.That(result.Succeeded, Is.True, result.Issues.Format());
            PhysicsShapeRecord shape = result.Artifact.Bodies[0].Shapes[0];

            // Conservative on purpose: a shape that is slightly too large makes a player bump
            // into geometry, a shape that is too small lets them pass through it.
            Assert.That(shape.Radius, Is.EqualTo(1.5f).Within(1e-5f));
            Assert.That(result.Issues.WarningCount, Is.EqualTo(1));
            Assert.That(result.Issues.HasErrors, Is.False);
        }

        [Test]
        public void AnUnrotatedShapeBakesToAnIdentityLocalRotation()
        {
            // Regression: the converter used default(Quaternion) as "no axis correction", but
            // that value is (0,0,0,0), and Unity's fuzzy quaternion equality made the guard
            // fire anyway, multiplying the local rotation to zero. A plain sphere or box then
            // threw "zero length" instead of baking. This is the smallest scene that reproduced
            // it: one axis-aligned box, no rotation anywhere.
            JitterPhysicsLevel level = CreateLevel(out Transform root);
            JitterStaticBodySource source = CreateSource(root, "ground", Vector3.zero);
            source.gameObject.AddComponent<BoxCollider>();

            source.gameObject.AddComponent<SphereCollider>();

            JitterPhysicsBuildResult result = JitterPhysicsArtifactBuilder.Build(level, RuntimeId);

            Assert.That(result.Succeeded, Is.True, result.Issues.Format());
            foreach (PhysicsShapeRecord shape in result.Artifact.Bodies[0].Shapes)
            {
                Assert.That(shape.LocalRotation.X, Is.EqualTo(0f));
                Assert.That(shape.LocalRotation.Y, Is.EqualTo(0f));
                Assert.That(shape.LocalRotation.Z, Is.EqualTo(0f));
                Assert.That(shape.LocalRotation.W, Is.EqualTo(1f));
            }
        }

        [Test]
        public void CapsuleLengthExcludesTheCaps()
        {
            JitterPhysicsLevel level = CreateLevel(out Transform root);
            JitterStaticBodySource source = CreateSource(root, "pillar", Vector3.zero);

            CapsuleCollider capsule = source.gameObject.AddComponent<CapsuleCollider>();
            capsule.radius = 0.5f;
            capsule.height = 3f;
            capsule.direction = 1;

            PhysicsShapeRecord shape = SingleShape(level);

            Assert.That(shape.ShapeType, Is.EqualTo(PhysicsShapeType.Capsule));
            Assert.That(shape.Radius, Is.EqualTo(0.5f).Within(1e-5f));

            // Unity measures the total height including both hemispherical caps; the record
            // stores only the cylindrical part.
            Assert.That(shape.Length, Is.EqualTo(2f).Within(1e-5f));
        }

        [Test]
        public void CapsuleShorterThanItsDiameterCollapsesToZeroLength()
        {
            JitterPhysicsLevel level = CreateLevel(out Transform root);
            JitterStaticBodySource source = CreateSource(root, "stub", Vector3.zero);

            CapsuleCollider capsule = source.gameObject.AddComponent<CapsuleCollider>();
            capsule.radius = 1f;
            capsule.height = 0.5f;

            PhysicsShapeRecord shape = SingleShape(level);

            // A degenerate capsule is a sphere, which is legal: rejecting it would break
            // authoring for no benefit.
            Assert.That(shape.Length, Is.EqualTo(0f));
            Assert.That(shape.Radius, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void TriggerCollidersAreRefused()
        {
            JitterPhysicsLevel level = CreateLevel(out Transform root);
            JitterStaticBodySource source = CreateSource(root, "zone", Vector3.zero);

            BoxCollider box = source.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;

            JitterPhysicsBuildResult result = JitterPhysicsArtifactBuilder.Build(level, RuntimeId);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Issues.HasErrors, Is.True);
        }

        [Test]
        public void ZeroScaleIsRefusedInsteadOfBakedAsADegenerateShape()
        {
            JitterPhysicsLevel level = CreateLevel(out Transform root);
            JitterStaticBodySource source = CreateSource(root, "flat", Vector3.zero);
            source.transform.localScale = new Vector3(1f, 0f, 1f);
            source.gameObject.AddComponent<BoxCollider>();

            JitterPhysicsBuildResult result = JitterPhysicsArtifactBuilder.Build(level, RuntimeId);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Issues.HasErrors, Is.True);
        }

        [Test]
        public void DuplicateSourceIdsAreRefused()
        {
            JitterPhysicsLevel level = CreateLevel(out Transform root);

            JitterStaticBodySource first = CreateSource(root, "cover", new Vector3(-2f, 0f, 0f));
            first.name = "OriginalCover";
            first.gameObject.AddComponent<BoxCollider>();

            JitterStaticBodySource second = CreateSource(root, "cover", new Vector3(2f, 0f, 0f));
            second.name = "DuplicatedCover";
            second.gameObject.AddComponent<BoxCollider>();

            JitterPhysicsBuildResult result = JitterPhysicsArtifactBuilder.Build(level, RuntimeId);

            // Ambiguous ids would make record order undefined, which is exactly the kind of
            // nondeterminism the artifact format exists to rule out.
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Issues.HasErrors, Is.True);
            Assert.That(result.Issues.Issues, Has.Count.EqualTo(1));

            JitterPhysicsIssue issue = result.Issues.Issues[0];
            Assert.That(issue.Context, Is.SameAs(second));
            Assert.That(issue.Message, Does.Contain("Duplicate Source Id 'cover'"));
            Assert.That(issue.Message, Does.Contain("'OriginalCover'"));
            Assert.That(issue.Message, Does.Contain("'DuplicatedCover'"));
            Assert.That(issue.Message, Does.Contain("Duplicating a GameObject copies its Source Id"));
            Assert.That(issue.Message, Does.Contain("Jitter Static Body Source > Source Id"));
            Assert.That(issue.Message, Does.Contain("unique value"));
        }

        [Test]
        public void MovingGeometryChangesTheArtifactHash()
        {
            JitterPhysicsLevel level = CreateLevelWithGround();
            string before = JitterPhysicsHash.Sha256Hex(WriteArtifact(level));

            JitterStaticBodySource source = level.GeometryRoot.GetComponentInChildren<JitterStaticBodySource>();
            source.transform.position += new Vector3(0f, 1f, 0f);

            Assert.That(JitterPhysicsHash.Sha256Hex(WriteArtifact(level)), Is.Not.EqualTo(before));
        }

        [Test]
        public void ShapeKeysAreUniqueForCollidersOnTheSameObject()
        {
            JitterPhysicsLevel level = CreateLevel(out Transform root);
            JitterStaticBodySource source = CreateSource(root, "multi", Vector3.zero);

            source.gameObject.AddComponent<BoxCollider>();
            source.gameObject.AddComponent<BoxCollider>();

            PhysicsArtifact artifact = BuildArtifact(level);
            IReadOnlyList<PhysicsShapeRecord> shapes = artifact.Bodies[0].Shapes;

            Assert.That(shapes.Count, Is.EqualTo(2));
            Assert.That(shapes[0].ShapeKey, Is.Not.EqualTo(shapes[1].ShapeKey));
        }

        [Test]
        public void BakeIsRefusedWithoutAWorldProfile()
        {
            JitterPhysicsLevel level = CreateLevel(out Transform root, assignProfile: false);
            JitterStaticBodySource source = CreateSource(root, "ground", Vector3.zero);
            source.gameObject.AddComponent<BoxCollider>();

            JitterPhysicsBuildResult result = JitterPhysicsArtifactBuilder.Build(level, RuntimeId);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Issues.HasErrors, Is.True);
        }

        [Test]
        public void BakeIsRefusedWithoutARuntimeCompatibilityId()
        {
            JitterPhysicsLevel level = CreateLevelWithGround();

            // No compatible Jitter2 means no way to know the artifact would rebuild the same
            // world, so the bake stops rather than producing something unverifiable.
            JitterPhysicsBuildResult result = JitterPhysicsArtifactBuilder.Build(level, null);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Issues.HasErrors, Is.True);
        }

        private static PhysicsArtifact BuildArtifact(JitterPhysicsLevel level)
        {
            JitterPhysicsBuildResult result = JitterPhysicsArtifactBuilder.Build(level, RuntimeId);
            Assert.That(result.Succeeded, Is.True, result.Issues.Format());
            return result.Artifact;
        }

        private static byte[] WriteArtifact(JitterPhysicsLevel level)
        {
            return PhysicsArtifactWriter.Write(BuildArtifact(level));
        }

        private static PhysicsShapeRecord SingleShape(JitterPhysicsLevel level)
        {
            PhysicsArtifact artifact = BuildArtifact(level);
            Assert.That(artifact.Bodies.Count, Is.EqualTo(1));
            Assert.That(artifact.Bodies[0].Shapes.Count, Is.EqualTo(1));
            return artifact.Bodies[0].Shapes[0];
        }

        private JitterPhysicsLevel CreateLevelWithGround()
        {
            JitterPhysicsLevel level = CreateLevel(out Transform root);
            JitterStaticBodySource source = CreateSource(root, "ground", Vector3.zero);
            BoxCollider box = source.gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(20f, 1f, 20f);
            return level;
        }

        private JitterPhysicsLevel CreateLevel(out Transform geometryRoot, bool assignProfile = true)
        {
            var levelObject = new GameObject("Level");
            spawned.Add(levelObject);
            var level = levelObject.AddComponent<JitterPhysicsLevel>();

            var root = new GameObject("GeometryRoot");
            spawned.Add(root);
            geometryRoot = root.transform;

            var serialized = new UnityEditor.SerializedObject(level);
            serialized.FindProperty("levelId").stringValue = "test_level";
            serialized.FindProperty("geometryRoot").objectReferenceValue = root.transform;

            if (assignProfile)
            {
                var profile = ScriptableObject.CreateInstance<JitterPhysicsWorldProfile>();
                spawned.Add(profile);
                serialized.FindProperty("worldProfile").objectReferenceValue = profile;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return level;
        }

        private JitterStaticBodySource CreateSource(Transform parent, string sourceId, Vector3 position)
        {
            var gameObject = new GameObject(sourceId);
            spawned.Add(gameObject);
            gameObject.transform.SetParent(parent);
            gameObject.transform.position = position;

            var source = gameObject.AddComponent<JitterStaticBodySource>();
            source.SetSourceId(sourceId);
            return source;
        }
    }
}
