namespace DataSakura.JitterPhysics.Contracts
{
    /// <summary>
    /// Identity constants that every layer of the package agrees on: editor tooling, the
    /// portable codec, the Unity artifact asset and the generated server projection.
    /// <para>
    /// The three versions below are deliberately independent. <see cref="PackageVersion"/>
    /// is the UPM SemVer, <see cref="ArtifactSchemaVersion"/> only changes when the binary
    /// layout changes, and the runtime compatibility id (computed elsewhere) changes when
    /// the runtime semantics change even if the layout does not.
    /// </para>
    /// </summary>
    public static class JitterPhysicsPackage
    {
        /// <summary>UPM package name; also the folder name inside <c>Packages/</c>.</summary>
        public const string PackageName = "com.datasakura.jitter-physics-baker";

        /// <summary>
        /// UPM SemVer of this source tree. Kept in sync with <c>package.json</c> by a test,
        /// because the portable assemblies also compile outside Unity where the package
        /// manifest is not available.
        /// </summary>
        public const string PackageVersion = "0.0.2";

        /// <summary>Human readable name used by editor windows and log prefixes.</summary>
        public const string DisplayName = "DataSakura Jitter Physics Baker";

        /// <summary>Version of the binary artifact layout produced and accepted by this tree.</summary>
        public const int ArtifactSchemaVersion = 1;

        /// <summary>
        /// Assembly name of the Jitter2 core the package integrates with. The package
        /// references it by name (never by GUID) so that it resolves against whatever copy
        /// the consumer already has, wherever that copy lives.
        /// </summary>
        public const string JitterAssemblyName = "Jitter2.Core";

        /// <summary>Prefix for every log line the package writes.</summary>
        public const string LogPrefix = "[JitterPhysics] ";
    }
}
