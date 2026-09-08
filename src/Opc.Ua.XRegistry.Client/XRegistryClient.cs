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
    /// This type is the sanctioned extension point. A domain registry derives from it and adds
    /// domain-specific naming and defaults;
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
        /// registry's companion namespace, using the well-known registry root Object.
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
            : this(session, registryNamespaceUri, default, telemetry)
        {
        }

        /// <summary>
        /// Initializes a registry client bound to a connected <paramref name="session"/>, the
        /// registry's companion namespace, and an explicit registry root Object.
        /// <para>
        /// A domain registry does not necessarily publish its root at
        /// <see cref="XRegistryWellKnown.RegistryObject"/> — that identifier is provisional, and a
        /// domain model can declare its own root, which a client typically discovers by Browse.
        /// Passing the resolved NodeId here makes the root a construction-time input that cannot
        /// subsequently drift.
        /// </para>
        /// </summary>
        /// <param name="session">The connected session whose server hosts the registry.</param>
        /// <param name="registryNamespaceUri">The registry companion namespace URI.</param>
        /// <param name="registryNodeId">
        /// The registry root Object. Pass a null NodeId to use the well-known root
        /// <see cref="XRegistryWellKnown.RegistryObject"/> in the resolved registry namespace.
        /// </param>
        /// <param name="telemetry">Telemetry context used by the generated proxies.</param>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> or
        /// <paramref name="telemetry"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="registryNamespaceUri"/> is null/empty.</exception>
        /// <exception cref="ServiceResultException">The server does not expose the registry namespace.</exception>
        protected XRegistryClient(
            ISession session,
            string registryNamespaceUri,
            NodeId registryNodeId,
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
            RegistryNodeId = registryNodeId.IsNull
                ? new NodeId(XRegistryWellKnown.RegistryObject, NamespaceIndex)
                : registryNodeId;
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
        /// Gets the NodeId of the registry root Object. By default this is the well-known
        /// identifier a server publishes in its registry namespace, so a caller does not have to
        /// Browse for it; a domain registry whose root lives elsewhere supplies it at construction.
        /// This is the starting point for the group lifecycle.
        /// </summary>
        public NodeId RegistryNodeId { get; }

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
        /// directly. The read goes through <see cref="SessionClientExtensions.ReadBytesAsync"/>, so a
        /// document larger than the session's <c>MaxByteStringLength</c> is fetched with range-based
        /// reads instead of failing. Returns a null <see cref="ByteString"/> when no fast-path node is
        /// registered, so the caller can fall back to a Browse or a registry-specific download.
        /// </summary>
        /// <param name="resourceId">The raw content-derived id bytes.</param>
        /// <param name="maxByteStringLength">
        /// The chunk size for the range-based reads; 0 uses the session's own limit.
        /// </param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The resource document bytes, or a null ByteString when not registered.</returns>
        /// <exception cref="ArgumentException"><paramref name="resourceId"/> is null/empty.</exception>
        public async Task<ByteString> ResolveResourceAsync(
            ByteString resourceId,
            int maxByteStringLength = 0,
            CancellationToken ct = default)
        {
            if (resourceId.IsNull || resourceId.Length == 0)
            {
                throw new ArgumentException("A resource id is required.", nameof(resourceId));
            }

            var fastPathNodeId = new NodeId(resourceId, NamespaceIndex);
            try
            {
                return await Session.ReadBytesAsync(fastPathNodeId, maxByteStringLength, ct)
                    .ConfigureAwait(false);
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
        /// the server publishes the Version's independent content-addressed fast path.
        /// </summary>
        /// <param name="groupNodeId">The NodeId of the group that owns the resource.</param>
        /// <param name="resourceId">The resource id to create or version.</param>
        /// <param name="document">The resource document bytes.</param>
        /// <param name="versionId">The version id; empty lets the server assign the next one.</param>
        /// <param name="chunkSize">The maximum Write chunk size in bytes.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The created resource version.</returns>
        /// <exception cref="ArgumentException"><paramref name="groupNodeId"/> or
        /// <paramref name="resourceId"/> is null/empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="chunkSize"/> is not positive.</exception>
        public async Task<ResourceRegistrationResult> RegisterResourceAsync(
            NodeId groupNodeId,
            string resourceId,
            ByteString document,
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

            return new ResourceRegistrationResult(resourceNodeId, assignedVersionId, Created: true);
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
        public async Task<GroupRegistrationResult> GetOrCreateGroupAsync(
            NodeId registryNodeId,
            string groupId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(groupId))
            {
                throw new ArgumentException("A group id is required.", nameof(groupId));
            }

            RegistryTypeClient registry = GetRegistry(registryNodeId);
            (NodeId groupNodeId, bool created) = await registry
                .GetOrCreateGroupAsync(groupId, ct)
                .ConfigureAwait(false);
            return new GroupRegistrationResult(groupNodeId, created);
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
        /// <returns>The resource version and whether this call created it.</returns>
        /// <exception cref="ArgumentException"><paramref name="groupNodeId"/> or
        /// <paramref name="resourceId"/> is null/empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="chunkSize"/> is not positive.</exception>
        public async Task<ResourceRegistrationResult> GetOrRegisterResourceAsync(
            NodeId groupNodeId,
            string resourceId,
            ByteString document,
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

            ResourceTypeClient resource = GetResource(resourceNodeId);
            if (created)
            {
                await resource.WriteDocumentAsync(fileHandle, document, chunkSize, ct)
                    .ConfigureAwait(false);
            }
            else
            {
                // The server opened a write handle for us either way. Nothing is written to an
                // existing version, but the handle still has to be released or it leaks and
                // eventually exhausts the server's upload budget.
                await resource.CloseAsync(fileHandle, CancellationToken.None).ConfigureAwait(false);
            }

            return new ResourceRegistrationResult(resourceNodeId, assignedVersionId, created);
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

        /// <summary>
        /// Returns <c>true</c> when the connected server advertises an xRegistry model version
        /// that uses the distinct Resource / Versions-folder / Version hierarchy (≥ 0.6.0).
        /// The result is read once from the server's namespace metadata and cached for the
        /// lifetime of this client instance.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>true</c> when the hierarchy is distinct; <c>false</c> (or unknown)
        /// selects the legacy flat layout.</returns>
        public async ValueTask<bool> UsesDistinctHierarchyAsync(CancellationToken ct = default)
        {
            if (m_hierarchyChecked)
            {
                return m_usesDistinctHierarchy;
            }
            m_usesDistinctHierarchy = await DetectDistinctHierarchyAsync(ct).ConfigureAwait(false);
            m_hierarchyChecked = true;
            return m_usesDistinctHierarchy;
        }

        /// <summary>
        /// Reads the <c>NamespaceVersion</c> property from the xRegistry namespace metadata
        /// object and returns <c>true</c> when the version is ≥ 0.6.0.
        /// </summary>
        private async ValueTask<bool> DetectDistinctHierarchyAsync(CancellationToken ct)
        {
            try
            {
                // NamespaceVersion lives at well-known numeric id 63563 in the xRegistry
                // companion namespace (which may differ from the domain registry namespace).
                int xregIdx = Session.NamespaceUris.GetIndex(
                    XRegistryWellKnown.XRegistryNamespaceUri);
                ushort ns = xregIdx > 0
                    ? (ushort)xregIdx
                    : NamespaceIndex; // fallback to domain namespace if xRegistry is not separate

                var versionNodeId = new NodeId(63563u, ns);
                DataValue value = await Session.ReadValueAsync(versionNodeId, ct)
                    .ConfigureAwait(false);
                if (StatusCode.IsBad(value.StatusCode) ||
                    !value.WrappedValue.TryGetValue(out string versionString) ||
                    string.IsNullOrEmpty(versionString))
                {
                    return false;
                }
                return IsVersionAtLeast(versionString, 0, 6, 0);
            }
            catch (ServiceResultException)
            {
                return false;
            }
        }

        /// <summary>
        /// Compares a "major.minor.patch" version string against a threshold.
        /// A version string may omit trailing components (e.g. "1" or "1.2"),
        /// in which case each omitted component is treated as 0 for the
        /// comparison — so "1" is equivalent to "1.0.0". Returns <c>true</c>
        /// when the parsed version is ≥ the threshold. Returns <c>false</c> on
        /// any parse failure of a component that IS present.
        /// </summary>
        public static bool IsVersionAtLeast(string version, int major, int minor, int patch)
        {
            if (string.IsNullOrEmpty(version))
            {
                return false;
            }
            string[] parts = version.Split('.');
            if (parts.Length < 1 || !int.TryParse(parts[0], out int maj))
            {
                return false;
            }

            // A missing minor/patch component is not a parse failure — it is
            // treated as 0, so a bare "1" compares equal to "1.0.0" rather than
            // always comparing strictly less than any threshold with the same
            // major component.
            int min = 0;
            if (parts.Length >= 2 && !int.TryParse(parts[1], out min))
            {
                return false;
            }

            int pat = 0;
            if (parts.Length >= 3)
            {
                // Strip any suffix after digits (e.g. "-preview").
                string patchPart = parts[2];
                int end = 0;
                while (end < patchPart.Length && char.IsDigit(patchPart[end]))
                {
                    end++;
                }
                if (end == 0 ||
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_1_OR_GREATER
                    !int.TryParse(patchPart.AsSpan(0, end), out pat))
#else
                    !int.TryParse(patchPart.Substring(0, end), out pat))
#endif
                {
                    return false;
                }
            }

            return maj > major ||
                   (maj == major && min > minor) ||
                   (maj == major && min == minor && pat >= patch);
        }

        private bool m_hierarchyChecked;
        private bool m_usesDistinctHierarchy;
    }
}
