using System.Collections.Generic;
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor.Tests
{
    /// <summary>
    /// Authoring rules that the deterministic bake depends on.
    /// <para>
    /// The identifiers tested here end up inside the artifact and decide record order, so
    /// they must survive renaming, reimporting and a domain reload. The collection rules
    /// decide what a designer actually ships: only explicitly marked, active geometry.
    /// </para>
    /// </summary>
    public sealed class JitterPhysicsAuthoringTests
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
        public void SourceIdIsCanonicalAndStableOnceAssigned()
        {
            JitterStaticBodySource source = CreateSource("Cover Cube 01");

            string first = source.EnsureSourceId();

            Assert.That(JitterPhysicsIdUtility.IsCanonical(first), Is.True);
            Assert.That(source.EnsureSourceId(), Is.EqualTo(first), "Repeated calls must not churn the id.");

            // Renaming the object must not change the artifact: the id is the identity,
            // the name is a label.
            source.gameObject.name = "Something Else Entirely";
            Assert.That(source.EnsureSourceId(), Is.EqualTo(first));
        }

        [Test]
        public void SourceIdIsSanitizedWhenSetExplicitly()
        {
            JitterStaticBodySource source = CreateSource("source");

            source.SetSourceId("Cover / Cube 01");

            Assert.That(source.SourceId, Is.EqualTo("cover_cube_01"));
            Assert.That(source.HasCanonicalSourceId, Is.True);
        }

        [Test]
        public void LevelIdIsCanonicalAndStableOnceAssigned()
        {
            JitterPhysicsLevel level = CreateLevel();

            string first = level.EnsureLevelId();

            Assert.That(JitterPhysicsIdUtility.IsCanonical(first), Is.True);
            Assert.That(level.EnsureLevelId(), Is.EqualTo(first));
        }

        [Test]
        public void OnlyExplicitlyMarkedSourcesUnderTheGeometryRootAreCollected()
        {
            JitterPhysicsLevel level = CreateLevel();
            var root = new GameObject("GeometryRoot");
            spawned.Add(root);
            SetGeometryRoot(level, root.transform);

            JitterStaticBodySource marked = CreateSource("Marked");
            marked.transform.SetParent(root.transform);

            // An unmarked collider under the root: never baked, because a designer must be
            // able to add scenery without changing the level hash.
            var unmarked = new GameObject("Unmarked", typeof(BoxCollider));
            unmarked.transform.SetParent(root.transform);
            spawned.Add(unmarked);

            // A marked source outside the root: out of scope for this level.
            JitterStaticBodySource outside = CreateSource("Outside");

            IReadOnlyList<JitterStaticBodySource> collected = level.CollectSources();

            Assert.That(collected, Has.Member(marked));
            Assert.That(collected, Has.No.Member(outside));
            Assert.That(collected.Count, Is.EqualTo(1));
        }

        [Test]
        public void InactiveSourcesAreNotCollected()
        {
            JitterPhysicsLevel level = CreateLevel();
            var root = new GameObject("GeometryRoot");
            spawned.Add(root);
            SetGeometryRoot(level, root.transform);

            JitterStaticBodySource active = CreateSource("Active");
            active.transform.SetParent(root.transform);

            JitterStaticBodySource inactive = CreateSource("Inactive");
            inactive.transform.SetParent(root.transform);
            inactive.gameObject.SetActive(false);

            IReadOnlyList<JitterStaticBodySource> collected = level.CollectSources();

            // Disabling an object is how a designer removes it from the level; an invisible
            // wall that still blocks movement would be the worst possible outcome here.
            Assert.That(collected, Has.Member(active));
            Assert.That(collected, Has.No.Member(inactive));
        }

        [Test]
        public void WorldProfileMapsToPortableSettings()
        {
            var profile = ScriptableObject.CreateInstance<JitterPhysicsWorldProfile>();
            spawned.Add(profile);

            PhysicsWorldSettings settings = profile.ToWorldSettings();

            Assert.That(settings.TickRate, Is.EqualTo(30));
            Assert.That(settings.SubstepCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(settings.Gravity.Y, Is.EqualTo(-9.81f).Within(1e-6f));
            Assert.That(settings.Gravity.IsFinite, Is.True);

            // Deterministic solving and single-threaded stepping are invariants of the
            // format, not authored values, so they cannot be misconfigured here.
            Assert.That(PhysicsWorldSettings.MultiThreaded, Is.False);
            Assert.That(PhysicsWorldSettings.DeterministicSolveMode, Is.EqualTo(1));
        }

        private JitterPhysicsLevel CreateLevel()
        {
            var gameObject = new GameObject("Level");
            spawned.Add(gameObject);
            return gameObject.AddComponent<JitterPhysicsLevel>();
        }

        private JitterStaticBodySource CreateSource(string name)
        {
            var gameObject = new GameObject(name, typeof(BoxCollider));
            spawned.Add(gameObject);
            return gameObject.AddComponent<JitterStaticBodySource>();
        }

        private static void SetGeometryRoot(JitterPhysicsLevel level, Transform root)
        {
            var serialized = new UnityEditor.SerializedObject(level);
            serialized.FindProperty("geometryRoot").objectReferenceValue = root;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}



