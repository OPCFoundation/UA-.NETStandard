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
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;
#if !NET9_0_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace Opc.Ua.Server
{
    /// <summary>
    /// Method-handler surface of <see cref="ConfigurationNodeManager"/> for the ServerConfiguration
    /// Push methods (OPC 10000-12 §7.10.5-§7.10.10): <c>UpdateCertificate</c>,
    /// <c>CreateSelfSignedCertificate</c>, <c>DeleteCertificate</c>, <c>CreateSigningRequest</c>,
    /// <c>GetRejectedList</c>, <c>GetCertificates</c> and KeyCredential push binding. Code in this
    /// file stages work through <c>m_coordinator</c>, <c>m_pendingKeyStore</c> and <c>m_keyGenerator</c>
    /// and may read <c>m_certificateGroups</c>, <c>m_rejectedStore</c>/<c>m_rejectedStoreInstance</c>
    /// and <c>m_configuration</c>; it never applies changes itself.
    /// </summary>
    public partial class ConfigurationNodeManager
    {
        /// <inheritdoc/>
        public async ValueTask BindKeyCredentialPushAsync(
            KeyCredentialPushSubject subject,
            CancellationToken cancellationToken = default)
        {
            if (subject == null)
            {
                throw new ArgumentNullException(nameof(subject));
            }

            NodeState? node = FindPredefinedNode<NodeState>(
                KeyCredentialPushSubject.StandardConfigurationFolderNodeId) ??
                await Server.NodeManager
                    .FindNodeInAddressSpaceAsync(KeyCredentialPushSubject.StandardConfigurationFolderNodeId, cancellationToken)
                    .ConfigureAwait(false);

            if (node is not KeyCredentialConfigurationFolderState folder)
            {
                if (node is not BaseObjectState passiveNode)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdUnknown,
                        "The standard KeyCredentialConfiguration folder is not present.");
                }

                folder = new KeyCredentialConfigurationFolderState(passiveNode.Parent);
                folder.Create(SystemContext, passiveNode);
                passiveNode.Parent?.ReplaceChild(SystemContext, folder);
                await AddPredefinedNodeAsync(SystemContext, folder, cancellationToken)
                    .ConfigureAwait(false);
            }

            await subject.BindAsync(
                    folder,
                    SystemContext,
                    (state, ct) => AddPredefinedNodeAsync(SystemContext, state, ct),
                    async (state, ct) => await DeleteNodeAsync(SystemContext, state.NodeId, ct).ConfigureAwait(false),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private async ValueTask<UpdateCertificateMethodStateResult> UpdateCertificateAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            ByteString certificate,
            ArrayOf<ByteString> issuerCertificates,
            string? privateKeyFormat,
            ByteString privateKey,
            CancellationToken ct)
        {
            // §7.10.5: UpdateCertificate may transfer private-key material,
            // so it requires an encrypted SecureChannel.
            HasApplicationSecureAdminAccess(context, requireEncryptedChannel: true);

            // OPC 10000-12 §7.10.3: the private key is sensitive material;
            // it must not be persisted into the
            // CertificateUpdateRequested / CertificateUpdated audit events.
            // The audit payload still reflects the public-key certificate,
            // issuer chain and key format so administrators can correlate
            // the request without exposing the secret.
            ArrayOf<Variant> inputArguments =
            [
                certificateGroupId,
                certificateTypeId,
                certificate,
                issuerCertificates,
                privateKeyFormat!,
                AuditEvents.RedactedPrivateKey
            ];

            Server.ReportCertificateUpdateRequestedAuditEvent(
                context,
                objectId,
                method,
                inputArguments,
                m_logger);
            Certificate? newCert = null;
            CertificateCollection? newIssuerCollection = null;
            Certificate? newCertificateWithKey = null;
            Certificate? previousCertificateWithKey = null;
            try
            {
                if (certificate.IsEmpty)
                {
                    throw new ArgumentNullException(nameof(certificate));
                }

                privateKeyFormat = privateKeyFormat?.ToUpperInvariant();
                if (privateKeyFormat is not null and not "PEM" and not "PFX" and not "")
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        $"The private key format {privateKeyFormat} is not supported.");
                }

                ServerCertificateGroup certificateGroup = VerifyGroupAndTypeId(
                    certificateGroupId,
                    certificateTypeId);

                // OPC 10000-12 §7.10.5: "The Purpose of the associated
                // CertificateGroup determines the validation rules for
                // Certificate being updated."
                bool isApplicationCertificateGroup = IsApplicationCertificateGroup(certificateGroup);

                NodeId sessionId = GetSessionId(context);
                await AcquireTransactionOwnershipAsync(sessionId, ct).ConfigureAwait(false);
                m_coordinator.ValidateSessionCanParticipate(sessionId);

                try
                {
                    newCert = Certificate.FromRawData(certificate);
                }
                catch
                {
                    throw new ServiceResultException(
                        StatusCodes.BadCertificateInvalid,
                        "Certificate data is invalid.");
                }

                // validate certificate type of new certificate
                if (!CertificateIdentifier.ValidateCertificateType(newCert, certificateTypeId))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadCertificateInvalid,
                        "Certificate type of new certificate doesn't match the provided certificate type.");
                }

                // identify the existing certificate to be updated
                // it should be of the same type and same subject name as the new certificate
                CertificateIdentifier existingCertIdentifier;
                CertificateIdentifier? subjectMatch = certificateGroup.ApplicationCertificates
                    .ToList()
                    .FirstOrDefault(cert =>
                        X509Utils.CompareDistinguishedName(cert.SubjectName!, newCert.Subject) &&
                        cert.CertificateType == certificateTypeId);

                if (subjectMatch != null)
                {
                    existingCertIdentifier = subjectMatch;
                }
                else if (m_configuration.CertificateManager is ICertificateRegistry registryFallback)
                {
                    // Subject changed mid-rotation: use the manager registry's
                    // currently-registered cert for this type to identify the
                    // configured identifier (matches by certificate type).
                    using (CertificateEntry? currentEntry = registryFallback
                        .AcquireApplicationCertificateByType(certificateTypeId))
                    {
                        if (currentEntry == null)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadInvalidArgument,
                                "No existing certificate found for the specified certificate type and subject name.");
                        }
                    }

                    existingCertIdentifier = certificateGroup.ApplicationCertificates
                        .ToList()
                        .FirstOrDefault(cert => cert.CertificateType == certificateTypeId) ??
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidArgument,
                            "No existing certificate found for the specified certificate type and subject name.");
                }
                else
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidArgument,
                        "No existing certificate found for the specified certificate type and subject name.");
                }

                newIssuerCollection = [];

                if (isApplicationCertificateGroup)
                {
                    // OPC 10000-12 §7.10.5: "If the CertificateGroup Purpose
                    // is ApplicationCertificateType, this list is redundant
                    // because the IssuerCertificates are already required
                    // to be in the associated TrustList, therefore the
                    // Server shall ignore this list." The caller-supplied
                    // issuerCertificates are therefore never parsed, staged,
                    // or imported for this group; newIssuerCollection stays
                    // empty. The Server instead validates newCert using the
                    // validation process defined in OPC 10000-4 against the
                    // group's own configured TrustList, which is
                    // authoritative, ignoring every suppressible validation
                    // error while still enforcing every other error.
                    try
                    {
                        await ValidateCertificateAgainstGroupTrustListAsync(
                            certificateGroup.TrustedStore,
                            certificateGroup.IssuerStore,
                            certificateGroup.BrowseName,
                            newCert,
                            m_configuration.SecurityConfiguration,
                            Server.Telemetry,
                            ct).ConfigureAwait(false);
                    }
                    catch (ServiceResultException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        m_logger.FailedToVerifyIntegrityAgainstTrustList(ex, newCert);
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed,
                            "Failed to verify integrity of the new certificate against the " +
                            "certificate group's TrustList.",
                            ex);
                    }
                }
                else
                {
                    try
                    {
                        // build issuer chain
                        foreach (ByteString issuerRawCert in issuerCertificates)
                        {
                            using Certificate issuerCertificate = Certificate.FromRawData(issuerRawCert);
                            newIssuerCollection.Add(issuerCertificate);
                        }
                    }
                    catch
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadCertificateInvalid,
                            "Issuer certificate data is invalid.");
                    }

                    // self signed
                    bool selfSigned = X509Utils.IsSelfSigned(newCert);
                    if (selfSigned && newIssuerCollection.Count != 0)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadCertificateInvalid,
                            "Issuer list not empty for self signed certificate.");
                    }

                    if (!selfSigned)
                    {
                        try
                        {
                            await ValidatePushCertificateAndIssuerChainAsync(
                                newCert,
                                newIssuerCollection,
                                m_configuration.SecurityConfiguration,
                                Server.Telemetry,
                                ct).ConfigureAwait(false);
                        }
                        catch (ServiceResultException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            m_logger.FailedToVerifyIntegrityAndIssuerList(ex, newCert);
                            throw new ServiceResultException(
                                StatusCodes.BadSecurityChecksFailed,
                                "Failed to verify integrity of the new certificate and the issuer list.",
                                ex);
                        }
                    }
                }

                // Capture the pre-transaction certificate/private key
                // before any mutation (OPC UA Part 12 §7.10.2) so a
                // reverse-compensation rollback can restore it if a later
                // staged operation in this transaction fails to commit.
                ICertificatePasswordProvider? passwordProvider = m_configuration
                    .SecurityConfiguration
                    .CertificatePasswordProvider;
                string? previousThumbprint;
                if (m_configuration.CertificateManager is ICertificateRegistry registry)
                {
                    using CertificateEntry? currentEntry = registry
                        .AcquireApplicationCertificateByType(existingCertIdentifier.CertificateType);
                    previousThumbprint = currentEntry?.Certificate.Thumbprint
                        ?? existingCertIdentifier.Thumbprint;
                }
                else
                {
                    previousThumbprint = existingCertIdentifier.Thumbprint;
                }

                previousCertificateWithKey = await CertificateIdentifierResolver
                    .LoadPrivateKeyAsync(
                        existingCertIdentifier,
                        passwordProvider,
                        m_configuration.ApplicationUri,
                        Server.Telemetry,
                        ct)
                    .ConfigureAwait(false);

                try
                {
                    switch (privateKeyFormat)
                    {
                        case null:
                        case "":
                            PendingCertificateKeyContext pendingKeyContext =
                                CreatePendingKeyContext(certificateGroup, existingCertIdentifier);
                            Certificate? pendingKey = await m_pendingKeyStore
                                .TryTakeAsync(pendingKeyContext, ct).ConfigureAwait(false);

                            Certificate exportableKey;
                            if (pendingKey != null && X509Utils.VerifyKeyPair(newCert, pendingKey))
                            {
                                // The regenerated key from a matching
                                // CreateSigningRequest(regeneratePrivateKey:
                                // true) is consumed here.
                                exportableKey = pendingKey;
                            }
                            else
                            {
                                pendingKey?.Dispose();
                                // CA2000: exportableKey is disposed by the
                                // `using` immediately below; the analyzer
                                // cannot track disposal through the
                                // conditional (?:) assignment.
#pragma warning disable CA2000
                                if (previousCertificateWithKey == null)
                                {
                                    throw new ServiceResultException(
                                        StatusCodes.BadSecurityChecksFailed,
                                        "A private key was not found");
                                }

                                try
                                {
                                    exportableKey = X509Utils.CreateCopyWithPrivateKey(
                                        previousCertificateWithKey, false);
                                }
                                catch (CryptographicException ex)
                                {
                                    throw new ServiceResultException(
                                        StatusCodes.BadSecurityChecksFailed,
                                        "The existing private key is not extractable, so it " +
                                        "cannot be carried over to the new certificate. Request " +
                                        "a new key with CreateSigningRequest and " +
                                        "RegeneratePrivateKey set to true instead.",
                                        ex);
                                }
#pragma warning restore CA2000
                            }

                            using (exportableKey)
                            {
                                newCertificateWithKey = DefaultCertificateFactory.Instance
                                    .CreateWithPrivateKey(newCert, exportableKey);
                            }
                            break;
                        case "PFX":
                        {
#if !NET9_0_OR_GREATER
                            // https://github.com/OPCFoundation/UA-.NETStandard/commit/0b24d62b7c2bab2e5ed08e694103d49278e457af
                            // CopyWithPrivateKey apparently does not support ephemeral key sets on Windows
                            bool noEphemeralKeySet = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#else
                            // But it seems to work on .NET 9 - and we prefer that over files
                            const bool noEphemeralKeySet = false;
#endif
                            using Certificate certWithPrivateKey = X509Utils.CreateCertificateFromPKCS12(
                                privateKey.ToArray(),
                                passwordProvider?.GetPassword(existingCertIdentifier),
                                noEphemeralKeySet);
                            newCertificateWithKey = DefaultCertificateFactory.Instance
                                .CreateWithPrivateKey(newCert, certWithPrivateKey);
                            break;
                        }
                        case "PEM":
                            newCertificateWithKey = DefaultCertificateFactory.Instance
                                .CreateWithPEMPrivateKey(
                                    newCert,
                                    privateKey.ToArray(),
                                    passwordProvider?.GetPassword(existingCertIdentifier));
                            break;
                    }
                }
                catch (Exception ex) when (ex is not ServiceResultException)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed,
                        "Failed to verify integrity of the new certificate and the private key.", ex);
                }

                NodeId groupNodeId = certificateGroup.NodeId;
                Certificate stagedNewCert = newCertificateWithKey!;
                CertificateCollection stagedIssuers = newIssuerCollection;
                Certificate? stagedPreviousCert = previousCertificateWithKey;
                // Populated by CommitAsync with the thumbprints of exactly
                // the issuer certificates it newly adds (excluding any
                // already present in the issuer store); RollbackAsync only
                // ever runs after CommitAsync has fully completed (the
                // coordinator only reverse-compensates operations that
                // committed in full), so it always reads the value
                // CommitAsync wrote.
                ArrayOf<string> stagedNewlyAddedIssuerThumbprints = ArrayOf<string>.Empty;

                // CA2025: the coordinator guarantees CommitAsync/RollbackAsync
                // always complete (awaited to conclusion) before it invokes
                // DisposeStaged, so stagedNewCert/stagedIssuers/
                // stagedPreviousCert are never disposed while an operation
                // delegate is still using them; the analyzer cannot see
                // across that ordering contract.
