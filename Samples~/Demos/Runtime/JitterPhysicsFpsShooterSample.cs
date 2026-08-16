using Jitter2.Collision;
using Jitter2.Dynamics;
using Jitter2.LinearMath;
using UnityEngine;

namespace DataSakura.JitterPhysics.Samples
{
    /// <summary>
    /// A first-person player that walks the baked level, fires physical projectiles and shoots a
    /// hitscan ray at it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the sample that matters for a shooter, because it exercises the three things a
    /// shooter actually asks of static geometry: standing on it, being stopped by it, and hitting
    /// it. A level that merely loads can still fail all three.
    /// </para>
    /// <para>
    /// The player is a kinematic capsule moved by code and swept against the world, not a dynamic
    /// body pushed by the solver. A dynamic character slides down slopes, gets shoved by its own
    /// bullets and tips over; every shooter that tries it ends up fighting the solver instead of
    /// using it.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(JitterPhysicsSampleWorld))]
    [RequireComponent(typeof(JitterPhysicsBodyViews))]
    [AddComponentMenu("DataSakura/Jitter Physics/Sample: FPS Shooter")]
    public sealed class JitterPhysicsFpsShooterSample : MonoBehaviour
    {
        [Header("Player")]
        [SerializeField]
        private Vector3 spawnPoint = new Vector3(0f, 2f, -18f);

        [SerializeField]
        [Range(1f, 20f)]
        private float moveSpeed = 6f;

        [SerializeField]
        [Range(1f, 15f)]
        private float jumpSpeed = 5.5f;

        [SerializeField]
        [Range(0.1f, 1f)]
        private float capsuleRadius = 0.4f;

        [SerializeField]
        [Range(0.2f, 2f)]
        private float capsuleHeight = 1f;

        [SerializeField]
        [Range(0.5f, 10f)]
        private float mouseSensitivity = 2.5f;

        [Header("Weapon")]
        [Tooltip("Speed of a fired projectile, in metres per second.")]
        [SerializeField]
        [Range(5f, 120f)]
        private float muzzleVelocity = 35f;

        [SerializeField]
        [Range(0.05f, 0.5f)]
        private float bulletRadius = 0.12f;

        [SerializeField]
        [Range(0.05f, 5f)]
        private float bulletMass = 0.4f;

        [Tooltip("Seconds a projectile survives before it is removed from the world.")]
        [SerializeField]
        [Range(1f, 30f)]
        private float bulletLifetime = 8f;

        [SerializeField]
        [Range(0.02f, 1f)]
        private float fireInterval = 0.12f;

        [Tooltip("How far the hitscan shot reaches, in metres.")]
        [SerializeField]
        [Range(5f, 500f)]
        private float hitscanRange = 120f;

        private JitterPhysicsSampleWorld sampleWorld;
        private JitterPhysicsBodyViews views;
        private Camera view;
        private RigidBody player;

        private Vector3 velocity;
        private float yaw;
        private float pitch;
        private float nextFireTime;
        private bool grounded;
        private string lastHit = "-";

        private void Awake()
        {
            sampleWorld = GetComponent<JitterPhysicsSampleWorld>();
            views = GetComponent<JitterPhysicsBodyViews>();
            view = Camera.main;
        }

        private void Start()
        {
            if (!sampleWorld.IsReady)
            {
                return;
            }

            player = JitterPhysicsSampleConvert.CreateKinematicCapsule(
                sampleWorld.World, spawnPoint, capsuleRadius, capsuleHeight);

            yaw = transform.eulerAngles.y;
            Cursor.lockState = CursorLockMode.Locked;
        }

