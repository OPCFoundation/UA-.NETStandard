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
    /// <summary>
    /// Base client for an in-server xRegistry registry. Every wire interaction goes through the
    /// source-generated <c>*TypeClient</c> ObjectType proxies, so a domain registry inherits the
    /// whole lifecycle unchanged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This type is the sanctioned extension point. A domain registry — the PubSub Schema Registry,
    /// a WoT registry — derives from it and adds domain-specific naming and defaults;
    /// <see cref="GenericXRegistryClient"/> is the plain, sealed implementation for callers that
    /// only need the base model.
    /// </para>
    /// <para>
    /// Because a domain model subtypes the xRegistry base types (for example
    /// <c>SchemaFileType : ResourceType</c>), the generator emits a proxy chain that mirrors the
    /// OPC UA type hierarchy (<c>SchemaFileTypeClient : ResourceTypeClient : FileTypeClient</c>).
    /// A generic client therefore drives a domain registry through the base proxies, and a domain
    /// client reuses every convenience method defined here.
    /// </para>
    /// </remarks>
    public abstract class XRegistryClient
    {
        /// <summary>
        /// Initializes a registry client bound to a connected <paramref name="session"/> and the
        /// registry's companion namespace.
        /// </summary>
        /// <param name="session">The connected session whose server hosts the registry.</param>
        /// <param name="registryNamespaceUri">The registry companion namespace URI.</param>
        /// <param name="telemetry">Telemetry context used by the generated proxies.</param>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> or
        /// <paramref name="telemetry"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="registryNamespaceUri"/> is null/empty.</exception>
        /// <exception cref="ServiceResultException">The server does not expose the registry namespace.</exception>
        protected XRegistryClient(
            ISession session,
            string registryNamespaceUri,
            ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            if (string.IsNullOrEmpty(registryNamespaceUri))
            {
                throw new ArgumentException("A registry namespace URI is required.", nameof(registryNamespaceUri));
            }

            int index = session.NamespaceUris.GetIndex(registryNamespaceUri);
            if (index <= 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "The server does not expose the registry namespace '{0}'.",
                    registryNamespaceUri);
            }

            RegistryNamespaceUri = registryNamespaceUri;
            NamespaceIndex = (ushort)index;
        }

        /// <summary>
        /// Gets the session the client operates on.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Gets the registry companion namespace URI the client is bound to.
        /// </summary>
        public string RegistryNamespaceUri { get; }

        /// <summary>
        /// Gets the resolved registry companion namespace index on the connected server.
        /// </summary>
        public ushort NamespaceIndex { get; }

        /// <summary>
        /// Gets the telemetry context handed to the generated proxies.
        /// </summary>
        protected ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Returns the typed proxy for a registry root Object. The proxy also drives a domain
        /// registry whose type is a subtype of <c>RegistryType</c>.
        /// </summary>
        /// <param name="registryNodeId">The NodeId of the registry Object.</param>
        /// <returns>The registry proxy.</returns>
        /// <exception cref="ArgumentException"><paramref name="registryNodeId"/> is null.</exception>
        public RegistryTypeClient GetRegistry(NodeId registryNodeId)
        {
            if (registryNodeId.IsNull)
            {
                throw new ArgumentException("A registry NodeId is required.", nameof(registryNodeId));
            }
            return new RegistryTypeClient(Session, registryNodeId, Telemetry);
        }

        /// <summary>
        /// Returns the typed proxy for a resource group Object.
        /// </summary>
        /// <param name="groupNodeId">The NodeId of the group Object.</param>
        /// <returns>The group proxy.</returns>
        /// <exception cref="ArgumentException"><paramref name="groupNodeId"/> is null.</exception>
        public GroupTypeClient GetGroup(NodeId groupNodeId)
        {
            if (groupNodeId.IsNull)
            {
                throw new ArgumentException("A group NodeId is required.", nameof(groupNodeId));
            }
            return new GroupTypeClient(Session, groupNodeId, Telemetry);
        }

        /// <summary>
        /// Returns the typed proxy for a resource Object. <c>ResourceType</c> is a
        /// <c>FileType</c>, so the proxy also exposes the standard file transfer methods.
        /// </summary>
        /// <param name="resourceNodeId">The NodeId of the resource Object.</param>
        /// <returns>The resource proxy.</returns>
        /// <exception cref="ArgumentException"><paramref name="resourceNodeId"/> is null.</exception>
        public ResourceTypeClient GetResource(NodeId resourceNodeId)
        {
            if (resourceNodeId.IsNull)
            {
                throw new ArgumentException("A resource NodeId is required.", nameof(resourceNodeId));
            }
            return new ResourceTypeClient(Session, resourceNodeId, Telemetry);
        }

        /// <summary>
        /// Resolves a resource document from its content-derived id through the Opaque-NodeId fast
        /// path: the Opaque NodeId is built deterministically from the raw content-id bytes and read
        /// in a single operation. Returns a null <see cref="ByteString"/> when no fast-path node is
        /// registered, so the caller can fall back to a Browse or a registry-specific download.
        /// </summary>
        /// <param name="resourceId">The raw content-derived id bytes.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The resource document bytes, or a null ByteString when not registered.</returns>
        /// <exception cref="ArgumentException"><paramref name="resourceId"/> is null/empty.</exception>
        public async Task<ByteString> ResolveResourceAsync(
            ByteString resourceId,
            CancellationToken ct = default)
        {
            if (resourceId.IsNull || resourceId.Length == 0)
            {
                throw new ArgumentException("A resource id is required.", nameof(resourceId));
            }

            var fastPathNodeId = new NodeId(resourceId, NamespaceIndex);
            try
            {
                DataValue value = await Session.ReadValueAsync(fastPathNodeId, ct).ConfigureAwait(false);
                _ = value.WrappedValue.TryGetValue(out ByteString document);
                return document;
            }
            catch (ServiceResultException sre) when (
                sre.StatusCode == StatusCodes.BadNodeIdUnknown ||
                sre.StatusCode == StatusCodes.BadNodeIdInvalid)
            {
                return default;
            }
        }

        /// <summary>
        /// Registers a resource document in a group through the model's own lifecycle: the group's
        /// <c>CreateResource</c> creates the resource version and opens it for writing, the document
        /// is streamed through the inherited <c>FileType</c> Write, and Close finalizes it. On close
        /// the server bootstraps the resource's content-derived identity.
        /// </summary>
        /// <param name="groupNodeId">The NodeId of the group that owns the resource.</param>
        /// <param name="resourceId">The resource id to create or version.</param>
        /// <param name="document">The resource document bytes.</param>
        /// <param name="versionId">The version id; empty lets the server assign the next one.</param>
        /// <param name="chunkSize">The maximum Write chunk size in bytes.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The created resource NodeId and the version id the server assigned.</returns>
        /// <exception cref="ArgumentException"><paramref name="groupNodeId"/> or
        /// <paramref name="resourceId"/> is null/empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="chunkSize"/> is not positive.</exception>
        public async Task<(NodeId ResourceNodeId, string AssignedVersionId)> RegisterResourceAsync(
            NodeId groupNodeId,
            string resourceId,
            ReadOnlyMemory<byte> document,
            string versionId = "",
            int chunkSize = ResourceTypeClientExtensions.DefaultChunkSize,
            CancellationToken ct = default)
        {
            if (groupNodeId.IsNull)
            {
                throw new ArgumentException("A group NodeId is required.", nameof(groupNodeId));
            }
            if (string.IsNullOrEmpty(resourceId))
            {
                throw new ArgumentException("A resource id is required.", nameof(resourceId));
            }
            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize));
            }

            GroupTypeClient group = GetGroup(groupNodeId);
            (NodeId resourceNodeId, string assignedVersionId, uint fileHandle) =
                await group.CreateResourceAsync(resourceId, versionId ?? string.Empty, true, ct)
                    .ConfigureAwait(false);

            ResourceTypeClient resource = GetResource(resourceNodeId);
            await resource.WriteDocumentAsync(fileHandle, document, chunkSize, ct).ConfigureAwait(false);

            return (resourceNodeId, assignedVersionId);
        }

        /// <summary>
        /// Creates a resource group under a registry root.
        /// </summary>
        /// <param name="registryNodeId">The registry root NodeId.</param>
        /// <param name="groupId">The group id to create.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The NodeId of the created group.</returns>
        /// <exception cref="ArgumentException"><paramref name="groupId"/> is null/empty.</exception>
        public async Task<NodeId> CreateGroupAsync(
            NodeId registryNodeId,
            string groupId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(groupId))
            {
                throw new ArgumentException("A group id is required.", nameof(groupId));
            }

            RegistryTypeClient registry = GetRegistry(registryNodeId);
            return await registry.CreateGroupAsync(groupId, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets a resource group, creating it when it does not exist yet.
        /// </summary>
        /// <param name="registryNodeId">The registry root NodeId.</param>
        /// <param name="groupId">The group id.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The group NodeId and whether this call created it.</returns>
        /// <exception cref="ArgumentException"><paramref name="groupId"/> is null/empty.</exception>
        public async Task<(NodeId GroupNodeId, bool Created)> GetOrCreateGroupAsync(
            NodeId registryNodeId,
            string groupId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(groupId))
            {
                throw new ArgumentException("A group id is required.", nameof(groupId));
            }

            RegistryTypeClient registry = GetRegistry(registryNodeId);
            return await registry.GetOrCreateGroupAsync(groupId, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Registers a resource document idempotently: an existing version with the same
        /// <paramref name="resourceId"/> and <paramref name="versionId"/> is reused rather than
        /// rejected, and the document is only streamed when this call created the version.
        /// </summary>
        /// <param name="groupNodeId">The NodeId of the group that owns the resource.</param>
        /// <param name="resourceId">The resource id to create or version.</param>
        /// <param name="document">The resource document bytes.</param>
        /// <param name="versionId">The version id; empty lets the server assign the next one.</param>
        /// <param name="chunkSize">The maximum Write chunk size in bytes.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The resource NodeId, the assigned version id, and whether it was created.</returns>
        /// <exception cref="ArgumentException"><paramref name="groupNodeId"/> or
        /// <paramref name="resourceId"/> is null/empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="chunkSize"/> is not positive.</exception>
        public async Task<(NodeId ResourceNodeId, string AssignedVersionId, bool Created)>
            GetOrRegisterResourceAsync(
                NodeId groupNodeId,
                string resourceId,
                ReadOnlyMemory<byte> document,
                string versionId = "",
                int chunkSize = ResourceTypeClientExtensions.DefaultChunkSize,
                CancellationToken ct = default)
        {
            if (groupNodeId.IsNull)
            {
                throw new ArgumentException("A group NodeId is required.", nameof(groupNodeId));
            }
            if (string.IsNullOrEmpty(resourceId))
            {
                throw new ArgumentException("A resource id is required.", nameof(resourceId));
            }
            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize));
            }

            GroupTypeClient group = GetGroup(groupNodeId);
            (NodeId resourceNodeId, string assignedVersionId, uint fileHandle, bool created) =
                await group.GetOrCreateResourceAsync(resourceId, versionId ?? string.Empty, true, ct)
                    .ConfigureAwait(false);

            if (created)
            {
                ResourceTypeClient resource = GetResource(resourceNodeId);
                await resource.WriteDocumentAsync(fileHandle, document, chunkSize, ct)
                    .ConfigureAwait(false);
            }

            return (resourceNodeId, assignedVersionId, created);
        }

        /// <summary>
        /// Deletes a resource version. The <paramref name="expectedEpoch"/> is the model's
        /// optimistic-concurrency check — the server rejects the call when the resource has moved on.
        /// </summary>
        /// <param name="resourceNodeId">The resource NodeId.</param>
        /// <param name="expectedEpoch">The epoch the caller last observed.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task DeleteResourceAsync(
            NodeId resourceNodeId,
            uint expectedEpoch,
            CancellationToken ct = default)
        {
            ResourceTypeClient resource = GetResource(resourceNodeId);
            await resource.DeleteAsync(expectedEpoch, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes a resource group and every version it owns.
        /// </summary>
        /// <param name="groupNodeId">The group NodeId.</param>
        /// <param name="expectedEpoch">The epoch the caller last observed.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task DeleteGroupAsync(
            NodeId groupNodeId,
            uint expectedEpoch,
            CancellationToken ct = default)
        {
            GroupTypeClient group = GetGroup(groupNodeId);
            await group.DeleteAsync(expectedEpoch, ct).ConfigureAwait(false);
        }
    }
}
