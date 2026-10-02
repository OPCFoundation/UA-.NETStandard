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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.OpenUsd.Client;
using UaLens.Connection;

namespace UaLens.Plugins.Companions.Providers
{
    internal sealed record OpenUsdWorkflowSample(
        string Origin,
        ExpandedNodeId Source,
        Guid BindingId,
        string PrimPath,
        string PropertyName,
        DataValue SourceValue,
        Variant Value,
        ByteString EncodedValue,
        uint SequenceNumber,
        DateTime PublishTime);

    /// <summary>
    /// Selected-instance tasks. All acquisition finishes and is revalidated before a local file is created.
    /// </summary>
    internal sealed class OpenUsdWorkflowTasks
    {
        public OpenUsdWorkflowTasks(
            Func<CompanionContext, OpenUsdConnectorOptions, IOpenUsdCompanionReader> createReader,
            Func<CompanionContext, IOpenUsdWorkflowSource> createSource,
            IOpenUsdWorkflowExporter exporter,
            OpenUsdWorkflowFederation? federation)
        {
            m_createReader = createReader;
            m_createSource = createSource;
            m_exporter = exporter;
            m_federation = federation;
        }

        public static ArrayOf<CompanionOperation> Operations { get; } =
        [
            new("read-bindings", "Read bindings with source timestamps", CompanionOperationSafety.ReadOnly)
            {
                Inputs = []
            },
            new("observe-bindings", "Observe bounded live bindings", CompanionOperationSafety.ReadOnly)
            {
                Inputs =
                [
                    new("seconds", "Observation seconds", BuiltInType.UInt32, "1-15; not a real-time guarantee"),
                    new("maxSamples", "Maximum converted samples", BuiltInType.UInt32,
                        "1-128, additionally limited by the host result-field budget")
                ]
            },
            new("replay-history", "Replay bounded history into memory", CompanionOperationSafety.ReadOnly)
            {
                Inputs = HistoryInputs(false)
            },
            new("export-history", "Export bounded history layer", CompanionOperationSafety.LocalFile)
            {
                Inputs = HistoryInputs(true)
            },
            new("command-preflight", "Inspect command bindings; no actuation", CompanionOperationSafety.ReadOnly)
            {
                Inputs = []
            },
            new("compose-preview", "Verify configured peer composition", CompanionOperationSafety.ReadOnly)
            {
                Inputs = []
            },
            new("export-composition", "Export configured peer composition", CompanionOperationSafety.LocalFile)
            {
                Inputs = [DestinationInput]
            }
        ];