        private void Update()
        {
            if (!sampleWorld.IsReady || player == null)
            {
                return;
            }

            Look();
            Move();
            Shoot();

            if (JitterPhysicsSampleInput.WasKeyPressedThisFrame(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
            }
        }

        private void Look()
        {
            Vector2 look = JitterPhysicsSampleInput.LookDelta;
            yaw += look.x * mouseSensitivity;
            pitch = Mathf.Clamp(pitch - look.y * mouseSensitivity, -85f, 85f);
        }

        private void Move()
        {
            Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
            Vector2 move = JitterPhysicsSampleInput.Move;
            Vector3 wish = facing * new Vector3(move.x, 0f, move.y);

            if (wish.sqrMagnitude > 1f)
            {
                wish.Normalize();
            }

            velocity.x = wish.x * moveSpeed;
            velocity.z = wish.z * moveSpeed;

            // Gravity is taken from the artifact, not from Unity's Physics settings: the server
            // uses the artifact's value, and a client falling at a different rate would disagree
            // about where the player is standing.
            velocity.y += sampleWorld.World.Gravity.Y * Time.deltaTime;

            if (grounded && velocity.y < 0f)
            {
                velocity.y = 0f;

                if (JitterPhysicsSampleInput.WasKeyPressedThisFrame(KeyCode.Space))
                {
                    velocity.y = jumpSpeed;
                    grounded = false;
                }
            }

            Vector3 position = player.Position.ToUnity();
            Vector3 target = position + velocity * Time.deltaTime;

            target = ResolveHorizontal(position, target);
            target = ResolveVertical(target);

            player.Position = target.ToJitter();

            if (view != null)
            {
                view.transform.SetPositionAndRotation(
                    target + Vector3.up * (capsuleHeight * 0.5f + capsuleRadius * 0.5f),
                    Quaternion.Euler(pitch, yaw, 0f));
            }
        }

        /// <summary>
        /// Stops the player at the first surface in the way horizontally.
        /// </summary>
        /// <remarks>
        /// A ray from the capsule centre, not a sweep of the capsule itself: this is a sample, and
        /// a ray is enough to show the baked walls are solid. A shipping character controller wants
        /// a proper shape cast, or it will clip corners the ray misses.
        /// </remarks>
        private Vector3 ResolveHorizontal(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            delta.y = 0f;

            float distance = delta.magnitude;
            if (distance <= Mathf.Epsilon)
            {
                return to;
            }

            Vector3 direction = delta / distance;
            float probe = distance + capsuleRadius;

            if (Raycast(from, direction, probe, out _, out _, out float hitDistance))
            {
                float allowed = Mathf.Max(0f, hitDistance - capsuleRadius);
                return from + direction * allowed + Vector3.up * (to.y - from.y);
            }

            return to;
        }

        private Vector3 ResolveVertical(Vector3 target)
        {
            float feetOffset = capsuleHeight * 0.5f + capsuleRadius;
            float probe = feetOffset + 0.15f;

            grounded = false;

            if (Raycast(target, Vector3.down, probe, out _, out _, out float hitDistance)
                && velocity.y <= 0f)
            {
                grounded = true;
                velocity.y = 0f;
                return new Vector3(target.x, target.y - hitDistance + feetOffset, target.z);
            }

            return target;
        }

        private void Shoot()
        {
            if (JitterPhysicsSampleInput.IsPrimaryButtonPressed && Time.time >= nextFireTime)
            {
                nextFireTime = Time.time + fireInterval;
                FireProjectile();
            }

            if (JitterPhysicsSampleInput.WasSecondaryButtonPressedThisFrame)
            {
                FireHitscan();
            }
        }

        /// <summary>Fires a physical projectile that collides with the level and with other bodies.</summary>
        public void FireProjectile()
        {
            Vector3 origin = MuzzlePosition();
            Vector3 direction = LookDirection();

            RigidBody bullet = JitterPhysicsSampleConvert.CreateSphere(
                sampleWorld.World, origin, bulletRadius, bulletMass, restitution: 0.25f, friction: 0.5f);
            bullet.Velocity = (direction * muzzleVelocity).ToJitter();

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "Bullet";
            Destroy(visual.GetComponent<Collider>());
            visual.transform.localScale = Vector3.one * (bulletRadius * 2f);
            visual.transform.SetParent(transform, worldPositionStays: true);

            views.Track(bullet, visual.transform, bulletLifetime);
        }

        /// <summary>Fires an instant ray and reports what it hit.</summary>
        public void FireHitscan()
        {
            Vector3 origin = MuzzlePosition();
            Vector3 direction = LookDirection();

            if (!Raycast(origin, direction, hitscanRange, out RigidBody body, out Vector3 normal,
                    out float distance))
            {
                lastHit = "miss";
                Debug.DrawRay(origin, direction * hitscanRange, Color.grey, 1f);
                return;
            }

            Vector3 point = origin + direction * distance;
            bool isStatic = body != null && body.MotionType == MotionType.Static;
            lastHit = $"{(isStatic ? "level" : "body")} at {distance:F1} m";

            Debug.DrawLine(origin, point, isStatic ? Color.cyan : Color.red, 1f);
            Debug.DrawRay(point, normal, Color.yellow, 1f);

            // A dynamic body that is hit should react, so the ray is not merely a query result.
            if (body != null && body.MotionType == MotionType.Dynamic)
            {
                body.ApplyImpulse((direction * 2f).ToJitter());
            }
        }

        private bool Raycast(
            Vector3 origin,
            Vector3 direction,
            float maxDistance,
            out RigidBody body,
            out Vector3 normal,
            out float distance)
        {
            body = null;
            normal = Vector3.up;
            distance = 0f;

            JVector jOrigin = origin.ToJitter();
            JVector jDirection = direction.normalized.ToJitter();

            // The player's own capsule is filtered out. Without it every shot hits the shooter, and
            // every downward ground probe reports the character standing on itself.
            bool hit = sampleWorld.World.DynamicTree.RayCast(
                jOrigin,
                jDirection,
                maxDistance,
                proxy => JitterPhysicsSampleConvert.BodyOf(proxy) != player,
                null,
                out IDynamicTreeProxy hitProxy,
                out JVector hitNormal,
                out float lambda);

            if (!hit)
            {
                return false;
            }

            body = JitterPhysicsSampleConvert.BodyOf(hitProxy);
            normal = hitNormal.ToUnity();
            distance = lambda;
            return true;
        }

        private Vector3 MuzzlePosition() =>
            player.Position.ToUnity()
            + Vector3.up * (capsuleHeight * 0.5f)
            + LookDirection() * (capsuleRadius + bulletRadius + 0.05f);

        private Vector3 LookDirection() => Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;

        private void OnGUI()
        {
            if (!sampleWorld.IsReady)
            {
                GUI.Label(new Rect(12f, 12f, 900f, 24f),
                    $"Jitter world not ready: {sampleWorld.FailureMessage}");
                return;
            }

            GUI.Label(new Rect(12f, 12f, 900f, 24f),
                $"level={sampleWorld.LevelId}  grounded={grounded}  projectiles={views.Count}  "
                + $"last ray: {lastHit}");
            GUI.Label(new Rect(12f, 34f, 900f, 24f),
                "WASD move   Space jump   LMB projectiles   RMB hitscan   Esc release cursor");

            // A crosshair, because aiming at geometry is the whole point of the sample.
            GUI.Label(new Rect(Screen.width * 0.5f - 4f, Screen.height * 0.5f - 10f, 20f, 20f), "+");
        }
    }
}

