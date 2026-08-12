namespace DataSakura.JitterPhysics.Authoring
{
    /// <summary>
    /// Shared authoring presentation constants, so that every component of the package
    /// appears under one Add Component group and draws its gizmos in one palette.
    /// </summary>
    public static class JitterPhysicsAuthoringConstants
    {
        /// <summary>Add Component menu group of the package.</summary>
        public const string ComponentMenuRoot = "Jitter Physics/";

        /// <summary>Menu root of every editor command of the package.</summary>
        public const string EditorMenuRoot = "Tools/DataSakura/Jitter Physics/";

        /// <summary>Inspector ordering base, keeping level above sources.</summary>
        public const int LevelMenuOrder = 0;

        /// <summary>Inspector ordering of static body sources.</summary>
        public const int SourceMenuOrder = 10;
    }
}
