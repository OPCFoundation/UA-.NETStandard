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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.XRegistry.Client
{
    public abstract partial class XRegistryClient
    {
        /// <summary>
        /// Verifies a logical federation target using this client's already established, authenticated
        /// Session. This does not connect to a document-supplied URL, read content or use the fast path.
        /// </summary>
        /// <exception cref="ArgumentNullException">The target is null.</exception>
        /// <exception cref="ArgumentException">The locator is invalid.</exception>
        /// <exception cref="ServiceResultException">
        /// The peer, identity, hierarchy or FileType capability differs.
        /// </exception>
        public async ValueTask VerifyLogicalResourceAsync(
            XRegistryFederationTarget target,
            string endpointUrl,
            CancellationToken cancellationToken = default)
        {
            ResourceTypeClient resource = await VerifyFederationResourceAsync(
                target, endpointUrl, cancellationToken).ConfigureAwait(false);
            await CloseBindingAsync(resource.Session).ConfigureAwait(false);
        }

        /// <summary>
        /// Follows a local proxy's actual ExternalReference to the verified remote logical Resource.
        /// The remote Session and trusted target must be selected independently of the proxy metadata.
        /// Use the returned generated proxy's FileType methods and GetVersionsAsync for content access.
        /// </summary>
        /// <param name="referencingSession">
        /// The Session exposing the local proxy and its namespace/server tables.
        /// </param>
        /// <param name="proxyNodeId">The local logical proxy Object.</param>
        /// <param name="trustedTarget">The independently configured immutable trusted binding.</param>
        /// <param name="cancellationToken">Cancels metadata verification before any file is opened.</param>
        /// <returns>The generated client for the remote logical Resource, not a content lookup Variable.</returns>
        /// <exception cref="ArgumentNullException">A Session or target is null.</exception>
        /// <exception cref="ServiceResultException">
        /// Metadata, trust, identity or required capability is invalid.
        /// </exception>
        public async ValueTask<ResourceTypeClient> FollowExternalReferenceAsync(
            ISession referencingSession,
            NodeId proxyNodeId,
            XRegistryFederationTarget trustedTarget,
            CancellationToken cancellationToken = default)
        {
            if (referencingSession is null)
            {
                throw new ArgumentNullException(nameof(referencingSession));
            }
            if (trustedTarget is null)
            {
                throw new ArgumentNullException(nameof(trustedTarget));
            }
            cancellationToken.ThrowIfCancellationRequested();
            ISessionClient binding = await CaptureFederationBindingAsync(referencingSession, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                return await FollowBoundExternalReferenceAsync(
                    binding, proxyNodeId, trustedTarget, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await CloseBindingAsync(binding).ConfigureAwait(false);
            }
        }

        private async ValueTask<ResourceTypeClient> FollowBoundExternalReferenceAsync(
            ISessionClient referencingSession,
            NodeId proxyNodeId,
            XRegistryFederationTarget trustedTarget,
            CancellationToken cancellationToken)
        {
            using var typeCache = new NodeCache(new NodeCacheContext(referencingSession), Telemetry);
            await RequireObjectTypeAsync(
                referencingSession, typeCache, proxyNodeId, ObjectTypeIds.ResourceType, cancellationToken)
                .ConfigureAwait(false);

            DataValue originValue = await ReadFederationPropertyAsync(
                referencingSession, proxyNodeId, BrowseNames.OriginRegistry,
                DataTypeIds.RegistryOriginDataType, cancellationToken).ConfigureAwait(false);
            IServiceMessageContext sourceContext = referencingSession.MessageContext;
            var decodingContext = new ServiceMessageContext(Telemetry, EncodeableFactory.Create())
            {
                NamespaceUris = sourceContext.NamespaceUris,
                ServerUris = sourceContext.ServerUris,
                MaxStringLength = sourceContext.MaxStringLength,
                MaxByteStringLength = sourceContext.MaxByteStringLength,
                MaxArrayLength = sourceContext.MaxArrayLength,
                MaxMessageSize = sourceContext.MaxMessageSize,
                MaxEncodingNestingLevels = sourceContext.MaxEncodingNestingLevels,
                MaxDecoderRecoveries = sourceContext.MaxDecoderRecoveries
            };
            decodingContext.Factory.Builder.AddOpcUaXRegistry().Commit();
            if (!originValue.WrappedValue.TryGetValue(out ExtensionObject extension) ||
                !extension.TryGetValue(out RegistryOriginDataType? origin, decodingContext))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "The proxy does not expose a RegistryOriginDataType.");
            }
            trustedTarget.VerifyOrigin(origin);

            DataValue referenceValue = await ReadFederationPropertyAsync(
                referencingSession, proxyNodeId, BrowseNames.ExternalReference,
                Ua.DataTypeIds.ExpandedNodeId, cancellationToken).ConfigureAwait(false);
            if (!referenceValue.WrappedValue.TryGetValue(out ExpandedNodeId reference) || reference.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "The proxy reference is not a NodeId.");
            }
            string? serverUri = referencingSession.MessageContext.ServerUris.GetString(reference.ServerIndex);
            string? namespaceUri = reference.NamespaceUri;
            if (string.IsNullOrEmpty(namespaceUri))
            {
                namespaceUri = referencingSession.MessageContext.NamespaceUris.GetString(reference.NamespaceIndex);
            }
            if (string.IsNullOrEmpty(serverUri) ||
                string.IsNullOrEmpty(namespaceUri) ||
                !string.Equals(serverUri, trustedTarget.ServerUri, StringComparison.Ordinal) ||
                new ExpandedNodeId(reference.InnerNodeId, namespaceUri) != trustedTarget.ResourceNodeId)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The proxy reference is not the trusted remote Resource.");
            }

            string endpointUrl = await ReadFederationStringAsync(
                referencingSession, proxyNodeId, BrowseNames.ResourceUrl, cancellationToken).ConfigureAwait(false);
            return await VerifyFederationResourceAsync(trustedTarget, endpointUrl, cancellationToken)
                .ConfigureAwait(false);
        }

        private async ValueTask<ResourceTypeClient> VerifyFederationResourceAsync(
            XRegistryFederationTarget target,
            string endpointUrl,
            CancellationToken cancellationToken)
        {
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            cancellationToken.ThrowIfCancellationRequested();
            XRegistryFederationTarget.ValidateEndpoint(endpointUrl);
            RegistryOriginDataType origin = target.OriginRegistry;
            if (!string.IsNullOrEmpty(origin.OriginUri))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The native provider requires a trusted application/registry-root origin.");
            }
            ISessionClient binding = await CaptureFederationBindingAsync(Session, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                return await VerifyBoundFederationResourceAsync(
                    binding, target, origin, endpointUrl, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await CloseBindingAsync(binding).ConfigureAwait(false);
                throw;
            }
        }

        private async ValueTask<ResourceTypeClient> VerifyBoundFederationResourceAsync(
            ISessionClient session,
            XRegistryFederationTarget target,
            RegistryOriginDataType origin,
            string endpointUrl,
            CancellationToken cancellationToken)
        {
            using var typeCache = new NodeCache(new NodeCacheContext(session), Telemetry);
            if (!session.Connected)
            {
                throw new ServiceResultException(StatusCodes.BadSessionClosed);
            }
            EndpointDescription endpoint = session.Endpoint;
            if (endpoint.SecurityMode is not (MessageSecurityMode.Sign or MessageSecurityMode.SignAndEncrypt))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityModeInsufficient, "Federation requires an authenticated server Session.");
            }
            if (!string.Equals(endpoint.EndpointUrl, endpointUrl, StringComparison.Ordinal) ||
                !string.Equals(endpoint.Server.ApplicationUri, target.ServerUri, StringComparison.Ordinal) ||
                !string.Equals(
                    session.MessageContext.ServerUris.GetString(0), target.ServerUri, StringComparison.Ordinal) ||
                m_registryIdentity != origin.RegistryNodeId)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The authorized Session does not match the pinned origin/locator.");
            }

            NodeId registry = ResolveFederationNode(session, origin.RegistryNodeId);
            NodeId resource = ResolveFederationNode(session, target.ResourceNodeId);
            await RequireObjectTypeAsync(session, typeCache, registry, ObjectTypeIds.RegistryType, cancellationToken)
                .ConfigureAwait(false);
            await RequireObjectTypeAsync(session, typeCache, resource, ObjectTypeIds.ResourceType, cancellationToken)
                .ConfigureAwait(false);
            await RequireObjectTypeAsync(session, typeCache, resource, Ua.ObjectTypeIds.FileType, cancellationToken)
                .ConfigureAwait(false);

            ushort domainNamespace = RequireFederationNamespace(session, RegistryNamespaceUri);
            NodeId group = await RequireFederationChildAsync(
                session, registry, ReferenceTypeIds.HierarchicalReferences,
                new QualifiedName(target.GroupId, domainNamespace), NodeClass.Object, cancellationToken)
                .ConfigureAwait(false);
            await RequireObjectTypeAsync(session, typeCache, group, ObjectTypeIds.GroupType, cancellationToken)
                .ConfigureAwait(false);
            string groupXid = await ReadFederationStringAsync(session, group, BrowseNames.Xid, cancellationToken)
                .ConfigureAwait(false);
            string resourceXid = await ReadFederationStringAsync(session, resource, BrowseNames.Xid, cancellationToken)
                .ConfigureAwait(false);
            NodeId ownedResource = await RequireFederationChildAsync(
                session, group, ReferenceTypeIds.HierarchicalReferences,
                new QualifiedName(target.ResourceId, domainNamespace), NodeClass.Object, cancellationToken)
                .ConfigureAwait(false);
            if (ownedResource != resource ||
                !string.Equals(groupXid, target.GroupXid, StringComparison.Ordinal) ||
                !string.Equals(resourceXid, target.ResourceXid, StringComparison.Ordinal))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The Resource's ownership or logical Xid differs from the binding.");
            }

            NodeId versions = await RequireFederationChildAsync(
                session, resource, ReferenceTypeIds.HasComponent,
                new QualifiedName(BrowseNames.Versions,
                    RequireFederationNamespace(session, XRegistryWellKnown.XRegistryNamespaceUri)),
                NodeClass.Object, cancellationToken).ConfigureAwait(false);
            await RequireObjectTypeAsync(
                session, typeCache, versions, ObjectTypeIds.ResourceVersionsType, cancellationToken)
                .ConfigureAwait(false);
            foreach (string methodName in s_federationReadMethods)
            {
                NodeId method = await RequireFederationChildAsync(
                    session, resource, ReferenceTypeIds.HasComponent,
                    new QualifiedName(methodName, 0), NodeClass.Method, cancellationToken).ConfigureAwait(false);
                Node node = await session.ReadNodeAsync(method, cancellationToken).ConfigureAwait(false);
                if (node is not MethodNode { Executable: true } fileMethod)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "The logical Resource does not support the required FileType reads.");
                }
                if (!fileMethod.UserExecutable)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                }
            }

            var proxy = new ResourceTypeClient(session, resource, Telemetry);
            ResourceVersionsTypeClient? versionProxy = await proxy.GetVersionsAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            if (versionProxy is null || versionProxy.ObjectId != versions)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The generated client cannot resolve the verified Versions hierarchy.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return proxy;
        }

        private static async ValueTask RequireObjectTypeAsync(
            ISessionClient session,
            NodeCache typeCache,
            NodeId nodeId,
            ExpandedNodeId requiredType,
            CancellationToken cancellationToken)
        {
            Node node = await session.ReadNodeAsync(nodeId, cancellationToken).ConfigureAwait(false);
            if (node.NodeClass != NodeClass.Object)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeClassInvalid, "A federation target must be an Object.");
            }
            var browser = new Browser(session, new BrowserOptions
            {
                ReferenceTypeId = ReferenceTypeIds.HasTypeDefinition
            });
            ArrayOf<ReferenceDescription> types = await browser.BrowseAsync(nodeId, cancellationToken)
                .ConfigureAwait(false);
            NodeId expectedType = ResolveFederationNode(session, requiredType);
            if (types.Count != 1 ||
                types[0].NodeId.ServerIndex != 0 ||
                !await typeCache.IsTypeOfAsync(
                    ResolveFederationNode(session, types[0].NodeId), expectedType, cancellationToken)
                    .ConfigureAwait(false))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch,
                    "The federation Object does not have the required xRegistry/FileType role.");
            }
        }

        private static async ValueTask<NodeId> RequireFederationChildAsync(
            ISessionClient session,
            NodeId parent,
            NodeId referenceType,
            QualifiedName browseName,
            NodeClass nodeClass,
            CancellationToken cancellationToken)
        {
            var browser = new Browser(session, new BrowserOptions { ReferenceTypeId = referenceType });
            ArrayOf<ReferenceDescription> references = await browser.BrowseAsync(parent, cancellationToken)
                .ConfigureAwait(false);
            NodeId found = NodeId.Null;
            foreach (ReferenceDescription reference in references)
            {
                if (reference.BrowseName != browseName)
                {
                    continue;
                }
                if (!found.IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadBrowseNameDuplicated, "Federation metadata or ownership is ambiguous.");
                }
                if (reference.NodeClass != nodeClass || reference.NodeId.ServerIndex != 0)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeClassInvalid, "The federation member has an invalid role or application.");
                }
                found = ResolveFederationNode(session, reference.NodeId);
            }
            if (found.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "A required federation member is absent.");
            }
            return found;
        }

        private static async ValueTask<DataValue> ReadFederationPropertyAsync(
            ISessionClient session,
            NodeId parent,
            string name,
            ExpandedNodeId expectedDataType,
            CancellationToken cancellationToken)
        {
            NodeId property = await RequireFederationChildAsync(
                session, parent, ReferenceTypeIds.HasProperty,
                new QualifiedName(name, RequireFederationNamespace(session, XRegistryWellKnown.XRegistryNamespaceUri)),
                NodeClass.Variable, cancellationToken).ConfigureAwait(false);
            Node metadata = await session.ReadNodeAsync(property, cancellationToken).ConfigureAwait(false);
            if (metadata is not VariableNode variable ||
                variable.ValueRank != ValueRanks.Scalar ||
                variable.DataType != ResolveFederationNode(session, expectedDataType))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, $"{name} must declare the required scalar federation DataType.");
            }
            DataValue value = await session.ReadValueAsync(property, cancellationToken).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                throw new ServiceResultException(value.StatusCode);
            }
            return value;
        }

        private static async ValueTask<string> ReadFederationStringAsync(
            ISessionClient session,
            NodeId parent,
            string name,
            CancellationToken cancellationToken)
        {
            DataValue value = await ReadFederationPropertyAsync(
                session, parent, name, Ua.DataTypeIds.String, cancellationToken).ConfigureAwait(false);
            if (!value.WrappedValue.TryGetValue(out string text) || string.IsNullOrEmpty(text))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "The federation identity/locator is empty.");
            }
            return text;
        }

        private static NodeId ResolveFederationNode(ISessionClient session, ExpandedNodeId nodeId)
        {
            var resolved = ExpandedNodeId.ToNodeId(nodeId, session.MessageContext.NamespaceUris);
            if (resolved.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown, "The federation namespace is unavailable.");
            }
            return resolved;
        }

        private static ushort RequireFederationNamespace(ISessionClient session, string namespaceUri)
        {
            int index = session.MessageContext.NamespaceUris.GetIndex(namespaceUri);
            if (index < 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The Session does not expose the required federation namespace.");
            }
            return (ushort)index;
        }

        private static ValueTask<ISessionClient> CaptureFederationBindingAsync(
            ISessionClient session,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (session is not ISessionBindingProvider provider)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "Federation requires a generation-bound session provider.");
            }
            return provider.CreateBindingAsync(ct);
        }

        private static async ValueTask CloseBindingAsync(ISessionClient binding)
        {
            try
            {
                await binding.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                binding.Dispose();
            }
        }

        private static readonly string[] s_federationReadMethods =
            [Ua.BrowseNames.Open, Ua.BrowseNames.Read, Ua.BrowseNames.Close];
    }
}
