using Jitter2.Dynamics;
using UnityEngine;

namespace DataSakura.JitterPhysics.Samples
{
    /// <summary>
    /// Drops bouncing balls onto the baked level: the shortest demonstration that the artifact
    /// really is collision geometry and not just data that loaded without complaining.
    /// </summary>
    /// <remarks>
    /// Watch for two things. A ball must come to rest <em>on</em> a surface rather than inside or
    /// through it, which is what proves the collider conversion kept its transform; and a resting
    /// ball must eventually go to sleep, which is what proves the world settings from the artifact
    /// were applied rather than left at Jitter2's defaults.
    /// </remarks>
    [RequireComponent(typeof(JitterPhysicsSampleWorld))]
    [RequireComponent(typeof(JitterPhysicsBodyViews))]
    [AddComponentMenu("DataSakura/Jitter Physics/Sample: Bouncing Ball")]
    public sealed class JitterPhysicsBouncingBallSample : MonoBehaviour
    {
        [Header("Spawning")]
        [Tooltip("Where balls appear.")]
        [SerializeField]
        private Vector3 spawnPoint = new Vector3(0f, 12f, 0f);

        [Tooltip("Horizontal spread, so repeated drops do not land on the same spot.")]
        [SerializeField]
        private float spawnSpread = 3f;

        [Tooltip("Balls dropped when the sample starts.")]
        [SerializeField]
        [Range(0, 32)]
        private int initialBalls = 5;

        [Header("Ball")]
        [SerializeField]
        [Range(0.1f, 2f)]
        private float radius = 0.5f;

        [SerializeField]
        [Range(0.1f, 20f)]
        private float mass = 1f;

        [Tooltip("0 lands dead, 1 returns almost all of the impact energy.")]
        [SerializeField]
        [Range(0f, 0.99f)]
        private float restitution = 0.75f;

        [SerializeField]
        [Range(0f, 2f)]
        private float friction = 0.3f;

        [Header("Input")]
        [SerializeField]
        private KeyCode dropKey = KeyCode.Space;

        [SerializeField]
        private KeyCode clearKey = KeyCode.Backspace;

        private JitterPhysicsSampleWorld sampleWorld;
        private JitterPhysicsBodyViews views;
        private int dropped;

        private void Awake()
        {
            sampleWorld = GetComponent<JitterPhysicsSampleWorld>();
            views = GetComponent<JitterPhysicsBodyViews>();
        }

        private void Start()
        {
            if (!sampleWorld.IsReady)
            {
                return;
            }

            for (int i = 0; i < initialBalls; i++)
            {
                Drop();
            }
        }

        private void Update()
        {
            if (!sampleWorld.IsReady)
            {
                return;
            }

            if (JitterPhysicsSampleInput.WasKeyPressedThisFrame(dropKey))
            {
                Drop();
            }

            if (JitterPhysicsSampleInput.WasKeyPressedThisFrame(clearKey))
            {
                views.Clear(sampleWorld.World);
                dropped = 0;
            }
        }

        /// <summary>Drops one ball and returns its body.</summary>
        public RigidBody Drop()
        {
            if (!sampleWorld.IsReady)
            {
                return null;
            }

            Vector3 position = spawnPoint + new Vector3(
                Random.Range(-spawnSpread, spawnSpread),
                Random.Range(0f, spawnSpread),
                Random.Range(-spawnSpread, spawnSpread));

            RigidBody body = JitterPhysicsSampleConvert.CreateSphere(
                sampleWorld.World, position, radius, mass, restitution, friction);

            GameObject view = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            view.name = $"Ball {dropped++}";

            // The visual collider would be a second, competing physics representation: Unity would
            // resolve it against Unity colliders while Jitter2 resolves the real one.
            Destroy(view.GetComponent<Collider>());
            view.transform.localScale = Vector3.one * (radius * 2f);
            view.transform.SetParent(transform, worldPositionStays: true);

            views.Track(body, view.transform);
            return body;
        }

        private void OnGUI()
        {
            if (!sampleWorld.IsReady)
            {
                GUI.Label(new Rect(12f, 12f, 900f, 24f),
                    $"Jitter world not ready: {sampleWorld.FailureMessage}");
                return;
            }

            int awake = 0;
            foreach (RigidBody body in sampleWorld.World.RigidBodies)
            {
                if (body.MotionType != MotionType.Static && body.IsActive)
                {
                    awake++;
                }
            }

            GUI.Label(new Rect(12f, 12f, 900f, 24f),
                $"level={sampleWorld.LevelId}  static bodies={sampleWorld.StaticBodyCount}  "
                + $"balls={views.Count}  awake={awake}  tick={sampleWorld.TickRate}Hz");
            GUI.Label(new Rect(12f, 34f, 900f, 24f),
                $"[{dropKey}] drop a ball   [{clearKey}] clear");
        }
    }
}
