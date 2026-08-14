using System.Collections.Generic;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Editor.Diagnostics;
using NUnit.Framework;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor.Tests
{
    /// <summary>Exact stale-geometry decisions used by the Scene View overlay.</summary>
    public sealed class JitterPhysicsGeometryOverlayTests
    {
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
        public void UnchangedBodyPoseMatchesTheBakedRecord()
        {
            Transform transform = CreateTransform();
            transform.position = new Vector3(2f, 3f, 4f);
            transform.rotation = Quaternion.Euler(10f, 20f, 30f);

            PhysicsBodyRecord baked = BodyAt(transform);

            Assert.That(
                JitterPhysicsGeometryComparer.BodyPoseMatches(baked, transform),
                Is.True);
        }

        [Test]
        public void MovingABodyMarksItsGeometryAsChanged()
        {
            Transform transform = CreateTransform();
            PhysicsBodyRecord baked = BodyAt(transform);

            transform.position = new Vector3(0f, 0.125f, 0f);

            Assert.That(
                JitterPhysicsGeometryComparer.BodyPoseMatches(baked, transform),
                Is.False);
        }

        [Test]
        public void IdenticalPrimitiveShapesMatch()
        {
            PhysicsShapeRecord baked = PhysicsShapeRecord.Box(
                "/box#0",
                new PhysicsVector3(1f, 2f, 3f),
                PhysicsQuaternion.Identity,
                new PhysicsVector3(4f, 5f, 6f));
            PhysicsShapeRecord current = PhysicsShapeRecord.Box(
                "/box#0",
                new PhysicsVector3(1f, 2f, 3f),
                PhysicsQuaternion.Identity,
                new PhysicsVector3(4f, 5f, 6f));

            Assert.That(JitterPhysicsGeometryComparer.ShapesMatch(baked, current), Is.True);
        }

        [Test]
        public void ResizingAPrimitiveMarksItAsChanged()
        {
            PhysicsShapeRecord baked = PhysicsShapeRecord.Sphere(
                "/sphere#0", PhysicsVector3.Zero, PhysicsQuaternion.Identity, 1f);
            PhysicsShapeRecord current = PhysicsShapeRecord.Sphere(
                "/sphere#0", PhysicsVector3.Zero, PhysicsQuaternion.Identity, 1.001f);

            Assert.That(JitterPhysicsGeometryComparer.ShapesMatch(baked, current), Is.False);
        }

        [Test]
        public void EditingOneMeshVertexMarksItAsChanged()
        {
            PhysicsShapeRecord baked = Triangle(new PhysicsVector3(0f, 1f, 0f));
            PhysicsShapeRecord current = Triangle(new PhysicsVector3(0f, 1.01f, 0f));

            Assert.That(JitterPhysicsGeometryComparer.ShapesMatch(baked, current), Is.False);
        }

        [Test]
        public void ReorderingMeshIndicesMarksItAsChanged()
        {
            PhysicsVector3[] vertices =
            {
                PhysicsVector3.Zero,
                new PhysicsVector3(1f, 0f, 0f),
                new PhysicsVector3(0f, 1f, 0f),
            };

            PhysicsShapeRecord baked = PhysicsShapeRecord.Mesh(
                "/mesh#0", PhysicsVector3.Zero, PhysicsQuaternion.Identity, vertices, new[] { 0, 1, 2 });
            PhysicsShapeRecord current = PhysicsShapeRecord.Mesh(
                "/mesh#0", PhysicsVector3.Zero, PhysicsQuaternion.Identity, vertices, new[] { 0, 2, 1 });

            Assert.That(JitterPhysicsGeometryComparer.ShapesMatch(baked, current), Is.False);
        }

        private Transform CreateTransform()
        {
            var gameObject = new GameObject("Geometry");
            spawned.Add(gameObject);
            return gameObject.transform;
        }

        private static PhysicsBodyRecord BodyAt(Transform transform)
        {
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;

            return new PhysicsBodyRecord(
                "body",
                new PhysicsVector3(position.x, position.y, position.z).Canonical(),
                new PhysicsQuaternion(rotation.x, rotation.y, rotation.z, rotation.w).Canonical(),
                0.2f,
                0f,
                new[]
                {
                    PhysicsShapeRecord.Box(
                        "/box#0",
                        PhysicsVector3.Zero,
                        PhysicsQuaternion.Identity,
                        new PhysicsVector3(1f, 1f, 1f)),
                });
        }

        private static PhysicsShapeRecord Triangle(PhysicsVector3 top)
        {
            return PhysicsShapeRecord.Mesh(
                "/mesh#0",
                PhysicsVector3.Zero,
                PhysicsQuaternion.Identity,
                new[]
                {
                    PhysicsVector3.Zero,
                    new PhysicsVector3(1f, 0f, 0f),
                    top,
                },
                new[] { 0, 1, 2 });
        }
    }
}
