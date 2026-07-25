/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace TestData
{
    /// <summary>
    /// A class that provides access to archived data.
    /// </summary>
    internal sealed class HistoryArchive : IDisposable
    {
        public HistoryArchive(ITelemetryContext telemetry)
        {
            m_logger = telemetry.CreateLogger<HistoryArchive>();
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            m_updateTimer?.Dispose();
            m_updateTimer = null;
        }

        /// <summary>
        /// Returns an object that can be used to browse the archive.
        /// </summary>
        public HistoryFile GetHistoryFile(NodeId nodeId)
        {
            lock (m_lock)
            {
                if (m_records == null)
                {
                    return null;
                }

                if (!m_records.TryGetValue(nodeId, out HistoryRecord record))
                {
                    return null;
                }

                return new HistoryFile(m_lock, record.RawData);
            }
        }

        /// <summary>
        /// Returns a stable request-local snapshot of annotation timestamps.
        /// Annotations are deliberately kept outside RawData.
        /// </summary>
        public IReadOnlyList<DateTime> GetAnnotationTimestamps(NodeId nodeId)
        {
            lock (m_lock)
            {
                if (m_annotations == null ||
                    !m_annotations.TryGetValue(nodeId, out List<DateTime> annotations))
                {
                    return Array.Empty<DateTime>();
                }

                return annotations.ToArray();
            }
        }

        /// <summary>
        /// Creates a new record in the archive.
        /// </summary>
        public void CreateRecord(
            NodeId nodeId,
            BuiltInType dataType,
            bool useDeterministicCttPattern = false)
        {
            lock (m_lock)
            {
                var record = new HistoryRecord
                {
                    RawData = [],
                    Historizing = true
                };

                DateTime now = DateTime.UtcNow;

                for (int ii = 1000; ii >= 0; ii--)
                {
                    var entry = new HistoryEntry
                    {
                        Value = new DataValue { ServerTimestamp = now.AddSeconds(-(ii * 10)) }
                    };
                    entry.Value.SourceTimestamp = entry.Value.ServerTimestamp.AddMilliseconds(1234);
                    entry.IsModified = false;

                    if (useDeterministicCttPattern)
                    {
                        int sampleIndex = 1000 - ii;
                        entry.Value.Value = CreateScalarValue(dataType, sampleIndex, now);
                        entry.Value.StatusCode = GetSeededStatusCode(sampleIndex);
                    }
                    else
                    {
                        // Preserve the original TestData Int32 fixture byte-for-byte
                        // in meaning: oldest value 1000 through newest value 0.
                        switch (dataType)
                        {
                            case BuiltInType.Int32:
                                entry.Value.Value = ii;
                                break;
                        }
                    }

                    record.RawData.Add(entry);
                }

                m_records ??= [];

                m_records[nodeId] = record;

                // Deterministic in-memory annotation sidecar. Two annotations
                // intentionally share a timestamp to exercise multiplicity.
                m_annotations ??= [];
                m_annotations[nodeId] =
                [
                    now.AddSeconds(-7500),
                    now.AddSeconds(-5000),
                    now.AddSeconds(-5000),
                    now.AddSeconds(-2500)
                ];

                m_updateTimer ??= new Timer(OnUpdate, null, 10000, 10000);
            }
        }

        /// <summary>
        /// Periodically adds new values into the archive.
        /// </summary>
        private void OnUpdate(object state)
        {
            try
            {
                DateTime now = DateTime.UtcNow;

                lock (m_lock)
                {
                    foreach (HistoryRecord record in m_records.Values)
                    {
                        if (!record.Historizing || record.RawData.Count >= 2000)
                        {
                            continue;
                        }

                        var entry = new HistoryEntry
                        {
                            Value = new DataValue { ServerTimestamp = now }
                        };
                        entry.Value.SourceTimestamp = entry.Value.ServerTimestamp
                            .AddMilliseconds(-4567);
                        entry.IsModified = false;

                        entry.Value.Value = CreateNextScalarValue(record);

                        record.RawData.Add(entry);
                    }
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error updating history.");
            }
        }

        private static object CreateNextScalarValue(HistoryRecord record)
        {
            object lastValue = record.RawData[^1].Value.Value;
            BuiltInType dataType = TypeInfo.Construct(lastValue).BuiltInType;
            return dataType switch
            {
                BuiltInType.Boolean => !(bool)lastValue,
                BuiltInType.SByte => (sbyte)((sbyte)lastValue + 1),
                BuiltInType.Byte => (byte)((byte)lastValue + 1),
                BuiltInType.Int16 => (short)((short)lastValue + 1),
                BuiltInType.UInt16 => (ushort)((ushort)lastValue + 1),
                BuiltInType.Int32 => (int)lastValue + 1,
                BuiltInType.UInt32 => (uint)lastValue + 1,
                BuiltInType.Int64 => (long)lastValue + 1,
                BuiltInType.UInt64 => (ulong)lastValue + 1,
                BuiltInType.Float => (float)lastValue + 1,
                BuiltInType.Double => (double)lastValue + 1,
                BuiltInType.String => ((int)record.RawData.Count).ToString(
                    CultureInfo.InvariantCulture),
                BuiltInType.DateTime => ((DateTime)lastValue).AddSeconds(1),
                BuiltInType.Guid => Guid.NewGuid(),
                BuiltInType.ByteString => BitConverter.GetBytes(record.RawData.Count),
                _ => lastValue
            };
        }

        private static object CreateScalarValue(
            BuiltInType dataType,
            int value,
            DateTime now)
        {
            return dataType switch
            {
                BuiltInType.Boolean => (value & 1) == 0,
                BuiltInType.SByte => (sbyte)(value % 100),
                BuiltInType.Byte => (byte)(value % 200),
                BuiltInType.Int16 => (short)value,
                BuiltInType.UInt16 => (ushort)value,
                BuiltInType.Int32 => value,
                BuiltInType.UInt32 => (uint)value,
                BuiltInType.Int64 => (long)value,
                BuiltInType.UInt64 => (ulong)value,
                BuiltInType.Float => (float)value,
                BuiltInType.Double => (double)value,
                BuiltInType.String => value.ToString(CultureInfo.InvariantCulture),
                BuiltInType.DateTime => now.AddSeconds(value),
                BuiltInType.Guid => new Guid(value, 0, 0, new byte[8]),
                BuiltInType.ByteString => BitConverter.GetBytes(value),
                _ => value
            };
        }

        private static StatusCode GetSeededStatusCode(int sampleIndex)
        {
            return (sampleIndex % 10) switch
            {
                7 => StatusCodes.BadDataUnavailable,
                9 => StatusCodes.UncertainSubstituteValue,
                _ => StatusCodes.Good
            };
        }

        private readonly Lock m_lock = new();
        private Timer m_updateTimer;
        private Dictionary<NodeId, HistoryRecord> m_records;
        private Dictionary<NodeId, List<DateTime>> m_annotations;
        private readonly ILogger m_logger;
    }

    /// <summary>
    /// A single entry in the archive.
    /// </summary>
    internal sealed class HistoryEntry
    {
        public DataValue Value;
        public bool IsModified;
    }

    /// <summary>
    /// A record in the archive.
    /// </summary>
    internal sealed class HistoryRecord
    {
        public List<HistoryEntry> RawData;
        public bool Historizing;
    }
}
