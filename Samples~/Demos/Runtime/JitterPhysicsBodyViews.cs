using System.Collections.Generic;
using Jitter2;
using Jitter2.Dynamics;
using UnityEngine;

namespace DataSakura.JitterPhysics.Samples
{
    /// <summary>
    /// Keeps a renderer in step with a Jitter2 body, and retires both together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Jitter2 knows nothing about Unity: it simulates bodies, and nothing draws them. Every
    /// sample therefore needs this bridge, so it lives here once rather than three times.
    /// </para>
    /// <para>
    /// Poses are read in <c>LateUpdate</c>, after the world has been stepped, so a frame never
    /// draws a body where it was before the step it already took.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("DataSakura/Jitter Physics/Sample Body Views")]
    public sealed class JitterPhysicsBodyViews : MonoBehaviour
    {
        private readonly List<Entry> entries = new List<Entry>();

        private struct Entry
        {
            public RigidBody Body;
            public Transform View;
            public float ExpiresAt;
        }

        /// <summary>Number of tracked bodies.</summary>
        public int Count => entries.Count;

        /// <summary>
        /// Starts drawing <paramref name="view"/> at <paramref name="body"/>'s pose.
        /// </summary>
        /// <param name="body">Simulated body.</param>
        /// <param name="view">Transform to move. Destroyed with the body.</param>
        /// <param name="lifetimeSeconds">
        /// Seconds before both are removed, or zero to keep them indefinitely. Samples that fire
        /// projectiles rely on this: without it, a few minutes of shooting leaves thousands of
        /// bodies in the broadphase and the frame rate quietly collapses.
        /// </param>
        public void Track(RigidBody body, Transform view, float lifetimeSeconds = 0f)
        {
            entries.Add(new Entry
            {
                Body = body,
                View = view,
                ExpiresAt = lifetimeSeconds > 0f ? Time.time + lifetimeSeconds : float.PositiveInfinity,
            });
        }

        /// <summary>Removes every tracked body from <paramref name="world"/> and destroys its view.</summary>
        public void Clear(World world)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                Retire(world, entries[i]);
            }

            entries.Clear();
        }

        private void LateUpdate()
        {
            JitterPhysicsSampleWorld sampleWorld = GetComponent<JitterPhysicsSampleWorld>();
            if (sampleWorld == null || !sampleWorld.IsReady)
            {
                return;
            }

            float now = Time.time;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry entry = entries[i];

                if (entry.View == null || now >= entry.ExpiresAt)
                {
                    Retire(sampleWorld.World, entry);
                    entries.RemoveAt(i);
                    continue;
                }

                entry.View.SetPositionAndRotation(
                    entry.Body.Position.ToUnity(),
                    entry.Body.Orientation.ToUnity());
            }
        }

        private static void Retire(World world, Entry entry)
        {
            if (world != null)
            {
                world.Remove(entry.Body);
            }

            if (entry.View != null)
            {
                Destroy(entry.View.gameObject);
            }
        }
    }
}
