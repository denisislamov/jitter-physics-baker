using System.Security.Cryptography;
using System.Text;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.UnityArtifact;
using UnityEngine;

namespace DataSakura.JitterPhysics.Samples
{
    /// <summary>
    /// Re-checks a baked artifact at runtime and reports what a dedicated server would compare
    /// against before it lets anyone connect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The editor already validated this artifact when it was baked. That is not the same
    /// question. What ships is the file, and between baking and loading it can be replaced by a
    /// stale copy, truncated by a bad transfer, or paired with a manifest from a different bake -
    /// none of which the bake-time check can see.
    /// </para>
    /// <para>
    /// The interesting field is <c>runtimeCompatibilityId</c>. Two builds can hold byte-identical
    /// artifacts and still simulate differently, because the id also covers the Jitter sources,
    /// the precision profile and the conversion semantics. A client and a server that disagree on
    /// it must refuse each other, and this sample shows the value they would compare.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(JitterPhysicsSampleWorld))]
    [AddComponentMenu("DataSakura/Jitter Physics/Sample: Artifact Verification")]
    public sealed class JitterPhysicsArtifactVerificationSample : MonoBehaviour
    {
        [Tooltip("The same artifact the sample world loads.")]
        [SerializeField]
        private JitterPhysicsArtifactAsset artifact;

        [Tooltip("Log the report once at start.")]
        [SerializeField]
        private bool reportOnStart = true;

        private JitterPhysicsSampleWorld sampleWorld;
        private string report = "not run";
        private bool passed;

        private void Awake() => sampleWorld = GetComponent<JitterPhysicsSampleWorld>();

        private void Start()
        {
            if (reportOnStart)
            {
                Verify();
            }
        }

        /// <summary>Runs every check and returns true when all of them pass.</summary>
        public bool Verify()
        {
            var text = new StringBuilder();
            passed = true;

            if (artifact == null)
            {
                report = "No artifact assigned.";
                passed = false;
                Debug.LogError($"[JitterPhysics] {report}", this);
                return false;
            }

            byte[] payload = artifact.GetPayloadBytes();
            Check(text, "payload present", payload != null && payload.Length > 0,
                payload == null ? "0 bytes" : $"{payload.Length} bytes");

            // Hashing the bytes that are actually in the build is the only way to tell a correct
            // artifact from a stale one carrying the right metadata.
            string actualHash = Sha256(payload);
            Check(text, "payload hash matches metadata",
                string.Equals(actualHash, artifact.ArtifactHash, System.StringComparison.OrdinalIgnoreCase),
                $"expected {Short(artifact.ArtifactHash)}, got {Short(actualHash)}");

            PhysicsArtifactResult decoded = JitterPhysicsArtifactLoader.Load(artifact);
            Check(text, "decodes and validates", decoded.Succeeded,
                decoded.Succeeded ? "ok" : $"{decoded.Error.Code}: {decoded.Error.Message}");

            if (decoded.Succeeded)
            {
                PhysicsArtifact value = decoded.Artifact;

                Check(text, "level id matches asset",
                    string.Equals(value.LevelId, artifact.LevelId, System.StringComparison.Ordinal),
                    $"asset '{artifact.LevelId}', binary '{value.LevelId}'");

                Check(text, "body count matches asset",
                    value.Bodies.Count == artifact.BodyCount,
                    $"asset {artifact.BodyCount}, binary {value.Bodies.Count}");

                Check(text, "tick rate matches asset",
                    value.WorldSettings.TickRate == artifact.TickRate,
                    $"asset {artifact.TickRate}, binary {value.WorldSettings.TickRate}");

                text.AppendLine($"  runtime compatibility id : {value.RuntimeCompatibilityId}");
                text.AppendLine($"  shapes / vertices / tris : {value.ShapeCount} / {value.VertexCount} / {value.TriangleCount}");
            }

            if (sampleWorld != null && sampleWorld.IsReady)
            {
                text.AppendLine($"  topology fingerprint     : {sampleWorld.TopologyFingerprint}");
                text.AppendLine(
                    "  A server that built this artifact must print the same fingerprint. A different");
                text.AppendLine(
                    "  one means the two sides created different geometry from the same file.");
            }

            report = text.ToString();

            if (passed)
            {
                Debug.Log($"[JitterPhysics] artifact verification passed\n{report}", this);
            }
            else
            {
                Debug.LogError($"[JitterPhysics] artifact verification FAILED\n{report}", this);
            }

            return passed;
        }

        private void Check(StringBuilder text, string what, bool ok, string detail)
        {
            passed &= ok;
            text.AppendLine($"  [{(ok ? "pass" : "FAIL")}] {what}: {detail}");
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(12f, Screen.height - 220f, 1000f, 24f),
                passed ? "Artifact verification: PASSED" : "Artifact verification: FAILED");
            GUI.Label(new Rect(12f, Screen.height - 196f, 1000f, 190f), report);
        }

        private static string Sha256(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return string.Empty;
            }

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                var text = new StringBuilder(hash.Length * 2);

                for (int i = 0; i < hash.Length; i++)
                {
                    text.Append(hash[i].ToString("x2"));
                }

                return text.ToString();
            }
        }

        private static string Short(string hash) =>
            string.IsNullOrEmpty(hash) || hash.Length <= 12 ? hash : hash.Substring(0, 12);
    }
}
