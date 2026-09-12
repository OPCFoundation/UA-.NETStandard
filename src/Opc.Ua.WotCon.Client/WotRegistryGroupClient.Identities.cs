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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Client
{
    public sealed partial class WotRegistryGroupClient
    {
        /// <summary>
        /// Creates a TD Resource/Version from its exact ThingId using the generated typed Method.
        /// </summary>
        public ValueTask<WotRegistryResourceAllocation> CreateThingDescriptionResourceAsync(
            string thingId,
            string versionId = "",
            bool requestFileOpen = false,
            CancellationToken ct = default)
        {
            RequireKind(WoTDocumentKindEnum.ThingDescription);
            return CreateDocumentResourceAsync(thingId, versionId, requestFileOpen, ct);
        }

        /// <summary>
        /// Creates a TM Resource/Version from its exact ModelId using the generated typed Method.
        /// </summary>
        public ValueTask<WotRegistryResourceAllocation> CreateThingModelResourceAsync(
            string modelId,
            string versionId = "",
            bool requestFileOpen = false,
            CancellationToken ct = default)
        {
            RequireKind(WoTDocumentKindEnum.ThingModel);
            return CreateDocumentResourceAsync(modelId, versionId, requestFileOpen, ct);
        }

        /// <summary>
        /// Resolves or creates a TD Resource/Version with independent logical and Version creation flags.
        /// </summary>
        public ValueTask<(WotRegistryResourceAllocation Resource, bool CreatedResource, bool CreatedVersion)>
            GetOrCreateThingDescriptionResourceAsync(
                string thingId,
                string versionId = "",
                bool requestFileOpen = false,
                CancellationToken ct = default)
        {
            RequireKind(WoTDocumentKindEnum.ThingDescription);
            return GetOrCreateDocumentResourceAsync(thingId, versionId, requestFileOpen, ct);
        }

        /// <summary>
        /// Resolves or creates a TM Resource/Version with independent logical and Version creation flags.
        /// </summary>
        public ValueTask<(WotRegistryResourceAllocation Resource, bool CreatedResource, bool CreatedVersion)>
            GetOrCreateThingModelResourceAsync(
                string modelId,
                string versionId = "",
                bool requestFileOpen = false,
                CancellationToken ct = default)
        {
            RequireKind(WoTDocumentKindEnum.ThingModel);
            return GetOrCreateDocumentResourceAsync(modelId, versionId, requestFileOpen, ct);
        }

        /// <summary>
        /// Creates using this receiver's established document kind. Empty VersionId always
        /// requests a new Version; returned identifiers are the server's committed allocation.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<WotRegistryResourceAllocation> CreateDocumentResourceAsync(
            string sourceId,
            string versionId = "",
            bool requestFileOpen = false,
            CancellationToken ct = default)
        {
            await WotProvisioningContract.VerifyResourceAsync(Session, GroupNodeId, Kind, false, ct)
                .ConfigureAwait(false);
            (NodeId logical, NodeId version, string resourceId, string assignedVersion, uint handle) =
                Proxy switch
                {
                    ThingModelGroupTypeClient tm => await tm
                        .CreateThingModelResourceAsync(sourceId, versionId, requestFileOpen, ct).ConfigureAwait(false),
                    ThingDescriptionGroupTypeClient td => await td
                        .CreateThingDescriptionResourceAsync(sourceId, versionId, requestFileOpen, ct)
                        .ConfigureAwait(false),
                    _ => throw new ServiceResultException(
                        StatusCodes.BadNotSupported, "A typed group proxy is required.")
                };
            return Allocation(logical, version, resourceId, assignedVersion, handle, pending: true);
        }

        /// <summary>
        /// Resolves or creates using this receiver's kind. Empty VersionId selects an existing default.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<(WotRegistryResourceAllocation Resource, bool CreatedResource, bool CreatedVersion)>
            GetOrCreateDocumentResourceAsync(
                string sourceId,
                string versionId = "",
                bool requestFileOpen = false,
                CancellationToken ct = default)
        {
            await WotProvisioningContract.VerifyResourceAsync(Session, GroupNodeId, Kind, true, ct)
                .ConfigureAwait(false);
            (NodeId logical, NodeId version, string resourceId, string assignedVersion,
                uint handle, bool createdResource, bool createdVersion) =
                Proxy switch
                {
                    ThingModelGroupTypeClient tm => await tm
                        .GetOrCreateThingModelResourceAsync(sourceId, versionId, requestFileOpen, ct)
                        .ConfigureAwait(false),
                    ThingDescriptionGroupTypeClient td => await td
                        .GetOrCreateThingDescriptionResourceAsync(sourceId, versionId, requestFileOpen, ct)
                        .ConfigureAwait(false),
                    _ => throw new ServiceResultException(
                        StatusCodes.BadNotSupported, "A typed group proxy is required.")
                };
            WotRegistryResourceAllocation result = Allocation(
                logical, version, resourceId, assignedVersion, handle, createdVersion);
            if (!createdVersion && await result.Version.HasContentAsync(ct).ConfigureAwait(false) == false)
            {
                result.Version.MarkPendingStructuralVersion();
            }
            return (result, createdResource, createdVersion);
        }

        private WotRegistryResourceAllocation Allocation(
            NodeId logical,
            NodeId version,
            string resourceId,
            string versionId,
            uint fileHandle,
            bool pending)
        {
            if (logical.IsNull ||
                version.IsNull ||
                logical == version ||
                string.IsNullOrEmpty(resourceId) ||
                string.IsNullOrEmpty(versionId))
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError, "Typed provisioning returned invalid Resource/Version identities.");
            }
            return new WotRegistryResourceAllocation(
                OpenResourceClient(logical, resourceId, string.Empty, pendingStructuralVersion: false),
                OpenResourceClient(version, resourceId, versionId, pending),
                fileHandle);
        }

        private void RequireKind(WoTDocumentKindEnum kind)
        {
            if (Kind != kind)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument, "The typed resource Method does not match this group's kind.");
            }
        }
    }

    /// <summary>
    /// The server-assigned logical Resource, exact Version and optional session-owned write handle.
    /// Close a nonzero FileHandle on Version; it is not a logical-default file handle.
    /// </summary>
    public sealed class WotRegistryResourceAllocation
    {
        internal WotRegistryResourceAllocation(
            WotRegistryResourceClient logicalResource,
            WotRegistryResourceClient version,
            uint fileHandle)
        {
            LogicalResource = logicalResource;
            Version = version;
            FileHandle = fileHandle;
        }

        /// <summary>
        /// Gets the stable logical Resource wrapper.
        /// </summary>
        public WotRegistryResourceClient LogicalResource { get; }

        /// <summary>
        /// Gets the exact Version wrapper.
        /// </summary>
        public WotRegistryResourceClient Version { get; }

        /// <summary>
        /// Gets the exact-Version write handle, or zero when no file was requested.
        /// </summary>
        public uint FileHandle { get; }
    }
}
