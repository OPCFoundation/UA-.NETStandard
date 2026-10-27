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
using System.Threading.Tasks;
using Opc.Ua.DataChannels;
using Opc.Ua.Di;
using Opc.Ua.Fx.Ac;
using Opc.Ua.Fx.Cm;
using Opc.Ua.Fx.Data;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.StateMachine;

namespace Opc.Ua.PubSub.Server
{
    public sealed partial class PubSubNodeManager
    {
        /// <summary>
        /// Projects the associated Part 14 reader and writer states onto a host-owned UAFX endpoint.
        /// The host retains ownership of the endpoint and runtime associations and disposes this binding before
        /// changing them. This does not establish communication, grant configuration authority or alter PubSub state.
        /// </summary>
        [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
            UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
        public IDisposable BindFxEndpointStatus(
            ISystemContext context,
            PubSubConnectionEndpointState endpoint,
            IDataSetReader? reader = null,
            IDataSetWriter? writer = null)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (endpoint is null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            if (endpoint.NodeId.IsNull || endpoint.Status is null || endpoint.Mode is null)
            {
                throw new ArgumentException("An initialized PubSub ConnectionEndpoint is required.", nameof(endpoint));
            }
            PubSubConnectionEndpointModeEnum mode = endpoint.Mode.Value;
            if (mode is not (PubSubConnectionEndpointModeEnum.Publisher or
                PubSubConnectionEndpointModeEnum.Subscriber or PubSubConnectionEndpointModeEnum.PublisherSubscriber) ||
                (mode == PubSubConnectionEndpointModeEnum.Publisher && reader is not null) ||
                (mode == PubSubConnectionEndpointModeEnum.Subscriber && writer is not null))
            {
                throw new ArgumentException("Runtime associations do not match the endpoint mode.", nameof(endpoint));
            }
            lock (m_addressSpaceGate)
            {
                if (m_fxStatusDisposed)
                {
                    throw new ObjectDisposedException(nameof(PubSubNodeManager));
                }
                if (endpoint.Status.OnSimpleReadValue is not null || endpoint.Status.OnReadValue is not null ||
                    endpoint.Status.OnReadValueAsync is not null || endpoint.Status.OnSimpleReadValueAsync is not null)
                {
                    throw new InvalidOperationException("The endpoint Status already has a read handler.");
                }
                var binding = new FxEndpointStatusBinding(
                    context, endpoint.Status, mode, reader?.State, writer?.State, ReleaseFxStatusBinding);
                m_fxStatusBindings.Add(binding);
                return binding;
            }
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            var nodes = new NodeStateCollection();
            if (m_options.ExposeFxModel)
            {
                nodes.AddOpcUaDi(context)
                    .AddOpcUaFxData(context)
                    .AddOpcUaFxAc(context)
                    .AddOpcUaFxCm(context);
            }
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <summary>
        /// Detaches endpoint projections without disposing host-owned nodes or PubSub components.
        /// </summary>
        private void DisposeFxStatusBindings()
        {
            FxEndpointStatusBinding[] bindings;
            lock (m_addressSpaceGate)
            {
                m_fxStatusDisposed = true;
                bindings = [.. m_fxStatusBindings];
                m_fxStatusBindings.Clear();
            }
            foreach (FxEndpointStatusBinding binding in bindings)
            {
                binding.Dispose();
            }
        }

        /// <summary>
        /// Releases a detached projection from the manager's finite live binding set.
        /// </summary>
        private void ReleaseFxStatusBinding(FxEndpointStatusBinding binding)
        {
            lock (m_addressSpaceGate)
            {
                m_fxStatusBindings.Remove(binding);
            }
        }

        /// <summary>
        /// Owns only status read and notification callbacks, never the associated communication resources.
        /// </summary>
        private sealed class FxEndpointStatusBinding : IDisposable
        {
            /// <summary>
            /// Installs the live reader/writer projection specified by Part 81 Figures 31-33.
            /// </summary>
            public FxEndpointStatusBinding(
                ISystemContext context,
                BaseDataVariableState<ConnectionEndpointStatusEnum> status,
                PubSubConnectionEndpointModeEnum mode,
                PubSubStateMachine? reader,
                PubSubStateMachine? writer,
                Action<FxEndpointStatusBinding> release)
            {
                m_context = context;
                m_status = status;
                m_mode = mode;
                m_reader = reader;
                m_writer = writer;
                m_release = release;
                bool initialized = false;
                try
                {
                    lock (m_gate)
                    {
                        status.OnSimpleReadValue = OnRead;
                        if (reader is not null)
                        {
                            reader.StateChanged += OnStateChanged;
                        }
                        if (writer is not null && writer != reader)
                        {
                            writer.StateChanged += OnStateChanged;
                        }
                        status.Value = GetStatus();
                        initialized = true;
                    }
                }
                finally
                {
                    if (!initialized)
                    {
                        Dispose();
                    }
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
                    if (m_status.OnSimpleReadValue == OnRead)
                    {
                        m_status.OnSimpleReadValue = null;
                    }
                    if (m_reader is not null)
                    {
                        m_reader.StateChanged -= OnStateChanged;
                    }
                    if (m_writer is not null && m_writer != m_reader)
                    {
                        m_writer.StateChanged -= OnStateChanged;
                    }
                }
                m_release(this);
            }

            /// <summary>
            /// Reads component health, not carrier readiness or management Call success.
            /// </summary>
            private ServiceResult OnRead(ISystemContext context, NodeState node, ref Variant value)
            {
                lock (m_gate)
                {
                    if (m_disposed)
                    {
                        return new ServiceResult(StatusCodes.BadInvalidState);
                    }
                    value = Variant.From(GetStatus());
                    return ServiceResult.Good;
                }
            }

            /// <summary>
            /// Makes reader timeouts and writer failures observable through normal data-change sampling.
            /// </summary>
            private void OnStateChanged(object? sender, PubSubStateChangedEventArgs args)
            {
                lock (m_gate)
                {
                    if (m_disposed)
                    {
                        return;
                    }
                    m_status.Value = GetStatus();
                    m_status.ClearChangeMasks(m_context, false);
                }
            }

            /// <summary>
            /// Missing required associations take precedence over error; error takes precedence over readiness.
            /// </summary>
            private ConnectionEndpointStatusEnum GetStatus()
            {
                PubSubState? reader = m_reader?.State;
                PubSubState? writer = m_writer?.State;
                if (!IsKnownState(reader) || !IsKnownState(writer))
                {
                    throw new InvalidOperationException("The PubSub runtime reported an unsupported component state.");
                }
                if ((m_mode != PubSubConnectionEndpointModeEnum.Publisher && reader is null) ||
                    (m_mode != PubSubConnectionEndpointModeEnum.Subscriber && writer is null))
                {
                    return ConnectionEndpointStatusEnum.Initial;
                }
                if (reader == PubSubState.Error || writer == PubSubState.Error)
                {
                    return ConnectionEndpointStatusEnum.Error;
                }
                if (reader is PubSubState.Disabled or PubSubState.Paused ||
                    writer is PubSubState.Disabled or PubSubState.Paused)
                {
                    return ConnectionEndpointStatusEnum.Ready;
                }
                if (reader == PubSubState.PreOperational || writer == PubSubState.PreOperational)
                {
                    return ConnectionEndpointStatusEnum.PreOperational;
                }
                return ConnectionEndpointStatusEnum.Operational;
            }

            /// <summary>
            /// Rejects corrupt restored state instead of projecting it as healthy communication.
            /// </summary>
            private static bool IsKnownState(PubSubState? state)
            {
                return state is null or PubSubState.Disabled or PubSubState.Paused or PubSubState.PreOperational
                    or PubSubState.Operational or PubSubState.Error;
            }

            /// <summary>The host's notification context.</summary>
            private readonly ISystemContext m_context;

            /// <summary>The borrowed generated endpoint Status variable.</summary>
            private readonly BaseDataVariableState<ConnectionEndpointStatusEnum> m_status;

            /// <summary>The configured endpoint mode, fixed for this binding's lifetime.</summary>
            private readonly PubSubConnectionEndpointModeEnum m_mode;

            /// <summary>The borrowed reader health source.</summary>
            private readonly PubSubStateMachine? m_reader;

            /// <summary>The borrowed writer health source.</summary>
            private readonly PubSubStateMachine? m_writer;

            /// <summary>Releases the manager-owned registration after detaching callbacks.</summary>
            private readonly Action<FxEndpointStatusBinding> m_release;

            /// <summary>Serializes callback lifetime and disposal.</summary>
            private readonly Lock m_gate = new();

            /// <summary>Whether all owned callbacks have been detached.</summary>
            private bool m_disposed;
        }

        /// <summary>Endpoint projections owned by this node manager.</summary>
        private readonly List<FxEndpointStatusBinding> m_fxStatusBindings = [];

        /// <summary>Prevents attaching projections after manager disposal.</summary>
        private bool m_fxStatusDisposed;
    }
}
