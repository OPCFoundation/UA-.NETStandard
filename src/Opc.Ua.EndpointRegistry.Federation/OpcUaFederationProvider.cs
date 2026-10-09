/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry.Federation
{
    /// <summary>
    /// Explicit discovery/preload over a caller-supplied authenticated SignAndEncrypt Session.
    /// This provider is deliberately not IEndpointRegistryResolutionProvider: network I/O is never implicit.
    /// </summary>
    public sealed partial class OpcUaFederationProvider
    {
        /// <summary>Creates a provider without connecting, discovering or reading any Node.</summary>
        public OpcUaFederationProvider(
            ISession session,
            FederationTrustBinding binding,
            RegistryRecordMapper mapper,
            ITelemetryContext telemetry)
        {
            m_session = session ?? throw new ArgumentNullException(nameof(session));
            m_binding = binding ?? throw new ArgumentNullException(nameof(binding));
            m_mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            session.MessageContext.Factory.Builder
                .AddOpcUaXRegistry()
                .AddOpcUaSchemaRegistry()
                .AddOpcUaEndpointRegistry()
                .Commit();
            CheckSession();
        }

        /// <summary>
        /// Discovers a concrete Message by browsing its rooted ownership path. The returned target is
        /// an actual browsed portable NodeId; names are never converted to a private NodeId formula.
        /// </summary>
        public async ValueTask<RegistryEntityReferenceDataType> DiscoverMessageAsync(
            string xid, CancellationToken cancellationToken = default)
        {
            CheckSession();
            string role = xid.Contains("/versions/", StringComparison.Ordinal) ? "MetadataVersion" : "MetadataResource";
            string logical = FederationSourceKey.ValidateXid(xid, role);
            NodeId target = await OwnedTargetAsync(logical, cancellationToken).ConfigureAwait(false);
            return new RegistryEntityReferenceDataType
            {
                OriginUri = m_binding.Origin.OriginUri,
                ApplicationUri = m_binding.Origin.ApplicationUri,
                RegistryNode = m_binding.Origin.RegistryNode,
                Role = role,
                Xid = xid,
                Locator = m_session.ConfiguredEndpoint.Description.EndpointUrl,
                HasNativeTarget = true,
                NativeTarget = FederationPortableIdentity.FromNode(target, m_session.NamespaceUris)
            };
        }

        /// <summary>
        /// Observes all independent evidence, checks ownership/type/Version before reading metadata,
        /// reads the native Snapshot (or TypedAccess generic view), and returns a new immutable cache.
        /// </summary>
        public async ValueTask<FederationResolutionCache> PreloadAsync(
            FederationResolutionCache cache,
            RegistryEntityReferenceDataType reference,
            CancellationToken cancellationToken = default)
        {
            if (cache is null)
            {
                throw new ArgumentNullException(nameof(cache));
            }
            CheckSession();
            var key = new FederationSourceKey(reference);
            NodeId root = Transport(m_binding.RegistryRoot);
            NodeId target = Transport(reference.NativeTarget);
            FederationNodeObservation rootEvidence = await ObserveNodeAsync(root, "RegistryRoot", cancellationToken)
                .ConfigureAwait(false);
            FederationNodeObservation targetEvidence = await ObserveNodeAsync(target, "MetadataResource", cancellationToken)
                .ConfigureAwait(false);
            NodeId owned = await OwnedTargetAsync(key.LogicalXid, cancellationToken).ConfigureAwait(false);
            if (owned != target)
            {
                throw new ArgumentException("The target is not owned by the authorized registry's Message collection.");
            }
            ushort xns = Namespace(XRegistry.Namespaces.xRegistry);
            string xid = Text(await ReadPropertyAsync(target, new QualifiedName(XRegistry.BrowseNames.Xid, xns),
                cancellationToken).ConfigureAwait(false));
            string version = Text(await ReadPropertyAsync(target, new QualifiedName(XRegistry.BrowseNames.VersionId, xns),
                cancellationToken).ConfigureAwait(false));
            bool hasDocument = Flag(await ReadPropertyAsync(target, new QualifiedName(XRegistry.BrowseNames.HasDocument, xns),
                cancellationToken).ConfigureAwait(false));
            uint maxVersions = Number(await ReadPropertyAsync(target, new QualifiedName(XRegistry.BrowseNames.MaxVersions, xns),
                cancellationToken).ConfigureAwait(false));
            uint before = Number(await ReadPropertyAsync(target, new QualifiedName(XRegistry.BrowseNames.Epoch, xns),
                cancellationToken).ConfigureAwait(false));
            var evidence = new FederationMetadataObservation(reference,
                m_session.ConfiguredEndpoint.Description.EndpointUrl!,
                m_session.ConfiguredEndpoint.Description.Server.ApplicationUri!,
                rootEvidence, targetEvidence, m_binding.RegistryRoot, xid, version, hasDocument, maxVersions);
            new FederationMetadataSelector().Select(reference, m_binding, evidence);
            RegistryReadResultDataType result = await ReadMetadataAsync(root, target, key.LogicalXid, cancellationToken)
                .ConfigureAwait(false);
            uint after = Number(await ReadPropertyAsync(target, new QualifiedName(XRegistry.BrowseNames.Epoch, xns),
                cancellationToken).ConfigureAwait(false));
            if (before != result.Epoch || after != before)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "Metadata changed while independently observing the target; retry explicit preload.");
            }
            RegistryObjectValueDataType metadata;
            if (result.Document.TryGetValue(out RegistryObjectValueDataType? raw, m_session.MessageContext) && raw is not null)
            {
                metadata = raw;
            }
            else if (result.Document.TryGetValue(out MessageDefinitionDataType? record, m_session.MessageContext) &&
                record is not null && m_mapper.Restore(record) is RegistryObjectValueDataType restored)
            {
                metadata = restored;
            }
            else
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Expected native Message metadata.");
            }
            CheckSession();
            return cache.WithObservation(m_binding, evidence, new EndpointRegistryMessageObservation
            {
                Source = reference,
                Metadata = metadata,
                Epoch = result.Epoch,
                VersionId = version
            });
        }

        private void CheckSession()
        {
            EndpointDescription endpoint = m_session.ConfiguredEndpoint.Description;
            if (endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                endpoint.SecurityPolicyUri == SecurityPolicies.None ||
                endpoint.Server.ApplicationUri != m_binding.ApplicationUri ||
                !m_binding.Authorizes(endpoint.EndpointUrl!))
            {
                throw new ArgumentException("The authenticated Session endpoint/application/security contradicts trust.");
            }
            FederationPortableIdentity.ValidateTable(m_session.NamespaceUris.ToArray(), true);
            FederationPortableIdentity.ValidateTable(m_session.ServerUris.ToArray(), false);
            if (m_session.ServerUris.GetString(0) != m_binding.ApplicationUri)
            {
                throw new ArgumentException("The authenticated ServerArray does not identify the expected application.");
            }
        }

        private NodeId Transport(ExpandedNodeId node)
        {
            FederationTrustBinding.Portable(node);
            NodeId target = ExpandedNodeId.ToNodeId(node, m_session.NamespaceUris);
            return target.IsNull
                ? throw new ArgumentException("The portable NamespaceUri is absent from this Session.")
                : target;
        }

        private ushort Namespace(string uri)
        {
            int index = m_session.NamespaceUris.GetIndex(uri);
            return index < 0 ? throw new ArgumentException("The required model NamespaceUri is not exposed.")
                : checked((ushort)index);
        }

        private async ValueTask<NodeId> OwnedTargetAsync(string logicalXid, CancellationToken ct)
        {
            string[] segments = logicalXid.Split('/');
            ushort ns = Namespace(Namespaces.EndpointRegistry);
            NodeId node = Transport(m_binding.RegistryRoot);
            string collection = segments[1] == "messagegroups" ? BrowseNames.MessageGroups : BrowseNames.Endpoints;
            node = await ChildAsync(node, new QualifiedName(collection, ns), ct).ConfigureAwait(false);
            node = await ChildAsync(node, new QualifiedName(segments[2], ns), ct).ConfigureAwait(false);
            node = await ChildAsync(node, new QualifiedName(BrowseNames.Messages, ns), ct).ConfigureAwait(false);
            return await ChildAsync(node, new QualifiedName(segments[4], ns), ct).ConfigureAwait(false);
        }

        private async ValueTask<FederationNodeObservation> ObserveNodeAsync(NodeId node, string role, CancellationToken ct)
        {
            Node observed = await m_session.ReadNodeAsync(node, ct).ConfigureAwait(false);
            var types = new List<ExpandedNodeId>();
            ArrayOf<ReferenceDescription> definitions = await BrowseAsync(node, Ua.ReferenceTypeIds.HasTypeDefinition,
                BrowseDirection.Forward, false, ct).ConfigureAwait(false);
            if (definitions.Count != 1)
            {
                throw new ArgumentException("An Object requires one unambiguous type definition.");
            }
            NodeId type = Local(definitions[0].NodeId);
            while (!type.IsNull)
            {
                ExpandedNodeId portable = FederationPortableIdentity.FromNode(type, m_session.NamespaceUris);
                if (types.Contains(portable) || types.Count >= 128)
                {
                    throw new ArgumentException("Cyclic or excessive type ancestry.");
                }
                types.Add(portable);
                ArrayOf<ReferenceDescription> parents = await BrowseAsync(type, Ua.ReferenceTypeIds.HasSubtype,
                    BrowseDirection.Inverse, false, ct).ConfigureAwait(false);
                if (parents.Count > 1)
                {
                    throw new ArgumentException("Ambiguous type ancestry.");
                }
                type = parents.Count == 0 ? NodeId.Null : Local(parents[0].NodeId);
            }
            return new FederationNodeObservation(FederationPortableIdentity.FromNode(node, m_session.NamespaceUris),
                observed.NodeClass, role, [.. types]);
        }

        private async ValueTask<Variant> ReadPropertyAsync(NodeId parent, QualifiedName name, CancellationToken ct)
        {
            NodeId node = await ChildAsync(parent, name, ct).ConfigureAwait(false);
            DataValue value = await m_session.ReadValueAsync(node, ct).ConfigureAwait(false);
            Check(value.StatusCode);
            return value.WrappedValue;
        }

        private static string Text(Variant value) => value.TryGetValue(out string result)
            ? result : throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Expected String Property.");

        private static bool Flag(Variant value) => value.TryGetValue(out bool result)
            ? result : throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Expected Boolean Property.");

        private static uint Number(Variant value) => value.TryGetValue(out uint result)
            ? result : throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Expected UInt32 Property.");

        private async ValueTask<RegistryReadResultDataType> ReadMetadataAsync(
            NodeId root, NodeId target, string xid, CancellationToken ct)
        {
            ArrayOf<ReferenceDescription> children = await BrowseAsync(target, Ua.ReferenceTypeIds.HierarchicalReferences,
                BrowseDirection.Forward, true, ct).ConfigureAwait(false);
            NodeId snapshot = Find(children, new QualifiedName(BrowseNames.Snapshot, Namespace(Namespaces.EndpointRegistry)));
            if (!snapshot.IsNull)
            {
                DataValue value = await m_session.ReadValueAsync(snapshot, ct).ConfigureAwait(false);
                Check(value.StatusCode);
                if (value.WrappedValue.TryGetValue(out ExtensionObject extension) &&
                    extension.TryGetValue(out RegistryReadResultDataType? read, m_session.MessageContext) && read is not null)
                {
                    Check(read.StatusCode);
                    if (!read.ContinuationPoint.IsNull && read.ContinuationPoint.Length > 0)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded, "Incomplete native Snapshot.");
                    }
                    return read;
                }
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Invalid native Snapshot.");
            }
            NodeId accessNode = await ChildAsync(root,
                new QualifiedName(XRegistry.BrowseNames.TypedAccess, Namespace(XRegistry.Namespaces.xRegistry)), ct)
                .ConfigureAwait(false);
            var access = new NativeRegistryAccessTypeClient(m_session, accessNode, m_telemetry);
            var members = new List<RegistryMemberDataType>();
            ByteString continuation = ByteString.Empty;
            uint epoch = 0;
            do
            {
                RegistryReadResultDataType page = await access.ReadDocumentAsync(new RegistryReadRequestDataType
                {
                    TargetXid = xid,
                    DocumentKind = "metadata",
                    View = 0,
                    MaxItems = 100,
                    ContinuationPoint = continuation
                }, ct).ConfigureAwait(false);
                Check(page.StatusCode);
                if (epoch != 0 && page.Epoch != epoch ||
                    !page.Document.TryGetValue(out RegistryObjectValueDataType? map, m_session.MessageContext) || map is null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "Inconsistent native metadata page.");
                }
                epoch = page.Epoch;
                foreach (RegistryMemberDataType member in map.Members)
                {
                    members.Add(member);
                }
                if (members.Count > 100000)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }
                continuation = page.ContinuationPoint;
            }
            while (!continuation.IsNull && continuation.Length > 0);
            return new RegistryReadResultDataType
            {
                Epoch = epoch,
                StatusCode = StatusCodes.Good,
                Document = new ExtensionObject(new RegistryObjectValueDataType { Kind = 5, Members = [.. members] }),
                ContinuationPoint = ByteString.Empty,
                Issues = []
            };
        }

        private async ValueTask<NodeId> ChildAsync(NodeId parent, QualifiedName name, CancellationToken ct)
        {
            ArrayOf<ReferenceDescription> children = await BrowseAsync(parent, Ua.ReferenceTypeIds.HierarchicalReferences,
                BrowseDirection.Forward, true, ct).ConfigureAwait(false);
            NodeId node = Find(children, name);
            return node.IsNull ? throw new ServiceResultException(StatusCodes.BadNotFound,
                "The authorized ownership path or required Property is absent: " + name) : node;
        }

        private NodeId Find(ArrayOf<ReferenceDescription> children, QualifiedName name)
        {
            NodeId result = NodeId.Null;
            foreach (ReferenceDescription child in children)
            {
                if (child.BrowseName == name)
                {
                    if (!result.IsNull)
                    {
                        throw new ArgumentException("Ambiguous browsed ownership path or Property.");
                    }
                    result = Local(child.NodeId);
                }
            }
            return result;
        }

        private NodeId Local(ExpandedNodeId node)
        {
            if (node.ServerIndex != 0)
            {
                throw new ArgumentException("Ownership/type references cannot leave the authenticated application.");
            }
            NodeId result = ExpandedNodeId.ToNodeId(node, m_session.NamespaceUris);
            return result.IsNull ? throw new ArgumentException("Unresolved browsed Node namespace.") : result;
        }

        private async ValueTask<ArrayOf<ReferenceDescription>> BrowseAsync(
            NodeId node, NodeId relation, BrowseDirection direction, bool subtypes, CancellationToken ct)
        {
            BrowseResponse first = await m_session.BrowseAsync(null, null, 100,
            [
                new BrowseDescription
                {
                    NodeId = node,
                    BrowseDirection = direction,
                    ReferenceTypeId = relation,
                    IncludeSubtypes = subtypes,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ], ct).ConfigureAwait(false);
            Check(first.Results[0].StatusCode);
            var result = new List<ReferenceDescription>();
            foreach (ReferenceDescription reference in first.Results[0].References)
            {
                result.Add(reference);
            }
            ByteString point = first.Results[0].ContinuationPoint;
            try
            {
                while (!point.IsNull && point.Length > 0)
                {
                    BrowseNextResponse next = await m_session.BrowseNextAsync(null, false, [point], ct).ConfigureAwait(false);
                    point = ByteString.Empty;
                    Check(next.Results[0].StatusCode);
                    point = next.Results[0].ContinuationPoint;
                    foreach (ReferenceDescription reference in next.Results[0].References)
                    {
                        result.Add(reference);
                    }
                    if (result.Count > 100000)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                    }
                }
            }
            finally
            {
                if (!point.IsNull && point.Length > 0)
                {
                    await m_session.BrowseNextAsync(null, true, [point], CancellationToken.None).ConfigureAwait(false);
                }
            }
            return [.. result];
        }

        private static void Check(StatusCode status)
        {
            if (StatusCode.IsBad(status))
            {
                throw new ServiceResultException(status);
            }
        }

        private readonly ISession m_session;
        private readonly FederationTrustBinding m_binding;
        private readonly RegistryRecordMapper m_mapper;
        private readonly ITelemetryContext m_telemetry;
    }
}
