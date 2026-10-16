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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Client.Subscriptions.Streaming;

namespace Opc.Ua.GMS.Client
{
    public sealed partial class GmsResultClient
    {
        /// <summary>
        /// Builds a subtype-aware generated intermediate-event filter with explicit characteristic placeholder names.
        /// Names belong to the server's instance namespace, not automatically the GMS model namespace.
        /// </summary>
        public EventFilter CreateIntermediateResultFilter(ArrayOf<QualifiedName> characteristics = default)
        {
            ValidateCharacteristicSelection(characteristics);
            QualifiedName[][] standard = IntermediateResultEventTypeRecord.Decoder.GetStandardFields(Session.NamespaceUris);
            NodeId typeId = NodeId.Create(ObjectTypes.IntermediateResultEventType,
                GmsModel.NamespaceUri, Session.NamespaceUris);
            EventFilter filter = IntermediateResultEventTypeRecord.EventFilters.Build(Session.NamespaceUris);
            var clauses = new List<SimpleAttributeOperand>();
            foreach (QualifiedName[] path in standard)
            {
                clauses.Add(new SimpleAttributeOperand
                {
                    TypeDefinitionId = typeId,
                    AttributeId = Attributes.Value,
                    BrowsePath = path
                });
            }
            ushort gmsIndex = (ushort)Session.NamespaceUris.GetIndex(GmsModel.NamespaceUri);
            foreach (QualifiedName characteristic in characteristics)
            {
                foreach (string[] path in s_characteristicPaths)
                {
                    var qualified = new List<QualifiedName> { characteristic };
                    foreach (string segment in path)
                    {
                        qualified.Add(new QualifiedName(segment,
                            segment == Opc.Ua.BrowseNames.EngineeringUnits ? (ushort)0 : gmsIndex));
                    }
                    clauses.Add(new SimpleAttributeOperand
                    {
                        TypeDefinitionId = typeId,
                        AttributeId = Attributes.Value,
                        BrowsePath = qualified.ToArrayOf()
                    });
                }
            }
            filter.SelectClauses = clauses.ToArrayOf();
            m_limits.Measure(Variant.FromStructure(filter), Session.MessageContext);
            return filter;
        }

        /// <summary>
        /// Decodes a selected intermediate event after verifying the concrete subtype and bounded field layout.
        /// Event fields are Variants, not DataValues; no acquisition quality is fabricated for them.
        /// </summary>
        public async ValueTask<GmsIntermediateResultNotification> DecodeIntermediateResultAsync(
            ArrayOf<Variant> fields,
            ArrayOf<QualifiedName> characteristics = default,
            CancellationToken cancellationToken = default)
        {
            ValidateCharacteristicSelection(characteristics);
            int standardCount = IntermediateResultEventTypeRecord.Decoder.GetStandardFields(Session.NamespaceUris).Length;
            RequireCount(fields.Count, standardCount + characteristics.Count * s_characteristicPaths.Length);
            m_limits.Measure(Variant.From(fields), Session.MessageContext);
            if (!fields[1].TryGetValue(out NodeId eventType) || eventType.IsNull ||
                !await Session.NodeCache.IsTypeOfAsync(eventType,
                    NodeId.Create(ObjectTypes.IntermediateResultEventType, GmsModel.NamespaceUri, Session.NamespaceUris),
                    cancellationToken).ConfigureAwait(false))
            {
                throw ServiceResultException.Create(StatusCodes.BadTypeMismatch,
                    "The notification is not a concrete intermediate-result event.");
            }
            var standard = new Variant[standardCount];
            for (int i = 0; i < standard.Length; i++)
            {
                standard[i] = fields[i].Copy();
            }
            var selected = new GmsCharacteristicEventValues[characteristics.Count];
            for (int i = 0; i < selected.Length; i++)
            {
                var values = new Variant[s_characteristicPaths.Length];
                for (int j = 0; j < values.Length; j++)
                {
                    values[j] = fields[standardCount + i * values.Length + j].Copy();
                }
                selected[i] = new GmsCharacteristicEventValues(characteristics[i], values);
            }
            return new GmsIntermediateResultNotification(standard, selected);
        }

