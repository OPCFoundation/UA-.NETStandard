/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Onboarding;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Retains push enrollment through an explicit secret store and the running GDS's approved request service.
    /// One instance exclusively owns its scope across all processes. The selected store must publish complete
    /// values durably before returning. Uncertain CSR and request creation require operator recovery.
    /// </summary>
    public sealed class SecretStoreDeviceRegistrarPushEnrollmentProvider :
        IDeviceRegistrarPushCertificateEnrollmentProvider,
        IDisposable
    {
        /// <summary>
        /// Borrows an initialized GDS, its lifecycle providers and durable secret store.
        /// The namespace-URI group address must identify one of that GDS's existing certificate authorities.
        /// </summary>
        public SecretStoreDeviceRegistrarPushEnrollmentProvider(
            ApplicationsNodeManager manager,
            ISecretStore secrets,
            string scope,
            ExpandedNodeId certificateGroupId,
            NodeId certificateTypeId,
            IDeviceRegistrarIdentityLifecycle candidates,
            IDeviceRegistrarCertificateLifecycle devices,
            ITelemetryContext telemetry,
            IDeviceRegistrarExternalAuthenticationProvider? externalAuthentication = null)
            : this(secrets, scope, certificateGroupId, certificateTypeId, candidates, devices, telemetry,
                externalAuthentication)
        {
            Bind(manager);
        }

        /// <summary>
        /// Defers attachment to the exact GDS created by the hosting service.
        /// </summary>
        internal SecretStoreDeviceRegistrarPushEnrollmentProvider(
            ISecretStore secrets,
            string scope,
            ExpandedNodeId certificateGroupId,
            NodeId certificateTypeId,
            IDeviceRegistrarIdentityLifecycle candidates,
            IDeviceRegistrarCertificateLifecycle devices,
            ITelemetryContext telemetry,
            IDeviceRegistrarExternalAuthenticationProvider? externalAuthentication = null)
        {
            m_secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            m_candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
            m_devices = devices ?? throw new ArgumentNullException(nameof(devices));
            m_externalAuthentication = externalAuthentication;
            if (string.IsNullOrWhiteSpace(scope))
            {
                throw new ArgumentException("An exclusive durable enrollment scope is required.", nameof(scope));
            }
            if (certificateGroupId.IsNull || certificateGroupId.ServerIndex != 0 ||
                string.IsNullOrEmpty(certificateGroupId.NamespaceUri) || certificateTypeId.IsNull ||
                certificateTypeId.NamespaceIndex != 0)
            {
                throw new ArgumentException(
                    "A namespace-URI group and standard concrete certificate type are required.");
            }
            m_scope = scope;
            m_groupId = certificateGroupId;
            m_typeId = certificateTypeId;
            m_context = ServiceMessageContext.Create(telemetry ?? throw new ArgumentNullException(nameof(telemetry)));
        }

        /// <inheritdoc/>
        public async ValueTask<DeviceRegistrarPushEnrollment?> FindAsync(
            string productInstanceUri,
            NodeId applicationId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            SecretIdentifier identifier = GetIdentifier(productInstanceUri, applicationId);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using ISecret? secret = await m_secrets.GetAsync(identifier, cancellationToken).ConfigureAwait(false);
                if (secret == null)
                {
                    return null;
                }
                Checkpoint checkpoint = Decode(secret.Bytes, productInstanceUri, applicationId);
                return checkpoint.Enrollment;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Gets the retained queue identifier for administrative approval, or a null NodeId before queue retention.
        /// Uncertain queue insertion must be resolved from the GDS request database, not retried.
        /// </summary>
        public async ValueTask<NodeId> FindRequestAsync(
            string productInstanceUri,
            NodeId applicationId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            SecretIdentifier identifier = GetIdentifier(productInstanceUri, applicationId);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using ISecret? secret = await m_secrets.GetAsync(identifier, cancellationToken).ConfigureAwait(false);
                return secret == null ? NodeId.Null : Decode(secret.Bytes, productInstanceUri, applicationId).RequestId;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Attaches independently recovered original evidence to an uncertain transition without creating a key
        /// or submitting another request. CSR recovery requires a null request identifier; uncertain queue
        /// insertion requires its original unaccepted request with exactly the retained CSR, group and type.
        /// </summary>
        public async ValueTask RecoverAsync(
            OnboardingIdentitySelection identity,
            NodeId applicationId,
            ByteString certificateRequest,
            NodeId requestId = default,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }
            if (certificateRequest.IsEmpty)
            {
                throw new ArgumentException("The original device CSR is required.", nameof(certificateRequest));
            }
            ByteString csr = ByteString.From(certificateRequest.Span);
            SecretIdentifier identifier = GetIdentifier(identity.ProductInstanceUri, applicationId);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await RequireApprovalAsync(identity, applicationId, cancellationToken).ConfigureAwait(false);
                using ISecret? secret = await m_secrets.GetAsync(identifier, cancellationToken).ConfigureAwait(false);
                if (secret == null)
                {
                    throw new ServiceResultException(StatusCodes.BadNotFound);
                }
                Checkpoint checkpoint = Decode(secret.Bytes, identity.ProductInstanceUri, applicationId);
                RequireIdentity(checkpoint, identity);
                if ((checkpoint.Stage != Stage.CreatingCsr || !requestId.IsNull) &&
                    (checkpoint.Stage != Stage.StartingRequest || requestId.IsNull || checkpoint.Csr != csr))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "Recovery must retain the original uncertain transition.");
                }
                await RequireManager().ValidatePushSigningRecoveryAsync(
                    applicationId, m_groupId, checkpoint.CertificateTypeId, csr, requestId, cancellationToken)
                    .ConfigureAwait(false);
                await RequireApprovalAsync(identity, applicationId, cancellationToken).ConfigureAwait(false);
                checkpoint = checkpoint with
                {
                    Stage = requestId.IsNull ? Stage.CsrRetained : Stage.RequestRetained,
                    Csr = csr,
                    RequestId = requestId
                };
                await SaveAsync(identifier, checkpoint, applicationId, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public ValueTask<DeviceRegistrarPushEnrollment> GetOrCreateAsync(
            OnboardingIdentitySelection identity,
            NodeId applicationId,
            Func<CancellationToken, ValueTask<ByteString>> createSigningRequest,
            CancellationToken cancellationToken = default)
        {
            return GetOrCreateAsync(identity, applicationId, m_typeId, createSigningRequest, cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask<DeviceRegistrarPushEnrollment> GetOrCreateAsync(
            OnboardingIdentitySelection identity,
            NodeId applicationId,
            NodeId certificateTypeId,
            Func<CancellationToken, ValueTask<ByteString>> createSigningRequest,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (certificateTypeId.IsNull || certificateTypeId.NamespaceIndex != 0)
            {
                throw new ArgumentException(
                    "A standard concrete certificate type is required.", nameof(certificateTypeId));
            }
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }
            if (createSigningRequest == null)
            {
                throw new ArgumentNullException(nameof(createSigningRequest));
            }
            if (!identity.IsExternalAuthentication &&
                (string.IsNullOrEmpty(identity.EncodedTicket) ||
                identity.MatchingTicket is not (DeviceIdentityTicketType or CompositeIdentityTicketType)))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "Push enrollment requires the validated signed identity ticket.");
            }
            SecretIdentifier identifier = GetIdentifier(identity.ProductInstanceUri, applicationId);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ApplicationsNodeManager manager = RequireManager();
                await RequireApprovalAsync(identity, applicationId, cancellationToken).ConfigureAwait(false);
                using ISecret? existing = await m_secrets.GetAsync(identifier, cancellationToken).ConfigureAwait(false);
                Checkpoint checkpoint;
                if (existing == null)
                {
                    manager.ValidatePushCertificateSelection(applicationId, m_groupId, certificateTypeId);
                    checkpoint = new(Stage.CreatingCsr, identity, default, NodeId.Null, null, certificateTypeId);
                    await SaveAsync(identifier, checkpoint, applicationId, cancellationToken).ConfigureAwait(false);
                    ByteString csr = await createSigningRequest(cancellationToken).ConfigureAwait(false);
                    if (csr.IsEmpty)
                    {
                        throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                    }
                    checkpoint = checkpoint with { Stage = Stage.CsrRetained, Csr = ByteString.From(csr.Span) };
                    await SaveAsync(identifier, checkpoint, applicationId, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    checkpoint = Decode(existing.Bytes, identity.ProductInstanceUri, applicationId);
                    RequireIdentity(checkpoint, identity);
                    if (checkpoint.CertificateTypeId != certificateTypeId)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed,
                            "The retained push certificate type cannot be replaced.");
                    }
                }
                if (checkpoint.Stage is Stage.CreatingCsr or Stage.StartingRequest)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "A push CSR or request creation outcome is uncertain; recover its original evidence.");
                }
                if (checkpoint.Stage == Stage.CsrRetained)
                {
                    checkpoint = checkpoint with { Stage = Stage.StartingRequest };
                    await SaveAsync(identifier, checkpoint, applicationId, cancellationToken).ConfigureAwait(false);
                    await RequireApprovalAsync(identity, applicationId, cancellationToken).ConfigureAwait(false);
                    NodeId requestId = await manager.StartPushSigningRequestAsync(
                        applicationId, m_groupId, checkpoint.CertificateTypeId, checkpoint.Csr, cancellationToken)
                        .ConfigureAwait(false);
                    checkpoint = checkpoint with { Stage = Stage.RequestRetained, RequestId = requestId };
                    await SaveAsync(identifier, checkpoint, applicationId, cancellationToken).ConfigureAwait(false);
                }
                if (checkpoint.Stage == Stage.RequestRetained)
                {
                    await RequireApprovalAsync(identity, applicationId, cancellationToken).ConfigureAwait(false);
                    DeviceRegistrarPushEnrollment enrollment = await manager.CompletePushSigningRequestAsync(
                        checkpoint.Identity, applicationId, checkpoint.RequestId,
                        m_groupId, checkpoint.CertificateTypeId, checkpoint.Csr,
                        m_candidates, m_devices, cancellationToken)
                        .ConfigureAwait(false);
                    await RequireApprovalAsync(identity, applicationId, cancellationToken).ConfigureAwait(false);
                    checkpoint = checkpoint with { Stage = Stage.Completed, Enrollment = enrollment };
                    await SaveAsync(identifier, checkpoint, applicationId, cancellationToken).ConfigureAwait(false);
                }
                return checkpoint.Enrollment ?? throw new InvalidDataException("The push enrollment is incomplete.");
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Releases serialization after the host has stopped all push attempts.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) == 0)
            {
                m_gate.Dispose();
            }
        }

        /// <summary>
        /// Attaches one actual GDS without permitting another host to reinterpret retained request identifiers.
        /// </summary>
        internal void Bind(ApplicationsNodeManager manager)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            ApplicationsNodeManager? existing = Interlocked.CompareExchange(ref m_manager, manager, null);
            if (existing != null && !ReferenceEquals(existing, manager))
            {
                throw new InvalidOperationException("Push enrollment is already bound to another GDS.");
            }
        }

        /// <summary>
        /// Requires current persisted software approval and that the application is either the original
        /// DCA application or a managed application recorded for the confirmed, non-singleton device.
        /// </summary>
        private async ValueTask RequireApprovalAsync(
            OnboardingIdentitySelection identity,
            NodeId applicationId,
            CancellationToken ct)
        {
            if (identity.IsExternalAuthentication)
            {
                DeviceRegistrarExternalAuthenticationResult external =
                    await (m_externalAuthentication ??
                        throw new ServiceResultException(StatusCodes.BadConfigurationError))
                        .EvaluateAsync(identity.SelectedIdentity, ct).ConfigureAwait(false) ??
                    throw new ServiceResultException(StatusCodes.BadCertificateUntrusted);
                if (external.ProductInstanceUri != identity.ProductInstanceUri)
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                }
                DeviceRegistrarDevice device = await m_devices.FindDeviceAsync(
                    identity.ProductInstanceUri, ct).ConfigureAwait(false) ??
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                RequireManager().ValidateExternalDcaApplication(
                    device.DcaApplicationId, external.Decision.Application);
                if (external.Decision.RequiresSoftwareUpdate && !device.SoftwareStatus)
                {
                    throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
                }
            }
            await ApplicationsNodeManager.RequirePushApprovalAsync(
                identity, applicationId, m_candidates, m_devices, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Rejects replacement of the original authenticated identity and signed ticket envelopes.
        /// </summary>
        private static void RequireIdentity(Checkpoint checkpoint, OnboardingIdentitySelection identity)
        {
            if (checkpoint.Identity.SelectedIdentity != identity.SelectedIdentity ||
                checkpoint.Identity.IsExternalAuthentication != identity.IsExternalAuthentication ||
                checkpoint.Identity.EncodedTicket != identity.EncodedTicket ||
                checkpoint.Identity.EncodedComposite != identity.EncodedComposite)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The retained push identity cannot be replaced.");
            }
        }

        /// <summary>
        /// Publishes a bounded complete checkpoint before exposing the next externally visible effect.
        /// </summary>
        private async ValueTask SaveAsync(
            SecretIdentifier identifier,
            Checkpoint checkpoint,
            NodeId applicationId,
            CancellationToken ct)
        {
            byte[] encoded = Encode(checkpoint, applicationId);
            await m_secrets.SetAsync(identifier, encoded, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Encodes the original signed identity envelopes, CSR, URI-based request identity and approved trust output.
        /// </summary>
        private byte[] Encode(Checkpoint checkpoint, NodeId applicationId)
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, m_context, leaveOpen: true))
            {
                encoder.WriteUInt32(null, checkpoint.Identity.IsExternalAuthentication ? 2u : 1u);
                encoder.WriteString(null, m_scope);
                encoder.WriteString(null, m_groupId.ToString());
                encoder.WriteNodeId(null, checkpoint.CertificateTypeId);
                encoder.WriteString(null, EncodeId(applicationId));
                encoder.WriteByte(null, (byte)checkpoint.Stage);
                encoder.WriteString(null, checkpoint.Identity.ProductInstanceUri);
                encoder.WriteByteString(null, checkpoint.Identity.SelectedIdentity);
                encoder.WriteString(null, checkpoint.Identity.EncodedTicket);
                encoder.WriteString(null, checkpoint.Identity.EncodedComposite);
                if (!checkpoint.Identity.IsExternalAuthentication)
                {
                    BaseTicketType ticket = checkpoint.Identity.MatchingTicket;
                    encoder.WriteBoolean(null, ticket is CompositeIdentityTicketType);
                    encoder.WriteEncodeable(null, ticket);
                }
                encoder.WriteByteString(null, checkpoint.Csr);
                encoder.WriteString(null, checkpoint.RequestId.IsNull ? string.Empty : EncodeId(checkpoint.RequestId));
                if (checkpoint.Enrollment is { } enrollment)
                {
                    encoder.WriteString(null, enrollment.CertificateGroupId);
                    encoder.WriteString(null, enrollment.CertificateTypeId);
                    encoder.WriteEncodeable(null, enrollment.TrustList);
                }
            }
            if (stream.Length > kMaximumCheckpointSize)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            return stream.ToArray();
        }

        /// <summary>
        /// Rejects substituted scope, identity, profile, incomplete stages and trailing checkpoint bytes.
        /// </summary>
        private Checkpoint Decode(ReadOnlySpan<byte> bytes, string productInstanceUri, NodeId applicationId)
        {
            if (bytes.Length > kMaximumCheckpointSize)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var decoder = new BinaryDecoder(stream, m_context);
            uint version = decoder.ReadUInt32(null);
            if ((version != 1 && version != 2) || decoder.ReadString(null) != m_scope ||
                decoder.ReadString(null) != m_groupId.ToString())
            {
                throw new InvalidDataException("The push enrollment checkpoint belongs to another scope or profile.");
            }
            NodeId certificateTypeId = decoder.ReadNodeId(null);
            if (certificateTypeId.IsNull || certificateTypeId.NamespaceIndex != 0 ||
                decoder.ReadString(null) != EncodeId(applicationId))
            {
                throw new InvalidDataException(
                    "The push enrollment checkpoint belongs to another application or type.");
            }
            byte stageCode = decoder.ReadByte(null);
            var stage = (Stage)stageCode;
            string product = decoder.ReadString(null) ??
                throw new InvalidDataException("The push enrollment product identity is missing.");
            ByteString selected = decoder.ReadByteString(null);
            string ticketEnvelope = decoder.ReadString(null) ??
                throw new InvalidDataException("The push enrollment ticket is missing.");
            string compositeEnvelope = decoder.ReadString(null) ??
                throw new InvalidDataException("The push enrollment composite envelope is missing.");
            OnboardingIdentitySelection identity;
            if (version == 2)
            {
                if (ticketEnvelope.Length != 0 || compositeEnvelope.Length != 0)
                {
                    throw new InvalidDataException("External authentication has no ticket envelope.");
                }
                identity = OnboardingIdentitySelection.FromExternalAuthentication(selected, product);
            }
            else
            {
                BaseTicketType ticket = decoder.ReadBoolean(null)
                    ? decoder.ReadEncodeable<CompositeIdentityTicketType>(null)
                    : decoder.ReadEncodeable<DeviceIdentityTicketType>(null);
                identity = new OnboardingIdentitySelection(
                    selected, ticket, product, ticketEnvelope, compositeEnvelope);
            }
            ByteString csr = decoder.ReadByteString(null);
            string request = decoder.ReadString(null) ??
                throw new InvalidDataException("The push enrollment request identity is missing.");
            NodeId requestId = request.Length == 0 ? NodeId.Null :
                ExpandedNodeId.ToNodeId(ExpandedNodeId.Parse(request), RequireManager().PushNamespaceUris);
            DeviceRegistrarPushEnrollment? enrollment = null;
            if (stage == Stage.Completed)
            {
                enrollment = new DeviceRegistrarPushEnrollment(
                    identity, applicationId, requestId, decoder.ReadString(null)!, decoder.ReadString(null)!,
                    decoder.ReadEncodeable<TrustListDataType>(null), csr);
            }
            if (product != productInstanceUri || (version == 1 && string.IsNullOrEmpty(ticketEnvelope)) ||
                stageCode > (byte)Stage.Completed ||
                (stageCode >= (byte)Stage.CsrRetained && csr.IsEmpty) ||
                (stageCode >= (byte)Stage.RequestRetained && requestId.IsNull) ||
                (stageCode < (byte)Stage.RequestRetained && !requestId.IsNull) ||
                (stage == Stage.CreatingCsr && !csr.IsEmpty) ||
                decoder.Position != stream.Length ||
                (enrollment != null &&
                    (string.IsNullOrEmpty(enrollment.CertificateGroupId) ||
                    string.IsNullOrEmpty(enrollment.CertificateTypeId) ||
                    enrollment.TrustList.SpecifiedLists != (uint)TrustListMasks.All)))
            {
                throw new InvalidDataException("The push enrollment checkpoint is inconsistent.");
            }
            return new Checkpoint(stage, identity, csr, requestId, enrollment, certificateTypeId);
        }

        /// <summary>
        /// Creates a scope-bound key with length-prefixed fields and namespace URI application identity.
        /// </summary>
        private SecretIdentifier GetIdentifier(string productInstanceUri, NodeId applicationId)
        {
            if (!Uri.TryCreate(productInstanceUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException(
                    "An absolute product instance URI is required.", nameof(productInstanceUri));
            }
            string application = EncodeId(applicationId);
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(m_scope);
                writer.Write(productInstanceUri);
                writer.Write(application);
            }
            return new SecretIdentifier(
                "PushEnrollment-" + Convert.ToBase64String(SHA256.HashData(stream.ToArray())), m_secrets.StoreType);
        }

        /// <summary>
        /// Uses namespace URIs rather than retaining host-specific application/request namespace indices.
        /// </summary>
        private string EncodeId(NodeId id)
        {
            NamespaceTable namespaces = RequireManager().PushNamespaceUris;
            if (id.IsNull || string.IsNullOrEmpty(namespaces.GetString(id.NamespaceIndex)))
            {
                throw new ArgumentException("A local registered identifier is required.", nameof(id));
            }
            return NodeId.ToExpandedNodeId(id, namespaces).ToString();
        }

        /// <summary>
        /// Requires attachment to the running GDS before any journal access.
        /// </summary>
        private ApplicationsNodeManager RequireManager()
        {
            return Volatile.Read(ref m_manager) ?? throw new ServiceResultException(
                StatusCodes.BadServerHalted, "The push enrollment provider has not been attached to its GDS.");
        }

        /// <summary>
        /// Rejects new use after the owner has stopped and released the provider.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(SecretStoreDeviceRegistrarPushEnrollmentProvider));
            }
        }

        /// <summary>
        /// Persisted stages fence uncertain external effects before CSR creation and queue insertion.
        /// </summary>
        private enum Stage : byte
        {
            /// <summary>
            /// CSR creation may have begun; never automatically repeat it.
            /// </summary>
            CreatingCsr,

            /// <summary>
            /// The exact returned CSR is durable and has not been submitted.
            /// </summary>
            CsrRetained,

            /// <summary>
            /// Queue insertion may have begun; its request identifier requires explicit recovery.
            /// </summary>
            StartingRequest,

            /// <summary>
            /// The exact shared GDS request is durable and awaits approval or receipt recovery.
            /// </summary>
            RequestRetained,

            /// <summary>
            /// The immutable receipt and full trust output have been retained.
            /// </summary>
            Completed
        }

        /// <summary>
        /// One detached device/application enrollment transition.
        /// </summary>
        private sealed record Checkpoint(
            Stage Stage,
            OnboardingIdentitySelection Identity,
            ByteString Csr,
            NodeId RequestId,
            DeviceRegistrarPushEnrollment? Enrollment,
            NodeId CertificateTypeId);

        /// <summary>The maximum complete binary journal value.</summary>
        private const int kMaximumCheckpointSize = 16 * 1024 * 1024;

        /// <summary>The host-owned durable storage.</summary>
        private readonly ISecretStore m_secrets;

        /// <summary>The exclusive deployment scope.</summary>
        private readonly string m_scope;

        /// <summary>The existing authority selected by namespace URI.</summary>
        private readonly ExpandedNodeId m_groupId;

        /// <summary>The selected standard concrete application certificate type.</summary>
        private readonly NodeId m_typeId;

        /// <summary>The persisted software approval gate.</summary>
        private readonly IDeviceRegistrarIdentityLifecycle m_candidates;

        /// <summary>The shared DCA ownership and application scope.</summary>
        private readonly IDeviceRegistrarCertificateLifecycle m_devices;

        /// <summary>The external issuer and approval boundary rechecked before request effects.</summary>
        private readonly IDeviceRegistrarExternalAuthenticationProvider? m_externalAuthentication;

        /// <summary>The bounded OPC UA checkpoint codec context.</summary>
        private readonly IServiceMessageContext m_context;

        /// <summary>Serializes one exclusive journal owner.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);

        /// <summary>The one running GDS that owns application and request identities.</summary>
        private ApplicationsNodeManager? m_manager;

        /// <summary>Whether the owning host released this provider.</summary>
        private int m_disposed;
    }
}
