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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Json;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Transcoding;
using JsonDataSetMessage = Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage;

namespace UaLens.Plugins.PubSub;

internal sealed record PubSubFieldValue(
    string Name,
    string Type,
    Variant Value,
    string Text,
    StatusCode Status,
    DateTimeUtc SourceTimestamp,
    DateTimeUtc ServerTimestamp,
    bool Truncated);

internal sealed record PubSubMessageRow(
    DateTimeOffset ReceivedAt,
    string Publisher,
    ushort? WriterGroup,
    ushort Writer,
    uint? Sequence,
    string MessageType,
    StatusCode Status,
    DateTimeUtc PublishedAt,
    string MetadataVersion,
    int FieldCount,
    string Security);

internal sealed record PubSubMetadataRow(
    string Publisher,
    ushort WriterGroup,
    ushort Writer,
    string Name,
    uint MajorVersion,
    uint MinorVersion,
    string Fields);

internal sealed record PubSubEvidence(DateTimeOffset Timestamp, string Area, StatusCode Status, string Detail);

internal sealed record PubSubObservationSnapshot(
    ArrayOf<PubSubFieldValue> Values,
    ArrayOf<PubSubMessageRow> Messages,
    ArrayOf<PubSubMetadataRow> Metadata,
    ArrayOf<PubSubEvidence> Evidence,
    long AcceptedDataSets,
    long RejectedDataSets,
    long EvictedMessages,
    long MetadataUpdates,
    long MetadataEvictions,
    long TruncatedValues,
    long DiscoveryDrops);

/// <summary>
/// Bounded, read-only observation sink. A received vector is prepared completely
/// before it atomically replaces local state; it never invokes a UA Write service.
/// </summary>
internal sealed class PubSubObservationStore : ISubscribedDataSetSink, IReceivedNetworkMessageSink
{
    public PubSubObservationStore(int retainedMessages, TimeProvider? clock = null)
    {
        if (retainedMessages is < 1 or > 512)
        {
            throw new ArgumentOutOfRangeException(nameof(retainedMessages));
        }
        m_capacity = retainedMessages;
        m_clock = clock ?? TimeProvider.System;
        TransportDiagnostics = new PubSubDiagnostics(PubSubDiagnosticsLevel.High, m_clock);
    }

    public IPubSubDiagnostics TransportDiagnostics { get; }

    public PubSubObservationSnapshot Snapshot()
    {
        lock (m_gate)
        {
            return new PubSubObservationSnapshot(
                m_values,
                [.. m_messages.Reverse()],
                [.. m_metadata.Values],
                [.. m_evidence.Reverse()],
                m_accepted,
                m_rejected,
                m_evicted,
                m_metadataUpdates,
                m_metadataEvictions,
                m_truncated,
                m_discoveryDrops);
        }
    }

