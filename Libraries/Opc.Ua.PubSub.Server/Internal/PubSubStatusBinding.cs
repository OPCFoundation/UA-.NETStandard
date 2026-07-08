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
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.StateMachine;
using Opc.Ua.Server;

namespace Opc.Ua.PubSub.Server.Internal
{
    /// <summary>
    /// Projects the runtime
    /// <see cref="IPubSubApplication.State"/> onto the
    /// <c>PublishSubscribe_Status_State</c> Variable
    /// (NodeId <c>i=17406</c>) and binds
    /// <see cref="IPubSubDiagnostics"/> counters onto the matching
    /// <c>PublishSubscribe_Diagnostics_Counters_*</c> Variables.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.10">
    /// Part 14 §9.1.10 PubSubStatusType</see> for the <c>State</c>
    /// Variable and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.11">
    /// Part 14 §9.1.11 PubSubDiagnosticsType</see> for the counter
    /// projection. The class is internal: <see cref="PubSubNodeManager"/>
    /// owns the binding lifetime and disposes it when the node
    /// manager is torn down.
    /// </remarks>
    internal sealed class PubSubStatusBinding : IDisposable
    {
        private static readonly NodeId s_statusStateNodeId = new(17406);

        private static readonly KeyValuePair<PubSubDiagnosticsCounterKind, NodeId>[] s_counterNodeIds =
        [
            new(PubSubDiagnosticsCounterKind.StateOperationalByMethod, new NodeId(17431)),
            new(PubSubDiagnosticsCounterKind.StateOperationalByParent, new NodeId(17436)),
            new(PubSubDiagnosticsCounterKind.StateOperationalFromError, new NodeId(17441)),
            new(PubSubDiagnosticsCounterKind.StatePausedByParent, new NodeId(17446)),
            new(PubSubDiagnosticsCounterKind.StateDisabledByMethod, new NodeId(17451))
        ];

        /// <summary>
        /// Number of counter NodeIds that are bound by the status binding.
        /// Exposed for testing purposes.
        /// </summary>
        public static int CounterNodeIdCount => s_counterNodeIds.Length;

        private readonly IPubSubApplication m_application;
        private readonly IPubSubDiagnostics m_diagnostics;
        private readonly IDiagnosticsNodeManager m_diagnosticsNodeManager;
        private readonly PubSubDiagnosticsExposure m_exposure;
        private readonly ILogger m_logger;
        private readonly Lock m_gate = new();
        private readonly List<BoundCounter> m_boundCounters = [];
        private BaseVariableState? m_stateVariable;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new <see cref="PubSubStatusBinding"/>.
        /// </summary>
        /// <param name="application">Runtime application.</param>
        /// <param name="diagnostics">Diagnostics sink.</param>
        /// <param name="diagnosticsNodeManager">
        /// Owner of the standard PubSub nodes loaded from the stack
        /// NodeSet.
        /// </param>
        /// <param name="exposure">Diagnostic exposure level.</param>
        /// <param name="telemetry">Telemetry context.</param>
        public PubSubStatusBinding(
            IPubSubApplication application,
            IPubSubDiagnostics diagnostics,
            IDiagnosticsNodeManager diagnosticsNodeManager,
            PubSubDiagnosticsExposure exposure,
            ITelemetryContext telemetry)
        {
            if (application is null)
            {
                throw new ArgumentNullException(nameof(application));
            }
            if (diagnostics is null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }
            if (diagnosticsNodeManager is null)
            {
                throw new ArgumentNullException(nameof(diagnosticsNodeManager));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_application = application;
            m_diagnostics = diagnostics;
            m_diagnosticsNodeManager = diagnosticsNodeManager;
            m_exposure = exposure;
            m_logger = telemetry.CreateLogger<PubSubStatusBinding>();
        }

        /// <summary>
        /// Number of diagnostic counters successfully bound. Useful
        /// for test assertions.
        /// </summary>
        public int BoundCounterCount
        {
            get
            {
                lock (m_gate)
                {
                    return m_boundCounters.Count;
                }
            }
        }

        /// <summary>
        /// <see langword="true"/> if the <c>Status.State</c> Variable
        /// was found and bound to the runtime state machine.
        /// </summary>
        public bool StateBound => m_stateVariable is not null;

        /// <summary>
        /// Activates the binding: resolves the standard nodes,
        /// installs the read callbacks, and subscribes to
        /// <see cref="PubSubStateMachine.StateChanged"/>.
        /// </summary>
        public void Bind()
        {
            BaseVariableState? stateVar = m_diagnosticsNodeManager
                .FindPredefinedNode<BaseVariableState>(s_statusStateNodeId);
            if (stateVar is null)
            {
                m_logger.LogWarning(
                    "PublishSubscribe Status State Variable {NodeId} not found; cannot bind state.",
                    s_statusStateNodeId);
            }
            else
            {
                stateVar.Value = Variant.From(m_application.State.State);
                stateVar.OnSimpleReadValue = OnReadStateValue;
                m_stateVariable = stateVar;
                m_application.State.StateChanged += OnStateChanged;
            }

            if (m_exposure == PubSubDiagnosticsExposure.None)
            {
                return;
            }

            foreach (KeyValuePair<PubSubDiagnosticsCounterKind, NodeId> kv in s_counterNodeIds)
            {
                BaseVariableState? counter = m_diagnosticsNodeManager
                    .FindPredefinedNode<BaseVariableState>(kv.Value);
                if (counter is null)
                {
                    m_logger.LogDebug(
                        "PublishSubscribe diagnostics counter {NodeId} not found in address space.",
                        kv.Value);
                    continue;
                }
                BindCounter(counter, kv.Key);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
            }
            m_application.State.StateChanged -= OnStateChanged;
            m_stateVariable?.OnSimpleReadValue = null;
            foreach (BoundCounter bound in m_boundCounters)
            {
                bound.Variable.OnSimpleReadValue = null;
            }
        }

        private void BindCounter(BaseVariableState counter, PubSubDiagnosticsCounterKind kind)
        {
            counter.Value = Variant.From((uint)m_diagnostics.Read(kind));
            counter.OnSimpleReadValue = (ISystemContext context, NodeState node, ref Variant value) =>
            {
                long current = m_diagnostics.Read(kind);
                value = Variant.From((uint)Math.Min(current, uint.MaxValue));
                return ServiceResult.Good;
            };
            lock (m_gate)
            {
                m_boundCounters.Add(new BoundCounter(counter, kind));
            }
        }

        private ServiceResult OnReadStateValue(
            ISystemContext context,
            NodeState node,
            ref Variant value)
        {
            value = Variant.From(m_application.State.State);
            return ServiceResult.Good;
        }

        private void OnStateChanged(object? sender, PubSubStateChangedEventArgs e)
        {
            BaseVariableState? stateVar = m_stateVariable;
            if (stateVar is null)
            {
                return;
            }
            try
            {
                stateVar.Value = Variant.From(e.NewState);
                stateVar.ClearChangeMasks(null!, includeChildren: false);
            }
            catch (Exception ex)
            {
                m_logger.LogDebug(
                    ex,
                    "Failed to propagate PubSub state change {New} to Status State Variable.",
                    e.NewState);
            }
        }

        private readonly struct BoundCounter
        {
            public BoundCounter(BaseVariableState variable, PubSubDiagnosticsCounterKind kind)
            {
                Variable = variable;
                Kind = kind;
            }

            public BaseVariableState Variable { get; }

            public PubSubDiagnosticsCounterKind Kind { get; }
        }
    }
}
