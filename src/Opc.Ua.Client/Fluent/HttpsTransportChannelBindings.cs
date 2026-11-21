// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.Bindings;

namespace Opc.Ua.Client
{
    internal sealed class HttpsTransportChannelBindings : ITransportChannelBindings
    {
        public HttpsTransportChannelBindings(
            ITransportChannelBindings inner,
            IOpcUaHttpClientFactory httpClientFactory)
        {
            m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
            if (httpClientFactory == null)
            {
                throw new ArgumentNullException(nameof(httpClientFactory));
            }
            m_httpsFactory = new HttpsTransportChannelFactory(httpClientFactory);
            m_opcHttpsFactory = new OpcHttpsTransportChannelFactory(httpClientFactory);
        }

        public ITransportChannel? Create(string uriScheme, ITelemetryContext telemetry)
        {
            if (string.Equals(uriScheme, Utils.UriSchemeHttps, StringComparison.Ordinal))
            {
                return m_httpsFactory.Create(telemetry);
            }
            if (string.Equals(uriScheme, Utils.UriSchemeOpcHttps, StringComparison.Ordinal))
            {
                return m_opcHttpsFactory.Create(telemetry);
            }
            return m_inner.Create(uriScheme, telemetry);
        }

        private readonly ITransportChannelBindings m_inner;
        private readonly HttpsTransportChannelFactory m_httpsFactory;
        private readonly OpcHttpsTransportChannelFactory m_opcHttpsFactory;
    }
}
