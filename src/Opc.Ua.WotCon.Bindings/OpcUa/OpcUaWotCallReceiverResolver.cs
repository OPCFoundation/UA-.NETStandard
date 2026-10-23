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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Bindings.OpcUa
{
    /// <summary>
    /// Resolves a Call receiver and verifies source ownership against one live OPC UA Session.
    /// </summary>
    public static class OpcUaWotCallReceiverResolver
    {
        /// <summary>
        /// Resolves and validates the Method identity declared by a compiled direct or browse-path target.
        /// </summary>
        /// <param name="session">The selected OPC UA Session.</param>
        /// <param name="form">The compiled Call form.</param>
        /// <param name="namespaceUris">The namespace snapshot captured for this operation.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The exact local Method NodeId.</returns>
        public static async ValueTask<NodeId> ResolveMethodAsync(
            ISession session,
            WotCompiledForm form,
            NamespaceTable namespaceUris,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(form);
            ArgumentNullException.ThrowIfNull(namespaceUris);
            if (!WotCallReceiver.TryGetEndpoint(form.Endpoint.BaseUri, out string expectedEndpoint) ||
                !WotCallReceiver.TryGetEndpoint(
                    session.ConfiguredEndpoint?.Description.EndpointUrl, out string actualEndpoint) ||
                !string.Equals(expectedEndpoint, actualEndpoint, StringComparison.Ordinal))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The Call source belongs to a different endpoint.");
            }

            NodeId method;
            if (form.Addressing.BrowsePathTarget is { } path)
            {
                if (path.Path.Length > 8192 || path.Elements.Count is 0 or > 256)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingLimitsExceeded, "The browse path exceeds the resolution bounds.");
                }
                if (!TryResolveNodeId(path.AnchorId, namespaceUris, out NodeId anchor))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid, "The browse-path anchor is not present in this Session.");
                }
                var elements = new RelativePathElement[path.Elements.Count];
                for (int index = 0; index < elements.Length; index++)
                {
                    WotBrowsePathStep step = path.Elements[index];
                    int targetNamespace = namespaceUris.GetIndex(step.TargetName.NamespaceUri ?? Namespaces.OpcUa);
                    if (targetNamespace is < 0 or > ushort.MaxValue)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadBrowseNameInvalid, "A browse-path namespace is not present in this Session.");
                    }
                    NodeId referenceType = step.ReferenceKind switch
                    {
                        RelativePathFormatter.ElementType.AnyHierarchical =>
                            Types.ReferenceTypeIds.HierarchicalReferences,
                        RelativePathFormatter.ElementType.AnyComponent => Types.ReferenceTypeIds.Aggregates,
                        _ => session.TypeTree.FindReferenceType(ResolveReferenceName(step, namespaceUris))
                    };
                    if (referenceType.IsNull)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadReferenceTypeIdInvalid,
                            "The browse-path reference type is unknown to this Session.");
                    }
                    elements[index] = new RelativePathElement
                    {
                        ReferenceTypeId = referenceType,
                        IsInverse = step.ReferenceKind == RelativePathFormatter.ElementType.InverseReference,
                        IncludeSubtypes = step.IncludeSubtypes,
                        TargetName = new QualifiedName(step.TargetName.Name, (ushort)targetNamespace)
                    };
                }
                ArrayOf<BrowsePath> requests =
                [
                    new BrowsePath { StartingNode = anchor, RelativePath = new RelativePath { Elements = elements } }
                ];
                TranslateBrowsePathsToNodeIdsResponse response = await session.TranslateBrowsePathsToNodeIdsAsync(
                    null, requests, cancellationToken).ConfigureAwait(false);
                ClientBase.ValidateResponse(response.Results, requests);
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);
                BrowsePathResult result = response.Results[0] ??
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse, "The browse-path result is missing.");
                if (!StatusCode.IsGood(result.StatusCode))
                {
                    throw new ServiceResultException(result.StatusCode, "The Server could not resolve the browse path.");
                }
                method = default;
                foreach (BrowsePathTarget candidate in result.Targets)
                {
                    if (candidate is null || candidate.RemainingPathIndex != uint.MaxValue ||
                        candidate.TargetId.ServerIndex != 0)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNodeIdInvalid, "A browse-path target must be complete and local.");
                    }
                    NodeId target = ExpandedNodeId.ToNodeId(candidate.TargetId, namespaceUris);
                    if (target.IsNull || (!method.IsNull && method != target))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNodeIdInvalid, "The browse path must resolve to one local Method.");
                    }
                    method = target;
                }
                if (method.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadNoMatch, "The browse path did not resolve.");
                }
                if ((!string.IsNullOrEmpty(form.Addressing.Target) &&
                        (!TryResolveNodeId(form.Addressing.Target, namespaceUris, out NodeId declared) ||
                            declared != method)) ||
                    (form.Addressing.Metadata.TryGetValue("pathHrefNodeId", out string? hrefId) &&
                        (!TryResolveNodeId(hrefId, namespaceUris, out NodeId hrefTarget) ||
                            hrefTarget != method)))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid, "The declared NodeId and browse path identify different Methods.");
                }
            }
            else if (!TryResolveNodeId(form.Addressing.Target, namespaceUris, out method))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid, "The Call target is not present in this Session.");
            }

            ArrayOf<ReadValueId> attributes =
            [
                new ReadValueId { NodeId = method, AttributeId = Attributes.NodeClass }
            ];
            ReadResponse nodeClass = await session.ReadAsync(
                null, 0, TimestampsToReturn.Neither, attributes, cancellationToken).ConfigureAwait(false);
            ClientBase.ValidateResponse(nodeClass.Results, attributes);
            ClientBase.ValidateDiagnosticInfos(nodeClass.DiagnosticInfos, attributes);
            if (!StatusCode.IsGood(nodeClass.Results[0].StatusCode))
            {
                throw new ServiceResultException(nodeClass.Results[0].StatusCode, "The Method NodeClass could not be read.");
            }
            if (!nodeClass.Results[0].WrappedValue.TryGetValue(out int actualClass) ||
                actualClass != (int)NodeClass.Method)
            {
                throw new ServiceResultException(StatusCodes.BadNodeClassInvalid, "The Call target must be a Method.");
            }
            return method;
        }

        /// <summary>
        /// Resolves the unique valid receiver for a compiled Call form.
        /// </summary>
        /// <param name="session">The selected OPC UA Session.</param>
        /// <param name="form">The compiled Call form.</param>
        /// <param name="method">The resolved Method NodeId.</param>
        /// <param name="namespaceUris">The namespace snapshot captured for this operation.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The exact Object NodeId that owns the Method.</returns>
        public static ValueTask<NodeId> ResolveAsync(
            ISession session,
            WotCompiledForm form,
            NodeId method,
            NamespaceTable namespaceUris,
            CancellationToken cancellationToken = default)
        {
            return ResolveAsync(session, form, method, namespaceUris, null, cancellationToken);
        }

        /// <summary>
        /// Resolves ownership while checking the caller's captured state between metadata requests.
        /// </summary>
        internal static async ValueTask<NodeId> ResolveAsync(
            ISession session,
            WotCompiledForm form,
            NodeId method,
            NamespaceTable namespaceUris,
            Action? validateSessionState,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(form);
            ArgumentNullException.ThrowIfNull(namespaceUris);
            validateSessionState?.Invoke();
            if (form.Addressing.Metadata.TryGetValue("callObjectId", out string? explicitReceiver))
            {
                if (!TryResolveNodeId(explicitReceiver, namespaceUris, out NodeId receiver))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid, "The Call receiver is not present in the Session namespace table.");
                }
                if (form.Addressing.Metadata.TryGetValue(
                    "componentOfCandidates", out string? candidateJson) &&
                    !ContainsCandidate(candidateJson, receiver, namespaceUris))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadMethodInvalid,
                        "The explicit Call receiver conflicts with the action componentOf candidates.");
                }
                await ValidateIfRequiredAsync(
                    session, form, receiver, method, namespaceUris, validateSessionState, cancellationToken)
                    .ConfigureAwait(false);
                validateSessionState?.Invoke();
                return receiver;
            }
            if (!form.Addressing.Metadata.TryGetValue(
                "componentOfCandidates", out string? candidatesJson) ||
                string.IsNullOrEmpty(candidatesJson))
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, "The Call receiver is missing.");
            }

            JsonDocument parsed;
            try
            {
                parsed = JsonDocument.Parse(candidatesJson);
            }
            catch (JsonException exception)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "The captured componentOf candidate list is invalid.", exception);
            }
            using JsonDocument document = parsed;
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "The captured componentOf candidate list is invalid.");
            }
            var valid = new HashSet<NodeId>();
            foreach (JsonElement candidate in document.RootElement.EnumerateArray())
            {
                if (candidate.ValueKind != JsonValueKind.String ||
                    candidate.GetString() is not { } text ||
                    !TryResolveNodeId(text, namespaceUris, out NodeId receiver))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid, "A componentOf candidate is not resolvable in this Session.");
                }
                try
                {
                    await ValidateIfRequiredAsync(
                        session, form, receiver, method, namespaceUris, validateSessionState, cancellationToken)
                        .ConfigureAwait(false);
                    validateSessionState?.Invoke();
                    valid.Add(receiver);
                }
                catch (ServiceResultException exception) when (
                    exception.StatusCode == StatusCodes.BadNodeClassInvalid ||
                    exception.StatusCode == StatusCodes.BadMethodInvalid)
                {
                    // Non-Object candidates and Objects without this Method cannot receive the Call.
                }
                if (valid.Count > 1)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadMethodInvalid,
                        "The action componentOf candidates identify more than one valid Call receiver.");
                }
            }
            if (valid.Count != 1)
            {
                throw new ServiceResultException(
                    StatusCodes.BadMethodInvalid,
                    "No unique componentOf candidate is a valid Object owner of the selected Method.");
            }
            foreach (NodeId receiver in valid)
            {
                return receiver;
            }
            throw new ServiceResultException(StatusCodes.BadMethodInvalid);
        }

        private static async ValueTask ValidateIfRequiredAsync(
            ISession session,
            WotCompiledForm form,
            NodeId receiver,
            NodeId method,
            NamespaceTable namespaceUris,
            Action? validateSessionState,
            CancellationToken cancellationToken)
        {
            if (!form.Addressing.Metadata.ContainsKey("sourceCallReceiver"))
            {
                return;
            }
            if (!WotCallReceiver.TryGetEndpoint(form.Endpoint.BaseUri, out string expectedEndpoint) ||
                !WotCallReceiver.TryGetEndpoint(
                    session.ConfiguredEndpoint?.Description.EndpointUrl, out string actualEndpoint) ||
                !string.Equals(expectedEndpoint, actualEndpoint, StringComparison.Ordinal))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The Call source belongs to a different endpoint.");
            }

            ArrayOf<ReadValueId> attributes =
            [
                new ReadValueId { NodeId = receiver, AttributeId = Attributes.NodeClass },
                new ReadValueId { NodeId = method, AttributeId = Attributes.NodeClass }
            ];
            ReadResponse response = await session.ReadAsync(
                null, 0, TimestampsToReturn.Neither, attributes, cancellationToken).ConfigureAwait(false);
            validateSessionState?.Invoke();
            ClientBase.ValidateResponse(response.Results, attributes);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, attributes);
            for (int index = 0; index < attributes.Count; index++)
            {
                DataValue value = response.Results[index];
                if (!StatusCode.IsGood(value.StatusCode))
                {
                    throw new ServiceResultException(value.StatusCode, "The Call receiver or Method could not be inspected.");
                }
                if (!value.WrappedValue.TryGetValue(out int nodeClass) ||
                    nodeClass != (int)(index == 0 ? NodeClass.Object : NodeClass.Method))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeClassInvalid, "The source Call requires an Object receiver and a Method.");
                }
            }

            if ((method == global::Opc.Ua.MethodIds.ConditionType_ConditionRefresh ||
                    method == global::Opc.Ua.MethodIds.ConditionType_ConditionRefresh2) &&
                receiver != global::Opc.Ua.ObjectTypeIds.ConditionType)
            {
                throw new ServiceResultException(
                    StatusCodes.BadMethodInvalid, "ConditionRefresh requires the ConditionType receiver.");
            }
            NodeId requiredConditionType =
                method == global::Opc.Ua.MethodIds.AcknowledgeableConditionType_Acknowledge ||
                method == global::Opc.Ua.MethodIds.AcknowledgeableConditionType_Confirm ||
                form.ConditionInvocation?.Action is "Acknowledge" or "Confirm"
                    ? global::Opc.Ua.ObjectTypeIds.AcknowledgeableConditionType
                    : method == global::Opc.Ua.MethodIds.ConditionType_Enable ||
                        method == global::Opc.Ua.MethodIds.ConditionType_Disable ||
                        method == global::Opc.Ua.MethodIds.ConditionType_AddComment ||
                        form.ConditionInvocation is not null
                            ? global::Opc.Ua.ObjectTypeIds.ConditionType : default;
            int remainingReferences = 10000;
            bool ownsMethod = await HasMethodAsync(receiver).ConfigureAwait(false);
            bool matchesCondition = requiredConditionType.IsNull;
            if (ownsMethod && matchesCondition)
            {
                return;
            }
            NodeId type = await SingleTargetAsync(
                receiver, Types.ReferenceTypeIds.HasTypeDefinition, BrowseDirection.Forward).ConfigureAwait(false);
            var visited = new HashSet<NodeId>();
            while (!type.IsNull)
            {
                if (visited.Count >= 100 || !visited.Add(type))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingLimitsExceeded, "The source ObjectType hierarchy is cyclic or too deep.");
                }
                matchesCondition |= type == requiredConditionType;
                ownsMethod |= await HasMethodAsync(type).ConfigureAwait(false);
                if (ownsMethod && matchesCondition)
                {
                    return;
                }
                type = await SingleTargetAsync(
                    type, Types.ReferenceTypeIds.HasSubtype, BrowseDirection.Inverse).ConfigureAwait(false);
            }
            throw new ServiceResultException(
                StatusCodes.BadMethodInvalid, "The source Object and its ObjectType hierarchy do not own the Method.");

            async ValueTask<bool> HasMethodAsync(NodeId owner)
            {
                validateSessionState?.Invoke();
                await foreach (ReferenceDescription reference in session.EnumerateReferencesAsync(
                    owner, Types.ReferenceTypeIds.HasComponent, nodeClassMask: (uint)NodeClass.Method,
                    ct: cancellationToken).ConfigureAwait(false))
                {
                    CheckReference(reference);
                    if (reference.NodeId.ServerIndex == 0 &&
                        ExpandedNodeId.ToNodeId(reference.NodeId, namespaceUris) == method)
                    {
                        return true;
                    }
                }
                validateSessionState?.Invoke();
                return false;
            }

            async ValueTask<NodeId> SingleTargetAsync(NodeId owner, NodeId referenceType, BrowseDirection direction)
            {
                validateSessionState?.Invoke();
                NodeId result = default;
                await foreach (ReferenceDescription reference in session.EnumerateReferencesAsync(
                    owner, referenceType, direction, false, (uint)NodeClass.ObjectType,
                    ct: cancellationToken).ConfigureAwait(false))
                {
                    CheckReference(reference);
                    NodeId candidate = ExpandedNodeId.ToNodeId(reference.NodeId, namespaceUris);
                    if (reference.NodeId.ServerIndex != 0 || candidate.IsNull ||
                        (!result.IsNull && result != candidate))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNodeIdInvalid, "The source ObjectType hierarchy is not local and unambiguous.");
                    }
                    result = candidate;
                }
                validateSessionState?.Invoke();
                return result;
            }

            void CheckReference(ReferenceDescription reference)
            {
                validateSessionState?.Invoke();
                if (--remainingReferences < 0)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingLimitsExceeded, "The source Call ownership browse exceeded its bound.");
                }
                if (reference is null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUnknownResponse, "The source ownership browse returned a missing reference.");
                }
            }
        }

        private static bool TryResolveNodeId(string? text, NamespaceTable namespaceUris, out NodeId nodeId)
        {
            nodeId = default;
            if (string.IsNullOrEmpty(text) || !WotPortableIdentity.IsPortableNodeId(text))
            {
                return false;
            }
            ExpandedNodeId expanded = ExpandedNodeId.Parse(text);
            nodeId = ExpandedNodeId.ToNodeId(expanded, namespaceUris);
            return expanded.ServerIndex == 0 && !nodeId.IsNull &&
                namespaceUris.GetString(nodeId.NamespaceIndex) is not null;
        }

        private static QualifiedName ResolveReferenceName(WotBrowsePathStep step, NamespaceTable namespaceUris)
        {
            int namespaceIndex = namespaceUris.GetIndex(step.ReferenceName.NamespaceUri ?? Namespaces.OpcUa);
            if (namespaceIndex is < 0 or > ushort.MaxValue)
            {
                throw new ServiceResultException(
                    StatusCodes.BadBrowseNameInvalid, "A reference namespace is not present in this Session.");
            }
            return new QualifiedName(step.ReferenceName.Name, (ushort)namespaceIndex);
        }

        private static bool ContainsCandidate(string? json, NodeId receiver, NamespaceTable namespaceUris)
        {
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                return document.RootElement.ValueKind == JsonValueKind.Array &&
                    document.RootElement.EnumerateArray().Any(value =>
                        value.ValueKind == JsonValueKind.String &&
                        TryResolveNodeId(value.GetString(), namespaceUris, out NodeId candidate) &&
                        candidate == receiver);
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
