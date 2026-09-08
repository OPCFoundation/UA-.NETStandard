/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Certificate-slot bookkeeping of <see cref="ConfigurationNodeManager"/>: resolving and
    /// occupancy-checking the application-certificate slots of a certificate group, staging a slot
    /// replacement with rollback, issuer import cleanup, rotation registration and endpoint-reference
    /// guards. Code in this file mutates <see cref="ServerCertificateGroup.ApplicationCertificates"/>
    /// entries, records rotations via <c>m_activeRotationCollector</c>, and acquires transaction
    /// ownership through <c>m_coordinator</c>.
    /// </summary>
    public partial class ConfigurationNodeManager
    {
        /// <summary>
        /// Finds the configured <see cref="CertificateIdentifier"/> for
        /// <paramref name="certificateTypeId"/> within <paramref name="certificateGroup"/>.
        /// The identifier is metadata-only (store path/type and
        /// certificate type); it may or may not currently resolve to a
        /// certificate on disk.
        /// </summary>
        private static CertificateIdentifier FindCertificateIdentifier(
            ServerCertificateGroup certificateGroup,
            NodeId certificateTypeId)
        {
            return certificateGroup.ApplicationCertificates
                .ToList()
                .FirstOrDefault(cert => cert.CertificateType == certificateTypeId)
                ?? throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "Certificate type not valid for certificate group.");
        }

        /// <summary>
        /// Asynchronously determines whether <paramref name="existingCertIdentifier"/>'s
        /// slot currently resolves to a certificate. The certificate
        /// manager's registry (keyed purely by configured certificate
        /// type, consistent with <c>GetCertificates</c>/<c>UpdateCertificate</c>
        /// elsewhere in this class) is authoritative when available;
        /// resolving directly against the store is used only as a
        /// fallback, since store resolution re-validates the certificate's
        /// cryptographic properties against the certificate type and would
        /// otherwise disagree with the registry for perfectly valid
        /// configurations. Used by <c>CreateSelfSignedCertificate</c> (OPC
        /// 10000-12 §7.10.6: never replace an occupied slot) and by
        /// <c>DeleteCertificate</c> (the slot must be occupied).
        /// </summary>
        /// <remarks>
        /// <para>
        /// The live occupancy above is netted against every operation
        /// already staged (but not yet committed) in the active
        /// transaction for this exact (<paramref name="certificateGroupId"/>,
        /// <c>existingCertIdentifier.CertificateType</c>) slot, via
        /// <see cref="IPushConfigurationTransactionCoordinator.GetStagedOperations"/>,
        /// so a later request in the same transaction is validated
        /// against the cumulative effect of every earlier request against
        /// this slot - not just the live state as it appeared before any
        /// staging began. This permits, for example, staging
        /// <c>DeleteCertificate</c> followed by <c>CreateSelfSignedCertificate</c>
        /// for the same slot in one transaction (the slot nets as
        /// unoccupied even though the live delete has not committed yet),
        /// and makes <c>CreateSelfSignedCertificate</c> followed by
        /// <c>DeleteCertificate</c> see the slot as occupied (even though
        /// the live create has not committed yet either).
        /// </para>
        /// <para>
        /// Only the returned <c>Occupied</c> flag is netted this way;
        /// <c>Thumbprint</c> always reflects the real live state.
        /// <see cref="IPushConfigurationTransactionCoordinator.Stage"/>
        /// supersedes (and discards, without ever committing) whichever
        /// operation a new request for the same slot replaces, so the
        /// single operation left staged for this slot must still act
        /// against whatever is genuinely live on disk/registry once it
        /// commits; ordered operation semantics are preserved because at
        /// most one staged operation can ever match this exact slot.
        /// </para>
        /// </remarks>
        private async ValueTask<(bool Occupied, string? Thumbprint)> IsSlotOccupiedAsync(
            NodeId certificateGroupId,
            CertificateIdentifier existingCertIdentifier,
            CancellationToken cancellationToken)
        {
            bool occupied;
            string? thumbprint;
            if (m_configuration.CertificateManager is ICertificateRegistry registry)
            {
                using CertificateEntry? entry = registry
                    .AcquireApplicationCertificateByType(existingCertIdentifier.CertificateType);
                occupied = entry != null;
                thumbprint = entry?.Certificate.Thumbprint;
            }
            else
            {
                Certificate? resolved = await CertificateIdentifierResolver.ResolveAsync(
                    existingCertIdentifier,
                    registry: null,
                    needPrivateKey: false,
                    m_configuration.ApplicationUri,
                    Server.Telemetry,
                    cancellationToken).ConfigureAwait(false);

                if (resolved == null)
                {
                    occupied = false;
                    thumbprint = null;
                }
                else
                {
                    using (resolved)
                    {
                        occupied = true;
                        thumbprint = resolved.Thumbprint;
                    }
                }
            }

            foreach (PushConfigurationOperation staged in m_coordinator.GetStagedOperations())
            {
                if (staged.AffectedCertificateType.IsNull ||
                    !Utils.IsEqual(staged.AffectedCertificateType, existingCertIdentifier.CertificateType) ||
                    !Utils.IsEqual(staged.AffectedCertificateGroup, certificateGroupId))
                {
                    continue;
                }

                // Stage() supersedes (and disposes) any earlier operation
                // staged for this same (group, type) pair, so at most one
                // entry here can ever match; that single match always
                // reflects the net effect of every request already made
                // against this slot in this transaction.
                occupied = !staged.LeavesCertificateSlotEmpty;
            }

            return (occupied, thumbprint);
        }

        /// <summary>
        /// Builds the scope used to persist or retrieve the pending
        /// regenerated private key (§7.10.10) for a certificate group/type
        /// slot.
        /// </summary>
        private PendingCertificateKeyContext CreatePendingKeyContext(
            ServerCertificateGroup certificateGroup,
            CertificateIdentifier existingCertIdentifier)
        {
            var baseStore = new CertificateStoreIdentifier(
                existingCertIdentifier.StorePath ?? string.Empty,
                existingCertIdentifier.StoreType ?? string.Empty,
                noPrivateKeys: false);
            return new PendingCertificateKeyContext(
                baseStore,
                certificateGroup.NodeId,
                existingCertIdentifier.CertificateType,
                m_configuration.SecurityConfiguration.CertificatePasswordProvider,
                Server.Telemetry);
        }

        /// <summary>
        /// Reserves cross-replica ownership of the server-wide PushManagement
        /// transaction at an <see langword="await"/> boundary before the
        /// synchronous <see cref="IPushConfigurationTransactionCoordinator.Stage"/>
        /// call that follows. The default per-server coordinator does not
        /// implement <see cref="IPushConfigurationTransactionOwnershipGate"/>,
        /// so this is a no-op for the non-distributed server; a distributed
        /// coordinator acquires or renews a shared lease so only one replica
        /// owns the transaction at a time.
        /// </summary>
        private ValueTask AcquireTransactionOwnershipAsync(
            NodeId sessionId,
            CancellationToken cancellationToken)
        {
            return m_coordinator is IPushConfigurationTransactionOwnershipGate gate
                ? gate.AcquireTransactionOwnershipAsync(sessionId, cancellationToken)
                : default;
        }

        /// <summary>
        /// Applies a single certificate-group slot mutation: removes
        /// <paramref name="removeThumbprint"/> (if any) from the
        /// application store, adds <paramref name="addCertificateWithKey"/>
        /// (if any), imports <paramref name="addIssuerChain"/> into the
        /// group's issuer store (skipping any issuer certificate whose
        /// thumbprint is already present), and synchronizes the
        /// certificate manager's registry. This single primitive
        /// implements every staged certificate operation's commit AND
        /// rollback: a rollback simply invokes it with the before/after
        /// roles swapped (remove what commit added, restore what commit
        /// removed).
        /// </summary>
        /// <remarks>
        /// <para>
        /// The coordinator only compensates operations that commit in
        /// full (see <see cref="PushConfigurationTransactionCoordinator.ApplyChangesAsync(NodeId, CancellationToken)"/>);
        /// an operation whose own <c>CommitAsync</c> throws is excluded
        /// from that reverse-order compensation. When <paramref name="removedCertificateBackup"/>
        /// is supplied and the certificate removal above already
        /// succeeded, this method is therefore self-compensating: it
        /// restores <paramref name="removedCertificateBackup"/> before
        /// propagating a failure to add <paramref name="addCertificateWithKey"/>,
        /// so the slot is never left empty just because the replacement
        /// certificate could not be written.
        /// </para>
        /// <para>
        /// The returned thumbprints identify exactly the issuer
        /// certificates this call newly added (excluding any that were
        /// already present in the issuer store before it ran). A caller
        /// that later needs to compensate this import - either via a
        /// reverse-order rollback once a later staged operation in the
        /// same transaction fails to commit, or via its own self-
        /// compensation - should remove exactly those thumbprints (for
        /// example with <see cref="RemoveIssuerCertificatesAsync"/>), so a
        /// pre-existing issuer certificate is never removed just because
        /// it was also part of this call's issuer chain.
        /// </para>
        /// <para>
        /// This method is also self-compensating for the reverse ordering:
        /// when the application certificate slot above has already been
        /// fully swapped (both <paramref name="removeThumbprint"/> removed
        /// and <paramref name="addCertificateWithKey"/> added) and
        /// <paramref name="removedCertificateBackup"/> is supplied, a
        /// subsequent failure importing <paramref name="addIssuerChain"/>
        /// is compensated via <see cref="RestoreCertificateSlotAfterIssuerImportFailureAsync"/>
        /// before it propagates: the previous application certificate is
        /// restored and exactly the issuer certificates this call's import
        /// loop newly added so far are removed again, so a partial issuer
        /// import can never leave a newer application certificate live
        /// alongside orphaned or half-imported issuers.
        /// </para>
        /// </remarks>
        private async Task<ArrayOf<string>> ApplyCertificateSlotChangeAsync(
            ServerCertificateGroup certificateGroup,
            CertificateIdentifier existingCertIdentifier,
            string? removeThumbprint,
            Certificate? addCertificateWithKey,
            CertificateCollection? addIssuerChain,
            CancellationToken ct,
            Certificate? removedCertificateBackup = null)
        {
            bool removedCertificate = false;
            using (ICertificateStore? appStore = CertificateIdentifierResolver
                .OpenStore(existingCertIdentifier, Server.Telemetry))
            {
                if (appStore == null)
                {
                    throw ServiceResultException.ConfigurationError(
                        "Failed to open application certificate store.");
                }

                if (!string.IsNullOrEmpty(removeThumbprint))
                {
                    m_logger.DeleteApplicationCertificate(removeThumbprint);
                    await appStore.DeleteAsync(removeThumbprint!, ct).ConfigureAwait(false);
                    removedCertificate = true;
                }

                if (addCertificateWithKey != null)
                {
                    ICertificatePasswordProvider? passwordProvider = m_configuration
                        .SecurityConfiguration
                        .CertificatePasswordProvider;
                    try
                    {
                        m_logger.AddApplicationCertificate(addCertificateWithKey);
                        Debug.Assert(addCertificateWithKey.HasPrivateKey);
                        await appStore.AddAsync(
                            addCertificateWithKey,
                            passwordProvider?.GetPassword(existingCertIdentifier),
                            ct).ConfigureAwait(false);
                    }
                    catch (Exception) when (removedCertificate && removedCertificateBackup != null)
                    {
                        // This operation already removed the previous
                        // certificate above before this add failed; self-
                        // compensate by restoring it (see remarks) before
                        // the original exception propagates below.
                        try
                        {
                            await appStore.AddAsync(
                                removedCertificateBackup,
                                passwordProvider?.GetPassword(existingCertIdentifier),
                                ct).ConfigureAwait(false);
                            m_logger.RestoredPreviousCertificateAfterReplacementFailed(
                                existingCertIdentifier.CertificateType);
                        }
                        catch (Exception restoreException)
                        {
                            m_logger.FailedToRestorePreviousCertificateAfterReplacementFailed(
                                restoreException,
                                existingCertIdentifier.CertificateType);
                        }

                        throw;
                    }
                }
            }

            List<string>? newlyAddedIssuerThumbprints = null;
            if (addIssuerChain is { Count: > 0 })
            {
                using ICertificateStore issuerStore = certificateGroup.IssuerStore.OpenStore(Server.Telemetry);
                try
                {
                    foreach (Certificate issuer in addIssuerChain)
                    {
                        bool alreadyPresent;
                        using (CertificateCollection existingMatches = await issuerStore
                            .FindByThumbprintAsync(issuer.Thumbprint, ct).ConfigureAwait(false))
                        {
                            alreadyPresent = existingMatches.Count > 0;
                        }

                        try
                        {
                            await issuerStore.AddAsync(issuer, ct: ct).ConfigureAwait(false);
                        }
                        catch (ArgumentException)
                        {
                            // ignore error if issuer cert already exists
                            alreadyPresent = true;
                        }

                        if (!alreadyPresent)
                        {
                            (newlyAddedIssuerThumbprints ??= []).Add(issuer.Thumbprint);
                        }
                    }
                }
                catch (Exception)
                    when (removedCertificate && addCertificateWithKey != null && removedCertificateBackup != null)
                {
                    // The application certificate slot above was already
                    // fully swapped (the previous certificate removed and
                    // the new one added) before this issuer import failed;
                    // self-compensate by restoring the previous certificate
                    // and removing exactly the issuer certificates this
                    // loop newly added so far (preserving every issuer that
                    // was already present before it ran), before the
                    // original exception propagates below.
                    await RestoreCertificateSlotAfterIssuerImportFailureAsync(
                        certificateGroup,
                        existingCertIdentifier,
                        addCertificateWithKey,
                        removedCertificateBackup,
                        newlyAddedIssuerThumbprints?.ToArrayOf() ?? ArrayOf<string>.Empty,
                        ct).ConfigureAwait(false);

                    throw;
                }
            }

            if (addCertificateWithKey != null)
            {
                if (m_configuration.CertificateManager is ICertificateLifecycle lifecycle)
                {
                    using Certificate certOnly = Certificate.FromRawData(addCertificateWithKey.RawData);
                    await lifecycle.UpdateApplicationCertificateAsync(
                        existingCertIdentifier.CertificateType,
                        certOnly,
                        issuerChain: null,
                        ct).ConfigureAwait(false);
                }
            }
            else if (m_configuration.CertificateManager != null)
            {
                // DeleteCertificate / rollback-of-create leaves nothing to
                // register. ICertificateLifecycle exposes no direct
                // "unregister" primitive, so a reload re-derives the
                // registry from the security configuration's stores; the
                // now-missing certificate file naturally drops this
                // type's entry from the reloaded snapshot.
                await m_configuration.CertificateManager.UpdateAsync(
                    m_configuration.SecurityConfiguration,
                    m_configuration.ApplicationUri,
                    ct).ConfigureAwait(false);
            }

            return newlyAddedIssuerThumbprints?.ToArrayOf() ?? ArrayOf<string>.Empty;
        }

        /// <summary>
        /// Self-compensates a completed application-certificate slot swap
        /// (the previous certificate removed and <paramref name="committedCertificateWithKey"/>
        /// added in its place) once importing that certificate's issuer
        /// chain fails after the swap has already committed: deletes
        /// <paramref name="committedCertificateWithKey"/> from the
        /// application store, restores <paramref name="removedCertificateBackup"/>
        /// in its place, and removes exactly <paramref name="newlyAddedIssuerThumbprints"/>
        /// from the group's issuer store, preserving every issuer that was
        /// already present before the failed import ran.
        /// </summary>
        /// <remarks>
        /// Every step here is best-effort cleanup running after the
        /// triggering issuer-import failure; each stage is isolated so a
        /// failure restoring the application certificate does not prevent
        /// the issuer cleanup from being attempted, and any compensation
        /// failure is only logged (never thrown), so the caller's
        /// <see langword="throw"/> of the original import failure is never
        /// masked or replaced.
        /// </remarks>
        private async Task RestoreCertificateSlotAfterIssuerImportFailureAsync(
            ServerCertificateGroup certificateGroup,
            CertificateIdentifier existingCertIdentifier,
            Certificate committedCertificateWithKey,
            Certificate removedCertificateBackup,
            ArrayOf<string> newlyAddedIssuerThumbprints,
            CancellationToken ct)
        {
            try
            {
                using ICertificateStore? appStore = CertificateIdentifierResolver
                    .OpenStore(existingCertIdentifier, Server.Telemetry);
                if (appStore != null)
                {
                    await appStore.DeleteAsync(committedCertificateWithKey.Thumbprint, ct)
                        .ConfigureAwait(false);
                    ICertificatePasswordProvider? passwordProvider = m_configuration
                        .SecurityConfiguration
                        .CertificatePasswordProvider;
                    await appStore.AddAsync(
                        removedCertificateBackup,
                        passwordProvider?.GetPassword(existingCertIdentifier),
                        ct).ConfigureAwait(false);
                    m_logger.RestoredPreviousCertificateAfterIssuerImportFailed(
                        existingCertIdentifier.CertificateType);
                }
            }
            catch (Exception restoreException)
            {
                m_logger.FailedToRestorePreviousCertificateAfterIssuerImportFailed(
                    restoreException,
                    existingCertIdentifier.CertificateType);
            }

            // RemoveIssuerCertificatesAsync never throws (it logs and
            // continues per thumbprint); it is still awaited within its
            // own scope here so a hypothetical future change to that
            // contract can never mask the original issuer-import failure
            // this method was called to compensate.
            await RemoveIssuerCertificatesAsync(certificateGroup, newlyAddedIssuerThumbprints, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Removes each issuer certificate identified by <paramref name="thumbprints"/>
        /// from <paramref name="certificateGroup"/>'s issuer store.
        /// </summary>
        /// <remarks>
        /// Used to compensate the issuers a completed <c>UpdateCertificate</c>
        /// commit imported via <see cref="ApplyCertificateSlotChangeAsync"/>,
        /// so a reverse-order rollback (or self-compensation) removes
        /// exactly the issuer certificates that commit newly added and
        /// never a pre-existing issuer certificate. A failure to remove
        /// one thumbprint is logged and does not prevent the remaining
        /// thumbprints from being attempted, since these failures are
        /// best-effort cleanup after the more critical application
        /// certificate has already been restored by the caller.
        /// </remarks>
        private async Task RemoveIssuerCertificatesAsync(
            ServerCertificateGroup certificateGroup,
            ArrayOf<string> thumbprints,
            CancellationToken ct)
        {
            if (thumbprints.Count == 0)
            {
                return;
            }

            using ICertificateStore issuerStore = certificateGroup.IssuerStore.OpenStore(Server.Telemetry);
            // Indexed rather than foreach: ArrayOf<T>'s enumerator is a
            // ReadOnlySpan<T>.Enumerator (a ref struct), which cannot be
            // held across the await below.
            for (int i = 0; i < thumbprints.Count; i++)
            {
                string thumbprint = thumbprints[i];
                try
                {
                    await issuerStore.DeleteAsync(thumbprint, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.FailedToRemoveStagedIssuerCertificate(
                        ex,
                        thumbprint,
                        certificateGroup.NodeId);
                }
            }
        }

        /// <summary>
        /// Records that <paramref name="oldCertificateWithKey"/> was
        /// replaced or removed by a just-committed operation so
        /// <c>ApplyChanges</c> can force-close the SecureChannels that were
        /// negotiated against it (OPC UA Part 12 §7.10.9), once the whole
        /// transaction commits successfully. Takes its own reference; the
        /// caller's own copy is unaffected.
        /// </summary>
        /// <remarks>
        /// Adds to the collector that is flowed, through the ambient
        /// async call chain, by whichever call to <c>ApplyChanges</c> is
        /// currently running the coordinator's commit loop. This is
        /// deliberately NOT a single shared/global collection: a
        /// concurrent or duplicate <c>ApplyChanges</c> call that finds no
        /// active transaction (and so short-circuits with
        /// <see cref="StatusCodes.BadNothingToDo"/> without running any
        /// commit) never sees, and can therefore never drain or dispose,
        /// the rotations produced by another call's still-running
        /// successful commit.
        /// </remarks>
        private void RegisterPendingRotation(NodeId certificateType, Certificate oldCertificateWithKey)
        {
            List<PendingCertificateRotation>? collector = m_activeRotationCollector.Value;
            if (collector == null)
            {
                // Not reachable through the standard ApplyChanges method
                // handler, which always sets the collector before running
                // the coordinator's commit loop; nothing to correlate this
                // rotation with.
                return;
            }

            using Certificate rotationCopy = Certificate.FromRawData(oldCertificateWithKey.RawData);
            collector.Add(new PendingCertificateRotation
            {
                OldCertificate = rotationCopy.AddRef(),
                CertificateType = certificateType
            });
        }

        /// <summary>
        /// Conservative OPC 10000-12 §7.10.7 safety check for <c>DeleteCertificate</c>:
        /// rejects deleting the last remaining active application
        /// certificate across every certificate group, since every secure
        /// endpoint would then have no certificate to present.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is a deliberately conservative subset of the full "is this
        /// certificate the sole reference of an active endpoint" check,
        /// which would additionally require correlating each endpoint's
        /// configured SecurityPolicyUri to a specific certificate
        /// group/type. It never rejects a delete that the full check would
        /// allow, but a deployment that assigns different certificate
        /// groups to different endpoints could be more permissive than a
        /// full per-endpoint check.
        /// </para>
        /// <para>
        /// The live registry alone is not enough: staging one
        /// <c>DeleteCertificate</c> request per certificate type within
        /// the same transaction would otherwise pass this check
        /// individually for every request (none of the earlier staged
        /// deletes have actually been applied to the live registry yet)
        /// and still leave every certificate-group/type slot empty once
        /// <c>ApplyChanges</c> commits them all together. This check
        /// therefore nets the live registry against every certificate
        /// type already staged (but not yet committed) in the active
        /// transaction, via <see cref="IPushConfigurationTransactionCoordinator.GetStagedOperations"/>,
        /// before deciding whether this additional delete is safe.
        /// </para>
        /// </remarks>
        private void EnsureCertificateNotSoleEndpointReference(NodeId certificateTypeId)
        {
            if (m_configuration.CertificateManager is not ICertificateRegistry registry)
            {
                return;
            }

            var occupiedTypes = new HashSet<NodeId>();
            using (CertificateEntryCollection snapshot = registry.SnapshotApplicationCertificates())
            {
                foreach (CertificateEntry entry in snapshot)
                {
                    occupiedTypes.Add(entry.CertificateType);
                }
            }

            foreach (PushConfigurationOperation staged in m_coordinator.GetStagedOperations())
            {
                if (staged.AffectedCertificateType.IsNull)
                {
                    continue;
                }

                if (staged.LeavesCertificateSlotEmpty)
                {
                    occupiedTypes.Remove(staged.AffectedCertificateType);
                }
                else
                {
                    occupiedTypes.Add(staged.AffectedCertificateType);
                }
            }

            // The delete about to be staged removes this type too.
            occupiedTypes.Remove(certificateTypeId);

            if (occupiedTypes.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "Deleting this certificate would leave the server with no application certificate " +
                    "for any active endpoint.");
            }
        }

        /// <summary>
        /// OPC 10000-12 §7.10.7 endpoint-reference determination, evaluated
        /// during <c>ApplyChanges</c> preparation: rejects deleting a
        /// certificate that is still referenced by an active
        /// <see cref="EndpointDescription"/>. Because a delete that is
        /// superseded within the same transaction by a
        /// <c>CreateSelfSignedCertificate</c>/<c>UpdateCertificate</c> for the
        /// same slot never reaches commit (the operations coalesce), only a
        /// delete that genuinely empties the slot is checked here.
        /// </summary>
        /// <param name="deletedThumbprint">
        /// The thumbprint of the certificate the staged delete removes.
        /// </param>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadInvalidState"/> when the
        /// certificate is still referenced by an endpoint.
        /// </exception>
        private void EnsureCertificateNotEndpointReferenced(string? deletedThumbprint)
        {
            if (string.IsNullOrEmpty(deletedThumbprint))
            {
                return;
            }

            ArrayOf<EndpointDescription> endpoints =
                (Server as IServerEndpointRegistryProvider)?.ServerEndpoints ?? default;

            // Resolve the certificate each endpoint currently presents from the
            // active certificate registry rather than the EndpointDescription's
            // ServerCertificate blob captured at startup: after a successful
            // certificate rotation that blob may be stale, so the live registry
            // (keyed by the endpoint's SecurityPolicyUri, exactly as the channel
            // handshake resolves the presented certificate) is authoritative for
            // which certificate/type is presented at this moment. When no
            // registry is available (an external/mocked IServerInternal) the
            // endpoint's own blob is the only source and is used as a fallback.
            var registry = m_configuration.CertificateManager as ICertificateRegistry;

            if (IsCertificateReferencedByEndpoint(deletedThumbprint!, endpoints, registry, Server.Telemetry))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "The certificate is referenced by an EndpointDescription and cannot be deleted " +
                    "(OPC 10000-12 §7.10.7).");
            }
        }
    }
}
