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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Gds;
using Opc.Ua.Gds.Client;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using CertificateValidationOptions = Opc.Ua.Security.Certificates.CertificateValidationOptions;
using CertificateValidationResult = Opc.Ua.CertificateValidationResult;

namespace UaLens.Connection
{
    internal sealed record IdentityEnrollmentReview(string SourceId, string Subject, DateTimeOffset ExpiresAt)
    {
        public string GdsEndpoint { get; init; } = string.Empty;
        public string ApplicationUri { get; init; } = string.Empty;
        public NodeId ApplicationId { get; init; }
        public NodeId CertificateGroupId { get; init; }
        public NodeId CertificateTypeId { get; init; }
    }

    internal sealed record IdentityEnrollmentResult(string Subject, string Thumbprint, DateTime NotAfter);

    /// <summary>
    /// One dialog-owned enrollment. Preparation has no remote or store mutation;
    /// requesting and adopting are separate explicit, single-use decisions.
    /// </summary>
    internal interface IIdentityEnrollment : IAsyncDisposable
    {
        Task<IdentityEnrollmentReview> PrepareAsync(
            CertificateIdentityReference reference, UserTokenPolicy policy, CancellationToken cancellationToken);

        Task<IdentityEnrollmentResult> RequestAsync(
            IdentityEnrollmentReview review, IProgress<string>? progress, CancellationToken cancellationToken);

