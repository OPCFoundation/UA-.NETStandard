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
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml;
using Opc.Ua.Identity;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Owns admission identity facts independently of the live Session and later role refreshes.
    /// This in-memory principal is never serialized into a durable operation record.
    /// </summary>
    internal sealed class RecoverableExecutionIdentity : IUserIdentity, IIdentityClaims
    {
        /// <summary>
        /// Copies validated facts and the existing token-handler snapshot without using display names as authority.
        /// </summary>
        public RecoverableExecutionIdentity(
            IUserIdentity effective, IUserIdentity authenticated, IServiceMessageContext encoding)
            : this(effective, authenticated,
                new CaptureBudget(encoding ?? throw new ArgumentNullException(nameof(encoding))))
        {
        }

        private RecoverableExecutionIdentity(
            IUserIdentity effective, IUserIdentity authenticated, CaptureBudget budget)
        {
            if (effective == null)
            {
                throw new ArgumentNullException(nameof(effective));
            }
            if (authenticated is not IIdentityClaims claims)
            {
                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
            m_budget = budget;
            DisplayName = effective.DisplayName;
            PolicyId = effective.PolicyId;
            TokenType = effective.TokenType;
            // Non-issued UserIdentity implementations legitimately leave the legacy name unset.
            IssuedTokenType = effective.IssuedTokenType ?? XmlQualifiedName.Empty;
            SupportsSignatures = effective.SupportsSignatures;
            budget.Charge(DisplayName);
            budget.Charge(PolicyId);
            budget.Charge(IssuedTokenType.Name);
            budget.Charge(IssuedTokenType.Namespace);
            foreach (NodeId role in effective.GrantedRoleIds)
            {
                budget.Charge(role.ToString());
            }
            GrantedRoleIds = CopyRoles(effective.GrantedRoleIds);
            budget.Charge(effective.TokenHandler.Token);
            if (effective.TokenHandler is UserNameIdentityTokenHandler user)
            {
                budget.Charge(user.DecryptedPassword?.Length ?? 0);
            }
            else if (effective.TokenHandler is IssuedIdentityTokenHandler issued)
            {
                byte[]? decrypted = issued.DecryptedTokenData;
                try
                {
                    budget.Charge(decrypted?.Length ?? 0);
                }
                finally
                {
                    if (decrypted != null)
                    {
                        Array.Clear(decrypted, 0, decrypted.Length);
                    }
                }
            }
            TokenHandler = effective.TokenHandler.Copy();
            if (ReferenceEquals(TokenHandler, effective.TokenHandler) ||
                ReferenceEquals(TokenHandler.Token, effective.TokenHandler.Token))
            {
                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
            switch (TokenHandler.Token)
            {
                case AnonymousIdentityToken:
                    break;
                case UserNameIdentityToken userToken:
                    userToken.Password = CopyBytes(userToken.Password);
                    break;
                case X509IdentityToken certificateToken:
                    certificateToken.CertificateData = CopyBytes(certificateToken.CertificateData);
                    break;
                case IssuedIdentityToken issuedToken:
                    issuedToken.TokenData = CopyBytes(issuedToken.TokenData);
                    break;
                default:
                    throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
            Issuer = claims.Issuer;
            Subject = claims.Subject;
            budget.Charge(Issuer);
            budget.Charge(Subject);
            m_groups = CopyStrings(claims.Groups, budget);
            m_roles = CopyStrings(claims.Roles, budget);
            var copied = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> claim in claims.Claims)
            {
                budget.Charge(claim.Key);
                copied.Add(claim.Key, CopyClaim(claim.Value, budget));
            }
            m_claims = new ReadOnlyDictionary<string, object?>(copied);
        }

        /// <inheritdoc/>
        public string DisplayName { get; }

        /// <inheritdoc/>
        public string PolicyId { get; }

        /// <inheritdoc/>
        public UserTokenType TokenType { get; }

        /// <inheritdoc/>
        public XmlQualifiedName IssuedTokenType { get; }

        /// <inheritdoc/>
        public bool SupportsSignatures { get; }

        /// <inheritdoc/>
        public ArrayOf<NodeId> GrantedRoleIds { get; }

        /// <inheritdoc/>
        public IUserIdentityTokenHandler TokenHandler { get; }

        /// <inheritdoc/>
        public string? Issuer { get; }

        /// <inheritdoc/>
        public string? Subject { get; }

        /// <inheritdoc/>
        IReadOnlyDictionary<string, object?> IIdentityClaims.Claims => m_claims;

        /// <inheritdoc/>
        IReadOnlyList<string> IIdentityClaims.Groups => m_groups;

        /// <inheritdoc/>
        IReadOnlyList<string> IIdentityClaims.Roles => m_roles;

        /// <summary>
        /// Copies transport-established facts and any upstream principal into the same bounded ownership capture.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public SecureChannelContext CaptureChannel(SecureChannelContext source, ArrayOf<string> locales)
        {
            if (source.EndpointDescription == null)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityModeRejected);
            }
            EndpointDescription endpoint = m_budget.Copy(source.EndpointDescription);
            m_budget.Charge(source.SecureChannelId);
            m_budget.Charge(source.ClientChannelCertificate.Length);
            m_budget.Charge(source.ServerChannelCertificate.Length);
            m_budget.Charge(source.ChannelThumbprint.Length);
            foreach (string locale in locales)
            {
                m_budget.Charge(locale);
            }
            IUserIdentity? upstream = source.UpstreamIdentity == null ? null :
                new RecoverableExecutionIdentity(source.UpstreamIdentity, source.UpstreamIdentity, m_budget);
            IPAddress? peer = null;
            if (source.PeerAddress is { } address)
            {
                byte[] bytes = address.GetAddressBytes();
                m_budget.Charge(bytes.Length);
                peer = address.AddressFamily == AddressFamily.InterNetworkV6
                    ? new IPAddress(bytes, address.ScopeId) : new IPAddress(bytes);
            }
            return new SecureChannelContext(
                source.SecureChannelId, endpoint, source.MessageEncoding,
                source.ClientChannelCertificate.IsNull ? null : source.ClientChannelCertificate.ToArray(),
                source.ServerChannelCertificate.IsNull ? null : source.ServerChannelCertificate.ToArray(),
                source.ChannelThumbprint.IsNull ? null : source.ChannelThumbprint.ToArray(),
                peer)
            {
                HasTransportSecurity = source.HasTransportSecurity,
                UpstreamIdentity = upstream
            };
        }

        /// <summary>
        /// Gives the invocation and retained history independent role identifiers, including opaque identifiers.
        /// </summary>
        internal static ArrayOf<NodeId> CopyRoles(ArrayOf<NodeId> source)
        {
            NodeId[] roles = source.Span.ToArray();
            for (int i = 0; i < roles.Length; i++)
            {
                if (roles[i].TryGetValue(out ByteString identifier))
                {
                    roles[i] = new NodeId(new ByteString(identifier.Span.ToArray()), roles[i].NamespaceIndex);
                }
            }
            return roles;
        }

        /// <summary>
        /// Copies the claims contract's scalar/string-list shapes; unfamiliar mutable values fail before admission.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static object? CopyClaim(object? value, CaptureBudget budget)
        {
            budget.Charge(32);
            switch (value)
            {
                case string text:
                    budget.Charge(text);
                    return text;
                case null or bool or byte or sbyte or short or ushort or int or uint or long or ulong or
                    float or double or decimal:
                    return value;
                case IReadOnlyList<string> strings:
                    return CopyStrings(strings, budget);
                default:
                    throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
        }

        private static ReadOnlyCollection<string> CopyStrings(IReadOnlyList<string> source, CaptureBudget budget)
        {
            budget.Charge(8L * source.Count);
            foreach (string value in source)
            {
                budget.Charge(value);
            }
            return Array.AsReadOnly(source.ToArray());
        }

        private static ByteString CopyBytes(ByteString value)
        {
            return value.IsNull ? default : new ByteString(value.Span.ToArray());
        }

        /// <summary>
        /// Uses the existing message-sized reservation for principal and channel facts, not a separate quota policy.
        /// </summary>
        private sealed class CaptureBudget(IServiceMessageContext encoding)
        {
            public void Charge(string? text)
            {
                Charge(64L + (text == null ? 0 : Encoding.UTF8.GetByteCount(text)));
            }

            public void Charge(long bytes)
            {
                if (bytes < 0 || bytes > m_remaining)
                {
                    throw new ServiceResultException(StatusCodes.BadResourceUnavailable,
                        "The independently owned identity exceeds the reserved message-sized capture.");
                }
                m_remaining -= (int)bytes;
            }

            public void Charge(IEncodeable value)
            {
                byte[] bytes = Encode(value);
                Array.Clear(bytes, 0, bytes.Length);
            }

            public T Copy<T>(T value) where T : IEncodeable
            {
                byte[] bytes = Encode(value);
                try
                {
                    return BinaryDecoder.DecodeMessage<T>(bytes, encoding);
                }
                finally
                {
                    Array.Clear(bytes, 0, bytes.Length);
                }
            }

            private byte[] Encode(IEncodeable value)
            {
                using var stream = new MemoryStream(
                    new byte[m_remaining], 0, m_remaining, writable: true, publiclyVisible: true);
                stream.SetLength(0);
                try
                {
                    using var encoder = new BinaryEncoder(stream, encoding, leaveOpen: true);
                    encoder.EncodeMessage(value, value.BinaryEncodingId);
                    Charge(encoder.Close());
                    return stream.ToArray();
                }
                catch (NotSupportedException exception)
                {
                    throw new ServiceResultException(StatusCodes.BadResourceUnavailable, exception);
                }
                finally
                {
                    Array.Clear(stream.GetBuffer(), 0, stream.Capacity);
                }
            }

            private int m_remaining = encoding.MaxMessageSize > 0 ? encoding.MaxMessageSize :
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
        }

        private readonly CaptureBudget m_budget;
        private readonly IReadOnlyDictionary<string, object?> m_claims;
        private readonly IReadOnlyList<string> m_groups;
        private readonly IReadOnlyList<string> m_roles;
    }
}
