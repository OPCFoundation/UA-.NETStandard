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
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;

namespace UaLens.Plugins.PubSub;

internal sealed class PubSubSyntheticSource : IPublishedDataSetSource
{
    public PubSubSyntheticSource(PubSubConfiguration configuration, TimeProvider clock)
    {
        m_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        m_clock = clock ?? throw new ArgumentNullException(nameof(clock));
        m_metadata = PubSubStackConfiguration.CreateMetadata(configuration);
    }

    public DataSetMetaDataType BuildMetaData()
    {
        return m_metadata;
    }

    public ValueTask<PublishedDataSetSnapshot> SampleAsync(
        DataSetMetaDataType metaData,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int sequence = Interlocked.Increment(ref m_sequence);
        DateTimeOffset timestamp = m_clock.GetUtcNow();
        var fields = new DataSetField[m_configuration.Fields.Count];
        for (int i = 0; i < fields.Length; i++)
        {
            PubSubFieldConfiguration field = m_configuration.Fields[i];
            Variant value = field.Type switch
            {
                BuiltInType.Boolean => new Variant(sequence % 2 == 0),
                BuiltInType.Int32 => new Variant(sequence),
                BuiltInType.Double => new Variant((double)sequence),
                BuiltInType.DateTime => new Variant(timestamp.UtcDateTime),
                BuiltInType.String => new Variant(sequence.ToString(CultureInfo.InvariantCulture)),
                _ => throw new InvalidOperationException("This synthetic field type is not supported.")
            };
            fields[i] = new DataSetField
            {
                Name = field.Name,
                Value = value,
                StatusCode = StatusCodes.Good,
                SourceTimestamp = DateTimeUtc.From(timestamp),
                Encoding = m_configuration.EffectiveFieldMask == DataSetFieldContentMask.RawData
                    ? PubSubFieldEncoding.RawData
                    : m_configuration.EffectiveFieldMask == DataSetFieldContentMask.None
                        ? PubSubFieldEncoding.Variant
                        : PubSubFieldEncoding.DataValue
            };
        }
        return ValueTask.FromResult(new PublishedDataSetSnapshot(
            metaData.ConfigurationVersion, fields, DateTimeUtc.From(timestamp)));
    }

    private readonly PubSubConfiguration m_configuration;
    private readonly TimeProvider m_clock;
    private readonly DataSetMetaDataType m_metadata;
    private int m_sequence;
}

/// <summary>
/// Enforces an independent sample cap even if the scheduler asks for an extra cycle.
/// Completion tells the document to stop its runtime, not to publish empty replacement data.
/// </summary>
internal sealed class PubSubBoundedSource : IPublishedDataSetSource, IMetaDataChangeNotifier
{
    public PubSubBoundedSource(IPublishedDataSetSource source, int maximumSamples)
    {
        m_source = source ?? throw new ArgumentNullException(nameof(source));
        if (maximumSamples is < 1 or > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSamples));
        }
        m_maximumSamples = maximumSamples;
    }

    public Task Completed => m_completed.Task;

    public int Samples => Math.Min(Volatile.Read(ref m_samples), m_maximumSamples);

    public event EventHandler? MetaDataChanged
    {
        add
        {
            if (m_source is IMetaDataChangeNotifier notifier)
            {
                notifier.MetaDataChanged += value;
            }
        }
        remove
        {
            if (m_source is IMetaDataChangeNotifier notifier)
            {
                notifier.MetaDataChanged -= value;
            }
        }
    }

    public DataSetMetaDataType BuildMetaData()
    {
        return m_source.BuildMetaData();
    }

    public async ValueTask<PublishedDataSetSnapshot> SampleAsync(
        DataSetMetaDataType metaData,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int sample = Interlocked.Increment(ref m_samples);
        if (sample > m_maximumSamples)
        {
            throw new ServiceResultException(
                StatusCodes.BadTooManyOperations, "The publication sample limit was reached.");
        }
        PublishedDataSetSnapshot snapshot = await m_source.SampleAsync(metaData, cancellationToken)
            .ConfigureAwait(false);
        if (sample == m_maximumSamples)
        {
            m_completed.TrySetResult();
        }
        return snapshot;
    }

    private readonly IPublishedDataSetSource m_source;
    private readonly int m_maximumSamples;
    private readonly TaskCompletionSource m_completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int m_samples;
}

