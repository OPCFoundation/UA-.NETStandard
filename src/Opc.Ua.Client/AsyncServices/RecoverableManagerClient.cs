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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Binds the generated manager proxy to discovered instance Methods before any submission is attempted.
    /// </summary>
    internal sealed class RecoverableManagerClient : IDisposable
    {
        /// <summary>
        /// Creates non-owning generated plumbing over the captured, send-once transport.
        /// </summary>
        private RecoverableManagerClient(ISession session, NodeId managerId, ArrayOf<NodeId> methods)
        {
            m_transport = new ManagerTransport(session.TransportChannel, managerId, methods);
            m_client = new ManagerSessionClient(m_transport, session.MessageContext.Telemetry)
            {
                ReturnDiagnostics = session.ReturnDiagnostics,
                DefaultTimeoutHint = session.DefaultTimeoutHint
            };
            SessionConfiguration configuration = session.SaveSessionConfiguration();
            m_client.SessionCreated(configuration.SessionId, configuration.AuthenticationToken);
            m_proxy = new RecoverableExecutionManagerTypeClient(
                m_client, managerId, session.MessageContext.Telemetry);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_client.Dispose();
            m_transport.Dispose();
        }

        /// <summary>
        /// Discovers the manager, its mandatory members and the current admission capabilities.
        /// Only an absent namespace/root or incompatible revision establishes unsupported discovery.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Capabilities own the manager proxy; RecoverableExecutionBinding disposes them. " +
                "TODO: model this asynchronous ownership handoff in disposal analysis.")]
        internal static async ValueTask<RecoverableCapabilities> DiscoverAsync(
            ISession session, RecoverableSessionOptions options, CancellationToken ct)
        {
            int index = session.NamespaceUris.GetIndex(NamespaceUri);
            if (index < 0)
            {
                return RecoverableCapabilities.Unsupported;
            }
            ushort namespaceIndex = checked((ushort)index);
            ArrayOf<NodeId> roots = await ResolveAsync(
                session, ObjectIds.Server, ["RecoverableExecutionManager"], namespaceIndex,
                allowMissing: true, ct).ConfigureAwait(false);
            if (roots.IsEmpty)
            {
                return RecoverableCapabilities.Unsupported;
            }
            NodeId manager = roots[0];
            ArrayOf<NodeId> members = await ResolveAsync(session, manager,
                ["Capabilities", "Submit", "GetResult", "Acknowledge", "Cancel", "Abandon"],
                namespaceIndex, allowMissing: false, ct).ConfigureAwait(false);
            ArrayOf<NodeId> properties = await ResolveAsync(session, members[0],
                ["ContractVersion", "SupportedServices", "DefaultExecutionAllowance",
                    "DefaultResultRetentionTime", "SupportedRecoveryModes"],
                namespaceIndex, allowMissing: false, ct).ConfigureAwait(false);
            var reads = new ReadValueId[properties.Count];
            for (int i = 0; i < reads.Length; i++)
            {
                reads[i] = new ReadValueId { NodeId = properties[i], AttributeId = Attributes.Value };
            }
            ReadResponse response = await session.ReadAsync(
                null, 0, TimestampsToReturn.Neither, reads, ct).ConfigureAwait(false);
            try
            {
                if (response.Results.Count != reads.Length)
                {
                    throw InvalidResponse();
                }
                ClientBase.ValidateResponse(response.Results, reads.ToArrayOf());
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, reads.ToArrayOf());
                RequireGood(response.Results[0].StatusCode);
                if (!response.Results[0].WrappedValue.TryGetValue(out string? revision) ||
                    string.IsNullOrEmpty(revision))
                {
                    throw InvalidResponse();
                }
                if (!string.Equals(revision, "0.4.0", StringComparison.Ordinal))
                {
                    return RecoverableCapabilities.Unsupported;
                }
                RequireGood(response.Results[1].StatusCode);
                RequireGood(response.Results[4].StatusCode);
                if (!response.Results[1].WrappedValue.TryGetValue(out ArrayOf<NodeId> services) ||
                    !response.Results[4].WrappedValue.TryGetValue(out ArrayOf<int> modes) ||
                    !modes.Contains((int)ServiceOperationRecoveryMode.Session))
                {
                    throw InvalidResponse();
                }
                foreach (NodeId service in services)
                {
                    if (service.IsNull)
                    {
                        throw InvalidResponse();
                    }
                }
                foreach (int mode in modes)
                {
                    if (mode is < (int)ServiceOperationRecoveryMode.Session or
                        > (int)ServiceOperationRecoveryMode.Durable)
                    {
                        throw InvalidResponse();
                    }
                }
                double execution = options.ExecutionAllowance ?? ReadBudget(response.Results[2]);
                double retention = options.ResultRetentionTime ?? ReadBudget(response.Results[3]);
                var client = new RecoverableManagerClient(session, manager,
                    [members[1], members[2], members[3], members[4], members[5]]);
                return new RecoverableCapabilities(true, services, modes, execution, retention, client);
            }
            finally
            {
                response.Reuse();
            }
        }

        /// <summary>
        /// Sends the one generated Submit invocation; neither this binding nor the proxy retries it.
        /// </summary>
        internal ValueTask<SubmitResponseDataType> SubmitAsync(SubmitRequestDataType request, CancellationToken ct)
        {
            return m_proxy.SubmitAsync(request, ct);
        }

        /// <summary>
        /// Collects through a fresh ordinary Call.
        /// </summary>
        internal ValueTask<GetResultResponseDataType> GetResultAsync(
            GetResultRequestDataType request, CancellationToken ct)
        {
            return m_proxy.GetResultAsync(request, ct);
        }

        /// <summary>
        /// Invokes Acknowledge without an action field or another original request.
        /// </summary>
        internal ValueTask<ControlResponseDataType> AcknowledgeAsync(
            OperationControlRequestDataType request, CancellationToken ct)
        {
            return m_proxy.AcknowledgeAsync(request, ct);
        }

        /// <summary>
        /// Requests cancellation of the owned invocation, not cancellation of an ordinary exchange.
        /// </summary>
        internal ValueTask<ControlResponseDataType> CancelAsync(
            OperationControlRequestDataType request, CancellationToken ct)
        {
            return m_proxy.CancelAsync(request, ct);
        }

        /// <summary>
        /// Waives result recovery without requesting cancellation.
        /// </summary>
        internal ValueTask<ControlResponseDataType> AbandonAsync(
            OperationControlRequestDataType request, CancellationToken ct)
        {
            return m_proxy.AbandonAsync(request, ct);
        }

        /// <summary>
        /// Resolves actual instance NodeIds, rejecting ambiguous, partial and remote targets.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static async ValueTask<ArrayOf<NodeId>> ResolveAsync(
            ISession session, NodeId root, ArrayOf<string> names, ushort namespaceIndex,
            bool allowMissing, CancellationToken ct)
        {
            var paths = new BrowsePath[names.Count];
            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = new BrowsePath
                {
                    StartingNode = root,
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName(names[i], namespaceIndex)
                            }
                        ]
                    }
                };
            }
            TranslateBrowsePathsToNodeIdsResponse response = await session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths, ct).ConfigureAwait(false);
            try
            {
                if (response.Results.Count != paths.Length)
                {
                    throw InvalidResponse();
                }
                ClientBase.ValidateResponse(response.Results, paths.ToArrayOf());
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, paths.ToArrayOf());
                var nodes = new NodeId[paths.Length];
                for (int i = 0; i < nodes.Length; i++)
                {
                    BrowsePathResult result = response.Results[i] ?? throw InvalidResponse();
                    if (allowMissing &&
                        (result.StatusCode == StatusCodes.BadNoMatch ||
                            result.StatusCode == StatusCodes.BadNotFound ||
                            result.StatusCode == StatusCodes.BadNodeIdUnknown))
                    {
                        return [];
                    }
                    RequireGood(result.StatusCode);
                    if (result.Targets.Count != 1 ||
                        result.Targets[0] == null ||
                        result.Targets[0].RemainingPathIndex != uint.MaxValue ||
                        result.Targets[0].TargetId.ServerIndex != 0)
                    {
                        throw InvalidResponse();
                    }
                    nodes[i] = ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, session.NamespaceUris);
                    if (nodes[i].IsNull)
                    {
                        throw InvalidResponse();
                    }
                }
                return nodes;
            }
            finally
            {
                response.Reuse();
            }
        }

        /// <summary>
        /// Reads an advertised exact budget without silently clamping or replacing invalid metadata.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static double ReadBudget(in DataValue value)
        {
            RequireGood(value.StatusCode);
            if (!value.WrappedValue.TryGetValue(out double budget))
            {
                throw InvalidResponse();
            }
            if (double.IsNaN(budget) || double.IsInfinity(budget) || budget <= 0)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                    "Recovery budgets must be positive finite milliseconds.");
            }
            return budget;
        }

        /// <summary>
        /// Keeps authorization, transport and data-quality errors distinct from unsupported discovery.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void RequireGood(StatusCode status)
        {
            if (!StatusCode.IsGood(status))
            {
                throw new ServiceResultException(status);
            }
        }

        /// <summary>
        /// Reports malformed protocol data without exposing operation identifiers or payloads.
        /// </summary>
        private static ServiceResultException InvalidResponse()
        {
            return new ServiceResultException(StatusCodes.BadDecodingError, "Invalid recoverable manager response.");
        }

        /// <summary>
        /// Prevents generated ObjectType compatibility fallback after any manager Call.
        /// </summary>
        private sealed class ManagerSessionClient(ITransportChannel transport, ITelemetryContext telemetry) :
            SessionClient(transport, telemetry), IMethodCallRetryPolicy
        {
            /// <inheritdoc/>
            public bool AllowMethodIdFallback => false;
        }

        /// <summary>
        /// Maps known declarations to pre-resolved instances and validates the complete ordinary Call envelope.
        /// </summary>
        private sealed class ManagerTransport : ITransportChannel
        {
            /// <summary>
            /// Captures a non-owning transport and the five actual Method instances.
            /// </summary>
            internal ManagerTransport(ITransportChannel transport, NodeId manager, ArrayOf<NodeId> methods)
            {
                m_transport = transport;
                m_manager = manager;
                ExpandedNodeId[] declarations =
                [
                    RseMethodIds.RecoverableExecutionManagerType_Submit,
                    RseMethodIds.RecoverableExecutionManagerType_GetResult,
                    RseMethodIds.RecoverableExecutionManagerType_Acknowledge,
                    RseMethodIds.RecoverableExecutionManagerType_Cancel,
                    RseMethodIds.RecoverableExecutionManagerType_Abandon
                ];
                for (int i = 0; i < declarations.Length; i++)
                {
                    var declaration = ExpandedNodeId.ToNodeId(declarations[i], MessageContext.NamespaceUris);
                    m_methods.Add(declaration, (methods[i], i));
                }
            }

            /// <inheritdoc/>
            public TransportChannelFeatures SupportedFeatures => TransportChannelFeatures.None;

            /// <inheritdoc/>
            public EndpointDescription EndpointDescription => m_transport.EndpointDescription;

            /// <inheritdoc/>
            public EndpointConfiguration EndpointConfiguration => m_transport.EndpointConfiguration;

            /// <inheritdoc/>
            public byte[] ChannelThumbprint => m_transport.ChannelThumbprint;

            /// <inheritdoc/>
            public byte[] ClientChannelCertificate => m_transport.ClientChannelCertificate;

            /// <inheritdoc/>
            public byte[] ServerChannelCertificate => m_transport.ServerChannelCertificate;

            /// <inheritdoc/>
            public IServiceMessageContext MessageContext => m_transport.MessageContext;

            /// <inheritdoc/>
            public int OperationTimeout
            {
                get => m_transport.OperationTimeout;
                set => m_transport.OperationTimeout = value;
            }

            /// <inheritdoc/>
            public async ValueTask<IServiceResponse> SendRequestAsync(
                IServiceRequest request, CancellationToken ct = default)
            {
                if (request is not CallRequest call ||
                    call.MethodsToCall.Count != 1 ||
                    call.MethodsToCall[0].ObjectId != m_manager ||
                    !m_methods.TryGetValue(call.MethodsToCall[0].MethodId, out (NodeId Instance, int Index) method) ||
                    call.MethodsToCall[0].InputArguments.Count != 1)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
                call.MethodsToCall[0].MethodId = method.Instance;
                IServiceResponse response = await m_transport.SendRequestAsync(request, ct).ConfigureAwait(false);
                try
                {
                    if (response?.ResponseHeader == null ||
                        response.ResponseHeader.RequestHandle != request.RequestHeader.RequestHandle)
                    {
                        throw InvalidResponse();
                    }
                    RequireGood(response.ResponseHeader.ServiceResult);
                    if (response is not CallResponse result ||
                        result.Results.Count != 1 ||
                        result.Results[0] is not { } item)
                    {
                        throw InvalidResponse();
                    }
                    ClientBase.ValidateDiagnosticInfos(result.DiagnosticInfos, call.MethodsToCall);
                    if (!StatusCode.IsGood(item.StatusCode))
                    {
                        throw ServiceResultException.Create(item.StatusCode, 0,
                            result.DiagnosticInfos, result.ResponseHeader.StringTable);
                    }
                    if (item.InputArgumentResults.Count is not (0 or 1) ||
                        (item.InputArgumentResults.Count == 1 && !StatusCode.IsGood(item.InputArgumentResults[0])) ||
                        item.InputArgumentDiagnosticInfos.Count is not (0 or 1) ||
                        item.OutputArguments.Count != 1 ||
                        !item.OutputArguments[0].TryGetValue(out ExtensionObject output) ||
                        output.IsNull ||
                        !HasDeclaredResponse(output, method.Index))
                    {
                        throw InvalidResponse();
                    }
                    return CoreUtils.Clone(result)!;
                }
                finally
                {
                    if (response is IPooledEncodeable pooled)
                    {
                        pooled.Reuse();
                    }
                }
            }

            /// <inheritdoc/>
            public ValueTask ReconnectAsync(
                ITransportWaitingConnection? connection = null, CancellationToken ct = default)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }

            /// <inheritdoc/>
            public ValueTask CloseAsync(CancellationToken ct = default)
            {
                return default;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
            }

            /// <summary>
            /// Rejects null and wrong typed scalar outputs before the generated proxy can accept them.
            /// </summary>
            private bool HasDeclaredResponse(ExtensionObject output, int method)
            {
                return method switch
                {
                    0 => output.TryGetValue(out SubmitResponseDataType? response, MessageContext) &&
                        response?.Operation != null,
                    1 => output.TryGetValue(out GetResultResponseDataType? response, MessageContext) &&
                        response?.Operation != null,
                    _ => output.TryGetValue(out ControlResponseDataType? response, MessageContext) &&
                        response?.Operation != null
                };
            }

            private readonly ITransportChannel m_transport;
            private readonly NodeId m_manager;
            private readonly Dictionary<NodeId, (NodeId Instance, int Index)> m_methods = [];
        }

        /// <summary>
        /// The namespace URI of the committed experimental model.
        /// </summary>
        internal const string NamespaceUri = RseNamespaces.Rse;

        private readonly ManagerTransport m_transport;
        private readonly ManagerSessionClient m_client;
        private readonly RecoverableExecutionManagerTypeClient m_proxy;
    }
}
