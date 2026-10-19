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
using Opc.Ua.GMS.Contracts;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Machinery.Server.Results;

namespace Opc.Ua.GMS.Server
{
    /// <summary>
    /// A lifetime-owned result binding on one GMS root registered by the existing Machinery builder.
    /// </summary>
    public sealed class GmsResultBinding : IAsyncDisposable, IDisposable
    {
        /// <summary>
        /// Stages the binding using the owner's context, options and explicit provider.
        /// </summary>
        internal GmsResultBinding(
            GMSState state,
            ISystemContext context,
            IGmsResultStore store,
            GmsResultBindingOptions options)
        {
            m_state = state;
            m_options = options;
            m_context = context;
            m_lifetimeToken = m_lifetime.Token;
            var message = new ServiceMessageContext(context.Telemetry, context.EncodeableFactory)
            {
                NamespaceUris = context.NamespaceUris
            };
            m_store = new GmsResultStoreAdapter(store, options.Limits, message, m_lifetimeToken);
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (GmsCorrection correction in options.Corrections)
            {
                if (correction == null || !identifiers.Add(correction.Identifier))
                {
                    throw ServiceResultException.Create(StatusCodes.BadConfigurationError,
                        "Corrections must be non-null and have unique identifiers.");
                }
                GmsResultBudget.MeasureCorrection(correction, options.Limits, message);
            }
            m_message = message;
            m_provider = store;
        }

        /// <summary>
        /// Creates a typed publisher only for the committed machine that owns this binding.
        /// </summary>
        public GmsResultPublisher CreatePublisher(IMachineHandle<GMSState> machine)
        {
            if (machine == null)
            {
                throw new ArgumentNullException(nameof(machine));
            }
            EnsureActive();
            if (!ReferenceEquals(machine.State, m_state) || machine.Results == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument, "The supplied machine does not own this GMS result binding.");
            }
            return new GmsResultPublisher(this, machine.Results);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (s_ownershipGate)
            {
                if (m_active == -1)
                {
                    return;
                }
                Volatile.Write(ref m_active, -1);
                if (s_storeOwners.TryGetValue(m_provider, out GmsResultBinding? owner) && ReferenceEquals(owner, this))
                {
                    s_storeOwners.Remove(m_provider);
                }
            }
            try
            {
                m_lifetime.Cancel();
            }
            finally
            {
                m_lifetime.Dispose();
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }

        /// <summary>
        /// Composes shared result behavior, retaining the exact generated result-management descendant.
        /// </summary>
        internal void Configure(IResultManagementBuilder builder, ISystemContext context)
        {
            builder.WithStore(m_store)
                .WithResultsFolder(m_options.PublishedResults, (systemContext, parent, name) =>
                {
                    GMSResultState result = systemContext.CreateInstanceOfGMSResultType(parent, name);
                    if (m_options.IncludeUsedTools)
                    {
                        result.AddUsedTools(systemContext);
                        result.UsedTools!.StatusCode = StatusCodes.BadWaitingForInitialData;
                    }
                    result.StatusCode = StatusCodes.BadWaitingForInitialData;
                    return result;
                })
                .WithResultProjection(PrepareProjection);
            var finalEventType = NodeId.Create(
                Machinery.Result.ObjectTypes.ResultReadyEventType,
                Machinery.Result.Namespaces.MachineryResult,
                context.NamespaceUris);
            ArrayOf<NodeId> eventTypes = m_options.EnableIntermediateEvents
                ? [finalEventType, NodeId.Create(ObjectTypes.IntermediateResultEventType,
                    GmsModel.NamespaceUri, context.NamespaceUris)]
                : [finalEventType];
            builder.WithResultReadyEvents(eventTypes, CreateEvent);
            if (m_options.EnableFileTransfer)
            {
                builder.WithFileTransfer();
            }
            ConfigureCorrections(context);
        }

