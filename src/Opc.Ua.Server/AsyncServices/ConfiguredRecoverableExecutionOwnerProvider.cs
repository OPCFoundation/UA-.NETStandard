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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Identity;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Resolves owners from explicitly configured certificate and validated issuer/subject mappings.
    /// Application signing, certificate validation and user authentication remain mandatory.
    /// </summary>
    /// <remarks>
    /// Keep the authority and logical identifiers stable across restart. Retain old mapping aliases
    /// while persisted records depend on them; retaining an alias does not restore certificate trust.
    /// This provider does not infer continuity from ApplicationUri, display names or TLS alone.
    /// </remarks>
    public sealed class ConfiguredRecoverableExecutionOwnerProvider : IRecoverableExecutionOwnerProvider
    {
        /// <summary>
        /// Captures unambiguous immutable mappings without enabling a recovery mode.
        /// </summary>
        public ConfiguredRecoverableExecutionOwnerProvider(
            Uuid authorityId,
            ArrayOf<RecoverableExecutionApplicationMapping> applications,
            ArrayOf<RecoverableExecutionUserMapping> users)
        {
            if (authorityId == Uuid.Empty)
            {
                throw new ArgumentException("A nonzero mapping authority is required.", nameof(authorityId));
            }
            if (applications.Count == 0)
            {
                throw new ArgumentException("At least one application mapping is required.", nameof(applications));
            }
            if (users.Count == 0)
            {
                throw new ArgumentException("At least one user mapping is required.", nameof(users));
            }
            m_authorityId = authorityId;
            foreach (RecoverableExecutionApplicationMapping mapping in applications)
            {
                if (mapping == null || !m_applications.TryAdd(mapping.CertificateSha256, mapping.ApplicationId))
                {
                    throw new ArgumentException(
                        "Application mappings must be nonnull and unambiguous.", nameof(applications));
                }
            }
            foreach (RecoverableExecutionUserMapping mapping in users)
            {
                if (mapping == null || !m_users.TryAdd((mapping.Issuer, mapping.Subject), mapping.UserId))
                {
                    throw new ArgumentException("User mappings must be nonnull and unambiguous.", nameof(users));
                }
                _ = s_utf8.GetByteCount(mapping.Issuer);
                _ = s_utf8.GetByteCount(mapping.Subject);
            }
        }

        /// <inheritdoc/>
        public ValueTask<RecoverableExecutionOwner> ResolveAsync(
            OperationContext context,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            cancellationToken.ThrowIfCancellationRequested();
            ISession session = ValidateSession(context);
            IUserIdentity identity = session.Identity;
            if (session.IdentityToken.TokenType == UserTokenType.Anonymous ||
                identity is not IIdentityClaims claims)
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            string? issuer = claims.Issuer;
            string? subject = claims.Subject;
            if (string.IsNullOrEmpty(issuer) ||
                string.IsNullOrEmpty(subject) ||
                !m_users.TryGetValue((issuer, subject), out Uuid userId))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            ByteString certificateHash = new(SHA256.HashData(session.ClientCertificate.RawData));
            if (!m_applications.TryGetValue(certificateHash, out Uuid applicationId))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            ByteString evidence = EncodeEvidence(certificateHash, issuer, subject);
            _ = ValidateSession(context);
            if (!ReferenceEquals(identity, session.Identity))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            return new ValueTask<RecoverableExecutionOwner>(new RecoverableExecutionOwner(
                new RecoverableExecutionOwnerId(m_authorityId, applicationId, userId), evidence));
        }

        /// <inheritdoc/>
        public ValueTask ValidatePersistedOwnerAsync(
            RecoverableExecutionOwner owner,
            CancellationToken cancellationToken = default)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (owner.Id.AuthorityId != m_authorityId ||
                !TryDecodeEvidence(owner.ContinuityContext, out ByteString certificateHash,
                    out string issuer, out string subject) ||
                !m_applications.TryGetValue(certificateHash, out Uuid applicationId) ||
                applicationId != owner.Id.ApplicationId ||
                !m_users.TryGetValue((issuer, subject), out Uuid userId) ||
                userId != owner.Id.UserId)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
            return default;
        }

        private static ISession ValidateSession(OperationContext context)
        {
            ISession session = context.Session;
            if (session == null || !session.Activated)
            {
                throw new ServiceResultException(StatusCodes.BadSessionNotActivated);
            }
            if (session.IsClosing || session.HasExpired)
            {
                throw new ServiceResultException(StatusCodes.BadSessionClosed);
            }
            SecureChannelContext? channel = context.ChannelContext;
            if (channel?.EndpointDescription == null || !session.IsSecureChannelValid(channel.SecureChannelId))
            {
                throw new ServiceResultException(StatusCodes.BadSecureChannelIdInvalid);
            }
            if (string.IsNullOrEmpty(context.SecurityPolicyUri) ||
                context.SecurityPolicyUri == SecurityPolicies.None ||
                context.SecurityPolicyUri != session.EndpointDescription.SecurityPolicyUri ||
                channel.EndpointDescription.SecurityMode != session.EndpointDescription.SecurityMode)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityModeRejected);
            }
            if (session.ClientCertificate == null ||
                !ClientCertificateProvenance.IsValidated(session) ||
                session.IsIdentityStale ||
                !ReferenceEquals(context.UserIdentity, session.EffectiveIdentity))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            return session;
        }

        private static ByteString EncodeEvidence(ByteString certificateHash, string issuer, string subject)
        {
            int issuerLength = s_utf8.GetByteCount(issuer);
            int subjectLength = s_utf8.GetByteCount(subject);
            byte[] bytes = new byte[checked(kFixedEvidenceSize + issuerLength + subjectLength)];
            bytes[0] = kEvidenceVersion;
            certificateHash.Span.CopyTo(bytes.AsSpan(1));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(33), issuerLength);
            s_utf8.GetBytes(issuer, 0, issuer.Length, bytes, 37);
            int subjectOffset = 37 + issuerLength;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(subjectOffset), subjectLength);
            s_utf8.GetBytes(subject, 0, subject.Length, bytes, subjectOffset + 4);
            return new ByteString(bytes);
        }

        private static bool TryDecodeEvidence(
            ByteString evidence,
            out ByteString certificateHash,
            out string issuer,
            out string subject)
        {
            certificateHash = default;
            issuer = string.Empty;
            subject = string.Empty;
            ReadOnlySpan<byte> bytes = evidence.Span;
            if (bytes.Length < kFixedEvidenceSize || bytes[0] != kEvidenceVersion)
            {
                return false;
            }
            int issuerLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(33, 4));
            if (issuerLength <= 0 || issuerLength > bytes.Length - kFixedEvidenceSize)
            {
                return false;
            }
            int subjectOffset = 37 + issuerLength;
            int subjectLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(subjectOffset, 4));
            if (subjectLength <= 0 || subjectLength != bytes.Length - subjectOffset - 4)
            {
                return false;
            }
            byte[] encoded = evidence.ToArray();
            try
            {
                issuer = s_utf8.GetString(encoded, 37, issuerLength);
                subject = s_utf8.GetString(encoded, subjectOffset + 4, subjectLength);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
            certificateHash = evidence.Slice(1, 32);
            return true;
        }

        private readonly Uuid m_authorityId;
        private readonly Dictionary<ByteString, Uuid> m_applications = [];
        private readonly Dictionary<(string Issuer, string Subject), Uuid> m_users = [];
        private static readonly UTF8Encoding s_utf8 = new(false, true);
        private const byte kEvidenceVersion = 1;
        private const int kFixedEvidenceSize = 41;
    }
}