#pragma warning disable CA2025
                m_coordinator.Stage(sessionId, new PushConfigurationOperation
                {
                    AffectedCertificateGroup = groupNodeId,
                    AffectedCertificateType = certificateTypeId,
                    CommitAsync = async ct2 =>
                    {
                        stagedNewlyAddedIssuerThumbprints = await ApplyCertificateSlotChangeAsync(
                            certificateGroup,
                            existingCertIdentifier,
                            previousThumbprint,
                            stagedNewCert,
                            stagedIssuers,
                            ct2,
                            stagedPreviousCert).ConfigureAwait(false);
                        if (stagedPreviousCert != null)
                        {
                            RegisterPendingRotation(certificateTypeId, stagedPreviousCert);
                        }

                        Server.ReportCertificateUpdatedAuditEvent(
                            context,
                            objectId,
                            method,
                            inputArguments,
                            certificateGroupId,
                            certificateTypeId,
                            m_logger);
                    },
                    RollbackAsync = async ct2 =>
                    {
                        await ApplyCertificateSlotChangeAsync(
                            certificateGroup,
                            existingCertIdentifier,
                            stagedNewCert.Thumbprint,
                            stagedPreviousCert,
                            null,
                            ct2).ConfigureAwait(false);
                        // Remove exactly the issuers the commit above newly
                        // added, preserving every issuer that was already
                        // present in the store before this operation ran.
                        await RemoveIssuerCertificatesAsync(
                            certificateGroup,
                            stagedNewlyAddedIssuerThumbprints,
                            ct2).ConfigureAwait(false);
                    },
                    DisposeStaged = () =>
                    {
                        stagedNewCert.Dispose();
                        stagedIssuers.Dispose();
                        stagedPreviousCert?.Dispose();
                    }
                });
