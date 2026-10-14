/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// One immutable-schema, atomically updated source. All allocations are bounded before committing a sample.
    /// </summary>
    internal sealed class McpPubSubDataSetSource : IPublishedDataSetSource
    {
        /// <summary>
        /// Creates an inactive source and validates all initial values.
        /// </summary>
        internal McpPubSubDataSetSource(
            PubSubApplicationInput input, McpPubSubOptions options, IServiceMessageContext context, TimeProvider time)
        {
            m_input = input;
            m_options = options;
            m_context = context;
            m_time = time;
            m_fields = [.. input.Fields.ToList().Select(field => new DataSetField
            {
                Name = field.Name,
                Value = field.InitialValue is { } value
                    ? PubSubInputValidation.Value(field, value, options)
                    : PubSubInputValidation.DefaultValue(field, options)
            })];
            _ = McpPubSubDataSetBuffer.Encode(m_fields, options, context);
        }

        /// <inheritdoc/>
        public DataSetMetaDataType BuildMetaData()
        {
            var fields = new FieldMetaData[m_input.Fields.Count];
            for (int index = 0; index < fields.Length; index++)
            {
                PubSubFieldInput field = m_input.Fields[index];
                byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(m_input.DataSetName + "\0" + field.Name));
                fields[index] = new FieldMetaData
                {
                    Name = field.Name,
                    BuiltInType = (byte)field.Type,
                    DataType = new NodeId((uint)field.Type),
                    ValueRank = field.ValueRank,
                    DataSetFieldId = new Uuid(field.Id is null
                        ? new Guid(digest.AsSpan(0, 16)) : Guid.Parse(field.Id))
                };
            }
            return new DataSetMetaDataType
            {
                Name = m_input.DataSetName,
                DataSetClassId = m_input.DataSetClassId is null
                    ? Uuid.Empty : new Uuid(Guid.Parse(m_input.DataSetClassId)),
                Fields = new ArrayOf<FieldMetaData>(fields),
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 0 }
            };
        }

        /// <inheritdoc/>
        public ValueTask<PublishedDataSetSnapshot> SampleAsync(
            DataSetMetaDataType metaData, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_state)
            {
                return ValueTask.FromResult(new PublishedDataSetSnapshot(
                    metaData.ConfigurationVersion,
                    new ArrayOf<DataSetField>((DataSetField[])m_fields.Clone()),
                    DateTimeUtc.From(m_time.GetUtcNow())));
            }
        }

        /// <summary>
        /// Commits every supplied field together after validating duplicates, membership and total encoded size.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal JsonObject Update(PubSubPublishInput input)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentOutOfRangeException.ThrowIfLessThan(input.Fields.Count, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(input.Fields.Count, m_options.MaxFields);
            var changes = new Dictionary<string, Variant>(StringComparer.Ordinal);
            foreach (PubSubFieldValueInput value in input.Fields)
            {
                ArgumentNullException.ThrowIfNull(value);
                PubSubFieldInput? definition = m_input.Fields.Find(field => field.Name == value.Name);
                if (definition is null ||
                    !changes.TryAdd(value.Name,
                        PubSubInputValidation.Value(definition, value.Value, m_options)))
                {
                    throw new ArgumentException("Updates require unique, declared field names.");
                }
            }
            lock (m_state)
            {
                DataSetField[] proposed =
                [
                    .. m_fields.Select(field => changes.TryGetValue(field.Name, out Variant value)
                        ? new DataSetField { Name = field.Name, Value = value } : field)
                ];
                byte[] encoded = McpPubSubDataSetBuffer.Encode(proposed, m_options, m_context);
                m_fields = proposed;
                return new JsonObject
                {
                    ["updatedFields"] = changes.Count,
                    ["encodedSampleBytes"] = encoded.Length,
                    ["state"] = "sample-updated",
                    ["delivery"] = "next publishing interval; not an end-to-end delivery acknowledgement"
                };
            }
        }

        private readonly PubSubApplicationInput m_input;
        private readonly McpPubSubOptions m_options;
        private readonly IServiceMessageContext m_context;
        private readonly TimeProvider m_time;
        private readonly Lock m_state = new();
        private DataSetField[] m_fields;
    }

    /// <summary>
    /// Bounded decoded snapshots with exact byte accounting and explicit drop counters.
    /// </summary>
    internal sealed class McpPubSubDataSetBuffer : ISubscribedDataSetSink
    {
        /// <summary>
        /// Creates an empty buffer without opening any connection.
        /// </summary>
        internal McpPubSubDataSetBuffer(
            McpPubSubOptions options, IServiceMessageContext context, TimeProvider time)
        {
            m_options = options;
            m_context = context;
            m_time = time;
            m_rate = new McpPubSubRateBudget(options.MaxMessagesPerSecond, time);
        }

        /// <inheritdoc/>
        public ValueTask WriteAsync(
            IReadOnlyList<DataSetField> fields, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!m_rate.TryTake())
            {
                lock (m_state)
                {
                    m_rateDropped++;
                }
                return ValueTask.CompletedTask;
            }
            byte[] encoded;
            try
            {
                encoded = Encode(fields, m_options, m_context);
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or ServiceResultException)
            {
                lock (m_state)
                {
                    m_invalidDropped++;
                }
                return ValueTask.CompletedTask;
            }
            lock (m_state)
            {
                while (m_messages.Count > 0 &&
                    (m_messages.Count >= m_options.MaxBufferedMessages ||
                        m_bytes > m_options.MaxBufferedBytes - encoded.Length))
                {
                    m_bytes -= m_messages.Dequeue().Payload.Length;
                    m_overflowDropped++;
                }
                m_messages.Enqueue(new BufferedDataSet(encoded, m_time.GetUtcNow()));
                m_bytes += encoded.Length;
                m_received++;
            }
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Returns bounded snapshots in arrival order, removing only the returned page when requested.
        /// </summary>
        internal JsonObject Read(int maximum, bool clear)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maximum, m_options.MaxBufferedMessages);
            lock (m_state)
            {
                BufferedDataSet[] items = [.. m_messages.Take(maximum)];
                var values = new JsonArray();
                foreach (BufferedDataSet item in items)
                {
                    values.Add((JsonNode)new JsonObject
                    {
                        ["receivedAt"] = item.ReceivedAt,
                        ["encodedBytes"] = item.Payload.Length,
                        ["fields"] = JsonNode.Parse(item.Payload)
                    });
                }
                if (clear)
                {
                    foreach (BufferedDataSet item in items)
                    {
                        _ = m_messages.Dequeue();
                        m_bytes -= item.Payload.Length;
                    }
                }
                JsonObject result = StatusCore();
                result["dataSets"] = values;
                result["more"] = !clear ? items.Length < m_messages.Count : m_messages.Count > 0;
                return result;
            }
        }

        /// <summary>
        /// Snapshots all buffer counters, including dropped records rather than silently hiding overflow.
        /// </summary>
        internal JsonObject Status()
        {
            lock (m_state)
            {
                return StatusCore();
            }
        }

        /// <summary>
        /// Encodes the entire snapshot through the stack's typed data-value encoder under a hard allocation ceiling.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static byte[] Encode(
            IReadOnlyList<DataSetField> fields, McpPubSubOptions options, IServiceMessageContext context)
        {
            ArgumentNullException.ThrowIfNull(fields);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(fields.Count, options.MaxFields);
            var names = new HashSet<string>(StringComparer.Ordinal);
            using var stream = new McpBoundedMemoryStream(options.MaxMessageBytes);
            using (var encoder = new JsonEncoder(stream, context, JsonEncoderOptions.Verbose))
            {
                foreach (DataSetField field in fields)
                {
                    PubSubInputValidation.Text(field.Name, 128);
                    if (!names.Add(field.Name))
                    {
                        throw new ArgumentException("Received field names must be unique.");
                    }
                    var value = new DataValue(
                        field.Value, field.StatusCode, field.SourceTimestamp, field.ServerTimestamp);
                    encoder.WriteDataValue(field.Name, value);
                }
                _ = encoder.Close();
            }
            return stream.ToArray();
        }

        /// <summary>
        /// Builds the secret-free counter projection while holding the private state scope.
        /// </summary>
        private JsonObject StatusCore()
        {
            return new JsonObject
            {
                ["bufferedMessages"] = m_messages.Count,
                ["bufferedBytes"] = m_bytes,
                ["receivedDataSets"] = m_received,
                ["overflowDropped"] = m_overflowDropped,
                ["invalidDropped"] = m_invalidDropped,
                ["rateDropped"] = m_rateDropped
            };
        }

        private readonly McpPubSubOptions m_options;
        private readonly IServiceMessageContext m_context;
        private readonly TimeProvider m_time;
        private readonly McpPubSubRateBudget m_rate;
        private readonly Lock m_state = new();
        private readonly Queue<BufferedDataSet> m_messages = new();
        private int m_bytes;
        private long m_received;
        private long m_overflowDropped;
        private long m_invalidDropped;
        private long m_rateDropped;

        /// <summary>
        /// Owns a bounded immutable byte snapshot.
        /// </summary>
        private sealed record BufferedDataSet(byte[] Payload, DateTimeOffset ReceivedAt);
    }

    /// <summary>
    /// A monotonic, thread-safe one-second admission window shared by all publications on one connection.
    /// </summary>
    internal sealed class McpPubSubRateBudget
    {
        /// <summary>
        /// Creates a finite window without scheduling work.
        /// </summary>
        internal McpPubSubRateBudget(int limit, TimeProvider time)
        {
            m_limit = limit;
            m_time = time;
            m_started = time.GetTimestamp();
        }

        /// <summary>
        /// Consumes one unit or refuses without moving the window's start.
        /// </summary>
        internal bool TryTake()
        {
            lock (m_state)
            {
                long now = m_time.GetTimestamp();
                if (m_time.GetElapsedTime(m_started, now) >= TimeSpan.FromSeconds(1))
                {
                    m_started = now;
                    m_count = 0;
                }
                if (m_count >= m_limit)
                {
                    return false;
                }
                m_count++;
                return true;
            }
        }

        private readonly int m_limit;
        private readonly TimeProvider m_time;
        private readonly Lock m_state = new();
        private long m_started;
        private int m_count;
    }
}
