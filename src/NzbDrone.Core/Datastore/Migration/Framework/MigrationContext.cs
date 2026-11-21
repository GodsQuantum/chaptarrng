using System;
using System.Threading;

namespace NzbDrone.Core.Datastore.Migration.Framework
{
    public class MigrationContext
    {
        private static readonly AsyncLocal<MigrationContext> ActiveContext = new();

        public static MigrationContext Current
        {
            get => ActiveContext.Value;
            set => ActiveContext.Value = value;
        }

        public MigrationType MigrationType { get; private set; }
        public long? DesiredVersion { get; set; }
        public Action<NzbDroneMigrationBase> BeforeMigration { get; set; }

        public MigrationContext(MigrationType migrationType, long? desiredVersion = null)
        {
            MigrationType = migrationType;
            DesiredVersion = desiredVersion;
        }
    }
}
