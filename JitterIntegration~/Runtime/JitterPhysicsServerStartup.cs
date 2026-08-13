using System;
using DataSakura.JitterPhysics.Contracts;
using Jitter2;

namespace DataSakura.JitterPhysics.Integration
{
    /// <summary>
    /// What the server expects the artifact to be before it agrees to run it.
    /// <para>
    /// Every field here is a claim the process makes about itself: which runtime semantics it
    /// was built with, which level it was launched to host, at which rate it ticks. Startup
    /// compares those claims with the artifact instead of adopting whatever the file says,
    /// because a server that quietly adapts to the artifact it happens to find is a server
    /// that will happily host the wrong map at the wrong tick rate.
    /// </para>
    /// </summary>
    public sealed class JitterPhysicsServerOptions
    {
        /// <summary>
        /// Runtime semantics id of this build. Required: an artifact baked for other semantics
        /// rebuilds into a different world, and the point of the id is that nobody has to
        /// notice that by hand.
        /// </summary>
        public string RuntimeCompatibilityId { get; }

        /// <summary>
        /// Level this process was launched to host, or <c>null</c> to accept whatever the
        /// artifact describes. Set it whenever the launcher knows the map: it turns "the
        /// deploy mounted the wrong volume" into a startup failure instead of a match where
        /// players spawn inside a different level.
        /// </summary>
        public string ExpectedLevelId { get; }

        /// <summary>
        /// Tick rate the server's loop actually runs at, or <c>0</c> to accept the artifact's.
        /// A mismatch is refused rather than absorbed: the client predicts at the rate the
        /// artifact was authored for, and a server stepping at another rate diverges by
        /// construction.
        /// </summary>
        public int TickRate { get; }

        public JitterPhysicsServerOptions(
            string runtimeCompatibilityId,
            string expectedLevelId = null,
            int tickRate = 0)
        {
            if (string.IsNullOrEmpty(runtimeCompatibilityId))
            {
                throw new ArgumentException(
                    "A server must know the runtime compatibility id it was built with.",
                    nameof(runtimeCompatibilityId));
            }

            RuntimeCompatibilityId = runtimeCompatibilityId;
            ExpectedLevelId = expectedLevelId;
            TickRate = tickRate;
        }
    }

    /// <summary>
    /// The result of bringing physics up: either a ready world, or the reason there is none.
    /// <para>
    /// There is deliberately no way to obtain a "partially ready" state. Connection approval is
    /// gated on <see cref="IsReady"/>, so a caller that forgets to check it gets an artifact of
    /// <c>null</c> and a world with no geometry rather than a match that starts without walls.
    /// </para>
    /// </summary>
    public sealed class JitterPhysicsServerState
    {
        /// <summary>The artifact the world was built from, or <c>null</c> when startup failed.</summary>
        public PhysicsArtifact Artifact { get; }

        /// <summary>Lowercase hex SHA-256 of the payload, for the handshake and the logs.</summary>
        public string ArtifactHash { get; }

        /// <summary>Where the artifact came from, as reported by the provider.</summary>
        public string Source { get; }

        /// <summary>Static topology hash, the value a client and a server compare to prove they agree.</summary>
        public string TopologyFingerprint { get; }

        /// <summary>Static bodies in the world.</summary>
        public int BodyCount { get; }

        /// <summary>Collision shapes in the world, counting one per mesh triangle.</summary>
        public int ShapeCount { get; }

        /// <summary>Milliseconds spent building the world.</summary>
        public double ElapsedMilliseconds { get; }

        /// <summary>Why startup failed; meaningful only when <see cref="IsReady"/> is false.</summary>
        public PhysicsArtifactError Error { get; }

        private JitterPhysicsServerState(
            PhysicsArtifact artifact,
            string artifactHash,
            string source,
            string topologyFingerprint,
            int bodyCount,
            int shapeCount,
            double elapsedMilliseconds,
            PhysicsArtifactError error)
        {
            Artifact = artifact;
            ArtifactHash = artifactHash;
            Source = source;
            TopologyFingerprint = topologyFingerprint;
            BodyCount = bodyCount;
            ShapeCount = shapeCount;
            ElapsedMilliseconds = elapsedMilliseconds;
            Error = error;
        }

        /// <summary>
        /// True when the static world is fully built. This is the only condition under which a
        /// server may start accepting players.
        /// </summary>
        public bool IsReady => !Error.IsError;

        /// <summary>Level the world was built for, or <c>null</c> when startup failed.</summary>
        public string LevelId => Artifact?.LevelId;

        /// <summary>Tick rate the level was authored for, or <c>0</c> when startup failed.</summary>
        public int TickRate => Artifact?.WorldSettings.TickRate ?? 0;

        /// <summary>
        /// Throws when the world is not ready. For call sites where continuing without physics
        /// is not a recoverable situation and an ignored return value would be a silent one.
        /// </summary>
        public JitterPhysicsServerState RequireReady()
        {
            if (!IsReady)
            {
                throw new InvalidOperationException(
                    JitterPhysicsPackage.LogPrefix + "Static physics world is not ready: " + Error);
            }

            return this;
        }

