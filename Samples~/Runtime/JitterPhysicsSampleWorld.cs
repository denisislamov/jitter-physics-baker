using System;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Integration;
using DataSakura.JitterPhysics.UnityArtifact;
using Jitter2;
using UnityEngine;

namespace DataSakura.JitterPhysics.Samples
{
    /// <summary>
    /// Owns a Jitter2 <see cref="World"/> for a scene, fills it from a baked artifact and advances
    /// it on the artifact's own fixed timestep.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The package deliberately does not own the tick loop, so this is where a game supplies one.
    /// It is a sample, not package API: a real game steps physics from wherever its simulation
    /// lives, which is rarely a <see cref="MonoBehaviour"/>.
    /// </para>
    /// <para>
    /// The timestep comes from the artifact rather than from Unity's fixed timestep. Both the
    /// client and the dedicated server must advance the same world by the same amount, and Unity's
    /// project setting is not something a server knows or shares.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("DataSakura/Jitter Physics/Sample World")]
    public sealed class JitterPhysicsSampleWorld : MonoBehaviour
    {
        [Tooltip("Artifact baked from this scene's static geometry. Bake it from the sample menu.")]
        [SerializeField]
        private JitterPhysicsArtifactAsset artifact;

        [Tooltip("Advance the world in Update. Turn off to step it yourself.")]
        [SerializeField]
        private bool stepAutomatically = true;

        [Tooltip("Upper bound on catch-up steps after a stall, so a hitch cannot spiral.")]
        [SerializeField]
        [Range(1, 8)]
        private int maxCatchUpSteps = 4;

        private float accumulator;

        /// <summary>The world, once the artifact has been applied. Null until then.</summary>
        public World World { get; private set; }

        /// <summary>True when the static world is built and safe to add bodies to.</summary>
        public bool IsReady { get; private set; }

        /// <summary>Why the world is not ready, when it is not.</summary>
        public string FailureMessage { get; private set; }

        /// <summary>Proves the client and the server built the same static topology.</summary>
        public string TopologyFingerprint { get; private set; }

        /// <summary>Static bodies created from the artifact.</summary>
        public int StaticBodyCount { get; private set; }

        /// <summary>Static shapes created from the artifact.</summary>
        public int StaticShapeCount { get; private set; }

        /// <summary>Ticks per second the artifact was baked for.</summary>
        public int TickRate { get; private set; }

        /// <summary>The level this world was built from.</summary>
        public string LevelId { get; private set; }

        /// <summary>Seconds per step.</summary>
        public float Timestep => TickRate > 0 ? 1f / TickRate : 0f;

        /// <summary>Raised after every completed step, on the main thread.</summary>
        public event Action Stepped;

        private void Awake()
        {
            if (artifact == null)
            {
                Fail("No artifact is assigned. Bake the sample level first.");
                return;
            }

            // Re-checked here even though the editor validated it at bake time. The asset on disk
            // is what actually ships, and it can be replaced by a stale copy long after baking.
            PhysicsArtifactResult loaded = JitterPhysicsArtifactLoader.Load(artifact);
            if (!loaded.Succeeded)
            {
                Fail($"{loaded.Error.Code}: {loaded.Error.Message}");
                return;
            }

            var world = new World();
            PhysicsWorldBuildResult built = JitterPhysicsWorldBuilder.Apply(world, loaded.Artifact);

            if (!built.Succeeded)
            {
                // The builder rolls back on failure, so the world holds nothing; disposing it here
                // keeps a half-built level from being handed to gameplay code that cannot tell.
                world.Dispose();
                Fail($"{built.Error.Code}: {built.Error.Message}");
                return;
            }

            World = world;
            LevelId = loaded.Artifact.LevelId;
            TickRate = loaded.Artifact.WorldSettings.TickRate;
            StaticBodyCount = built.BodyCount;
            StaticShapeCount = built.ShapeCount;
            TopologyFingerprint = built.TopologyFingerprint;
            IsReady = true;

            Debug.Log(
                $"[JitterPhysics] sample world ready level={LevelId} "
                + $"topology={Short(TopologyFingerprint)} bodies={StaticBodyCount} "
                + $"shapes={StaticShapeCount} tickRate={TickRate} "
                + $"elapsedMs={built.ElapsedMilliseconds:F1}",
                this);
        }

        private void Update()
        {
            if (!IsReady || !stepAutomatically)
            {
                return;
            }

            Advance(Time.deltaTime);
        }

        /// <summary>
        /// Advances the world by whole steps, holding the remainder for next time.
        /// </summary>
        /// <remarks>
        /// Leftover time is kept rather than rounded away, and catch-up is capped: after a long
        /// stall, replaying every missed step would stall the frame further and make the next
        /// backlog worse.
        /// </remarks>
        /// <param name="deltaTime">Real time elapsed since the previous call, in seconds.</param>
        public void Advance(float deltaTime)
        {
            if (!IsReady)
            {
                return;
            }

            float timestep = Timestep;
            accumulator += Mathf.Max(0f, deltaTime);

            int steps = 0;
            while (accumulator >= timestep && steps < maxCatchUpSteps)
            {
                World.Step(timestep, multiThread: false);
                accumulator -= timestep;
                steps++;
                Stepped?.Invoke();
            }

            if (accumulator > timestep * maxCatchUpSteps)
            {
                accumulator = 0f;
            }
        }

        private void OnDestroy()
        {
            World?.Dispose();
            World = null;
            IsReady = false;
        }

        private void Fail(string message)
        {
            FailureMessage = message;
            IsReady = false;
            Debug.LogError($"[JitterPhysics] sample world not started: {message}", this);
        }

        private static string Short(string hash) =>
            string.IsNullOrEmpty(hash) || hash.Length <= 12 ? hash : hash.Substring(0, 12);
    }
}