        Task<CertificateIdentityReference> AdoptAsync(
            IdentityEnrollmentResult result, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Renews a software-key user certificate through an already configured,
    /// securely connected GDS. It neither creates trust nor deletes the old key.
    /// Hardware enrollment requires the configured device's own enrollment adapter.
    /// </summary>
    internal sealed class GdsIdentityEnrollment : IIdentityEnrollment
    {
        public GdsIdentityEnrollment(
            ConfiguredCertificateSource source,
            IGlobalDiscoveryServerClient gds,
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            ICertificateValidatorEx validator,
            Func<ICertificateStore> openStore,
            ICertificateFactory? certificates = null,
            TimeProvider? timeProvider = null)
        {
            m_source = source ?? throw new ArgumentNullException(nameof(source));
            m_gds = gds ?? throw new ArgumentNullException(nameof(gds));
            if (applicationId.IsNull || certificateGroupId.IsNull || certificateTypeId.IsNull)
            {
                throw new ArgumentException("Configure exact application, certificate group and type identifiers.");
            }
            m_applicationId = applicationId;
            m_groupId = certificateGroupId;
            m_typeId = certificateTypeId;
            m_validator = validator ?? throw new ArgumentNullException(nameof(validator));
            m_openStore = openStore ?? throw new ArgumentNullException(nameof(openStore));
            m_certificates = certificates ?? DefaultCertificateFactory.Instance;
            m_time = timeProvider ?? TimeProvider.System;
        }

        public async Task<IdentityEnrollmentReview> PrepareAsync(
            CertificateIdentityReference reference, UserTokenPolicy policy, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(reference);
            ArgumentNullException.ThrowIfNull(policy);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), m_time);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, m_lifetime.Token, deadline.Token);
            await m_gate.WaitAsync(cancellation.Token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(m_closed, this);
                Clear();
                if (policy.TokenType != UserTokenType.Certificate ||
                    m_source.Purpose != CryptoPurpose.UserIdentityKey ||
                    m_source.CryptoProviderName is not null)
                {
                    throw new NotSupportedException(
                        "This enrollment adapter supports software-backed user keys, " +
                        "not application or hardware keys.");
                }
                reference.Validate();
                string securityPolicy = policy.SecurityPolicyUri ??
                    throw new ArgumentException("Resolve the selected endpoint's user-token security policy first.");
                m_source.RequireCryptoProvider(CryptoPurpose.UserIdentityKey, securityPolicy);
                ISession session = RequireGdsSession();
                CertificateIdentifier identifier = m_source.CreateIdentifier(reference);
                m_reference = reference;
                m_policy = CoreUtils.Clone(policy);
                m_store = identifier;
                m_password = m_source.ResolvePasswordSource(reference.PasswordSourceId);
                m_gdsSession = new WeakReference<ISession>(session);
                m_gdsSessionId = session.SessionId;
                m_gdsIdentity = session.Identity;
                m_endpointUrl = session.Endpoint.EndpointUrl;
                m_serverUri = session.Endpoint.Server?.ApplicationUri;
                m_channelPolicy = session.Endpoint.SecurityPolicyUri;
                m_channelCertificate = session.Endpoint.ServerCertificate.Copy();
                m_namespaceUris = [.. session.NamespaceUris.ToArray()];
                m_expires = m_time.GetUtcNow().AddMinutes(5);
                Certificate current = await m_source.Provider.GetPrivateKeyCertificateAsync(
                    identifier, m_password,
                    ct: cancellation.Token).ConfigureAwait(false) ??
                    throw new InvalidOperationException(
                        "The configured user certificate and private key are unavailable.");
                using (current)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (!current.HasPrivateKey)
                    {
                        throw new InvalidOperationException("A signing request requires the configured private key.");
                    }
                    ReferencedCertificateProvider.RequireMatchingReference(current, identifier);
                    ReferencedCertificateProvider.RequireIdentitySignature(current);
                    RequireFresh(m_expires);
                    m_registeredApplicationUri = await ReadApplicationUriAsync(cancellation.Token)
                        .ConfigureAwait(false);
                    IReadOnlyList<string> applicationUris = X509Utils.GetApplicationUrisFromCertificate(current);
                    if (applicationUris.Count != 1 || applicationUris[0] != m_registeredApplicationUri)
                    {
                        throw new InvalidOperationException(
                            "GDS renewal requires an existing key certificate with the registered ApplicationUri. " +
                            "Use the identity authority's enrollment adapter for other user certificates.");
                    }
                    m_request = ByteString.From(m_certificates.CreateSigningRequest(current));
                    if (m_request.Length is 0 or > 65536)
                    {
                        throw new InvalidOperationException("The signing request exceeds the enrollment bound.");
                    }
                    var request = new Pkcs10CertificationRequest(m_request.ToArray());
                    X509SubjectAltNameExtension? names = Pkcs10Utils.GetSubjectAltNameExtension(request.Attributes);
                    if (!request.Verify() ||
                        names is null ||
                        names.Uris.Count != 1 ||
                        names.Uris[0] != m_registeredApplicationUri)
                    {
                        throw new InvalidOperationException(
                            "The CSR does not prove the registered application binding.");
                    }
                    cancellation.Token.ThrowIfCancellationRequested();
                    RequireFresh(m_expires);
                    m_key = current.AddRef();
                    m_reference = reference with { Thumbprint = current.Thumbprint, SubjectName = current.Subject };
                    m_review = new IdentityEnrollmentReview(
                        m_source.Id, current.Subject, m_expires)
                    {
                        GdsEndpoint = new Uri(m_endpointUrl!).GetLeftPart(UriPartial.Path),
                        ApplicationUri = m_registeredApplicationUri,
                        ApplicationId = m_applicationId,
                        CertificateGroupId = m_groupId,
                        CertificateTypeId = m_typeId
                    };
                    return m_review;
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        public async Task<IdentityEnrollmentResult> RequestAsync(
            IdentityEnrollmentReview review, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), m_time);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, m_lifetime.Token, deadline.Token);
            await m_gate.WaitAsync(cancellation.Token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(m_closed, this);
                if (!ReferenceEquals(review, m_review) || m_key is null || m_reference is null)
                {
                    throw new InvalidOperationException("Prepare a new signing request before requesting enrollment.");
                }
                m_review = null;
                RequireFresh(review.ExpiresAt);
                if (await ReadApplicationUriAsync(cancellation.Token)
                    .ConfigureAwait(false) != m_registeredApplicationUri)
                {
                    throw new InvalidOperationException("The registered application changed. Prepare again.");
                }
                RequireFresh(review.ExpiresAt);
                progress?.Report("Submitting the reviewed signing request to the configured GDS.");
                NodeId requestId = await m_gds.StartSigningRequestAsync(
                    m_applicationId, m_groupId, m_typeId, m_request, cancellation.Token).ConfigureAwait(false);
                if (requestId.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadUnexpectedError, "GDS returned no request ID.");
                }
                using var polling = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, deadline.Token);
                for (int attempt = 0; attempt < 60; attempt++)
                {
                    RequireFresh(review.ExpiresAt);
                    ByteString certificate;
                    ByteString privateKey;
                    ArrayOf<ByteString> issuers;
                    try
                    {
                        (certificate, privateKey, issuers) = await m_gds.FinishRequestAsync(
                            m_applicationId, requestId, polling.Token).ConfigureAwait(false);
                    }
                    catch (ServiceResultException error) when (error.StatusCode == StatusCodes.BadNothingToDo)
                    {
                        progress?.Report("Waiting for GDS approval; cancel abandons only the local wait.");
                        await Task.Delay(TimeSpan.FromMilliseconds(500), m_time, polling.Token).ConfigureAwait(false);
                        continue;
                    }
                    polling.Token.ThrowIfCancellationRequested();
                    if (!privateKey.IsNull && privateKey.Length != 0)
                    {
                        throw new InvalidOperationException(
                            "A signing request unexpectedly returned private-key material.");
                    }
                    if (certificate.IsNull || certificate.Length == 0)
                    {
                        progress?.Report("Waiting for GDS approval; cancel abandons only the local wait.");
                        await Task.Delay(TimeSpan.FromMilliseconds(500), m_time, polling.Token).ConfigureAwait(false);
                        continue;
                    }
                    if (certificate.Length > 65536 || issuers.Count > 16)
                    {
                        throw new InvalidOperationException("The GDS certificate response exceeds its bounds.");
                    }
                    using var publicCertificate = new Certificate(certificate.Span);
                    if (!X509Utils.CompareDistinguishedName(publicCertificate.Subject, m_key.Subject) ||
                        publicCertificate.NotBefore.ToUniversalTime() > m_time.GetUtcNow().UtcDateTime ||
                        publicCertificate.NotAfter.ToUniversalTime() <= m_time.GetUtcNow().UtcDateTime)
                    {
                        throw new InvalidOperationException(
                            "The issued subject or validity does not match the request.");
                    }
                    ReferencedCertificateProvider.RequireIdentitySignature(publicCertificate);
                    using var chain = new CertificateCollection();
                    chain.Add(publicCertificate);
                    for (int issuer = 0; issuer < issuers.Count; issuer++)
                    {
                        if (issuers[issuer].Length is 0 or > 65536)
                        {
                            throw new InvalidOperationException("An issuer certificate is outside its bounds.");
                        }
                        using var item = new Certificate(issuers[issuer].Span);
                        chain.Add(item);
                    }
                    CertificateValidationResult validation = await m_validator.ValidateAsync(
                        chain, TrustListIdentifier.Users, StrictValidation(), polling.Token).ConfigureAwait(false);
                    validation.ThrowIfInvalid();
                    using Certificate combined = m_certificates.CreateWithPrivateKey(publicCertificate, m_key);
                    await RequireCompatibleAsync(combined, polling.Token).ConfigureAwait(false);
                    RequireFresh(review.ExpiresAt);
                    polling.Token.ThrowIfCancellationRequested();
                    m_issued = combined.AddRef();
                    m_chain = [.. chain];
                    m_expires = review.ExpiresAt;
                    m_result = new IdentityEnrollmentResult(combined.Subject, combined.Thumbprint, combined.NotAfter);
                    return m_result;
                }
                throw new TimeoutException("GDS approval did not complete within the bounded wait.");
            }
            finally
            {
                m_gate.Release();
            }
        }

        public async Task<CertificateIdentityReference> AdoptAsync(
            IdentityEnrollmentResult result, CancellationToken cancellationToken)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), m_time);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, m_lifetime.Token, deadline.Token);
            await m_gate.WaitAsync(cancellation.Token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(m_closed, this);
                if (!ReferenceEquals(result, m_result) || m_reference is null || m_key is null || m_issued is null)
                {
                    throw new InvalidOperationException("Request and review a certificate before adopting it.");
                }
                m_result = null;
                RequireFresh(m_expires);
                if (await ReadApplicationUriAsync(cancellation.Token)
                    .ConfigureAwait(false) != m_registeredApplicationUri)
                {
                    throw new InvalidOperationException("The registered application changed. Prepare again.");
                }
                RequireFresh(m_expires);
                CertificateIdentifier identifier = m_source.CreateIdentifier(m_reference);
                using Certificate current = await m_source.Provider.GetPrivateKeyCertificateAsync(
                    identifier, m_source.ResolvePasswordSource(m_reference.PasswordSourceId),
                    ct: cancellation.Token).ConfigureAwait(false) ??
                    throw new InvalidOperationException("The original identity is no longer available.");
                if (current.Thumbprint != m_key.Thumbprint)
                {
                    throw new InvalidOperationException("The configured identity changed. Prepare again.");
                }
                CertificateValidationResult validation = await m_validator.ValidateAsync(
                    m_chain ?? throw new InvalidOperationException("The reviewed issuer chain is unavailable."),
                    TrustListIdentifier.Users, StrictValidation(), cancellation.Token).ConfigureAwait(false);
                validation.ThrowIfInvalid();
                await RequireCompatibleAsync(m_issued, cancellation.Token).ConfigureAwait(false);
                RequireFresh(m_expires);
                using ICertificateStore store = m_openStore();
                if (store.StoreType != m_source.Store.StoreType || store.StorePath != m_source.Store.StorePath)
                {
                    throw new InvalidOperationException(
                        "The enrollment destination differs from the configured source.");
                }
                if (store.NoPrivateKeys)
                {
                    throw new InvalidOperationException(
                        "The configured enrollment store is not open for private keys.");
                }
                char[] password = m_source.ResolvePasswordSource(m_reference.PasswordSourceId).GetPassword(identifier);
                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    await store.AddAsync(m_issued, password, cancellation.Token).ConfigureAwait(false);
                }
                finally
                {
                    Array.Clear(password);
                }
                cancellation.Token.ThrowIfCancellationRequested();
                RequireFresh(m_expires);
                return m_reference with { Thumbprint = m_issued.Thumbprint, SubjectName = m_issued.Subject };
            }
            finally
            {
                m_gate.Release();
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (m_disposeGate)
            {
                return new ValueTask(m_disposal ??= DisposeCoreAsync());
            }
        }

        private async Task DisposeCoreAsync()
        {
            await m_lifetime.CancelAsync().ConfigureAwait(false);
            await m_gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!m_closed)
                {
                    m_closed = true;
                    Clear();
                }
            }
            finally
            {
                m_gate.Release();
                m_gate.Dispose();
                m_lifetime.Dispose();
            }
        }

        private async Task RequireCompatibleAsync(Certificate certificate, CancellationToken cancellationToken)
        {
            DateTime now = m_time.GetUtcNow().UtcDateTime;
            if (certificate.NotBefore.ToUniversalTime() > now || certificate.NotAfter.ToUniversalTime() <= now)
            {
                throw new InvalidOperationException("The issued user certificate is not currently valid.");
            }
            ReferencedCertificateProvider.RequireIdentitySignature(certificate);
            IReadOnlyList<string> uris = X509Utils.GetApplicationUrisFromCertificate(certificate);
            if (uris.Count != 1 || uris[0] != m_registeredApplicationUri)
            {
                throw new InvalidOperationException("The issued certificate has a different application binding.");
            }
            UserTokenPolicy policy = m_policy ?? throw new InvalidOperationException("The selected policy is missing.");
            ISession session = RequireGdsSession();
            var prepared = new PreparedIdentityCertificateProvider(m_source.Provider, certificate);
            try
            {
                var provider = new X509ClientIdentityProvider(
                    new CertificateIdentifier
                    {
                        Thumbprint = certificate.Thumbprint,
                        SubjectName = certificate.Subject
                    },
                    m_source.ResolvePasswordSource(m_reference!.PasswordSourceId), prepared);
                CanSatisfyResult result = await provider.CanSatisfyAsync(policy,
                    new IdentitySelectionContext(session.Endpoint, [policy], session.MessageContext,
                        [policy.SecurityPolicyUri!]), cancellationToken).ConfigureAwait(false);
                if (!result.CanSatisfy)
                {
                    throw new InvalidOperationException(
                        "The issued key is incompatible with the selected token policy.");
                }
            }
            finally
            {
                prepared.ReleasePreparedCertificate();
            }
        }

        private ISession RequireGdsSession()
        {
            ISession session = m_gds.Session ??
                throw new InvalidOperationException("Connect and authorize the configured GDS first.");
            if (!session.Connected ||
                !Uri.TryCreate(session.Endpoint.EndpointUrl, UriKind.Absolute, out Uri? endpoint) ||
                endpoint.UserInfo.Length != 0 ||
                endpoint.Fragment.Length != 0 ||
                session.Endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                session.Endpoint.SecurityPolicyUri == SecurityPolicies.None)
            {
                throw new InvalidOperationException(
                    "Enrollment requires an authenticated, signed and encrypted GDS session.");
            }
            return session;
        }

        private async Task<string> ReadApplicationUriAsync(CancellationToken cancellationToken)
        {
            ApplicationRecordDataType application = await m_gds.GetApplicationAsync(
                m_applicationId, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (application.ApplicationId != m_applicationId || string.IsNullOrWhiteSpace(application.ApplicationUri))
            {
                throw new InvalidOperationException(
                    "The configured GDS application record is unavailable or mismatched.");
            }
            ConnectionReference.ValidateUri(application.ApplicationUri);
            return application.ApplicationUri;
        }

        private void RequireFresh(DateTimeOffset expires)
        {
            ISession session = RequireGdsSession();
            if (m_time.GetUtcNow() >= expires ||
                m_gdsSession is null ||
                !m_gdsSession.TryGetTarget(out ISession? prepared) ||
                !ReferenceEquals(session, prepared) ||
                session.SessionId != m_gdsSessionId ||
                !ReferenceEquals(session.Identity, m_gdsIdentity) ||
                session.Endpoint.EndpointUrl != m_endpointUrl ||
                session.Endpoint.Server?.ApplicationUri != m_serverUri ||
                session.Endpoint.SecurityPolicyUri != m_channelPolicy ||
                session.Endpoint.ServerCertificate != m_channelCertificate ||
                !m_namespaceUris.Span.SequenceEqual(session.NamespaceUris.ToArray()) ||
                m_store is null ||
                m_source.Store.StoreType != m_store.StoreType ||
                m_source.Store.StorePath != m_store.StorePath ||
                m_source.Store.CertificateType != m_store.CertificateType ||
                m_reference is null ||
                !ReferenceEquals(m_source.ResolvePasswordSource(m_reference.PasswordSourceId), m_password))
            {
                throw new InvalidOperationException("The enrollment session or authorization expired. Prepare again.");
            }
        }

        private static CertificateValidationOptions StrictValidation()
        {
            return new()
            {
                RejectSHA1SignedCertificates = true,
                RejectUnknownRevocationStatus = true,
                AutoAcceptUntrustedCertificates = false,
                AllowCertificateDownload = false,
                AcceptError = static (_, _) => false
            };
        }

        private void Clear()
        {
            m_key?.Dispose();
            m_issued?.Dispose();
            m_chain?.Dispose();
            m_key = null;
            m_issued = null;
            m_chain = null;
            m_review = null;
            m_result = null;
            m_reference = null;
            m_store = null;
            m_password = null;
            m_gdsSession = null;
            m_gdsIdentity = null;
            m_namespaceUris = default;
            m_channelCertificate = default;
            m_registeredApplicationUri = null;
            m_policy = null;
            m_request = default;
        }

        private readonly ConfiguredCertificateSource m_source;
        private readonly IGlobalDiscoveryServerClient m_gds;
        private readonly NodeId m_applicationId;
        private readonly NodeId m_groupId;
        private readonly NodeId m_typeId;
        private readonly ICertificateValidatorEx m_validator;
        private readonly Func<ICertificateStore> m_openStore;
        private readonly ICertificateFactory m_certificates;
        private readonly TimeProvider m_time;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly CancellationTokenSource m_lifetime = new();
        private readonly Lock m_disposeGate = new();
        private Task? m_disposal;
        private WeakReference<ISession>? m_gdsSession;
        private NodeId m_gdsSessionId;
        private IUserIdentity? m_gdsIdentity;
        private Certificate? m_key;
        private Certificate? m_issued;
        private CertificateCollection? m_chain;
        private CertificateIdentityReference? m_reference;
        private UserTokenPolicy? m_policy;
        private IdentityEnrollmentReview? m_review;
        private IdentityEnrollmentResult? m_result;
        private ByteString m_request;
        private CertificateIdentifier? m_store;
        private ICertificatePasswordProvider? m_password;
        private string? m_endpointUrl;
        private string? m_serverUri;
        private string? m_channelPolicy;
        private string? m_registeredApplicationUri;
        private ByteString m_channelCertificate;
        private ArrayOf<string> m_namespaceUris;
        private DateTimeOffset m_expires;
        private bool m_closed;
    }
}