        /// <summary>
        /// Collects a bounded number of intermediate notifications using the existing streaming-subscription lifetime.
        /// Disposing the enumerator removes only its monitored item; the caller still owns the shared subscription.
        /// </summary>
        public IAsyncEnumerable<GmsIntermediateResultNotification> ObserveIntermediateResultsAsync(
            IStreamingSubscription streaming,
            int maximumEvents,
            ArrayOf<QualifiedName> characteristics = default,
            CancellationToken cancellationToken = default)
        {
            if (streaming == null)
            {
                throw new ArgumentNullException(nameof(streaming));
            }
            if (maximumEvents < 1 || maximumEvents > m_limits.MaximumResults)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumEvents));
            }
            EventFilter filter = CreateIntermediateResultFilter(characteristics);
            ArrayOf<QualifiedName> selection = characteristics.IsNull ? [] : new(characteristics.ToArray()!);
            return ObserveIntermediateCoreAsync(streaming, maximumEvents, selection, filter, null, cancellationToken);
        }

        /// <summary>
        /// Observes bounded intermediate results and reports successful server-side monitored-item creation.
        /// A deadline before this callback is setup failure, not an empty successful result window.
        /// It preserves the existing metadata budget, decoder and caller-owned subscription lifetime.
        /// </summary>
        public IAsyncEnumerable<GmsIntermediateResultNotification> ObserveIntermediateResultsAsync(
            IStreamingSubscription streaming,
            int maximumEvents,
            Func<CancellationToken, ValueTask> onReady,
            ArrayOf<QualifiedName> characteristics = default,
            CancellationToken cancellationToken = default)
        {
            if (streaming == null)
            {
                throw new ArgumentNullException(nameof(streaming));
            }
            if (onReady == null)
            {
                throw new ArgumentNullException(nameof(onReady));
            }
            if (maximumEvents < 1 || maximumEvents > m_limits.MaximumResults)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumEvents));
            }
            EventFilter filter = CreateIntermediateResultFilter(characteristics);
            ArrayOf<QualifiedName> selection = characteristics.IsNull ? [] : new(characteristics.ToArray()!);
            return ObserveIntermediateCoreAsync(streaming, maximumEvents, selection, filter, onReady, cancellationToken);
        }

        /// <summary>
        /// Runs a validated observation and removes its monitored item when the bounded stream ends.
        /// </summary>
        private async IAsyncEnumerable<GmsIntermediateResultNotification> ObserveIntermediateCoreAsync(
            IStreamingSubscription streaming,
            int maximumEvents,
            ArrayOf<QualifiedName> characteristics,
            EventFilter filter,
            Func<CancellationToken, ValueTask>? onReady,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            GMSResultManagementTypeClient management = await ResultsAsync(cancellationToken).ConfigureAwait(false);
            var options = new MonitoredItemOptions { QueueSize = (uint)maximumEvents, DiscardOldest = false };
            int count = 0;
            long bytes = 0;
            IAsyncEnumerable<EventNotification> events = onReady == null
                ? streaming.SubscribeEventsAsync(management.ObjectId, filter, options, cancellationToken)
                : streaming.SubscribeEventsWhenReadyAsync(
                    management.ObjectId, filter, onReady, options, ct: cancellationToken);
            await foreach (EventNotification notification in events.ConfigureAwait(false))
            {
                bytes += m_limits.Measure(Variant.From(notification.Fields), Session.MessageContext);
                if (bytes > m_limits.MaximumMetadataBytes)
                {
                    throw ServiceResultException.Create(StatusCodes.BadEncodingLimitsExceeded,
                        "The intermediate-event observation exceeds its total response byte bound.");
                }
                yield return await DecodeIntermediateResultAsync(
                    notification.Fields, characteristics, cancellationToken).ConfigureAwait(false);
                if (++count == maximumEvents)
                {
                    yield break;
                }
            }
        }

        /// <summary>
        /// Rejects ambiguous or unbounded placeholder selections without altering the session's namespace table.
        /// </summary>
        private void ValidateCharacteristicSelection(ArrayOf<QualifiedName> characteristics)
        {
            if (characteristics.Count > m_limits.MaximumCharacteristics)
            {
                throw new ArgumentOutOfRangeException(nameof(characteristics));
            }
            if (Session.NamespaceUris.GetIndex(GmsModel.NamespaceUri) < 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadNotSupported, "The server has no GMS model namespace.");
            }
            var names = new HashSet<QualifiedName>();
            foreach (QualifiedName name in characteristics)
            {
                if (name.IsNull || string.IsNullOrWhiteSpace(name.Name) || !names.Add(name))
                {
                    throw new ArgumentException("Characteristic names must be non-empty and unique.", nameof(characteristics));
                }
            }
        }

        private static readonly string[][] s_characteristicPaths =
        [
            [BrowseNames.CharacteristicIdentifier],
            [BrowseNames.ResultValue],
            [BrowseNames.ResultValue, Opc.Ua.BrowseNames.EngineeringUnits],
            [BrowseNames.UpperToleranceLimit],
            [BrowseNames.UpperToleranceLimit, Opc.Ua.BrowseNames.EngineeringUnits],
            [BrowseNames.UpperToleranceLimit, BrowseNames.ToleranceForm],
            [BrowseNames.LowerToleranceLimit],
            [BrowseNames.LowerToleranceLimit, Opc.Ua.BrowseNames.EngineeringUnits],
            [BrowseNames.LowerToleranceLimit, BrowseNames.ToleranceForm],
            [BrowseNames.Nominal],
            [BrowseNames.Nominal, Opc.Ua.BrowseNames.EngineeringUnits],
            [BrowseNames.CharacteristicsClass],
            [BrowseNames.ResultEvaluation],
            [BrowseNames.IsValid],
            [BrowseNames.Formula]
        ];
    }

    /// <summary>
    /// An immutable intermediate event containing its generated standard record and selected characteristic values.
    /// </summary>
    public sealed class GmsIntermediateResultNotification
    {
        /// <summary>
        /// Captures detached field arrays after validation.
        /// </summary>
        internal GmsIntermediateResultNotification(
            ArrayOf<Variant> standard,
            ArrayOf<GmsCharacteristicEventValues> characteristics)
        {
            m_standard = standard;
            m_characteristics = characteristics;
        }

        /// <summary>
        /// Gets a newly decoded copy retaining the real concrete event type, result envelope and program name.
        /// </summary>
        public IntermediateResultEventTypeRecord Event
        {
            get
            {
                var fields = new Variant[m_standard.Count];
                for (int i = 0; i < fields.Length; i++)
                {
                    fields[i] = m_standard[i].Copy();
                }
                return IntermediateResultEventTypeRecord.Decoder.Decode(fields)!;
            }
        }

        /// <summary>
        /// Gets the explicitly selected characteristic fields without inventing values for omitted fields.
        /// </summary>
        public ArrayOf<GmsCharacteristicEventValues> Characteristics => new(m_characteristics.ToArray()!);

        private readonly ArrayOf<Variant> m_standard;
        private readonly ArrayOf<GmsCharacteristicEventValues> m_characteristics;
    }

    /// <summary>
    /// Raw selected characteristic fields. UA event notifications do not carry per-field acquisition DataValues.
    /// </summary>
    public sealed class GmsCharacteristicEventValues
    {
        /// <summary>
        /// Captures the placeholder name and generated-layout extension fields.
        /// </summary>
        internal GmsCharacteristicEventValues(QualifiedName name, ArrayOf<Variant> values)
        {
            Name = name;
            m_values = values;
        }

        /// <summary>
        /// Gets the selected placeholder's actual qualified browse name.
        /// </summary>
        public QualifiedName Name { get; }

        /// <summary>
        /// Gets the characteristic identifier field.
        /// </summary>
        public Variant Identifier => m_values[0].Copy();

        /// <summary>
        /// Gets the measured value field.
        /// </summary>
        public Variant ResultValue => m_values[1].Copy();

        /// <summary>
        /// Gets the result engineering units.
        /// </summary>
        public Variant EngineeringUnits => m_values[2].Copy();

        /// <summary>
        /// Gets the upper absolute tolerance.
        /// </summary>
        public Variant UpperTolerance => m_values[3].Copy();

        /// <summary>
        /// Gets the upper-tolerance engineering units.
        /// </summary>
        public Variant UpperToleranceUnits => m_values[4].Copy();

        /// <summary>
        /// Gets the upper tolerance form.
        /// </summary>
        public Variant UpperToleranceForm => m_values[5].Copy();

        /// <summary>
        /// Gets the lower absolute tolerance.
        /// </summary>
        public Variant LowerTolerance => m_values[6].Copy();

        /// <summary>
        /// Gets the lower-tolerance engineering units.
        /// </summary>
        public Variant LowerToleranceUnits => m_values[7].Copy();

        /// <summary>
        /// Gets the lower tolerance form.
        /// </summary>
        public Variant LowerToleranceForm => m_values[8].Copy();

        /// <summary>
        /// Gets the nominal geometric specification value.
        /// </summary>
        public Variant Nominal => m_values[9].Copy();

        /// <summary>
        /// Gets the nominal engineering units.
        /// </summary>
        public Variant NominalUnits => m_values[10].Copy();

        /// <summary>
        /// Gets the importance class.
        /// </summary>
        public Variant CharacteristicsClass => m_values[11].Copy();

        /// <summary>
        /// Gets the provided tolerance evaluation.
        /// </summary>
        public Variant Evaluation => m_values[12].Copy();

        /// <summary>
        /// Gets geometric validity, independently of acquisition quality.
        /// </summary>
        public Variant IsValid => m_values[13].Copy();

        /// <summary>
        /// Gets the optional measurement formula.
        /// </summary>
        public Variant Formula => m_values[14].Copy();

        private readonly ArrayOf<Variant> m_values;
    }
}