#pragma warning restore CA2025

                // Ownership of these transferred to the staged operation
                // above; clear the local handles so the outer finally does
                // not double-dispose them.
                newCertificateWithKey = null;
                newIssuerCollection = null;
                previousCertificateWithKey = null;
            }
            catch (Exception e)
            {
                // report the failure of UpdateCertificate via an audit event
                Server.ReportCertificateUpdatedAuditEvent(
                    context,
                    objectId,
                    method,
                    inputArguments,
                    certificateGroupId,
                    certificateTypeId,
                    m_logger,
                    e);
                // Raise audit certificate event
                Server.ReportAuditCertificateEvent(newCert!, e, m_logger);
                throw;
            }
            finally
            {
                newCert?.Dispose();
                // Disposed only when ownership was not transferred to the
                // staged operation (i.e. an exception occurred before staging).
                newIssuerCollection?.Dispose();
                newCertificateWithKey?.Dispose();
                previousCertificateWithKey?.Dispose();
            }

            // §7.10.17: the staged operation started/continued the active
            // transaction, so refresh TransactionDiagnostics now (Result reads
            // Bad_InvalidState while active) rather than only at ApplyChanges.
            UpdateTransactionDiagnostics(context);

            return new UpdateCertificateMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                ApplyChangesRequired = true
            };
        }

        /// <summary>
        /// Creates a new self-signed certificate per OPC 10000-12 §7.10.6.
        /// The server generates a key pair internally, builds a self-signed
        /// certificate with the requested subject / DNS / IP and lifetime,
        /// asynchronously verifies the target slot is not occupied (a
        /// self-signed certificate never replaces an occupied slot; use
        /// <c>DeleteCertificate</c> first) - netted against every operation
        /// already staged in the active transaction (see
        /// <see cref="IsSlotOccupiedAsync"/>), so a <c>DeleteCertificate</c>
        /// staged earlier in the same transaction for this slot permits
        /// this call even though nothing has actually been removed from
        /// the store/registry yet - stages the new private-key
        /// certificate (also removing, and restoring on a later rollback,
        /// whatever the slot still genuinely holds live in that case),
        /// and returns the DER-encoded public certificate.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<CreateSelfSignedCertificateMethodStateResult>
            CreateSelfSignedCertificateAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            ArrayOf<string> dnsNames,
            ArrayOf<string> ipAddresses,
            ushort lifetimeInDays,
            ushort keySizeInBits,
            CancellationToken cancellationToken)
        {
            // §7.10.6: CreateSelfSignedCertificate does not transfer a
            // private key, so an authenticated SecureChannel is sufficient.
            HasApplicationSecureAdminAccess(context, requireEncryptedChannel: false);

            ServerCertificateGroup certificateGroup = VerifyGroupAndTypeId(
                certificateGroupId,
                certificateTypeId);

            // merge DNS names and IP addresses into one domain list. OPC
            // 10000-12 §7.10.6 requires at least one non-empty entry
            // across both lists, regardless of certificate type.
            var domainNames = new List<string>();
            if (!dnsNames.IsNull)
            {
                foreach (string dns in dnsNames)
                {
                    if (!string.IsNullOrEmpty(dns))
                    {
                        domainNames.Add(dns);
                    }
                }
            }
            if (!ipAddresses.IsNull)
            {
                foreach (string ip in ipAddresses)
                {
                    if (!string.IsNullOrEmpty(ip))
                    {
                        domainNames.Add(ip);
                    }
                }
            }

            if (domainNames.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "At least one DNS name or IP address must be provided.");
            }

            bool isHttpsCertificateType = certificateTypeId == ObjectTypeIds.HttpsCertificateType;
            if (string.IsNullOrEmpty(subjectName))
            {
                if (isHttpsCertificateType)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidArgument,
                        "SubjectName must be provided for HTTPS certificate types.");
                }

                // OPC 10000-12 §7.10.6/§7.10.21: for ApplicationCertificateTypes
                // the SubjectName may be omitted; the Server creates a
                // suitable default based on the Server's ApplicationIdentity.
                subjectName = CreateDefaultApplicationCertificateSubjectName(m_configuration.ApplicationName);
            }
            else if (isHttpsCertificateType && !SubjectCommonNameMatchesDomain(subjectName, domainNames))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "For HTTPS certificate types the SubjectName common name must match a " +
                    "supplied DNS name or IP address.");
            }

            // OPC 10000-12 §7.10.6: "keySizeInBits ... The CertificateTypeId
            // limits the values that may be set." Validated before invoking
            // the builder so an unsupported value is reported as
            // Bad_OutOfRange rather than a raw ArgumentException from the
            // certificate builder (or silently accepted for ECC types).
            bool isRsaCertificateType = certificateTypeId.IsNull ||
                certificateTypeId == ObjectTypeIds.ApplicationCertificateType ||
                certificateTypeId == ObjectTypeIds.RsaMinApplicationCertificateType ||
                certificateTypeId == ObjectTypeIds.RsaSha256ApplicationCertificateType;
            ValidateKeySizeForCertificateType(certificateTypeId, isRsaCertificateType, keySizeInBits);

            NodeId sessionId = GetSessionId(context);
            await AcquireTransactionOwnershipAsync(sessionId, cancellationToken).ConfigureAwait(false);
            m_coordinator.ValidateSessionCanParticipate(sessionId);

            CertificateIdentifier existingCertIdentifier =
                FindCertificateIdentifier(certificateGroup, certificateTypeId);

            // OPC 10000-12 §7.10.6: never replace an occupied slot;
            // DeleteCertificate is the standard mechanism to empty one.
            // Netted against every operation already staged in this
            // transaction, so a DeleteCertificate staged earlier for this
            // same slot permits this call to proceed.
            (bool occupied, string? previousThumbprint) = await IsSlotOccupiedAsync(
                certificateGroup.NodeId, existingCertIdentifier, cancellationToken).ConfigureAwait(false);
            if (occupied)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "The certificate slot is already occupied. Use DeleteCertificate to empty it first.");
            }

            // The slot may still genuinely hold a certificate on disk/the
            // registry even though it is not occupied above: an earlier
            // DeleteCertificate staged for this same slot in this
            // transaction nets it as unoccupied without having actually
            // removed anything from the store yet. Capture that
            // certificate now so this operation can restore it - instead
            // of just discarding the newly created one and leaving the
            // slot empty - if a later staged operation in the same
            // transaction fails and this one must be rolled back.
            Certificate? previousCertificateWithKey = string.IsNullOrEmpty(previousThumbprint)
                ? null
                : await CertificateIdentifierResolver
                    .LoadPrivateKeyAsync(
                        existingCertIdentifier,
                        m_configuration.SecurityConfiguration.CertificatePasswordProvider,
                        m_configuration.ApplicationUri,
                        Server.Telemetry,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (lifetimeInDays == 0)
            {
                lifetimeInDays = CertificateFactory.DefaultLifeTime;
            }

            DateTime utcToday = m_timeProvider.GetUtcNow().UtcDateTime.Date;
            ICertificateBuilder builder = s_certificateFactory
                .CreateApplicationCertificate(
                    m_configuration.ApplicationUri!,
                    m_configuration.ApplicationName!,
                    subjectName,
                    [.. domainNames])
                .SetNotBefore(utcToday.AddDays(-1))
                .SetNotAfter(utcToday.AddDays(lifetimeInDays));

            Certificate certificateWithKey;
            if (isRsaCertificateType)
            {
                ushort keySize = keySizeInBits > 0
                    ? keySizeInBits
                    : CertificateFactory.DefaultKeySize;
                certificateWithKey = builder.SetRSAKeySize(keySize).CreateForRSA();
            }
            else
            {
                ECCurve? curve =
                    CryptoUtils.GetCurveFromCertificateTypeId(certificateTypeId)
                    ?? throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "The ECC certificate type is not supported.");
                certificateWithKey = builder.SetECCurve(curve.Value).CreateForECDsa();
            }

            ByteString certBytes;
            try
            {
                certBytes = certificateWithKey.RawData.ToByteString();

                m_logger.StagedSelfSignedCertificate(
                    certificateWithKey.Subject,
                    certificateGroupId,
                    certificateTypeId);

                NodeId groupNodeId = certificateGroup.NodeId;
                Certificate stagedNewCert = certificateWithKey;
                Certificate? stagedPreviousCert = previousCertificateWithKey;
                // CA2025: the coordinator guarantees CommitAsync/RollbackAsync
                // complete before DisposeStaged runs; see the identical
                // suppression in UpdateCertificateAsync for the full
                // rationale.
#pragma warning disable CA2025
                m_coordinator.Stage(sessionId, new PushConfigurationOperation
                {
                    AffectedCertificateGroup = groupNodeId,
                    AffectedCertificateType = certificateTypeId,
                    CommitAsync = async ct =>
                    {
                        await ApplyCertificateSlotChangeAsync(
                            certificateGroup,
                            existingCertIdentifier,
                            previousThumbprint,
                            stagedNewCert,
                            null,
                            ct,
                            stagedPreviousCert).ConfigureAwait(false);
                        if (stagedPreviousCert != null)
                        {
                            RegisterPendingRotation(certificateTypeId, stagedPreviousCert);
                        }
                    },
                    // Mirrors the commit's before/after roles: when this
                    // slot genuinely held stagedPreviousCert live (a
                    // DeleteCertificate staged earlier in this same
                    // transaction had netted the slot as unoccupied without
                    // having actually removed it yet), restore it instead
                    // of leaving the slot empty; otherwise there was
                    // nothing live to restore.
                    RollbackAsync = ct => ApplyCertificateSlotChangeAsync(
                        certificateGroup,
                        existingCertIdentifier,
                        stagedNewCert.Thumbprint,
                        stagedPreviousCert,
                        null,
                        ct),
                    DisposeStaged = () =>
                    {
                        stagedNewCert.Dispose();
                        stagedPreviousCert?.Dispose();
                    }
                });
#pragma warning restore CA2025

                // Ownership transferred to the staged operation above;
                // clear the local handle so the finally below does not
                // double-dispose it.
                previousCertificateWithKey = null;
            }
            catch
            {
                certificateWithKey.Dispose();
                throw;
            }
            finally
            {
                // Disposed only when ownership was not transferred to the
                // staged operation (i.e. an exception occurred before staging).
                previousCertificateWithKey?.Dispose();
            }

            // The slot's future content no longer comes from a pending
            // signing request; discard any pending regenerated key for it.
            await m_pendingKeyStore
                .RemoveAsync(CreatePendingKeyContext(certificateGroup, existingCertIdentifier), cancellationToken)
                .ConfigureAwait(false);

            // §7.10.17: refresh TransactionDiagnostics now the operation is
            // staged so Result reads Bad_InvalidState while the transaction
            // is active.
            UpdateTransactionDiagnostics(context);

            return new CreateSelfSignedCertificateMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Certificate = certBytes
            };
        }

        /// <summary>
        /// Deletes the certificate occupying a certificate group/type slot
        /// per OPC 10000-12 §7.10.7. Unlike <c>CreateSelfSignedCertificate</c>,
        /// this is the standard mechanism for emptying an occupied slot; it
        /// always requires <c>ApplyChanges</c> to take effect. The
        /// occupied-slot check is netted against every operation already
        /// staged in the active transaction (see <see cref="IsSlotOccupiedAsync"/>),
        /// so a <c>CreateSelfSignedCertificate</c> staged earlier in the
        /// same transaction for this slot permits this call even though
        /// nothing has actually been added to the store/registry yet.
        /// </summary>
        private async ValueTask<DeleteCertificateMethodStateResult> DeleteCertificateAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            CancellationToken cancellationToken)
        {
            // §7.10.7: DeleteCertificate requires an authenticated (but not
            // necessarily encrypted) SecureChannel.
            HasApplicationSecureAdminAccess(context, requireEncryptedChannel: false);

            ServerCertificateGroup certificateGroup = VerifyGroupAndTypeId(
                certificateGroupId,
                certificateTypeId);

            NodeId sessionId = GetSessionId(context);
            await AcquireTransactionOwnershipAsync(sessionId, cancellationToken).ConfigureAwait(false);
            m_coordinator.ValidateSessionCanParticipate(sessionId);

            CertificateIdentifier existingCertIdentifier =
                FindCertificateIdentifier(certificateGroup, certificateTypeId);

            (bool occupied, string? previousThumbprint) = await IsSlotOccupiedAsync(
                certificateGroup.NodeId, existingCertIdentifier, cancellationToken).ConfigureAwait(false);
            if (!occupied)
            {
                // OPC 10000-12 §7.10.7: "If no Certificate is assigned to
                // the CertificateType slot then a Bad_InvalidState error is
                // returned."
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "The certificate slot is already empty.");
            }

            // Conservative net-state safety check applied at staging time so
            // the administrator gets immediate feedback: deleting every
            // application-certificate slot (netting the live registry against
            // everything already staged in this transaction) is rejected up
            // front. The authoritative OPC 10000-12 §7.10.7 endpoint-reference
            // determination happens later, during ApplyChanges preparation
            // (see the operation's PrepareAsync below).
            EnsureCertificateNotSoleEndpointReference(certificateTypeId);

            ICertificatePasswordProvider? passwordProvider = m_configuration
                .SecurityConfiguration
                .CertificatePasswordProvider;
            Certificate? previousCertificateWithKey = await CertificateIdentifierResolver
                .LoadPrivateKeyAsync(
                    existingCertIdentifier,
                    passwordProvider,
                    m_configuration.ApplicationUri,
                    Server.Telemetry,
                    cancellationToken)
                .ConfigureAwait(false);

            NodeId groupNodeId = certificateGroup.NodeId;
            Certificate? stagedPreviousCert = previousCertificateWithKey;
            m_coordinator.Stage(sessionId, new PushConfigurationOperation
            {
                AffectedCertificateGroup = groupNodeId,
                AffectedCertificateType = certificateTypeId,
                LeavesCertificateSlotEmpty = true,
                // OPC 10000-12 §7.10.7: "Certificates that are referenced by
                // EndpointDescriptions shall not be deleted. This
                // determination happens when ApplyChanges is called." Because
                // a delete-then-create/update for the same slot coalesces to
                // the later operation, only a delete that survives to commit
                // (i.e. genuinely leaves the slot empty) reaches this check.
                PrepareAsync = _ =>
                {
                    EnsureCertificateNotEndpointReferenced(previousThumbprint);
                    return Task.CompletedTask;
                },
                CommitAsync = async ct =>
                {
                    await ApplyCertificateSlotChangeAsync(
                        certificateGroup,
                        existingCertIdentifier,
                        previousThumbprint,
                        null,
                        null,
                        ct).ConfigureAwait(false);
                    if (stagedPreviousCert != null)
                    {
                        RegisterPendingRotation(certificateTypeId, stagedPreviousCert);
                    }
                },
                RollbackAsync = stagedPreviousCert == null
                    ? null
                    : ct => ApplyCertificateSlotChangeAsync(
                        certificateGroup,
                        existingCertIdentifier,
                        null,
                        stagedPreviousCert,
                        null,
                        ct),
                DisposeStaged = () => stagedPreviousCert?.Dispose()
            });

            await m_pendingKeyStore
                .RemoveAsync(CreatePendingKeyContext(certificateGroup, existingCertIdentifier), cancellationToken)
                .ConfigureAwait(false);

            // §7.10.17: refresh TransactionDiagnostics now the operation is
            // staged so Result reads Bad_InvalidState while the transaction
            // is active.
            UpdateTransactionDiagnostics(context);

            return new DeleteCertificateMethodStateResult
            {
                ServiceResult = ServiceResult.Good
            };
        }

        private async ValueTask<CreateSigningRequestMethodStateResult> CreateSigningRequestAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            bool regeneratePrivateKey,
            ByteString nonce,
            CancellationToken cancellationToken)
        {
            // §7.10.10: CreateSigningRequest may return a regenerated key's
            // signing request, so it requires an encrypted SecureChannel.
            HasApplicationSecureAdminAccess(context, requireEncryptedChannel: true);

            ServerCertificateGroup certificateGroup = VerifyGroupAndTypeId(
                certificateGroupId,
                certificateTypeId);

            // OPC 10000-12 §7.10.10: when a new private key is regenerated the
            // caller must supply at least 32 bytes of additional entropy in
            // the Nonce. An invalid Nonce is reported as Bad_InvalidArgument
            // and leaves all state unchanged.
            if (regeneratePrivateKey && (nonce.IsNull || nonce.Length < kMinimumRegenerateNonceLength))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "The Nonce must be at least 32 bytes long when regeneratePrivateKey is true.");
            }

            // OPC 10000-12 §7.10.10: while a transaction is active, only
            // its owning Session may regenerate the pending key, since a
            // second Session's ApplyChanges/CancelChanges could otherwise
            // race the pending-key lifecycle.
            NodeId sessionId = GetSessionId(context);
            m_coordinator.ValidateSessionCanParticipate(sessionId);

            CertificateIdentifier existingCertIdentifier =
                FindCertificateIdentifier(certificateGroup, certificateTypeId);

            // Look up the currently-active certificate via the manager
            // registry — the configured identifier is metadata only. The
            // acquired entry is disposed at method scope; the borrowed
            // certificate is only read.
            using CertificateEntry? currentEntry =
                (m_configuration.CertificateManager as ICertificateRegistry)?
                    .AcquireApplicationCertificateByType(certificateTypeId);
            Certificate? currentCert = currentEntry?.Certificate;

            if (string.IsNullOrEmpty(subjectName))
            {
                subjectName = (currentCert?.Subject ?? existingCertIdentifier.SubjectName)!;
            }

            PendingCertificateKeyContext pendingKeyContext =
                CreatePendingKeyContext(certificateGroup, existingCertIdentifier);

            Certificate certWithPrivateKey;
            if (regeneratePrivateKey)
            {
                ArrayOf<string> domainNames = currentCert != null
                    ? X509Utils.GetDomainsFromCertificate(currentCert)
                    : default;

                certWithPrivateKey = GenerateTemporaryApplicationCertificate(
                    certificateTypeId,
                    subjectName,
                    domainNames,
                    nonce,
                    cancellationToken);

                // A repeated signing request replaces (and disposes) any
                // previously pending key for this slot (§7.10.10).
                if (!await m_pendingKeyStore
                    .SaveAsync(pendingKeyContext, certWithPrivateKey, cancellationToken)
                    .ConfigureAwait(false))
                {
                    certWithPrivateKey.Dispose();
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "Secure persistence of the regenerated private key is not supported " +
                        "for this certificate store.");
                }
            }
            else
            {
                ICertificatePasswordProvider? passwordProvider = m_configuration
                    .SecurityConfiguration
                    .CertificatePasswordProvider;
                certWithPrivateKey = await CertificateIdentifierResolver
                    .LoadPrivateKeyAsync(
                        existingCertIdentifier,
                        passwordProvider,
                        m_configuration.ApplicationUri,
                        Server.Telemetry,
                        cancellationToken)
                    .ConfigureAwait(false) ??
                    throw ServiceResultException.Create(StatusCodes.BadInternalError, "Failed to load private key");

                // No regenerated key accompanies this request; discard any
                // previously pending one so a later UpdateCertificate does
                // not pick up a stale key.
                await m_pendingKeyStore.RemoveAsync(pendingKeyContext, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                m_logger.CreateSigningRequest(certWithPrivateKey);
                var certificateRequest = ByteString.From(s_certificateFactory.CreateSigningRequest(
                    certWithPrivateKey,
                    X509Utils.GetDomainsFromCertificate(certWithPrivateKey).ToArray()));

                return new CreateSigningRequestMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    CertificateRequest = certificateRequest
                };
            }
            finally
            {
                certWithPrivateKey.Dispose();
            }
        }

        /// <summary>
        /// Generates the temporary application certificate and private key
        /// for a <c>CreateSigningRequest</c> regenerate-key request
        /// (OPC 10000-12 §7.10.10), delegating to the injected
        /// <see cref="IPushCertificateKeyGenerator"/> so the caller-supplied
        /// <paramref name="additionalEntropy"/> is genuinely mixed into the
        /// new private key.
        /// </summary>
        private Certificate GenerateTemporaryApplicationCertificate(
            NodeId certificateTypeId,
            string subjectName,
            ArrayOf<string> domainNames,
            ByteString additionalEntropy,
            CancellationToken cancellationToken)
        {
            DateTime utcToday = m_timeProvider.GetUtcNow().UtcDateTime.Date;
            return m_keyGenerator.CreateApplicationCertificate(
                new PushCertificateKeyGenerationRequest
                {
                    CertificateTypeId = certificateTypeId,
                    ApplicationUri = m_configuration.ApplicationUri!,
                    ApplicationName = m_configuration.ApplicationName!,
                    SubjectName = subjectName,
                    DomainNames = domainNames,
                    KeySizeInBits = 0,
                    NotBefore = utcToday.AddDays(-1),
                    NotAfter = utcToday.AddDays(14),
                    AdditionalEntropy = additionalEntropy
                },
                cancellationToken);
        }

        private ServiceResult GetRejectedList(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ref ArrayOf<ByteString> certificates)
        {
            // GetRejectedList returns only public certificates, so an
            // authenticated SecureChannel is sufficient.
            HasApplicationSecureAdminAccess(context, requireEncryptedChannel: false);

            // No rejected store configured
            if (m_rejectedStore == null)
            {
                certificates = [];
                return StatusCodes.Good;
            }

            ICertificateStore store = GetRejectedStore();
            using CertificateCollection collection = store.EnumerateAsync()
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            var rawList = new List<ByteString>();
            foreach (Certificate cert in collection)
            {
                rawList.Add(cert.RawData.ToByteString());
            }
            certificates = rawList.ToArrayOf();

            return StatusCodes.Good;
        }

        /// <summary>
        /// Returns the rejected-certificate store instance, opening it on
        /// first use. The instance is held open for the node manager's
        /// lifetime (its parsed-certificate cache is reused across reads,
        /// refreshed by the store itself when the backing data changes) and
        /// disposed by <see cref="Dispose(bool)"/>.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The store could not be opened.
        /// </exception>
        private ICertificateStore GetRejectedStore()
        {
            ICertificateStore? store = Volatile.Read(ref m_rejectedStoreInstance);
            if (store != null)
            {
                return store;
            }

            ICertificateStore created = m_rejectedStore!.OpenStore(Server.Telemetry) ??
                throw ServiceResultException.ConfigurationError(
                    "Failed to open rejected certificate store.");
            ICertificateStore? current = Interlocked.CompareExchange(
                ref m_rejectedStoreInstance,
                created,
                null);
            if (current != null)
            {
                created.Dispose();
                return current;
            }
            return created;
        }

        private ServiceResult GetCertificates(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId certificateGroupId,
            ref ArrayOf<NodeId> certificateTypeIds,
            ref ArrayOf<ByteString> certificates)
        {
            // GetCertificates returns only public certificates, so an
            // authenticated SecureChannel is sufficient.
            HasApplicationSecureAdminAccess(context, requireEncryptedChannel: false);

            ServerCertificateGroup certificateGroup = VerifyGroupId(certificateGroupId);

            // Look up each certificate via the manager registry so the
            // returned blobs reflect the currently-active cert (the
            // configured identifier carries no Certificate cache).
            var registry = m_configuration.CertificateManager as ICertificateRegistry;
            (certificateTypeIds, certificates) = SelectOccupiedCertificateSlots(
                certificateGroup.ApplicationCertificates,
                certificateType => registry?.AcquireApplicationCertificateByType(certificateType));

            return ServiceResult.Good;
        }
    }
}
