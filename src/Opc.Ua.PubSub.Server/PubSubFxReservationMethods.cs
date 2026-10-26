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
using Opc.Ua.Fx.Ac;
using Opc.Ua.Fx.Data;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.Server;

namespace Opc.Ua.PubSub.Server
{
    /// <summary>
    /// Binds supported Part 81 verification and reservation commands to the application's Session-owned Part 14
    /// transaction, and optionally delegates CloseConnections to a host-owned lifecycle transaction. Other
    /// EstablishConnections commands and ConnectionEndpoint creation/configuration are not implemented by this
    /// binding.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class PubSubFxReservationMethods
    {
        /// <summary>
        /// Creates an EstablishConnections binding for an application that supports transactional communication IDs.
        /// </summary>
        public PubSubFxReservationMethods(IPubSubApplication application, PubSubServerOptions options)
            : this(application, options, null)
        {
        }

        /// <summary>
        /// Creates the Part 81 binding with an optional host-owned ConnectionEndpoint lifecycle capability.
        /// </summary>
        /// <param name="application">The PubSub application supporting communication-ID reservations.</param>
        /// <param name="options">The PubSub server options governing configuration method authorization.</param>
        /// <param name="connectionLifecycle">
        /// Optional host capability that atomically closes or removes ConnectionEndpoints.
        /// </param>
        public PubSubFxReservationMethods(
            IPubSubApplication application,
            PubSubServerOptions options,
            IPubSubFxConnectionLifecycle? connectionLifecycle)
        {
            if (application is null)
            {
                throw new ArgumentNullException(nameof(application));
            }
            m_application = application as IPubSubCommunicationIdApplication ??
                throw new ArgumentException("The application must support communication-ID reservations.",
                    nameof(application));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_connectionLifecycle = connectionLifecycle;
        }

        /// <summary>
        /// Binds an initialized, host-owned AutomationComponent without replacing an existing method handler.
        /// </summary>
        /// <param name="component">The actual local AutomationComponent, with its host-assigned identity.</param>
        /// <param name="listeningEndpoint">
        /// Optional asynchronous resolver for an active listening endpoint supporting PubSub over UASC.
        /// It must return null when that endpoint is unavailable. Reverse Connect-only hosts omit it.
        /// </param>
        public void Bind(
            AutomationComponentState component,
            Func<CancellationToken, ValueTask<string?>>? listeningEndpoint = null)
        {
            if (component is null)
            {
                throw new ArgumentNullException(nameof(component));
            }
            if (component.NodeId.IsNull || component.EstablishConnections is not { } method ||
                method.NodeId.IsNull || method.Parent != component)
            {
                throw new ArgumentException("An initialized AutomationComponent is required.", nameof(component));
            }
            if (method.OnCall is not null || method.OnCallAsync is not null ||
                method.OnCallMethod is not null || method.OnCallMethod2 is not null ||
                method.OnCallMethodWithResultAsync is not null || method.OnCallMethod2Async is not null)
            {
                throw new InvalidOperationException("EstablishConnections already has a handler.");
            }
            if (m_connectionLifecycle is not null)
            {
                if (component.CloseConnections is not { } candidate ||
                    candidate.NodeId.IsNull || candidate.Parent != component)
                {
                    throw new ArgumentException(
                        "An initialized CloseConnections Method is required when a lifecycle is supplied.",
                        nameof(component));
                }
                if (HasMethodHandler(candidate))
                {
                    throw new InvalidOperationException("CloseConnections already has a handler.");
                }
            }
            NodeId componentId = component.NodeId;
            method.OnCallAsync = (context, calledMethod, objectId, commandMask, assets, endpoints,
                reservations, configurations, cancellationToken) =>
                EstablishConnectionsAsync(context, objectId, component, componentId, commandMask,
                    assets, endpoints, reservations, configurations, listeningEndpoint, cancellationToken);
            if (m_connectionLifecycle is { } connectionLifecycle)
            {
                CloseConnectionsMethodState closeMethod = component.CloseConnections!;
                closeMethod.OnCallAsync = (context, calledMethod, objectId, endpoints, remove, cancellationToken) =>
                    CloseConnectionsAsync(
                        context, objectId, component, componentId, connectionLifecycle, endpoints, remove,
                        cancellationToken);
            }
        }

        /// <summary>
        /// Determines whether a generated CloseConnections Method already has a typed or generic call handler.
        /// </summary>
        private static bool HasMethodHandler(MethodState method)
        {
            return method is CloseConnectionsMethodState closeMethod &&
                (closeMethod.OnCall is not null || closeMethod.OnCallAsync is not null ||
                    HasGenericMethodHandler(method));
        }

        /// <summary>
        /// Determines whether the base MethodState contains an installed generic call handler.
        /// </summary>
        private static bool HasGenericMethodHandler(MethodState method)
        {
            return method.OnCallMethod is not null || method.OnCallMethod2 is not null ||
                method.OnCallMethodWithResultAsync is not null || method.OnCallMethod2Async is not null;
        }

        /// <summary>
        /// Delegates one authorized endpoint-list transaction to the host lifecycle owner.
        /// </summary>
        private async ValueTask<CloseConnectionsMethodStateResult> CloseConnectionsAsync(
            ISystemContext context,
            NodeId objectId,
            AutomationComponentState component,
            NodeId componentId,
            IPubSubFxConnectionLifecycle connectionLifecycle,
            ArrayOf<NodeId> endpoints,
            bool remove,
            CancellationToken cancellationToken)
        {
            var result = new CloseConnectionsMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                ResultsOut = []
            };
            try
            {
                _ = PubSubNodeManager.RequireConfigurationSession(
                    context, m_options.ExposeConfigurationMethods);
                if (objectId != componentId)
                {
                    result.ServiceResult = new ServiceResult(StatusCodes.BadNodeIdUnknown);
                    return result;
                }
                if (endpoints.IsNull)
                {
                    result.ServiceResult = new ServiceResult(StatusCodes.BadInvalidArgument);
                    return result;
                }
                cancellationToken.ThrowIfCancellationRequested();
                ArrayOf<StatusCode> endpointResults = await connectionLifecycle.CloseConnectionsAsync(
                    context, component, endpoints, remove, cancellationToken).ConfigureAwait(false);
                if (endpointResults.IsNull || endpointResults.Count != endpoints.Count)
                {
                    result.ServiceResult = new ServiceResult(StatusCodes.BadUnexpectedError);
                    return result;
                }
                result.ResultsOut = endpointResults;
            }
            catch (ServiceResultException exception)
            {
                result.ServiceResult = new ServiceResult(exception.StatusCode);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                result.ServiceResult = new ServiceResult(StatusCodes.BadRequestCancelledByClient);
            }
            return result;
        }

