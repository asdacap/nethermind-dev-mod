// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Nethermind.Core;
using Nethermind.Db;
using Prometheus;

namespace NethermindDevPlugin;

public class FakeColumnsDb<T>(
    Dictionary<T, IDb> innerDb
): IColumnsDb<T> where T : notnull
{
    public void Flush(bool onlyWal = false)
    {
        foreach (var keyValuePair in innerDb)
        {
            keyValuePair.Value.Flush(onlyWal);
        }
    }

    public void Dispose()
    {
    }

    public IDb GetColumnDb(T key)
    {
        return innerDb[key];
    }

    public IEnumerable<T> ColumnKeys => innerDb.Keys;
    public IColumnsWriteBatch<T> StartWriteBatch()
    {
        return new FakeWriteBatch(innerDb);
    }

    public IColumnDbSnapshot<T> CreateSnapshot()
    {
        return new FakeSnapshot(innerDb.ToDictionary((kv) => kv.Key, (kv) =>
        {
            return ((IKeyValueStoreWithSnapshot)kv.Value).CreateSnapshot();
        }));
    }

    private class FakeWriteBatch : IColumnsWriteBatch<T>
    {
        private Dictionary<T, IWriteBatch> _innerWriteBatch;

        public FakeWriteBatch(Dictionary<T, IDb> innerDb)
        {
            _innerWriteBatch = innerDb
                .ToDictionary((kv) => kv.Key, (kv) => kv.Value.StartWriteBatch());
        }

        public IWriteBatch GetColumnBatch(T key)
        {
            return _innerWriteBatch[key];
        }

        private static Histogram _rocksdBPersistenceTimes = Prometheus.Metrics.CreateHistogram("fake_column_dispose_time", "aha", new HistogramConfiguration()
        {
            LabelNames = new[] { "type" },
            Buckets = [1]
        });

        public void Dispose()
        {
            foreach (var keyValuePair in _innerWriteBatch)
            {
                long sw = Stopwatch.GetTimestamp();
                keyValuePair.Value.Dispose();
                _rocksdBPersistenceTimes.WithLabels(keyValuePair.Key.ToString()!).Observe(Stopwatch.GetTimestamp() - sw);
            }
        }
    }

    private class FakeSnapshot(Dictionary<T, IKeyValueStoreSnapshot> innerDb) : IColumnDbSnapshot<T>
    {
        public IReadOnlyKeyValueStore GetColumn(T key)
        {
            return innerDb[key];
        }

        public void Dispose()
        {
            foreach (var keyValuePair in innerDb)
            {
                keyValuePair.Value.Dispose();
            }
        }
    }
}