/// <summary>
/// Opt-in server sink with a count/rate budget. Local observation is committed only after
/// the adapter succeeds. UA writes themselves are not advertised as a server-side transaction.
/// </summary>
internal sealed class PubSubControlledWriteBack : ISubscribedDataSetSink, IAsyncDisposable
{
    public PubSubControlledWriteBack(
        ISubscribedDataSetSink target,
        PubSubObservationStore local,
        PubSubConfiguration configuration,
        TimeProvider clock)
    {
        m_target = target ?? throw new ArgumentNullException(nameof(target));
        m_local = local ?? throw new ArgumentNullException(nameof(local));
        m_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        m_clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async ValueTask WriteAsync(IReadOnlyList<DataSetField> fields, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Count > PubSubConfigurationValidation.MaxFields)
        {
            m_local.RecordEvidence("Write-back", StatusCodes.BadEncodingLimitsExceeded,
                "The complete received dataset exceeded the write-back field budget.");
            throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
        }
        m_local.ValidateFields([.. fields]);
        if (!Volatile.Read(ref m_enabled))
        {
            await m_local.WriteAsync(fields, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (fields.Count != m_configuration.Fields.Count || fields.Any(field => field.FieldIndex >= 0))
        {
            m_local.RecordEvidence("Write-back", StatusCodes.BadTypeMismatch,
                "Write-back rejected a partial or positionally incompatible dataset without writing.");
            throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                "Write-back accepts complete, positionally mapped datasets only.");
        }
        for (int i = 0; i < fields.Count; i++)
        {
            DataSetField field = fields[i];
            PubSubFieldConfiguration expected = m_configuration.Fields[i];
            if (field.Name != expected.Name || field.Value.IsNull || !field.Value.TypeInfo.IsScalar ||
                field.Value.TypeInfo.BuiltInType != expected.Type || !StatusCode.IsGood(field.StatusCode) ||
                (field.Value.TryGetValue(out double number) && !double.IsFinite(number)) ||
                (field.Value.TryGetValue(out float single) && !float.IsFinite(single)) ||
                PubSubValueDisplay.Create(field).Truncated)
            {
                m_local.RecordEvidence("Write-back", StatusCodes.BadTypeMismatch,
                    "The complete dataset was rejected before writing: " +
                    "field name, scalar type, quality or size mismatch.");
                throw new ServiceResultException(StatusCodes.BadTypeMismatch);
            }
        }
        if (!await m_gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new ServiceResultException(StatusCodes.BadTooManyOperations);
        }
        try
        {
            long now = m_clock.GetTimestamp();
            if (m_writes >= m_configuration.MaxPublishedMessages ||
                (m_writes > 0 && m_clock.GetElapsedTime(m_lastWrite, now).TotalMilliseconds <
                    m_configuration.PublishingIntervalMs))
            {
                m_local.RecordEvidence("Write-back", StatusCodes.BadTooManyOperations,
                    "UA write-back was rejected by the configured count/rate budget.");
                throw new ServiceResultException(StatusCodes.BadTooManyOperations);
            }
            m_writes++;
            m_lastWrite = now;
            if (Volatile.Read(ref m_enabled))
            {
                try
                {
                    await m_target.WriteAsync(fields, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!Volatile.Read(ref m_enabled) &&
                    !cancellationToken.IsCancellationRequested)
                {
                    m_local.RecordEvidence("Write-back", StatusCodes.BadSessionClosed,
                        "Primary-session write-back stopped; local reception remains active.");
                }
            }
            await m_local.WriteAsync(fields, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            m_gate.Release();
        }
    }

    public void Deactivate()
    {
        Volatile.Write(ref m_enabled, false);
    }

    public ValueTask DisposeAsync()
    {
        m_gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private readonly ISubscribedDataSetSink m_target;
    private readonly PubSubObservationStore m_local;
    private readonly PubSubConfiguration m_configuration;
    private readonly TimeProvider m_clock;
    private readonly SemaphoreSlim m_gate = new(1, 1);
    private int m_writes;
    private long m_lastWrite;
    private bool m_enabled = true;
}

internal sealed class PubSubControlledActionHandler : IPubSubActionHandler, IAsyncDisposable
{
    public PubSubControlledActionHandler(
        IPubSubActionHandler handler,
        PubSubObservationStore store,
        int maximumCalls,
        TimeProvider clock)
    {
        m_handler = handler ?? throw new ArgumentNullException(nameof(handler));
        m_store = store ?? throw new ArgumentNullException(nameof(store));
        if (maximumCalls is < 1 or > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCalls));
        }
        m_maximumCalls = maximumCalls;
        m_clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async ValueTask<PubSubActionHandlerResult> HandleAsync(
        PubSubActionInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            PubSubActionInputs.Validate(invocation.InputFields);
        }
        catch (ArgumentException)
        {
            m_store.RecordEvidence("Action responder", StatusCodes.BadInvalidArgument,
                "Invalid or unsupported scalar input was rejected without invoking the UA method.");
            return new PubSubActionHandlerResult { StatusCode = StatusCodes.BadInvalidArgument };
        }
        if (!await m_gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            m_store.RecordEvidence("Action responder", StatusCodes.BadTooManyOperations,
                "A concurrent Action was rejected without invoking the UA method.");
            return new PubSubActionHandlerResult { StatusCode = StatusCodes.BadTooManyOperations };
        }
        try
        {
            long now = m_clock.GetTimestamp();
            if (m_calls >= m_maximumCalls ||
                (m_calls > 0 && m_clock.GetElapsedTime(m_lastCall, now) < TimeSpan.FromMilliseconds(100)))
            {
                m_store.RecordEvidence("Action responder", StatusCodes.BadTooManyOperations,
                    "The responder count/rate limit rejected a request without invoking the UA method.");
                return new PubSubActionHandlerResult { StatusCode = StatusCodes.BadTooManyOperations };
            }
            m_calls++;
            m_lastCall = now;
            PubSubActionHandlerResult result = await m_handler.HandleAsync(invocation, cancellationToken)
                .ConfigureAwait(false);
            m_store.RecordEvidence("Action responder", result.StatusCode,
                string.Create(CultureInfo.InvariantCulture,
                    $"Handled request {invocation.RequestId}; the stack owns request/response correlation."));
            return result;
        }
        finally
        {
            m_gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        m_gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private readonly IPubSubActionHandler m_handler;
    private readonly PubSubObservationStore m_store;
    private readonly int m_maximumCalls;
    private readonly TimeProvider m_clock;
    private readonly SemaphoreSlim m_gate = new(1, 1);
    private int m_calls;
    private long m_lastCall;
}