        /// <summary>
        /// The self-check line. A deployment smoke test greps for it, so it carries everything
        /// needed to tell two builds apart — level, artifact, topology, counts, tick rate — and
        /// nothing that would leak a full payload or a path a log reader should not see. Hashes
        /// are short by design: a mismatch is visible, a log line stays readable.
        /// </summary>
        public string SelfCheck
        {
            get
            {
                if (!IsReady)
                {
                    return JitterPhysicsPackage.LogPrefix + "physics self-check FAILED: " + Error
                        + " source=" + (Source ?? "<unknown>");
                }

                return JitterPhysicsPackage.LogPrefix + "physics self-check OK"
                    + " level=" + Artifact.LevelId
                    + " artifact=" + Short(ArtifactHash)
                    + " topology=" + Short(TopologyFingerprint)
                    + " bodies=" + BodyCount
                    + " shapes=" + ShapeCount
                    + " triangles=" + Artifact.TriangleCount
                    + " tickRate=" + Artifact.WorldSettings.TickRate
                    + " elapsedMs=" + ElapsedMilliseconds.ToString(
                        "F1", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        /// <inheritdoc/>
        public override string ToString() => SelfCheck;

        internal static JitterPhysicsServerState Ready(
            PhysicsArtifactLoadResult load,
            PhysicsWorldBuildResult build)
        {
            return new JitterPhysicsServerState(
                load.Artifact,
                load.ArtifactHash,
                load.Source,
                build.TopologyFingerprint,
                build.BodyCount,
                build.ShapeCount,
                build.ElapsedMilliseconds,
                default);
        }

        internal static JitterPhysicsServerState Failed(PhysicsArtifactError error, string source)
        {
            return new JitterPhysicsServerState(null, error.ArtifactHash, source, null, 0, 0, 0d, error);
        }

        private static string Short(string hash)
        {
            if (string.IsNullOrEmpty(hash))
            {
                return "<none>";
            }

            return hash.Length >= JitterPhysicsArtifactNaming.ShortHashLength
                ? hash.Substring(0, JitterPhysicsArtifactNaming.ShortHashLength)
                : hash;
        }
    }

    /// <summary>
    /// Brings the static physics world up on a dedicated server, in one call, before anything
    /// else is allowed to happen.
    /// <para>
    /// The package does not run a physics service and does not own the tick loop — this is a
    /// startup step inside the consumer's match server, not a process. What it does own is the
    /// order: obtain the artifact, check it against what this build claims to be, build the
    /// world, and only then report readiness. Approving a connection before that point is what
    /// produces a match where the server has no walls and every client is "cheating".
    /// </para>
    /// </summary>
    public static class JitterPhysicsServerStartup
    {
        /// <summary>
        /// Resolves the artifact from <paramref name="provider"/> and builds it into
        /// <paramref name="world"/>. Never throws for bad input: a server has to log the reason
        /// and exit cleanly, not unwind through a startup path.
        /// </summary>
        public static JitterPhysicsServerState Start(
            World world,
            IPhysicsArtifactProvider provider,
            JitterPhysicsServerOptions options)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            // The provider already enforces the runtime id, the payload hash and the manifest
            // cross-check, so anything it returns is decoded and consistent with itself.
            PhysicsArtifactLoadResult load = provider.Load(options.RuntimeCompatibilityId);
            if (!load.Succeeded)
            {
                return JitterPhysicsServerState.Failed(load.Error, load.Source ?? provider.Description);
            }

            PhysicsArtifactError expectationError = CheckExpectations(load, options);
            if (expectationError.IsError)
            {
                return JitterPhysicsServerState.Failed(expectationError, load.Source);
            }

            PhysicsWorldBuildResult build = JitterPhysicsWorldBuilder.Apply(world, load.Artifact);
            if (!build.Succeeded)
            {
                // The builder rolls its own work back, so the world is left as it was found.
                return JitterPhysicsServerState.Failed(build.Error, load.Source);
            }

            return JitterPhysicsServerState.Ready(load, build);
        }

        private static PhysicsArtifactError CheckExpectations(
            PhysicsArtifactLoadResult load,
            JitterPhysicsServerOptions options)
        {
            PhysicsArtifact artifact = load.Artifact;

            if (!string.IsNullOrEmpty(options.ExpectedLevelId)
                && !string.Equals(options.ExpectedLevelId, artifact.LevelId, StringComparison.Ordinal))
            {
                return new PhysicsArtifactError(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"This server was launched to host '{options.ExpectedLevelId}', but the artifact "
                    + $"describes '{artifact.LevelId}'.",
                    artifact.LevelId,
                    load.ArtifactHash);
            }

            if (options.TickRate != 0 && options.TickRate != artifact.WorldSettings.TickRate)
            {
                return new PhysicsArtifactError(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"This server steps at {options.TickRate} Hz, the level was baked for "
                    + $"{artifact.WorldSettings.TickRate} Hz; prediction assumes both sides agree.",
                    artifact.LevelId,
                    load.ArtifactHash);
            }

            return default;
        }
    }
}

