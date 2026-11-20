// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Microsoft.Extensions.Options;
using Opc.Ua.Configuration;

namespace Opc.Ua.Client
{
    internal sealed class OpcUaClientApplicationConfigurationFeature :
        IOpcUaApplicationConfigurationFeature
    {
        public OpcUaClientApplicationConfigurationFeature(
            IOptions<OpcUaClientOptions> options)
        {
            m_options = options.Value;
        }

        public void ApplyDefaults(OpcUaApplicationOptions options)
        {
            options.ApplicationName ??= m_options.ApplicationName;
            options.ApplicationUri ??= m_options.ApplicationUri;
            options.ProductUri ??= m_options.ProductUri;
            options.SubjectName ??= m_options.SubjectName;
            options.PkiRoot ??= m_options.PkiRoot;
            options.AutoAcceptUntrustedCertificates ??=
                m_options.AutoAcceptUntrustedCertificates;
            options.RejectSHA1SignedCertificates ??=
                m_options.RejectSHA1SignedCertificates;
            options.MinimumCertificateKeySize ??= m_options.MinimumCertificateKeySize;
        }

        public IApplicationConfigurationBuilderSecurity Configure(
            IApplicationConfigurationBuilderTypes builder)
        {
            return builder.AsClient();
        }

        private readonly OpcUaClientOptions m_options;
    }
}