        /// <summary>
        /// Activates after shared registration; the owner retains this resource for rollback and shutdown.
        /// </summary>
        internal ValueTask<IAsyncDisposable> ActivateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                lock (s_ownershipGate)
                {
                    if (m_active != 0)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadInvalidState,
                            "The result binding is not staged.");
                    }
                    if (s_storeOwners.TryGetValue(m_provider, out _))
                    {
                        throw ServiceResultException.Create(StatusCodes.BadConfigurationError,
                            "A GMS result provider cannot be shared between machine instances.");
                    }
                    s_storeOwners.Add(m_provider, this);
                    Volatile.Write(ref m_active, 1);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
            return new ValueTask<IAsyncDisposable>(this);
        }

        /// <summary>
        /// Publishes through the one shared ingestion and notification path without holding a gate across providers.
        /// </summary>
        internal async ValueTask PublishAsync(
            IMachineryResultPublisher publisher,
            GmsResult result,
            CancellationToken cancellationToken)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }
            EnsureActive();
            if (result.IsIntermediate && !m_options.EnableIntermediateEvents)
            {
                throw ServiceResultException.Create(StatusCodes.BadNotSupported,
                    "Intermediate result events are not enabled on this binding.");
            }
            if (!m_options.IncludeUsedTools && !result.UsedTools.IsNull)
            {
                throw ServiceResultException.Create(StatusCodes.BadNotSupported,
                    "UsedTools is not enabled on this result binding.");
            }
            if (!m_options.EnableFileTransfer && !result.FileContent.IsNull && !result.FileContent.IsEmpty)
            {
                throw ServiceResultException.Create(StatusCodes.BadNotSupported,
                    "Result-file transfer is not enabled on this binding.");
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_lifetimeToken);
            linked.Token.ThrowIfCancellationRequested();
            await publisher.PublishAsync(m_store.Convert(result), linked.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Captures one immutable sample; the shared ring retains this assignment action when results shift.
        /// </summary>
        private Action<ISystemContext, ResultState> PrepareProjection(MachineryResult result)
        {
            GmsResult snapshot = m_store.Snapshot(result);
            DataValue sample = snapshot.Value;
            ArrayOf<string> tools = snapshot.UsedTools;
            return (_, state) =>
            {
                state.StatusCode = sample.StatusCode;
                state.Timestamp = sample.SourceTimestamp;
                if (state.ResultMetaData != null)
                {
                    state.ResultMetaData.StatusCode = sample.StatusCode;
                    state.ResultMetaData.Timestamp = sample.SourceTimestamp;
                }
                if (state.ResultContent != null)
                {
                    state.ResultContent.StatusCode = sample.StatusCode;
                    state.ResultContent.Timestamp = sample.SourceTimestamp;
                }
                if (state is GMSResultState { UsedTools: not null } gms)
                {
                    gms.UsedTools.Value = tools;
                    gms.UsedTools.StatusCode = tools.IsNull ? StatusCodes.BadNoDataAvailable : sample.StatusCode;
                    gms.UsedTools.Timestamp = sample.SourceTimestamp;
                }
            };
        }

        /// <summary>
        /// Selects the released final or intermediate base, with actual characteristic and GMS result instances.
        /// </summary>
        private ResultReadyEventState CreateEvent(ISystemContext context, NodeState parent, MachineryResult result)
        {
            GmsResult snapshot = m_store.Snapshot(result);
            ResultReadyEventState state;
            if (snapshot.IsIntermediate)
            {
                IntermediateResultEventState intermediate =
                    context.CreateInstanceOfIntermediateResultEventType(parent, default);
                foreach (GmsCharacteristic characteristic in snapshot.Characteristics)
                {
                    CharacteristicState child = intermediate.AddCharacteristics_Placeholder(
                        context, new QualifiedName(characteristic.Name, m_state.NodeId.NamespaceIndex));
                    GmsMeasurementNodes.SetCharacteristic(context, child, characteristic);
                }
                if (snapshot.ProgramName != null)
                {
                    intermediate.AddProgramName(context);
                    intermediate.ProgramName!.Value = snapshot.ProgramName;
                }
                state = intermediate;
            }
            else
            {
                state = context.CreateInstanceOfResultReadyEventType(parent, default);
            }
            GMSResultState value = context.CreateInstanceOfGMSResultType(state, default);
            if (m_options.IncludeUsedTools)
            {
                value.AddUsedTools(context);
            }
            state.CreateOrReplaceResult(context, value);
            return state;
        }

        /// <summary>
        /// Updates only an already-declared correction with the same shape and characteristic reference.
        /// </summary>
        internal async ValueTask PublishCorrectionAsync(GmsCorrection correction, CancellationToken cancellationToken)
        {
            if (correction == null)
            {
                throw new ArgumentNullException(nameof(correction));
            }
            cancellationToken.ThrowIfCancellationRequested();
            EnsureActive();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_lifetimeToken);
            cancellationToken = linked.Token;
            GmsResultBudget.MeasureCorrection(correction, m_options.Limits, m_message);
            CorrectionState state;
            lock (m_correctionsGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureActive();
                if (!m_corrections.TryGetValue(correction.Identifier, out CorrectionEntry? entry))
                {
                    throw ServiceResultException.Create(StatusCodes.BadNotFound,
                        "The correction is not configured on this GMS instance.");
                }
                GmsCorrection original = entry.Shape;
                if (original.CharacteristicIdentifier != correction.CharacteristicIdentifier ||
                    original.RelativeValue == null != (correction.RelativeValue == null) ||
                    original.AbsoluteValue == null != (correction.AbsoluteValue == null) ||
                    original.LowerControlLimit == null != (correction.LowerControlLimit == null) ||
                    original.UpperControlLimit == null != (correction.UpperControlLimit == null) ||
                    original.ProgramName == null != (correction.ProgramName == null) ||
                    original.Description.IsNull != correction.Description.IsNull)
                {
                    throw ServiceResultException.Create(StatusCodes.BadInvalidArgument,
                        "Changing a correction's reference or optional-node shape requires rebuilding the instance.");
                }
                state = entry.State;
                GmsMeasurementNodes.SetCorrection(m_context, state, correction);
            }
            await state.ClearChangeMasksAsync(m_context, includeChildren: true, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Stages the optional fixed correction tree before the one owner publishes it.
        /// </summary>
        private void ConfigureCorrections(ISystemContext context)
        {
            if (m_options.Corrections.IsNull)
            {
                return;
            }
            m_state.ResultManagement!.AddCorrectionsFolder(context);
            FolderState folder = m_state.ResultManagement.CorrectionsFolder!;
            foreach (GmsCorrection correction in m_options.Corrections)
            {
                CorrectionState state = context.CreateInstanceOfCorrectionType(
                    folder, new QualifiedName(correction.Identifier, m_state.NodeId.NamespaceIndex));
                state.ReferenceTypeId = ReferenceTypeIds.HasComponent;
                GmsMeasurementNodes.SetCorrection(context, state, correction);
                MakeCorrectionReadOnly(context, state);
                folder.AddChild(state);
                m_corrections.Add(correction.Identifier, new CorrectionEntry(state, correction));
            }
            BaseDataVariableState count = context.CreateInstanceOfBaseDataVariableType(folder,
                new QualifiedName("CorrectionCount",
                    (ushort)context.NamespaceUris.GetIndex(GmsModel.NamespaceUri)));
            count.DataType = Ua.DataTypeIds.Integer;
            count.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            count.Value = Variant.From((long)m_corrections.Count);
            count.AccessLevel = AccessLevels.CurrentRead;
            count.UserAccessLevel = AccessLevels.CurrentRead;
            folder.AddChild(count);
        }

        /// <summary>
        /// Keeps published correction observations client-readonly without changing their model declarations.
        /// </summary>
        private static void MakeCorrectionReadOnly(ISystemContext context, NodeState node)
        {
            if (node is BaseVariableState variable)
            {
                variable.AccessLevel = AccessLevels.CurrentRead;
                variable.UserAccessLevel = AccessLevels.CurrentRead;
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                MakeCorrectionReadOnly(context, child);
            }
        }

        /// <summary>
        /// Prevents publication before commitment or after lifetime termination.
        /// </summary>
        private void EnsureActive()
        {
            if (Volatile.Read(ref m_active) != 1)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState, "The GMS result binding is not active.");
            }
        }

        private readonly GMSState m_state;
        private readonly GmsResultBindingOptions m_options;
        private readonly GmsResultStoreAdapter m_store;
        private readonly IGmsResultStore m_provider;
        private readonly ISystemContext m_context;
        private readonly IServiceMessageContext m_message;
        private readonly CancellationTokenSource m_lifetime = new();
        private readonly CancellationToken m_lifetimeToken;
        private readonly Lock m_correctionsGate = new();
        private readonly Dictionary<string, CorrectionEntry> m_corrections = new(StringComparer.Ordinal);
        private int m_active;
        private static readonly Lock s_ownershipGate = new();
        private static readonly ConditionalWeakTable<IGmsResultStore, GmsResultBinding> s_storeOwners =
