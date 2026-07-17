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