    public async Task WaitForDataSetsAsync(long count, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task changed;
            lock (m_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (m_accepted >= count)
                {
                    return;
                }
                m_valuesChanged ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                changed = m_valuesChanged.Task;
            }
            await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask WriteAsync(IReadOnlyList<DataSetField> fields, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateFieldsCore(fields);
        var prepared = new PubSubFieldValue[fields.Count];
        long truncated = 0;
        bool delta = false;
        for (int i = 0; i < fields.Count; i++)
        {
            DataSetField field = fields[i];
            prepared[i] = PubSubValueDisplay.Create(field);
            if (prepared[i].Truncated)
            {
                truncated++;
            }
            delta |= field.FieldIndex >= 0;
        }

        lock (m_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (delta)
            {
                if (m_values.Count == 0 || fields.Any(field => field.FieldIndex < 0 ||
                    field.FieldIndex >= m_values.Count))
                {
                    m_rejected++;
                    throw new ServiceResultException(StatusCodes.BadNoData,
                        "A delta cannot be applied before a complete local dataset.");
                }
                PubSubFieldValue[] updated = [.. m_values];
                for (int i = 0; i < fields.Count; i++)
                {
                    updated[fields[i].FieldIndex] = prepared[i];
                }
                m_values = updated;
            }
            else if (prepared.Length > 0)
            {
                m_values = prepared;
            }
            m_accepted++;
            m_truncated += truncated;
            m_valuesChanged?.TrySetResult();
            m_valuesChanged = null;
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask OnReceivedAsync(
        ReceivedNetworkMessage received,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.CompletedTask;
        }
        PubSubNetworkMessage message = received.Message;
        lock (m_gate)
        {
            foreach (PubSubDataSetMessage dataSet in message.DataSetMessages
                .SafeSlice(0, PubSubConfigurationValidation.MaxFields))
            {
                bool hasSequence = dataSet switch
                {
                    UadpDataSetMessage uadp => (uadp.ContentMask & UadpDataSetMessageContentMask.SequenceNumber) != 0,
                    JsonDataSetMessage json => (json.ContentMask & JsonDataSetMessageContentMask.SequenceNumber) != 0,
                    _ => false
                };
                bool hasVersion = dataSet switch
                {
                    UadpDataSetMessage uadp => (uadp.ContentMask & UadpDataSetMessageContentMask.MajorVersion) != 0,
                    JsonDataSetMessage json => (json.ContentMask & JsonDataSetMessageContentMask.MetaDataVersion) != 0,
                    _ => false
                };
                string metadataVersion = hasVersion
                    ? string.Create(CultureInfo.InvariantCulture,
                        $"{dataSet.MetaDataVersion.MajorVersion}.{dataSet.MetaDataVersion.MinorVersion}")
                    : "not carried";
                if (m_messages.Count == m_capacity)
                {
                    m_messages.Dequeue();
                    m_evicted++;
                }
                m_messages.Enqueue(new PubSubMessageRow(
                    m_clock.GetUtcNow(),
                    PubSubValueDisplay.Bound(message.PublisherId.ToString(), 96),
                    message.WriterGroupId,
                    dataSet.DataSetWriterId,
                    hasSequence ? dataSet.SequenceNumber : null,
                    dataSet.MessageType.ToString(),
                    dataSet.Status,
                    dataSet.Timestamp,
                    metadataVersion,
                    dataSet.Fields.Count,
                    received.FrameSecured ? "Message security verified" : "No message-security evidence"));
            }
        }
        return ValueTask.CompletedTask;
    }

    public void ObserveMetadata(IDataSetMetaDataRegistry registry, DataSetMetaDataChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(change);
        lock (m_gate)
        {
            DataSetMetaDataType metadata = change.Current;
            if (metadata.Fields.Count > PubSubConfigurationValidation.MaxFields)
            {
                registry.Remove(change.Key);
                m_metadata.Remove((change.Key.PublisherId, change.Key.WriterGroupId, change.Key.DataSetWriterId));
                m_metadataEvictions++;
                RecordEvidence("Metadata", StatusCodes.BadEncodingLimitsExceeded,
                    "Metadata exceeded the 32-field limit and was removed from the document registry.");
                return;
            }
            ArrayOf<DataSetMetaDataKey> keys = registry.Keys;
            foreach (DataSetMetaDataKey key in keys)
            {
                if (registry.Keys.Count <= PubSubConfigurationValidation.MaxMetadata)
                {
                    break;
                }
                if (SameIdentity(key, change.Key))
                {
                    continue;
                }
                registry.Remove(key);
                m_metadata.Remove((key.PublisherId, key.WriterGroupId, key.DataSetWriterId));
                m_metadataEvictions++;
            }
            var identity = (change.Key.PublisherId, change.Key.WriterGroupId, change.Key.DataSetWriterId);
            if (!m_metadata.ContainsKey(identity) && m_metadata.Count >= PubSubConfigurationValidation.MaxMetadata)
            {
                m_metadata.Remove(m_metadata.Keys.First());
                m_metadataEvictions++;
            }
            m_metadata[identity] = new PubSubMetadataRow(
                PubSubValueDisplay.Bound(change.Key.PublisherId.ToString(), 96),
                change.Key.WriterGroupId,
                change.Key.DataSetWriterId,
                PubSubValueDisplay.Bound(metadata.Name, 96),
                metadata.ConfigurationVersion.MajorVersion,
                metadata.ConfigurationVersion.MinorVersion,
                string.Join(", ", metadata.Fields.ToList().Select(field =>
                    PubSubValueDisplay.Bound(field.Name, 64) + ": " + ((BuiltInType)field.BuiltInType).ToString())));
            m_metadataUpdates++;
        }
    }

    public void RecordEvidence(string area, StatusCode status, string safeDetail)
    {
        lock (m_gate)
        {
            if (m_evidence.Count >= PubSubConfigurationValidation.MaxEvidence)
            {
                m_evidence.Dequeue();
            }
            m_evidence.Enqueue(new PubSubEvidence(
                m_clock.GetUtcNow(),
                PubSubValueDisplay.Bound(area, 48),
                status,
                PubSubValueDisplay.Bound(safeDetail, PubSubConfigurationValidation.MaxValueCharacters)));
        }
    }

    public void BeginDiscovery()
    {
        lock (m_gate)
        {
            m_discoveryRemaining = PubSubConfigurationValidation.MaxDiscoveryResponses;
        }
    }

    public void EndDiscovery()
    {
        lock (m_gate)
        {
            m_discoveryRemaining = -1;
        }
    }

    public bool AcceptDiscoveryResponse()
    {
        lock (m_gate)
        {
            if (m_discoveryRemaining < 0)
            {
                return true;
            }
            if (m_discoveryRemaining == 0)
            {
                m_discoveryDrops++;
                return false;
            }
            m_discoveryRemaining--;
            return true;
        }
    }

    public void ValidateFields(ArrayOf<DataSetField> fields)
    {
        if (fields.Count > PubSubConfigurationValidation.MaxFields)
        {
            throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
        }
        ValidateFieldsCore(fields.ToList());
    }

    private void ValidateFieldsCore(IReadOnlyList<DataSetField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        bool valid = fields.Count <= PubSubConfigurationValidation.MaxFields;
        var indices = new HashSet<int>();
        for (int i = 0; valid && i < fields.Count; i++)
        {
            DataSetField field = fields[i];
            valid = field is not null && field.FieldIndex >= -1 &&
                field.FieldIndex < PubSubConfigurationValidation.MaxFields &&
                indices.Add(field.FieldIndex < 0 ? i : field.FieldIndex);
        }
        if (!valid)
        {
            lock (m_gate)
            {
                m_rejected++;
            }
            RecordEvidence("Local sink", StatusCodes.BadEncodingLimitsExceeded,
                "The whole dataset was rejected: invalid, duplicate, or excessive fields.");
            throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
        }
    }

    private static bool SameIdentity(in DataSetMetaDataKey left, in DataSetMetaDataKey right)
    {
        return left.PublisherId == right.PublisherId && left.WriterGroupId == right.WriterGroupId &&
            left.DataSetWriterId == right.DataSetWriterId;
    }

    private readonly Lock m_gate = new();
    private readonly int m_capacity;
    private readonly TimeProvider m_clock;
    private readonly Queue<PubSubMessageRow> m_messages = [];
    private readonly Queue<PubSubEvidence> m_evidence = [];
    private readonly Dictionary<(PublisherId, ushort, ushort), PubSubMetadataRow> m_metadata = [];
    private ArrayOf<PubSubFieldValue> m_values = [];
    private long m_accepted;
    private long m_rejected;
    private long m_evicted;
    private long m_metadataUpdates;
    private long m_metadataEvictions;
    private long m_truncated;
    private long m_discoveryDrops;
    private int m_discoveryRemaining = -1;
    private TaskCompletionSource? m_valuesChanged;
}

/// <summary>
/// Bounds displayed/retained values without boxing unions or expanding arbitrary object graphs.
/// </summary>
internal static class PubSubValueDisplay
{
    public static PubSubFieldValue Create(DataSetField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        Variant value = field.Value;
        string type = value.TypeInfo.ToString();
        string text;
        bool truncated = false;
        if (value.IsNull)
        {
            text = "(null)";
        }
        else if (value.TryGetValue(out string? stringValue))
        {
            text = Bound(stringValue, PubSubConfigurationValidation.MaxValueCharacters);
            truncated = stringValue?.Length > PubSubConfigurationValidation.MaxValueCharacters;
            value = new Variant(text);
        }
        else if (value.TryGetValue(out bool boolean))
        {
            text = boolean ? "True" : "False";
        }
        else if (value.TryGetValue(out sbyte int8))
        {
            text = int8.ToString(CultureInfo.InvariantCulture);
        }
        else if (value.TryGetValue(out byte uint8))
        {
            text = uint8.ToString(CultureInfo.InvariantCulture);
        }
        else if (value.TryGetValue(out short int16))
        {
            text = int16.ToString(CultureInfo.InvariantCulture);
        }
        else if (value.TryGetValue(out ushort uint16))
        {
            text = uint16.ToString(CultureInfo.InvariantCulture);
        }
        else if (value.TryGetValue(out int int32))
        {
            text = int32.ToString(CultureInfo.InvariantCulture);
        }
        else if (value.TryGetValue(out uint uint32))
        {
            text = uint32.ToString(CultureInfo.InvariantCulture);
        }
        else if (value.TryGetValue(out long int64))
        {
            text = int64.ToString(CultureInfo.InvariantCulture);
        }
        else if (value.TryGetValue(out ulong uint64))
        {
            text = uint64.ToString(CultureInfo.InvariantCulture);
        }
        else if (value.TryGetValue(out float float32))
        {
            text = float32.ToString("G9", CultureInfo.InvariantCulture);
        }
        else if (value.TryGetValue(out double float64))
        {
            text = float64.ToString("G17", CultureInfo.InvariantCulture);
        }
        else if (value.TryGetValue(out DateTimeUtc dateTime))
        {
            text = dateTime.ToString("O", CultureInfo.InvariantCulture);
        }
        else if (value.TryGetValue(out Uuid uuid))
        {
            text = uuid.ToString();
        }
        else if (value.TryGetValue(out ByteString bytes))
        {
            int length = Math.Min(bytes.Length, 128);
            text = Convert.ToHexString(bytes.Span[..length]);
            truncated = bytes.Length > length;
            value = truncated ? Variant.Null : new Variant(new ByteString(bytes.Span.ToArray()));
        }
        else
        {
            text = "Value received; structured/array display is not expanded by this bounded viewer.";
            value = Variant.Null;
            truncated = true;
        }
        return new PubSubFieldValue(
            Bound(field.Name, 64),
            type,
            value,
            text,
            field.StatusCode,
            field.SourceTimestamp,
            field.ServerTimestamp,
            truncated);
    }

    public static string Bound(string? value, int limit)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        return value.Length <= limit ? value : value[..limit] + "…";
    }
}