#if NET8_0_OR_GREATER
            [];
#else
            new();
#endif

        /// <summary>
        /// Captures the stable node and initial optional-member shape.
        /// </summary>
        /// <param name="State">The configured generated correction node.</param>
        /// <param name="Shape">Its original member presence and characteristic reference.</param>
        private sealed record CorrectionEntry(CorrectionState State, GmsCorrection Shape);
    }

    /// <summary>
    /// Publishes immutable GMS measurements through the shared Machinery result lifetime and event path.
    /// </summary>
    public sealed class GmsResultPublisher
    {
        /// <summary>
        /// Captures the one binding and committed shared publisher that own these results.
        /// </summary>
        internal GmsResultPublisher(GmsResultBinding binding, IMachineryResultPublisher publisher)
        {
            m_binding = binding;
            m_publisher = publisher;
        }

        /// <summary>
        /// Publishes an acquired result without changing its quality, timestamps or standard wire envelope.
        /// </summary>
        public ValueTask PublishAsync(GmsResult result, CancellationToken cancellationToken = default)
        {
            return m_binding.PublishAsync(m_publisher, result, cancellationToken);
        }

        /// <summary>
        /// Publishes a calculated correction sample; this updates monitoring data, not a device-control command.
        /// </summary>
        public ValueTask PublishCorrectionAsync(
            GmsCorrection correction,
            CancellationToken cancellationToken = default)
        {
            return m_binding.PublishCorrectionAsync(correction, cancellationToken);
        }

        private readonly GmsResultBinding m_binding;
        private readonly IMachineryResultPublisher m_publisher;
    }
}
