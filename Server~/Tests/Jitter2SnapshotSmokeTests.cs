using DataSakura.JitterPhysics.Contracts;
using Jitter2;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;
using NUnit.Framework;

namespace DataSakura.JitterPhysics.Server.Tests
{
    /// <summary>
    /// Proves that the dormant <c>Jitter2~/Runtime</c> snapshot compiles and simulates.
    /// <para>
    /// Unity never builds that folder, and the package only installs it on request, so
    /// without this project nothing would notice that the fallback copy the package ships
    /// is broken until a consumer tried to use it. The test also pins the world settings
    /// the artifact format treats as invariants — deterministic solving, single-threaded
    /// stepping — against the actual Jitter API rather than against a comment.
    /// </para>
    /// </summary>
    public sealed class Jitter2SnapshotSmokeTests
    {
        [Test]
        public void WorldAcceptsTheSettingsTheArtifactFormatRequires()
        {
            var world = new World();
            PhysicsWorldSettings settings = PhysicsWorldSettings.Default;

            world.Gravity = new JVector(settings.Gravity.X, settings.Gravity.Y, settings.Gravity.Z);
            world.SolveMode = SolveMode.Deterministic;
            world.SolverIterations = (settings.SolverIterations, settings.RelaxationIterations);

            Assert.That(world.SolveMode, Is.EqualTo(SolveMode.Deterministic));
            Assert.That(world.SolverIterations.solver, Is.EqualTo(settings.SolverIterations));
            Assert.That(world.SolverIterations.relaxation, Is.EqualTo(settings.RelaxationIterations));
            Assert.That(world.Gravity.Y, Is.EqualTo(settings.Gravity.Y).Within(1e-5f));
        }

        [Test]
        public void StaticBodyIsCreatedAndKeepsItsPose()
        {
            var world = CreateDeterministicWorld();

            RigidBody ground = world.CreateRigidBody();
            ground.AddShape(new BoxShape(new JVector(20f, 1f, 20f)));
            ground.Position = new JVector(0f, -0.5f, 0f);
            ground.Friction = 0.2f;
            ground.MotionType = MotionType.Static;

            for (int i = 0; i < 30; i++)
            {
                world.Step(1f / 30f, multiThread: false);
            }

            // A static body must not be moved by the solver: the artifact describes the
            // level, and a level that drifts is not a level.
            Assert.That(ground.MotionType, Is.EqualTo(MotionType.Static));
            Assert.That(ground.Position.Y, Is.EqualTo(-0.5f).Within(1e-5f));
        }

        [Test]
        public void DynamicBodyComesToRestOnStaticGeometry()
        {
            var world = CreateDeterministicWorld();

            RigidBody ground = world.CreateRigidBody();
            ground.AddShape(new BoxShape(new JVector(20f, 1f, 20f)));
            ground.Position = new JVector(0f, -0.5f, 0f);
            ground.MotionType = MotionType.Static;

            RigidBody falling = world.CreateRigidBody();
            falling.AddShape(new BoxShape(new JVector(1f, 1f, 1f)));
            falling.Position = new JVector(0f, 4f, 0f);

            for (int i = 0; i < 240; i++)
            {
                world.Step(1f / 30f, multiThread: false);
            }

            // Half the box above the ground surface at y = 0. The tolerance is generous on
            // purpose: this asserts that collision against baked static geometry works at
            // all, not that a particular solver produces a particular penetration.
            Assert.That(falling.Position.Y, Is.EqualTo(0.5f).Within(0.1f));
            Assert.That(falling.Position.Y, Is.GreaterThan(0f), "The body fell through the static ground.");
        }

        [Test]
        public void SnapshotIsSinglePrecisionAsTheLockDeclares()
        {
            // The compile profile in jitter2.lock.json says f32, and the runtime
            // compatibility id is derived from it. A snapshot silently built with
            // USE_DOUBLE_PRECISION would make a client and a server disagree while every
            // hash still matched.
            Assert.That(typeof(Real), Is.EqualTo(typeof(float)));
            Assert.That(JitterPhysicsSemantics.PrecisionMode, Is.EqualTo("f32"));
        }

        [Test]
        public void RepeatedIdenticalSimulationsProduceIdenticalResults()
        {
            JVector first = SimulateFallingBox();
            JVector second = SimulateFallingBox();

            // Same process, same inputs, same result: this is the topology/step determinism
            // the package relies on within one runtime. Bit-exactness *across* runtimes is
            // explicitly not claimed.
            Assert.That(second.X, Is.EqualTo(first.X));
            Assert.That(second.Y, Is.EqualTo(first.Y));
            Assert.That(second.Z, Is.EqualTo(first.Z));
        }

        private static JVector SimulateFallingBox()
        {
            var world = CreateDeterministicWorld();

            RigidBody ground = world.CreateRigidBody();
            ground.AddShape(new BoxShape(new JVector(20f, 1f, 20f)));
            ground.Position = new JVector(0f, -0.5f, 0f);
            ground.MotionType = MotionType.Static;

            RigidBody falling = world.CreateRigidBody();
            falling.AddShape(new BoxShape(new JVector(1f, 1f, 1f)));
            falling.Position = new JVector(0.25f, 3f, -0.25f);

            for (int i = 0; i < 120; i++)
            {
                world.Step(1f / 30f, multiThread: false);
            }

            return falling.Position;
        }

        private static World CreateDeterministicWorld()
        {
            PhysicsWorldSettings settings = PhysicsWorldSettings.Default;
            var world = new World
            {
                Gravity = new JVector(settings.Gravity.X, settings.Gravity.Y, settings.Gravity.Z),
                SolveMode = SolveMode.Deterministic,
                SolverIterations = (settings.SolverIterations, settings.RelaxationIterations),
            };

            return world;
        }
    }
}