        public async ValueTask<CompanionTaskInput> PrepareAsync(
            CompanionContext context, CompanionTarget target, string operationId,
            ArrayOf<CompanionValue> inputs, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompanionOperation operation = Operation(operationId);
            RequireInputs(operation.Inputs, inputs);
            string? destination = operation.Safety == CompanionOperationSafety.LocalFile
                ? OpenUsdCompanionExporter.ValidateDestination(Text(inputs[^1].Value)) : null;
            int maximum = SampleLimit(context);
            if (maximum < 1)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded, "No sample result budget.");
            }
            TimeSpan duration = TimeSpan.Zero;
            DateTime start = default;
            DateTime end = default;
            if (operationId == "observe-bindings")
            {
                duration = TimeSpan.FromSeconds(Unsigned(inputs[0].Value, 15));
                maximum = Unsigned(inputs[1].Value, maximum);
            }
            if (operationId is "replay-history" or "export-history")
            {
                start = Timestamp(inputs[0].Value);
                end = Timestamp(inputs[1].Value);
                if (end <= start || end - start > TimeSpan.FromDays(1))
                {
                    throw new ArgumentException("History requires an increasing UTC range of at most 24 hours.");
                }
                maximum = Unsigned(inputs[2].Value, maximum);
            }
            using CancellationTokenSource lifetime =
                CellCompanionSupport.BeginOperation(context, ModelUri, cancellationToken);
            IOpenUsdCompanionReader reader = m_createReader(context, OpenUsdCompanionProvider.CreateOptions());
            await using (reader.ConfigureAwait(false))
            {
                OpenUsdConnector.RepresentationInfo representation = await OpenUsdCompanionProvider.FindAsync(
                    reader, context, target.NodeId, lifetime.Token).ConfigureAwait(false);
                ByteString digest = OpenUsdWorkflowMetadata.Digest(context, representation);
                ArrayOf<OpenUsdWorkflowOrigin> origins = [];
                string review;
                if (operationId is "compose-preview" or "export-composition")
                {
                    OpenUsdWorkflowFederation federation = RequireFederation();
                    origins = await federation.PrepareAsync(
                        context, representation, m_createReader, lifetime.Token).ConfigureAwait(false);
                    review = OpenUsdWorkflowFederation.Review(origins);
                }
                else if (operationId == "command-preflight")
                {
                    int count = representation.Bindings.Count(
                        static binding => binding.Intent == OpenUsdIntentProfile.UsdToUaCommand);
                    CellCompanionSupport.CheckCount((count * 6) + 2, context.MaxFields, "Command preflight fields");
                    review = $"Inspect {count} command declaration(s). No typed, safe authorizable actuation " +
                        "contract is available; this is read-only preflight, not permission to call or write.";
                }
                else
                {
                    ArrayOf<OpenUsdConnector.BindingInfo> bindings = Bindings(representation, operationId);
                    CellCompanionSupport.CheckCount(bindings.Count, maximum, "Selected binding count");
                    review = operationId is "replay-history" or "export-history"
                        ? $"Replay [{start:O}, {end:O}) in source-time order, at most {maximum} samples. " +
                            "An oversized range fails instead of exporting a silently truncated history."
                        : $"Read only {bindings.Count} telemetry binding(s), at most {maximum} samples" +
                            (duration > TimeSpan.Zero ? $" over at most {duration.TotalSeconds} seconds." : ".");
                    review += " Sampling and USD time encoding are not lossless or hard real-time.";
                }
                if (destination is not null)
                {
                    review += $"\nNew local directory: {destination}. Existing paths are never overwritten.";
                }
                lifetime.Token.ThrowIfCancellationRequested();
                return new OpenUsdWorkflowTaskInput(
                    context, target, operationId, digest, maximum, duration, start, end, destination, review, origins);
            }
        }

        public async ValueTask<CompanionOperationResult> ExecuteAsync(
            CompanionContext context, CompanionTarget target, string operationId, CompanionTaskInput input,
            IProgress<CompanionTaskProgress>? progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = Operation(operationId);
            if (input is not OpenUsdWorkflowTaskInput task)
            {
                throw new ArgumentException("Prepare the exact OpenUSD task first.", nameof(input));
            }
            task.RequireMatch(context, target, operationId);
            if (task.Destination is not null)
            {
                _ = OpenUsdCompanionExporter.ValidateDestination(task.Destination);
            }
            using CancellationTokenSource lifetime =
                CellCompanionSupport.BeginOperation(context, ModelUri, cancellationToken);
            CancellationToken token = lifetime.Token;
            IOpenUsdCompanionReader reader = m_createReader(context, OpenUsdCompanionProvider.CreateOptions());
            await using (reader.ConfigureAwait(false))
            {
                OpenUsdConnector.RepresentationInfo representation = await OpenUsdCompanionProvider.FindAsync(
                    reader, context, target.NodeId, token).ConfigureAwait(false);
                task.RequireMetadata(context, representation);
                if (operationId == "command-preflight")
                {
                    return CommandPreflight(context, task, representation);
                }
                if (operationId is "compose-preview" or "export-composition")
                {
                    return await RequireFederation().ExecuteAsync(
                        context, task, representation, m_createReader, m_exporter, progress, token)
                            .ConfigureAwait(false);
                }
                ArrayOf<OpenUsdConnector.BindingInfo> bindings = Bindings(representation, operationId);
                var samples = new List<OpenUsdWorkflowSample>();
                progress?.Report(new CompanionTaskProgress("Acquiring the reviewed bounded OpenUSD values."));
                if (operationId == "read-bindings")
                {
                    for (int index = 0; index < bindings.Count; index++)
                    {
                        OpenUsdConnector.BindingInfo binding = bindings[index];
                        DataValue value = await reader.ReadValueAsync(binding.SourceNodeId, token)
                            .ConfigureAwait(false);
                        AddSample(context, task, samples, binding, value);
                    }
                }
                else if (operationId == "observe-bindings")
                {
                    await ObserveAsync(context, task, bindings, samples, token).ConfigureAwait(false);
                }
                else
                {
                    await ReadHistoryAsync(context, task, bindings, samples, token).ConfigureAwait(false);
                }
                if (samples.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNoData,
                        "No successful values were observed; source readiness and delivery were not confirmed.");
                }
                task.RequireMatch(context, target, operationId);
                task.RequireMetadata(context, await OpenUsdCompanionProvider.FindAsync(
                    reader, context, target.NodeId, token).ConfigureAwait(false));
                token.ThrowIfCancellationRequested();
                bool history = operationId is "replay-history" or "export-history";
                var sink = new MockUsdSink();
                Author(sink, samples, history);
                if (task.Destination is not null)
                {
                    progress?.Report(new CompanionTaskProgress("Writing the captured history; no asset fetch."));
                    await m_exporter.WriteAsync(context, task, [], [], [.. samples], token).ConfigureAwait(false);
                }
                string completion = operationId == "observe-bindings"
                    ? samples.Count >= task.MaximumSamples ? "Sample limit reached" : "Observation window ended"
                    : "Requested read completed";
                return Result(context, task, [.. samples], completion,
                    history
                        ? "Source-time ordered history replayed through IUsdSink.SetTimeSample. " +
                            "No served asset or external dependency was fetched."
                        : "Telemetry converted with OpenUsdConnector.Convert. Commands, alarms and history " +
                            "were not subscribed. The capture is bounded, not lossless or hard real-time.");
            }
        }

        internal static int SampleLimit(CompanionContext context)
        {
            return Math.Min(128, Math.Max(0, (context.MaxFields - 6) / 8));
        }

        internal static void Author(IUsdSink sink, IEnumerable<OpenUsdWorkflowSample> samples, bool history)
        {
            using (sink.BeginBatch())
            {
                foreach (OpenUsdWorkflowSample sample in samples)
                {
                    if (history)
                    {
                        sink.SetTimeSample(
                            sample.PrimPath, sample.PropertyName, sample.SourceValue.SourceTimestamp.ToDateTime(),
                            sample.Value);
                    }
                    else
                    {
                        sink.SetAttribute(sample.PrimPath, sample.PropertyName, sample.Value);
                    }
                }
            }
        }

        internal static CompanionOperationResult Result(
            CompanionContext context, OpenUsdWorkflowTaskInput task, ArrayOf<OpenUsdWorkflowSample> samples,
            string completion, string summary)
        {
            var fields = new CellCompanionFields(context.MaxFields);
            fields.Add("Correlation", Variant.From(new Uuid(task.CorrelationId)));
            fields.Add("Source session generation", Variant.From(task.SessionId));
            fields.Add("Metadata SHA256", Variant.From(task.MetadataDigest));
            fields.Add("Sample count", Variant.From(samples.Count));
            fields.AddText("Completion", completion);
            fields.AddText("Export directory", task.Destination);
            for (int index = 0; index < samples.Count; index++)
            {
                OpenUsdWorkflowSample sample = samples[index];
                string prefix = $"Sample {index + 1}";
                fields.AddText($"{prefix} origin", sample.Origin);
                fields.Add($"{prefix} source", Variant.From(sample.Source));
                fields.Add($"{prefix} binding", Variant.From(new Uuid(sample.BindingId)));
                fields.AddText($"{prefix} target", $"{sample.PrimPath}.{sample.PropertyName}");
                fields.Add($"{prefix} source value", Variant.From(sample.SourceValue));
                fields.Add($"{prefix} converted", sample.Value);
                fields.Add($"{prefix} sequence",
                    sample.SequenceNumber == 0 ? Variant.Null : Variant.From(sample.SequenceNumber));
                fields.Add($"{prefix} publish time",
                    sample.PublishTime == default ? Variant.Null : Variant.From((DateTimeUtc)sample.PublishTime));
            }
            return new CompanionOperationResult(summary + " " + completion + ".", fields.ToArray());
        }

        internal static ArrayOf<OpenUsdConnector.BindingInfo> Bindings(
            OpenUsdConnector.RepresentationInfo representation, string operationId)
        {
            bool history = operationId is "replay-history" or "export-history";
            var result = new List<OpenUsdConnector.BindingInfo>();
            var targets = new HashSet<string>(StringComparer.Ordinal);
            foreach (OpenUsdConnector.BindingInfo binding in representation.Bindings)
            {
                if (!binding.Enabled ||
                    binding.Intent != (history
                        ? OpenUsdIntentProfile.UaHistoryToUsd : OpenUsdIntentProfile.UaToUsdTelemetry))
                {
                    continue;
                }
                if (history && !binding.TimeSampled)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported, "An enabled history binding is not time-sampled.");
                }
                OpenUsdCompanionExporter.ValidatePrimPath(binding.PrimPath);
                OpenUsdCompanionExporter.ValidatePropertyName(binding.PropertyName);
                if (binding.SourceNodeId.IsNull || !double.IsFinite(binding.Scale) || !double.IsFinite(binding.Offset))
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Invalid binding source or scale.");
                }
                if (!targets.Add($"{binding.PrimPath}.{binding.PropertyName}"))
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyMatches, "Duplicate USD binding target.");
                }
                result.Add(binding);
            }
            if (result.Count == 0)
            {
                throw new ServiceResultException(StatusCodes.BadNoData, "No eligible enabled bindings are published.");
            }
            return [.. result];
        }

        private async Task ObserveAsync(
            CompanionContext context, OpenUsdWorkflowTaskInput task, ArrayOf<OpenUsdConnector.BindingInfo> bindings,
            List<OpenUsdWorkflowSample> samples, CancellationToken token)
        {
            IOpenUsdWorkflowSource source = m_createSource(context);
            ArrayOf<NodeId> nodes = [.. bindings.ToArray()!.Select(static binding => binding.SourceNodeId).Distinct()];
            await foreach (OpenUsdWorkflowChange change in source.ObserveAsync(nodes, token)
                .WithTimeoutAsync(task.Duration, ct: token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                bool found = false;
                for (int index = 0; index < bindings.Count; index++)
                {
                    OpenUsdConnector.BindingInfo binding = bindings[index];
                    if (binding.SourceNodeId == change.Source)
                    {
                        found = true;
                        AddSample(context, task, samples, binding, change.Value,
                            change.SequenceNumber, change.PublishTime);
                        if (samples.Count == task.MaximumSamples)
                        {
                            return;
                        }
                    }
                }
                if (!found)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid, "The capture delivered an unrequested binding source.");
                }
            }
        }

        private async Task ReadHistoryAsync(
            CompanionContext context, OpenUsdWorkflowTaskInput task, ArrayOf<OpenUsdConnector.BindingInfo> bindings,
            List<OpenUsdWorkflowSample> samples, CancellationToken token)
        {
            IOpenUsdWorkflowSource source = m_createSource(context);
            int received = 0;
            for (int index = 0; index < bindings.Count; index++)
            {
                OpenUsdConnector.BindingInfo binding = bindings[index];
                DateTime previous = DateTime.MinValue;
                await foreach (DataValue value in source.ReadHistoryAsync(
                    binding.SourceNodeId, task.Start, task.End, (uint)task.MaximumSamples, token).ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();
                    CellCompanionSupport.CheckCount(++received, task.MaximumSamples, "Received history values");
                    var time = value.SourceTimestamp.ToDateTime();
                    if (time < task.Start || time >= task.End || time <= previous)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidTimestamp,
                            "History must be strictly source-time ordered inside the reviewed half-open range.");
                    }
                    previous = time;
                    AddSample(context, task, samples, binding, value);
                }
            }
            samples.Sort(static (left, right) =>
            {
                int order = left.SourceValue.SourceTimestamp.CompareTo(right.SourceValue.SourceTimestamp);
                return order != 0 ? order : string.CompareOrdinal(
                    $"{left.PrimPath}.{left.PropertyName}", $"{right.PrimPath}.{right.PropertyName}");
            });
        }

        internal static void AddSample(
            CompanionContext context, OpenUsdWorkflowTaskInput task, List<OpenUsdWorkflowSample> samples,
            OpenUsdConnector.BindingInfo binding, in DataValue value,
            uint sequenceNumber = 0, DateTime publishTime = default,
            string origin = "primary", string? primPath = null)
        {
            CellCompanionSupport.CheckCount(samples.Count + 1, task.MaximumSamples, "Converted binding samples");
            if (value.IsNull || !StatusCode.IsGood(value.StatusCode))
            {
                throw new ServiceResultException(value.IsNull ? StatusCodes.BadNoData : value.StatusCode,
                    "A binding returned a non-good value; no successful capture was exported.");
            }
            if (value.SourceTimestamp.ToDateTime() == DateTime.MinValue)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidTimestamp, "A source timestamp is required.");
            }
            Variant converted = OpenUsdConnector.Convert(binding, value.WrappedValue);
            RequireConverted(converted);
            ByteString encoded = OpenUsdWorkflowMetadata.EncodeSample(context, value, converted);
            int totalBytes = encoded.Length;
            foreach (OpenUsdWorkflowSample sample in samples)
            {
                totalBytes = checked(totalBytes + sample.EncodedValue.Length);
            }
            CellCompanionSupport.CheckCount(
                totalBytes, OpenUsdWorkflowMetadata.MaximumTotalSampleBytes, "Captured sample bytes");
            string path = primPath ?? binding.PrimPath!;
            OpenUsdCompanionExporter.ValidatePrimPath(path);
            OpenUsdCompanionExporter.ValidatePropertyName(binding.PropertyName);
            DataValue snapshot = value.WithWrappedValue(
                DataValueCodec.Snapshot(value.WrappedValue, context.Session.MessageContext));
            samples.Add(new OpenUsdWorkflowSample(
                origin, NodeId.ToExpandedNodeId(binding.SourceNodeId, context.Session.NamespaceUris),
                binding.BindingDefinitionId, path,
                binding.PropertyName!, snapshot,
                DataValueCodec.Snapshot(converted, context.Session.MessageContext),
                encoded, sequenceNumber, publishTime));
        }

        internal static void RequireConverted(Variant value)
        {
            if (value.TryGetValue(out double scalar) && double.IsFinite(scalar))
            {
                return;
            }
            if (value.TryGetValue(out string? text) && text is "inherited" or "invisible")
            {
                return;
            }
            if (value.TryGetValue(out ArrayOf<double> doubles) &&
                doubles.Count == 3 &&
                doubles.ToArray()!.All(static item => double.IsFinite(item)))
            {
                return;
            }
            if (value.TryGetValue(out ArrayOf<float> singles) &&
                singles.Count == 3 &&
                singles.ToArray()!.All(static item => float.IsFinite(item)))
            {
                return;
            }
            throw new ServiceResultException(
                StatusCodes.BadTypeMismatch, "The shared connector cannot faithfully convert this binding value.");
        }

        private static CompanionOperationResult CommandPreflight(
            CompanionContext context, OpenUsdWorkflowTaskInput task, OpenUsdConnector.RepresentationInfo representation)
        {
            var fields = new CellCompanionFields(context.MaxFields);
            fields.Add("Correlation", Variant.From(new Uuid(task.CorrelationId)));
            fields.Add("Metadata SHA256", Variant.From(task.MetadataDigest));
            int count = 0;
            foreach (OpenUsdConnector.BindingInfo binding in representation.Bindings)
            {
                if (binding.Intent != OpenUsdIntentProfile.UsdToUaCommand)
                {
                    continue;
                }
                string prefix = $"Command {++count}";
                fields.Add($"{prefix} definition", Variant.From(new Uuid(binding.BindingDefinitionId)));
                fields.Add($"{prefix} target", Variant.From(binding.CommandTargetNodeId));
                fields.Add($"{prefix} method", Variant.From(binding.CommandMethodId));
                fields.AddText($"{prefix} trigger", binding.CommandTriggerPropertyName);
                fields.Add($"{prefix} enabled", Variant.From(binding.Enabled));
                fields.AddText($"{prefix} authorization", "Unavailable; no typed safe actuation contract");
            }
            return new CompanionOperationResult(
                $"Inspected {count} command declaration(s), not invoked. No Call or Write was issued. " +
                "Preflight is not command authorization.", fields.ToArray());
        }

        private OpenUsdWorkflowFederation RequireFederation()
        {
            return m_federation ??
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "Cross-server composition requires configuration: an explicit peer allowlist and session factory.");
        }

        private static CompanionOperation Operation(string id)
        {
            foreach (CompanionOperation operation in Operations)
            {
                if (operation.Id == id)
                {
                    return operation;
                }
            }
            throw new ServiceResultException(StatusCodes.BadNotSupported, "Unknown prepared OpenUSD task.");
        }

        private static ArrayOf<CompanionInputDefinition> HistoryInputs(bool export)
        {
            ArrayOf<CompanionInputDefinition> fields =
            [
                new("start", "Start UTC (inclusive)", BuiltInType.DateTime, "ISO 8601 UTC"),
                new("end", "End UTC (exclusive)", BuiltInType.DateTime, "After start, at most 24 hours"),
                new("maxSamples", "Maximum history samples", BuiltInType.UInt32,
                    "1-128, additionally limited by the host result-field budget; overflow fails")
            ];
            return export ? [.. fields, DestinationInput] : fields;
        }

        private static CompanionInputDefinition DestinationInput =>
            new("destination", "New local export directory", BuiltInType.String,
                "Absolute unused directory under an existing, non-linked local parent");

        private static void RequireInputs(ArrayOf<CompanionInputDefinition> definitions, ArrayOf<CompanionValue> values)
        {
            if (values.Count != definitions.Count)
            {
                throw new ArgumentException("Supply the exact OpenUSD task fields.");
            }
            for (int index = 0; index < values.Count; index++)
            {
                if (values[index].Name != definitions[index].Name ||
                    !values[index].Value.TypeInfo.IsScalar ||
                    values[index].Value.TypeInfo.BuiltInType != definitions[index].DataType)
                {
                    throw new ArgumentException("OpenUSD task fields have incorrect names, ranks or types.");
                }
            }
        }

        private static int Unsigned(Variant value, int maximum)
        {
            if (!value.TryGetValue(out uint number) || number == 0 || number > maximum)
            {
                throw new ArgumentException($"The value must be an unsigned integer between 1 and {maximum}.");
            }
            return (int)number;
        }

        private static string Text(Variant value)
        {
            return value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text)
                ? text : throw new ArgumentException("A nonempty local path is required.");
        }

        private static DateTime Timestamp(Variant value)
        {
            if (!value.TryGetValue(out DateTimeUtc time) || time.ToDateTime() <= DateTime.UnixEpoch)
            {
                throw new ArgumentException("An explicit UTC timestamp after the Unix epoch is required.");
            }
            return time.ToDateTime();
        }

        private const string ModelUri = "http://opcfoundation.org/UA/OpenUSD/";
        private readonly Func<CompanionContext, OpenUsdConnectorOptions, IOpenUsdCompanionReader> m_createReader;
        private readonly Func<CompanionContext, IOpenUsdWorkflowSource> m_createSource;
        private readonly IOpenUsdWorkflowExporter m_exporter;
        private readonly OpenUsdWorkflowFederation? m_federation;
    }
}
