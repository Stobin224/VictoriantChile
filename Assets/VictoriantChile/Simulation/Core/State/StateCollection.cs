using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace VictoriantChile.Simulation.Core.State
{
    internal static class StateCollection
    {
        public static IReadOnlyList<T> SnapshotSorted<T>(IEnumerable<T> values, Func<T, string> keySelector, string name)
        {
            if (values == null)
            {
                throw new ArgumentNullException(name);
            }

            if (keySelector == null)
            {
                throw new ArgumentNullException(nameof(keySelector));
            }

            List<T> snapshot = new List<T>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (T value in values)
            {
                if (value == null)
                {
                    throw new ArgumentNullException(name, "Collection cannot contain null values.");
                }

                string key = keySelector(value);
                if (string.IsNullOrEmpty(key))
                {
                    throw new ArgumentException("Collection key cannot be null or empty.", name);
                }

                if (!seen.Add(key))
                {
                    throw new ArgumentException("Duplicate collection key: " + key, name);
                }

                snapshot.Add(value);
            }

            snapshot.Sort((left, right) => string.Compare(keySelector(left), keySelector(right), StringComparison.Ordinal));
            return Array.AsReadOnly(snapshot.ToArray());
        }

        public static IReadOnlyDictionary<string, T> MapById<T>(IEnumerable<T> values, Func<T, string> keySelector)
        {
            Dictionary<string, T> result = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (T value in values)
            {
                result.Add(keySelector(value), value);
            }

            return new ReadOnlyDictionary<string, T>(result);
        }
    }
}