        /// <summary>
        /// Executes supported verification commands before reservation, preserving ordered results and abort outcomes.
        /// </summary>
        private async ValueTask<EstablishConnectionsMethodStateResult> EstablishConnectionsAsync(
            ISystemContext context,
            NodeId objectId,
            AutomationComponentState component,
            NodeId componentId,
            uint commandMask,
            ArrayOf<AssetVerificationDataType> assets,
            ArrayOf<ConnectionEndpointConfigurationDataType> endpoints,
            ArrayOf<ReserveCommunicationIdsDataType> reservations,
            ArrayOf<CommunicationConfigurationDataType> configurations,
            Func<CancellationToken, ValueTask<string?>>? listeningEndpoint,
            CancellationToken cancellationToken)
        {
            var result = new EstablishConnectionsMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                AssetVerificationResults = [],
                ConnectionEndpointConfigurationResults = [],
                ReserveCommunicationIdsResults = [],
                CommunicationConfigurationResults = []
            };
            try
            {
                NodeId sessionId = PubSubNodeManager.RequireConfigurationSession(
                    context, m_options.ExposeConfigurationMethods);
                cancellationToken.ThrowIfCancellationRequested();
                if (objectId != componentId)
                {
                    result.ServiceResult = new ServiceResult(StatusCodes.BadNodeIdUnknown);
                    return result;
                }
                if (commandMask == 0 || commandMask > 0x1FF)
                {
                    result.ServiceResult = new ServiceResult(StatusCodes.BadInvalidArgument);
                    return result;
                }
                uint verifyAssetCommand = (uint)FxCommandMask.VerifyAssetCmd;
                uint verifyFunctionalEntityCommand = (uint)FxCommandMask.VerifyFunctionalEntityCmd;
                uint reserveCommunicationIdsCommand = (uint)FxCommandMask.ReserveCommunicationIdsCmd;
                if ((commandMask & ~(verifyAssetCommand |
                    verifyFunctionalEntityCommand | reserveCommunicationIdsCommand)) != 0)
                {
                    result.ServiceResult = new ServiceResult(StatusCodes.BadInvalidState);
                    return result;
                }
                bool verifyAssets = (commandMask & verifyAssetCommand) != 0;
                bool verifyFunctionalEntities = (commandMask & verifyFunctionalEntityCommand) != 0;
                bool reserveCommunicationIds = (commandMask & reserveCommunicationIdsCommand) != 0;
                if ((verifyAssets && assets.IsEmpty) || (!verifyAssets && !assets.IsEmpty) ||
                    (verifyFunctionalEntities && endpoints.IsEmpty) ||
                    (!verifyFunctionalEntities && !endpoints.IsEmpty) || !configurations.IsEmpty ||
                    (reserveCommunicationIds && reservations.IsEmpty) ||
                    (!reserveCommunicationIds && !reservations.IsEmpty))
                {
                    result.ServiceResult = new ServiceResult(StatusCodes.BadInvalidArgument);
                    return result;
                }
                for (int i = 0; i < assets.Count; i++)
                {
                    AssetVerificationDataType asset = assets[i];
                    if (asset.VerificationMode is not (
                        AssetVerificationModeEnum.AssetCompatibility or
                        AssetVerificationModeEnum.AssetIdentity or
                        AssetVerificationModeEnum.AssetIdentityAndCompatibility) ||
                        asset.ExpectedVerificationResult is not (
                            AssetVerificationResultEnum.NotSet or
                            AssetVerificationResultEnum.Match or
                            AssetVerificationResultEnum.Compatible or
                            AssetVerificationResultEnum.Mismatch))
                    {
                        result.ServiceResult = new ServiceResult(StatusCodes.BadInvalidArgument);
                        return result;
                    }
                }
                if (verifyFunctionalEntities)
                {
                    bool hasExpectedVariables = false;
                    for (int i = 0; i < endpoints.Count; i++)
                    {
                        ConnectionEndpointConfigurationDataType endpoint = endpoints[i];
                        hasExpectedVariables |= !endpoint.ExpectedVerificationVariables.IsEmpty;
                        if (endpoint.ConnectionEndpoint.SwitchField !=
                                ConnectionEndpointDefinitionDataTypeFields.Node ||
                            endpoint.ConnectionEndpoint.Node.IsNull ||
                            !endpoint.ControlGroups.IsEmpty ||
                            !endpoint.ConfigurationData.IsEmpty ||
                            (endpoint.CommunicationLinks is not null &&
                                endpoint.CommunicationLinks.GetType() != typeof(CommunicationLinkConfigurationDataType)))
                        {
                            result.ServiceResult = new ServiceResult(StatusCodes.BadInvalidArgument);
                            return result;
                        }
                    }
                    if (!hasExpectedVariables)
                    {
                        result.ServiceResult = new ServiceResult(StatusCodes.BadInvalidArgument);
                        return result;
                    }
                }
                if (assets.Count > kMaxRequestsPerCall ||
                    endpoints.Count > kMaxRequestsPerCall - assets.Count ||
                    reservations.Count > kMaxRequestsPerCall - assets.Count - endpoints.Count)
                {
                    result.ServiceResult = new ServiceResult(StatusCodes.BadTooManyOperations);
                    return result;
                }

                var endpointResults = new ConnectionEndpointConfigurationResultDataType[endpoints.Count];
                for (int i = 0; i < endpointResults.Length; i++)
                {
                    endpointResults[i] = CreateUnprocessedEndpointResult(verifyFunctionalEntities);
                }
                result.ConnectionEndpointConfigurationResults =
                    new ArrayOf<ConnectionEndpointConfigurationResultDataType>(endpointResults);

                var assetResults = new AssetVerificationResultDataType[assets.Count];
                bool aborted = false;
                for (int i = 0; i < assets.Count; i++)
                {
                    AssetVerificationResultDataType assetResult =
                        await VerifyAssetAsync(context, assets[i], cancellationToken).ConfigureAwait(false);
                    assetResults[i] = assetResult;
                    if (StatusCode.IsBad(assetResult.VerificationStatus) ||
                        assetResult.VerificationResult is
                            AssetVerificationResultEnum.NotSet or AssetVerificationResultEnum.Mismatch ||
                        (assets[i].ExpectedVerificationResult == AssetVerificationResultEnum.Match &&
                            assetResult.VerificationResult == AssetVerificationResultEnum.Compatible))
                    {
                        aborted = true;
                    }
                }
                result.AssetVerificationResults = new ArrayOf<AssetVerificationResultDataType>(assetResults);

                if (!aborted && verifyFunctionalEntities)
                {
                    for (int i = 0; i < endpoints.Count; i++)
                    {
                        endpointResults[i] = await VerifyFunctionalEntityAsync(
                            context, component, componentId, endpoints[i], cancellationToken).ConfigureAwait(false);
                        if (StatusCode.IsBad(endpointResults[i].FunctionalEntityNodeResult) ||
                            StatusCode.IsBad(endpointResults[i].VerificationStatus) ||
                            endpointResults[i].VerificationResult == FunctionalEntityVerificationResultEnum.Mismatch ||
                            (endpointResults[i].VerificationResult == FunctionalEntityVerificationResultEnum.NotSet &&
                                !endpointResults[i].VerificationVariablesErrors.IsEmpty))
                        {
                            aborted = true;
                        }
                    }
                }

                var results = new ReserveCommunicationIdsResultDataType[reservations.Count];
                for (int i = 0; i < reservations.Count; i++)
                {
                    results[i] = new PubSubReserveCommunicationIdsResult2DataType
                    {
                        Result = StatusCodes.BadNothingToDo
                    };
                }
                result.ReserveCommunicationIdsResults = new ArrayOf<ReserveCommunicationIdsResultDataType>(results);
                if (aborted)
                {
                    result.ServiceResult = new ServiceResult(StatusCodes.Uncertain);
                    return result;
                }

                for (int i = 0; i < reservations.Count; i++)
                {
                    var reservationResult = (PubSubReserveCommunicationIdsResult2DataType)results[i];
                    if (aborted)
                    {
                        continue;
                    }
                    try
                    {
                        if (reservations[i] is not PubSubReserveCommunicationIds2DataType request)
                        {
                            throw new ServiceResultException(StatusCodes.BadNotSupported);
                        }
                        if (string.IsNullOrEmpty(request.TransportProfileUri))
                        {
                            throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                        }
                        Variant transportInfo = request.RequestTransportSpecificInfo
                            ? await ResolveTransportInfoAsync(request.TransportProfileUri,
                                listeningEndpoint, cancellationToken).ConfigureAwait(false)
                            : Variant.Null;
                        PubSubCommunicationIdReservation reserved =
                            await m_application.ReserveCommunicationIdsAsync(
                                sessionId, request.TransportProfileUri,
                                request.NumReqWriterGroupIds, request.NumReqDataSetWriterIds,
                                cancellationToken).ConfigureAwait(false);
                        reservationResult.DefaultPublisherId = reserved.DefaultPublisherId;
                        reservationResult.WriterGroupIds = reserved.WriterGroupIds;
                        reservationResult.DataSetWriterIds = reserved.DataSetWriterIds;
                        reservationResult.TransportSpecificInfo = transportInfo;
                        reservationResult.Result = StatusCodes.Good;
                    }
                    catch (ServiceResultException exception)
                    {
                        reservationResult.Result = exception.StatusCode;
                        aborted = true;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        reservationResult.Result = StatusCodes.BadRequestCancelledByClient;
                        aborted = true;
                    }
                    if (aborted)
                    {
                        result.ServiceResult = new ServiceResult(StatusCodes.Uncertain);
                    }
                }
            }
            catch (ServiceResultException exception)
            {
                result.ServiceResult = new ServiceResult(exception.StatusCode);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                result.ServiceResult = new ServiceResult(StatusCodes.BadRequestCancelledByClient);
            }
            return result;
        }

        /// <summary>
        /// Creates the command result for an endpoint that was not processed because an earlier command failed.
        /// </summary>
        private static ConnectionEndpointConfigurationResultDataType CreateUnprocessedEndpointResult(
            bool verifyFunctionalEntities)
        {
            return new ConnectionEndpointConfigurationResultDataType
            {
                FunctionalEntityNodeResult = StatusCodes.Good,
                ConnectionEndpointResult = StatusCodes.Good,
                VerificationResult = FunctionalEntityVerificationResultEnum.NotSet,
                VerificationStatus = verifyFunctionalEntities
                    ? StatusCodes.BadNothingToDo
                    : StatusCodes.Good,
                VerificationVariablesErrors = [],
                EstablishControlResult = [],
                ConfigurationDataResult = [],
                ReassignControlResult = [],
                CommunicationLinksResult = StatusCodes.Good,
                EnableCommunicationResult = StatusCodes.Good
            };
        }

        /// <summary>
        /// Resolves and verifies a FunctionalEntity related to the bound AutomationComponent.
        /// </summary>
        private static async ValueTask<ConnectionEndpointConfigurationResultDataType> VerifyFunctionalEntityAsync(
            ISystemContext context,
            AutomationComponentState component,
            NodeId componentId,
            ConnectionEndpointConfigurationDataType endpoint,
            CancellationToken cancellationToken)
        {
            var result = CreateUnprocessedEndpointResult(verifyFunctionalEntities: true);
            NodeId functionalEntityId = endpoint.FunctionalEntityNode;
            if (functionalEntityId.IsNull)
            {
                result.FunctionalEntityNodeResult = StatusCodes.BadNodeIdInvalid;
                result.VerificationStatus = StatusCodes.BadNodeIdInvalid;
                result.VerificationResult = FunctionalEntityVerificationResultEnum.Mismatch;
                return result;
            }
            if (context is not ServerSystemContext serverContext)
            {
                result.FunctionalEntityNodeResult = StatusCodes.BadNotSupported;
                result.VerificationStatus = StatusCodes.BadNotSupported;
                result.VerificationResult = FunctionalEntityVerificationResultEnum.Mismatch;
                return result;
            }

            NodeState? node = await serverContext.Server.NodeManager
                .FindNodeInAddressSpaceAsync(functionalEntityId, cancellationToken).ConfigureAwait(false);
            if (node is null)
            {
                result.FunctionalEntityNodeResult = StatusCodes.BadNodeIdUnknown;
                result.VerificationStatus = StatusCodes.BadNodeIdUnknown;
                result.VerificationResult = FunctionalEntityVerificationResultEnum.Mismatch;
                return result;
            }
            if (node is not BaseObjectState functionalEntity ||
                !context.TypeTable.IsTypeOf(
                    ExpandedNodeId.ToNodeId(functionalEntity.TypeDefinitionId, context.NamespaceUris),
                    ExpandedNodeId.ToNodeId(Opc.Ua.Fx.Ac.ObjectTypeIds.FunctionalEntityType,
                        context.NamespaceUris)) ||
                !await IsRelatedFunctionalEntityAsync(
                    context, serverContext, functionalEntity, component, componentId, cancellationToken)
                    .ConfigureAwait(false))
            {
                result.FunctionalEntityNodeResult = StatusCodes.BadInvalidArgument;
                result.VerificationStatus = StatusCodes.BadInvalidArgument;
                result.VerificationResult = FunctionalEntityVerificationResultEnum.Mismatch;
                return result;
            }

            result.FunctionalEntityNodeResult = StatusCodes.Good;
            ArrayOf<NodeIdValuePair> expectedVariables = endpoint.ExpectedVerificationVariables;
            if (expectedVariables.IsEmpty)
            {
                result.ConnectionEndpointId = endpoint.ConnectionEndpoint.Node;
                result.VerificationStatus = StatusCodes.GoodNoData;
                return result;
            }

            int namespaceIndex = context.NamespaceUris.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
            if (namespaceIndex < 0 || namespaceIndex > ushort.MaxValue ||
                functionalEntity.FindChild(context,
                    new QualifiedName("Verify", (ushort)namespaceIndex)) is not MethodState verify)
            {
                result.VerificationStatus = StatusCodes.BadMethodInvalid;
                return result;
            }

            ArrayOf<Variant> inputs = [Variant.FromStructure(expectedVariables)];
            var argumentErrors = new List<ServiceResult>();
            var outputs = new List<Variant>();
            ServiceResult methodResult = await verify.CallAsync(
                context, functionalEntityId, inputs, argumentErrors, outputs, cancellationToken)
                .ConfigureAwait(false);
            foreach (ServiceResult argumentError in argumentErrors)
            {
                if (ServiceResult.IsBad(argumentError))
                {
                    result.VerificationStatus = argumentError.StatusCode;
                    return result;
                }
            }
            result.VerificationStatus = methodResult.StatusCode;
            if (StatusCode.IsBad(methodResult.StatusCode))
            {
                return result;
            }
            if (outputs.Count != 2 ||
                !TryGetFunctionalEntityVerificationResult(outputs[0], out var verificationResult) ||
                !outputs[1].TryGetValue(out ArrayOf<StatusCode> verificationErrors))
            {
                result.VerificationStatus = StatusCodes.BadTypeMismatch;
                return result;
            }

            result.VerificationResult = verificationResult;
            result.VerificationVariablesErrors = verificationErrors;
            if (verificationResult != FunctionalEntityVerificationResultEnum.Mismatch &&
                (verificationResult != FunctionalEntityVerificationResultEnum.NotSet ||
                    verificationErrors.IsEmpty))
            {
                result.ConnectionEndpointId = endpoint.ConnectionEndpoint.Node;
            }
            return result;
        }

        /// <summary>
        /// Confirms the FunctionalEntity is hosted by an Asset in the bound AutomationComponent.
        /// </summary>
        private static async ValueTask<bool> IsRelatedFunctionalEntityAsync(
            ISystemContext context,
            ServerSystemContext serverContext,
            BaseObjectState functionalEntity,
            AutomationComponentState component,
            NodeId componentId,
            CancellationToken cancellationToken)
        {
            var references = new List<IReference>();
            functionalEntity.GetReferences(context, references);
            foreach (IReference reference in references)
            {
                if (reference.ReferenceTypeId != ReferenceTypeIds.IsHostedBy || reference.IsInverse)
                {
                    continue;
                }

                NodeId assetId = ExpandedNodeId.ToNodeId(reference.TargetId, context.NamespaceUris);
                if (assetId.IsNull)
                {
                    continue;
                }
                NodeState? assetNode = await serverContext.Server.NodeManager
                    .FindNodeInAddressSpaceAsync(assetId, cancellationToken).ConfigureAwait(false);
                if (assetNode is BaseObjectState asset &&
                    context.TypeTable.IsTypeOf(
                        ExpandedNodeId.ToNodeId(asset.TypeDefinitionId, context.NamespaceUris),
                        ExpandedNodeId.ToNodeId(Opc.Ua.Fx.Ac.ObjectTypeIds.IAssetRevisionType,
                            context.NamespaceUris)) &&
                    IsOwnedByComponent(asset, component, componentId))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Checks the in-memory parent chain rather than accepting a globally resolvable Asset.
        /// </summary>
        private static bool IsOwnedByComponent(NodeState node, AutomationComponentState component, NodeId componentId)
        {
            for (NodeState? current = node; current is not null;
                current = (current as BaseInstanceState)?.Parent)
            {
                if (current == component || current.NodeId == componentId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Reads a FunctionalEntity verification result when it is one of the defined values.
        /// </summary>
        private static bool TryGetFunctionalEntityVerificationResult(
            Variant value,
            out FunctionalEntityVerificationResultEnum verificationResult)
        {
            if (!value.TryGetValue(out int valueCode))
            {
                verificationResult = FunctionalEntityVerificationResultEnum.NotSet;
                return false;
            }

            verificationResult = valueCode switch
            {
                0 => FunctionalEntityVerificationResultEnum.NotSet,
                1 => FunctionalEntityVerificationResultEnum.Match,
                2 => FunctionalEntityVerificationResultEnum.Mismatch,
                _ => FunctionalEntityVerificationResultEnum.NotSet
            };
            return valueCode is >= 0 and <= 2;
        }

        /// <summary>
        /// Calls an asset's VerifyAsset method and maps its status and outputs into the command result.
        /// </summary>
        private static async ValueTask<AssetVerificationResultDataType> VerifyAssetAsync(
            ISystemContext context,
            AssetVerificationDataType asset,
            CancellationToken cancellationToken)
        {
            var result = new AssetVerificationResultDataType
            {
                VerificationStatus = StatusCodes.BadNothingToDo,
                VerificationResult = AssetVerificationResultEnum.NotSet,
                VerificationVariablesErrors = [],
                VerificationAdditionalVariablesErrors = []
            };
            if (asset.AssetToVerify.IsNull)
            {
                result.VerificationStatus = StatusCodes.BadNodeIdInvalid;
                return result;
            }
            if (context is not ServerSystemContext serverContext)
            {
                result.VerificationStatus = StatusCodes.BadNotSupported;
                return result;
            }

            try
            {
                NodeState? assetNode = await serverContext.Server.NodeManager
                    .FindNodeInAddressSpaceAsync(asset.AssetToVerify, cancellationToken)
                    .ConfigureAwait(false);
                if (assetNode is null)
                {
                    result.VerificationStatus = StatusCodes.BadNodeIdUnknown;
                    return result;
                }
                if (assetNode is not BaseObjectState assetObject ||
                    !context.TypeTable.IsTypeOf(
                        ExpandedNodeId.ToNodeId(assetObject.TypeDefinitionId, context.NamespaceUris),
                        ExpandedNodeId.ToNodeId(Opc.Ua.Fx.Ac.ObjectTypeIds.IAssetRevisionType,
                            context.NamespaceUris)))
                {
                    result.VerificationStatus = StatusCodes.BadInvalidArgument;
                    return result;
                }

                int namespaceIndex = context.NamespaceUris.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
                if (namespaceIndex < 0 || namespaceIndex > ushort.MaxValue ||
                    assetNode.FindChild(context,
                        new QualifiedName("VerifyAsset", (ushort)namespaceIndex)) is not MethodState verifyAsset)
                {
                    result.VerificationStatus = StatusCodes.BadMethodInvalid;
                    return result;
                }

                ArrayOf<Variant> inputs =
                [
                    Variant.From((int)asset.VerificationMode),
                    Variant.FromStructure(asset.ExpectedVerificationVariables),
                    Variant.FromStructure(asset.ExpectedAdditionalVerificationVariables)
                ];
                var argumentErrors = new List<ServiceResult>();
                var outputs = new List<Variant>();
                ServiceResult methodResult = await verifyAsset.CallAsync(
                    context, assetNode.NodeId, inputs, argumentErrors, outputs, cancellationToken)
                    .ConfigureAwait(false);
                foreach (ServiceResult argumentError in argumentErrors)
                {
                    if (ServiceResult.IsBad(argumentError))
                    {
                        result.VerificationStatus = argumentError.StatusCode;
                        return result;
                    }
                }
                result.VerificationStatus = methodResult.StatusCode;
                if (StatusCode.IsBad(methodResult.StatusCode))
                {
                    return result;
                }
                if (outputs.Count != 3 || !TryGetAssetVerificationResult(outputs[0], out var verificationResult) ||
                    !outputs[1].TryGetValue(out ArrayOf<StatusCode> verificationErrors) ||
                    !outputs[2].TryGetValue(out ArrayOf<StatusCode> additionalVerificationErrors))
                {
                    result.VerificationStatus = StatusCodes.BadTypeMismatch;
                    return result;
                }

                result.VerificationResult = verificationResult;
                result.VerificationVariablesErrors = verificationErrors;
                result.VerificationAdditionalVariablesErrors = additionalVerificationErrors;
            }
            catch (ServiceResultException exception)
            {
                result.VerificationStatus = exception.StatusCode;
            }

            return result;
        }

        /// <summary>
        /// Reads the integer representation of an asset verification result when it is a defined value.
        /// </summary>
        private static bool TryGetAssetVerificationResult(
            Variant value,
            out AssetVerificationResultEnum verificationResult)
        {
            if (!value.TryGetValue(out int valueCode))
            {
                verificationResult = AssetVerificationResultEnum.NotSet;
                return false;
            }

            verificationResult = valueCode switch
            {
                0 => AssetVerificationResultEnum.NotSet,
                1 => AssetVerificationResultEnum.Match,
                2 => AssetVerificationResultEnum.Compatible,
                3 => AssetVerificationResultEnum.Mismatch,
                _ => AssetVerificationResultEnum.NotSet
            };
            return valueCode is >= 0 and <= 3;
        }

        /// <summary>
        /// Resolves transport-specific information before allocating any identifiers for the request.
        /// </summary>
        private static async ValueTask<Variant> ResolveTransportInfoAsync(
            string transportProfileUri,
            Func<CancellationToken, ValueTask<string?>>? listeningEndpoint,
            CancellationToken cancellationToken)
        {
            if (transportProfileUri != DataChannelProfiles.PubSubUasc || listeningEndpoint is null)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
            string? address = await listeningEndpoint(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(address) || System.Text.Encoding.UTF8.GetByteCount(address) > 4096 ||
                !Uri.TryCreate(address, UriKind.Absolute, out Uri? endpoint) ||
                endpoint.Scheme is not ("opc.tcp" or "opc.wss") || string.IsNullOrEmpty(endpoint.Host) ||
                !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Fragment) ||
                endpoint.Port == 0)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
            return Variant.From(address);
        }

        /// <summary>The finite batch bound, independent of the UInt16 identifier-domain limits.</summary>
        private const int kMaxRequestsPerCall = 128;

        /// <summary>The same application transaction used by normal Part 14 configuration methods.</summary>
        private readonly IPubSubCommunicationIdApplication m_application;

        /// <summary>The current administrative enablement policy.</summary>
        private readonly PubSubServerOptions m_options;

        /// <summary>The optional host-owned ConnectionEndpoint lifecycle transaction.</summary>
        private readonly IPubSubFxConnectionLifecycle? m_connectionLifecycle;
    }
}
