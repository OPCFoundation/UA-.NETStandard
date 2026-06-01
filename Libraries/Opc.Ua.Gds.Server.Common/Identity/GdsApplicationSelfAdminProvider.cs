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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Server.Identity
{
    /// <summary>
    /// Grants OPC 10000-12 §7.2 ApplicationSelfAdmin when the secure-channel
    /// ApplicationInstance certificate matches a registered GDS application.
    /// </summary>
    public sealed class GdsApplicationSelfAdminProvider : IIdentityAugmenter
    {
        private static readonly string[] s_certificateTypeIds =
        [
            nameof(Ua.ObjectTypeIds.HttpsCertificateType),
            nameof(Ua.ObjectTypeIds.UserCertificateType),
            nameof(Ua.ObjectTypeIds.ApplicationCertificateType),
            nameof(Ua.ObjectTypeIds.RsaMinApplicationCertificateType),
            nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType),
            nameof(Ua.ObjectTypeIds.EccApplicationCertificateType),
            nameof(Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType),
            nameof(Ua.ObjectTypeIds.EccNistP384ApplicationCertificateType),
            nameof(Ua.ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType),
            nameof(Ua.ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType)
#if CURVE25519
            ,
            nameof(Ua.ObjectTypeIds.EccCurve25519ApplicationCertificateType),
            nameof(Ua.ObjectTypeIds.EccCurve448ApplicationCertificateType)
#endif
        ];

        private readonly IApplicationsDatabase m_database;
        private readonly ILogger<GdsApplicationSelfAdminProvider> m_logger;
        private readonly NamespaceTable? m_namespaces;
        private readonly Func<Certificate, bool>? m_isRegisteredCertificate;
        private readonly Func<Certificate, CancellationToken, ValueTask<bool>>? m_isRevokedCertificateAsync;

        /// <summary>
        /// Creates a provider backed by an applications database.
        /// </summary>
        /// <param name="database">The GDS applications database.</param>
        /// <param name="logger">Logger used to report successful elevation.</param>
        /// <param name="namespaces">Optional namespace table used when wrapping identities.</param>
        /// <param name="isRegisteredCertificate">
        /// Optional legacy registration check for certificates that are still accepted by the GDS
        /// application store.
        /// </param>
        /// <param name="isRevokedCertificateAsync">
        /// Optional revocation check. When supplied and it returns <c>true</c>, the augmenter
        /// declines to grant <see cref="GdsRole.ApplicationSelfAdmin"/> even when the channel
        /// certificate matches a registered application. The default implementation only inspects
        /// the registered ApplicationsDatabase entries; revocation must be enforced separately by
        /// the surrounding GDS pipeline (CRL trust list, secure-channel certificate validation,
        /// or by clearing the application's stored certificate when it is revoked).
        /// </param>
        public GdsApplicationSelfAdminProvider(
            IApplicationsDatabase database,
            ILogger<GdsApplicationSelfAdminProvider> logger,
            NamespaceTable? namespaces = null,
            Func<Certificate, bool>? isRegisteredCertificate = null,
            Func<Certificate, CancellationToken, ValueTask<bool>>? isRevokedCertificateAsync = null)
        {
            m_database = database ?? throw new ArgumentNullException(nameof(database));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            m_namespaces = namespaces;
            m_isRegisteredCertificate = isRegisteredCertificate;
            m_isRevokedCertificateAsync = isRevokedCertificateAsync;
        }

        /// <inheritdoc/>
        public async ValueTask<AuthenticationResult> AugmentAsync(
            IUserIdentity identity,
            AuthenticationContext context,
            CancellationToken ct = default)
        {
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            ct.ThrowIfCancellationRequested();

            if (context.ChannelCertificate == null)
            {
                return AuthenticationResult.NotHandled;
            }
            if (context.ChannelApplicationUri == null)
            {
                return AuthenticationResult.Accept(identity);
            }

            if (m_isRevokedCertificateAsync != null &&
                await m_isRevokedCertificateAsync(context.ChannelCertificate, ct).ConfigureAwait(false))
            {
                m_logger.LogDebug(
                    "ApplicationSelfAdmin denied for {AppUri}: channel certificate {Thumbprint} is revoked.",
                    context.ChannelApplicationUri,
                    context.ChannelCertificate.Thumbprint);
                return AuthenticationResult.Accept(identity);
            }

            ApplicationRecordDataType[]? matches =
                m_database.FindApplications(context.ChannelApplicationUri);
            if (matches == null || matches.Length == 0)
            {
                return AuthenticationResult.Accept(identity);
            }

            foreach (ApplicationRecordDataType match in matches)
            {
                if (MatchesRegisteredCertificate(match.ApplicationId, context.ChannelCertificate))
                {
                    IUserIdentity augmentedIdentity = CreateAugmentedIdentity(
                        identity,
                        context,
                        match.ApplicationId);
                    return AuthenticationResult.Accept(augmentedIdentity);
                }
            }

            if (matches.Length == 1 &&
                m_isRegisteredCertificate != null &&
                m_isRegisteredCertificate(context.ChannelCertificate))
            {
                IUserIdentity augmentedIdentity = CreateAugmentedIdentity(
                    identity,
                    context,
                    matches[0].ApplicationId);
                return AuthenticationResult.Accept(augmentedIdentity);
            }

            return AuthenticationResult.Accept(identity);
        }

        private IUserIdentity CreateAugmentedIdentity(
            IUserIdentity identity,
            AuthenticationContext context,
            NodeId applicationId)
        {
            if (identity is GdsRoleBasedIdentity existing &&
                existing.Roles.Contains(GdsRole.ApplicationSelfAdmin) &&
                Equals(existing.ApplicationId, applicationId))
            {
                return identity;
            }

            IReadOnlyList<NodeId>? administeredAppIds =
                (identity as GdsRoleBasedIdentity)?.AdministeredApplicationIds;
            var augmented = new GdsRoleBasedIdentity(
                identity,
                [GdsRole.ApplicationSelfAdmin],
                applicationId,
                administeredAppIds,
                m_namespaces ?? context.MessageContext.NamespaceUris);

            m_logger.LogInformation(
                "ApplicationSelfAdmin granted to {AppUri} (cert thumbprint {Thumbprint}).",
                context.ChannelApplicationUri,
                context.ChannelCertificate?.Thumbprint);
            return augmented;
        }

        private bool MatchesRegisteredCertificate(NodeId appId, Certificate channelCertificate)
        {
            byte[] channelRawData = channelCertificate.RawData;
            string channelThumbprint = channelCertificate.Thumbprint;
            foreach (string certificateTypeId in s_certificateTypeIds)
            {
                if (!m_database.GetApplicationCertificate(
                        appId,
                        certificateTypeId,
                        out ByteString certificate) ||
                    certificate.IsEmpty)
                {
                    continue;
                }

                if (Utils.IsEqual(certificate, channelRawData))
                {
                    return true;
                }

                using var registeredCertificate = Certificate.FromRawData(certificate);
                if (string.Equals(
                        registeredCertificate.Thumbprint,
                        channelThumbprint,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
