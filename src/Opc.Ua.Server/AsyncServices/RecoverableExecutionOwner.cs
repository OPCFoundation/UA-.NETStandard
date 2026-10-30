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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Identifies a stable authenticated application-and-user owner within one mapping authority.
    /// </summary>
    public readonly record struct RecoverableExecutionOwnerId
    {
        /// <summary>
        /// Creates an identity from explicit, nonzero identifiers that remain stable across restarts.
        /// </summary>
        public RecoverableExecutionOwnerId(Uuid authorityId, Uuid applicationId, Uuid userId)
        {
            if (authorityId == Uuid.Empty)
            {
                throw new ArgumentException("A nonzero mapping authority is required.", nameof(authorityId));
            }
            if (applicationId == Uuid.Empty)
            {
                throw new ArgumentException("A nonzero application identity is required.", nameof(applicationId));
            }
            if (userId == Uuid.Empty)
            {
                throw new ArgumentException("A nonzero user identity is required.", nameof(userId));
            }
            AuthorityId = authorityId;
            ApplicationId = applicationId;
            UserId = userId;
        }

        /// <summary>
        /// Gets the stable mapping authority, not a Session or process identifier.
        /// </summary>
        public Uuid AuthorityId { get; }

        /// <summary>
        /// Gets the explicitly mapped application identity.
        /// </summary>
        public Uuid ApplicationId { get; }

        /// <summary>
        /// Gets the explicitly mapped user identity.
        /// </summary>
        public Uuid UserId { get; }

        /// <summary>
        /// Returns a redacted diagnostic name, not a representation suitable for a storage key.
        /// </summary>
        public override string ToString()
        {
            return nameof(RecoverableExecutionOwnerId);
        }
    }

    /// <summary>
    /// Captures an owner and the provider evidence needed to validate its persisted continuity.
    /// This is local authorization state, not a wire envelope or a bearer credential.
    /// </summary>
    public sealed class RecoverableExecutionOwner
    {
        /// <summary>
        /// Creates an immutable owner capture. Persist its context only inside a protected record.
        /// </summary>
        public RecoverableExecutionOwner(RecoverableExecutionOwnerId id, ByteString continuityContext)
        {
            if (id.AuthorityId == Uuid.Empty || id.ApplicationId == Uuid.Empty || id.UserId == Uuid.Empty)
            {
                throw new ArgumentException("A complete owner identity is required.", nameof(id));
            }
            if (continuityContext.Length <= 0)
            {
                throw new ArgumentException("Continuity evidence is required.", nameof(continuityContext));
            }
            Id = id;
            ContinuityContext = new ByteString(continuityContext.Span.ToArray());
        }

        /// <summary>
        /// Gets the duplicate-detection and authorization identity shared by Owner and Durable modes.
        /// </summary>
        public RecoverableExecutionOwnerId Id { get; }

        /// <summary>
        /// Gets opaque provider evidence. It is neither a credential nor suitable for logging.
        /// </summary>
        public ByteString ContinuityContext { get; }
    }

    /// <summary>
    /// Explicitly maps a validated application certificate's SHA-256 digest to a stable application.
    /// Multiple different certificates may map to the same identity only through explicit configuration.
    /// </summary>
    public sealed class RecoverableExecutionApplicationMapping
    {
        /// <summary>
        /// Creates one authorized certificate-to-application mapping.
        /// </summary>
        public RecoverableExecutionApplicationMapping(Uuid applicationId, ByteString certificateSha256)
        {
            if (applicationId == Uuid.Empty)
            {
                throw new ArgumentException("A nonzero application identity is required.", nameof(applicationId));
            }
            if (certificateSha256.Length != 32)
            {
                throw new ArgumentException("A 32-byte SHA-256 digest is required.", nameof(certificateSha256));
            }
            ApplicationId = applicationId;
            CertificateSha256 = new ByteString(certificateSha256.Span.ToArray());
        }

        /// <summary>
        /// Gets the stable application identity shared by its explicitly authorized certificate aliases.
        /// </summary>
        public Uuid ApplicationId { get; }

        /// <summary>
        /// Gets the digest of the full DER application certificate, not its claimed ApplicationUri.
        /// </summary>
        public ByteString CertificateSha256 { get; }
    }

    /// <summary>
    /// Maps an exact validated issuer/subject pair to a stable user without using display names.
    /// </summary>
    public sealed class RecoverableExecutionUserMapping
    {
        /// <summary>
        /// Creates one authorized issuer-scoped user mapping.
        /// </summary>
        public RecoverableExecutionUserMapping(Uuid userId, string issuer, string subject)
        {
            if (userId == Uuid.Empty)
            {
                throw new ArgumentException("A nonzero user identity is required.", nameof(userId));
            }
            if (string.IsNullOrWhiteSpace(issuer))
            {
                throw new ArgumentException("A validated issuer is required.", nameof(issuer));
            }
            if (string.IsNullOrWhiteSpace(subject))
            {
                throw new ArgumentException("A validated subject is required.", nameof(subject));
            }
            UserId = userId;
            Issuer = issuer;
            Subject = subject;
        }

        /// <summary>
        /// Gets the stable user identity.
        /// </summary>
        public Uuid UserId { get; }

        /// <summary>
        /// Gets the exact ordinal issuer; it is not normalized or inferred.
        /// </summary>
        public string Issuer { get; }

        /// <summary>
        /// Gets the exact ordinal subject within the issuer's namespace.
        /// </summary>
        public string Subject { get; }
    }
}
