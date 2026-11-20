// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Client
{
    /// <summary>
    /// High-level client for the WoT Connectivity 1.1 registry surface
    /// (the well-known <c>WoTRegistry</c> object hosted by the connected
    /// server, see the <c>xRegistry</c>/<c>WoTRegistryType</c> model).
    /// <para>
    /// As the WoT registry model subtypes the xRegistry base model, this is a
    /// domain client in the sense of <c>XRegistryClient</c>: it inherits
    /// the base registry lifecycle and adds WoT-specific group/resource
    /// resolution, a typed <c>Refresh</c> result and a dependency-ordered
    /// bulk-load workflow. It composes (does <strong>not</strong> inherit) the
    /// source-generated <see cref="WoTRegistryTypeClient"/> proxy, so the typed
    /// proxy is reused directly rather than re-resolved per call.
    /// </para>
    /// </summary>
    public sealed partial class WotRegistryClient
    {
        /// <summary>
        /// Creates a typed document group from its exact catalogue URI and returns the assigned identity.
        /// The generated Method is discovered and its complete argument and executable contract verified.
        /// No inherited-Method fallback discards source authority.
        /// </summary>
        /// <param name="ct">The cancellation token passed to the asynchronous operation.</param>
        /// <param name="kind">The wo t document kind enum.</param>
        /// <param name="catalogUri">The catalog URI.</param>
        public async ValueTask<WotRegistryGroupClient> CreateDocumentGroupAsync(
            WoTDocumentKindEnum kind,
            string catalogUri,
            CancellationToken ct = default)
        {
            WotDocumentKinds.RequireDocument(kind, nameof(kind));
            await WotProvisioningContract.VerifyGroupAsync(Session, RegistryNodeId, false, ct).ConfigureAwait(false);
            (NodeId nodeId, string assignedId) = await Proxy.CreateDocumentGroupAsync(kind, catalogUri, ct)
                .ConfigureAwait(false);
            return await OpenGroupClientAsync(Session, nodeId, assignedId, Telemetry, ct, usesDistinctHierarchy: true)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Atomically resolves or creates a typed document group by exact kind and catalogue URI.
        /// </summary>
        /// <param name="ct">The cancellation token passed to the asynchronous operation.</param>
        /// <param name="kind">The wo t document kind enum.</param>
        /// <param name="catalogUri">The catalog URI.</param>
        public async ValueTask<(WotRegistryGroupClient Group, bool Created)> GetOrCreateDocumentGroupAsync(
            WoTDocumentKindEnum kind,
            string catalogUri,
            CancellationToken ct = default)
        {
            WotDocumentKinds.RequireDocument(kind, nameof(kind));
            await WotProvisioningContract.VerifyGroupAsync(Session, RegistryNodeId, true, ct).ConfigureAwait(false);
            (NodeId nodeId, string assignedId, bool created) = await Proxy.GetOrCreateDocumentGroupAsync(
                kind, catalogUri, ct).ConfigureAwait(false);
            WotRegistryGroupClient group = await OpenGroupClientAsync(
                Session, nodeId, assignedId, Telemetry, ct, usesDistinctHierarchy: true).ConfigureAwait(false);
            return (group, created);
        }
    }
}
