using System;
using System.Collections.Generic;

namespace VictoriantChile.Simulation.Core.State
{
    public sealed class GameStateContentMetadata
    {
        public GameStateContentMetadata(
            int contentPackVersion,
            int contentSchemaVersion,
            int minimumGameSchemaVersion,
            string defaultLanguage,
            IEnumerable<ContentFileIdentity> files)
        {
            if (contentPackVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(contentPackVersion), "Content pack version must be positive.");
            }

            if (contentSchemaVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(contentSchemaVersion), "Content schema version must be positive.");
            }

            if (minimumGameSchemaVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumGameSchemaVersion), "Minimum game schema version must be positive.");
            }

            if (string.IsNullOrEmpty(defaultLanguage))
            {
                throw new ArgumentException("Default language cannot be null or empty.", nameof(defaultLanguage));
            }

            ContentPackVersion = contentPackVersion;
            ContentSchemaVersion = contentSchemaVersion;
            MinimumGameSchemaVersion = minimumGameSchemaVersion;
            DefaultLanguage = defaultLanguage;
            Files = StateCollection.SnapshotSorted(files, item => item.RelativePath, nameof(files));
        }

        public int ContentPackVersion { get; }

        public int ContentSchemaVersion { get; }

        public int MinimumGameSchemaVersion { get; }

        public string DefaultLanguage { get; }

        public IReadOnlyList<ContentFileIdentity> Files { get; }
    }
}
