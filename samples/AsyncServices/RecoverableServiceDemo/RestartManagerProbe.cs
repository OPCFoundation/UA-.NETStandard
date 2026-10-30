/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the OPC Foundation MIT License 1.00.
 * See LICENSE.txt in the project root for license information.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

namespace RecoverableServiceDemo
{
    /// <summary>
    /// Wire-only observations supplement recovery-only Client handles, which may restore accepted results locally.
    /// These are ordinary native Calls with generated RSE structures and freshly discovered instance MethodIds.
    /// There is deliberately NO Submit helper and no synthesized response/header.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class RestartManagerProbe(
        SessionRequestContext context, NodeId manager, NodeId getResult, NodeId acknowledge) : IAsyncDisposable
    {
        internal static async Task<RestartManagerProbe> DiscoverAsync(ISession session, CancellationToken ct)
        {
            SessionRequestContext captured = await SessionRequestContext.CaptureAsync(session, ct)
                .ConfigureAwait(false);
            try
            {
                // This public context uses the captured token and physical send-once transport.
                // Never call manager Methods through ManagedSession's recoverable application interceptor.
                ISession physical = captured.Session;
                RestartPipe.Require(
                    physical is not ManagedSession, "The manager probe must use a captured physical Session.");
                VerifyGeneratedRseRegistrations(physical.MessageContext, "captured Session");
                VerifyGeneratedRseRegistrations(physical.TransportChannel.MessageContext, "captured transport");
                int index = physical.NamespaceUris.GetIndex("http://opcfoundation.org/UA/RSE/");
                RestartPipe.Require(index > 0, "The canonical RSE namespace is missing.");
                ushort ns = checked((ushort)index);
                ArrayOf<NodeId> root = await ResolveAsync(
                    physical, ObjectIds.Server, ["RecoverableExecutionManager"], ns, ct)
                    .ConfigureAwait(false);
                ArrayOf<NodeId> methods = await ResolveAsync(
                    physical, root[0], ["Submit", "GetResult", "Acknowledge", "Cancel", "Abandon"], ns, ct)
                    .ConfigureAwait(false);
                var distinctMethods = new HashSet<NodeId>();
                ExpandedNodeId[] declarations =
                [
                    RseMethodIds.RecoverableExecutionManagerType_Submit,
                    RseMethodIds.RecoverableExecutionManagerType_GetResult,
                    RseMethodIds.RecoverableExecutionManagerType_Acknowledge,
                    RseMethodIds.RecoverableExecutionManagerType_Cancel,
                    RseMethodIds.RecoverableExecutionManagerType_Abandon
                ];
                for (int i = 0; i < methods.Count; i++)
                {
                    _ = distinctMethods.Add(methods[i]);
                    RestartPipe.Require(methods[i] != ExpandedNodeId.ToNodeId(declarations[i], physical.NamespaceUris),
                        "RSE manager discovery returned a declaration rather than a live instance Method.");
                }
                RestartPipe.Require(distinctMethods.Count == 5,
                    "The five manager Method instances were not independently discovered.");
                return new RestartManagerProbe(captured, root[0], methods[1], methods[2]);
            }
            catch
            {
                await captured.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        internal Task<GetResultResponseDataType> GetAsync(
            Uuid clientId, ServiceOperationRecoveryMode mode, CancellationToken ct)
        {
            return InvokeAsync<GetResultResponseDataType>("GetResult", getResult, new GetResultRequestDataType
            {
                RecoveryMode = mode,
                ClientOperationId = clientId,
                ServerToken = default,
                WaitTime = 0
            }, ct);
        }

        internal Task<ControlResponseDataType> AcknowledgeAgainAsync(ByteString token, CancellationToken ct)
        {
            return InvokeAsync<ControlResponseDataType>("Acknowledge", acknowledge,
                new OperationControlRequestDataType { ServerToken = token }, ct);
        }

        private async Task<TResponse> InvokeAsync<TResponse>(
            string methodName, NodeId method, IEncodeable request, CancellationToken ct)
            where TResponse : class, IEncodeable, new()
        {
            if (request is GetResultRequestDataType selection)
            {
                AssertGetResultSelector(selection);
            }
            ISession physical = context.Session;
            IServiceMessageContext decoding = physical.TransportChannel.MessageContext;
            VerifyGeneratedRseRegistrations(decoding, "actual response decode context");
            ArrayOf<CallMethodRequest> calls =
            [
                new CallMethodRequest
                {
                    ObjectId = manager,
                    MethodId = method,
                    InputArguments = [Variant.From(new ExtensionObject(request))]
                }
            ];
            CallResponse response = await physical.CallAsync(null, calls, ct).ConfigureAwait(false);
            try
            {
                RestartPipe.Require(response.ResponseHeader.ServiceResult == StatusCodes.Good,
                    $"RSE probe {methodName} outer status=0x{(uint)response.ResponseHeader.ServiceResult:X8}; " +
                    "expected Good.");
                RestartPipe.Require(response.Results.Count == 1,
                    $"RSE probe {methodName} outer result count={response.Results.Count}; expected 1.");
                ClientBase.ValidateResponse(response.Results, calls);
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, calls);
                CallMethodResult item = response.Results[0];
                RestartPipe.Require(item.StatusCode == StatusCodes.Good,
                    $"RSE probe {methodName} Method status=0x{(uint)item.StatusCode:X8}, " +
                    $"output count={item.OutputArguments.Count}; expected Good.");
                RestartPipe.Require(item.OutputArguments.Count == 1,
                    $"RSE probe {methodName} output count={item.OutputArguments.Count}; expected 1.");
                Variant argument = item.OutputArguments[0];
                RestartPipe.Require(argument.TypeInfo.BuiltInType == BuiltInType.ExtensionObject &&
                    argument.TypeInfo.ValueRank == ValueRanks.Scalar,
                    $"RSE probe {methodName} output built-in type={argument.TypeInfo.BuiltInType}, " +
                    $"rank={argument.TypeInfo.ValueRank}; expected scalar ExtensionObject.");
                RestartPipe.Require(argument.TryGetValue(out ExtensionObject payload) && !payload.IsNull,
                    $"RSE probe {methodName} output is not a non-null scalar ExtensionObject.");
                var expected = new TResponse();
                var actualId = ExpandedNodeId.ToNodeId(payload.TypeId, decoding.NamespaceUris);
                RestartPipe.Require(
                    actualId == ExpandedNodeId.ToNodeId(expected.TypeId, decoding.NamespaceUris) ||
                    actualId == ExpandedNodeId.ToNodeId(expected.BinaryEncodingId, decoding.NamespaceUris),
                    $"RSE probe {methodName} output type={payload.TypeId}; " +
                    $"expected the declared response {expected.TypeId}.");
                RestartPipe.Require(payload.TryGetValue(out TResponse? result, decoding) && result != null,
                    $"RSE probe {methodName} declared output type={payload.TypeId} " +
                    $"could not be decoded as {expected.TypeId}.");
                return CoreUtils.Clone(result)!;
            }
            finally
            {
                response.Reuse();
            }
        }

        /// <summary>
        /// Rejects ambiguous selectors before dispatch. Empty ByteString is present, not the null selector.
        /// Token lookups must have an empty Client ID and a nonempty bounded token.
        /// </summary>
        private static void AssertGetResultSelector(GetResultRequestDataType request)
        {
            bool clientId = request.ClientOperationId != Uuid.Empty;
            bool token = !request.ServerToken.IsNull;
            RestartPipe.Require(clientId != token &&
                (!token || request.ServerToken.Length is >= 1 and <= 64),
                $"RSE probe GetResult selector shape: clientIdPresent={clientId}, tokenPresent={token}, " +
                $"tokenLength={request.ServerToken.Length}; expected exactly one valid selector.");
        }

        /// <summary>Checks rather than repairs registrations in the exact context used for decoding.</summary>
        private static void VerifyGeneratedRseRegistrations(IServiceMessageContext decoding, string contextName)
        {
            var trusted = ServiceMessageContext.CreateEmpty(decoding.Telemetry);
            trusted.Factory.Builder.AddOpcUaRse().Commit();
            IEncodeable[] required =
            [
                new GetResultRequestDataType(),
                new GetResultResponseDataType(),
                new OperationControlRequestDataType(),
                new ControlResponseDataType()
            ];
            foreach (IEncodeable definition in required)
            {
                foreach (ExpandedNodeId id in new[] { definition.TypeId, definition.BinaryEncodingId })
                {
                    RestartPipe.Require(trusted.Factory.TryGetEncodeableType(id, out IEncodeableType? expected) &&
                        decoding.Factory.TryGetEncodeableType(id, out IEncodeableType? actual) &&
                        ReferenceEquals(expected, actual),
                        $"RSE probe {contextName} lacks the generated registration for {id}.");
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            return context.DisposeAsync();
        }

        private static async Task<ArrayOf<NodeId>> ResolveAsync(
            ISession session, NodeId root, ArrayOf<string> names, ushort ns, CancellationToken ct)
        {
            var paths = new BrowsePath[names.Count];
            for (int i = 0; i < names.Count; i++)
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
                                TargetName = new QualifiedName(names[i], ns)
                            }
                        ]
                    }
                };
            }
            TranslateBrowsePathsToNodeIdsResponse response = await session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths, ct)
                .ConfigureAwait(false);
            try
            {
                ClientBase.ValidateResponse(response.Results, paths.ToArrayOf());
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, paths.ToArrayOf());
                RestartPipe.Require(
                    response.Results.Count == names.Count, "Manager discovery returned an incomplete path set.");
                var nodes = new NodeId[names.Count];
                for (int i = 0; i < nodes.Length; i++)
                {
                    BrowsePathResult result = response.Results[i];
                    RestartPipe.Require(result.StatusCode == StatusCodes.Good &&
                        result.Targets.Count == 1 &&
                        result.Targets[0].RemainingPathIndex == uint.MaxValue &&
                        result.Targets[0].TargetId.ServerIndex == 0,
                        "A manager instance path is missing, ambiguous, partial or remote.");
                    nodes[i] = ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, session.NamespaceUris);
                    RestartPipe.Require(!nodes[i].IsNull, "A manager instance NodeId is null.");
                }
                return nodes;
            }
            finally
            {
                response.Reuse();
            }
        }
    }
}
