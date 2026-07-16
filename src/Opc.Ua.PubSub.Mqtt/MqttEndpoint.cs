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

namespace Opc.Ua.PubSub.Mqtt
{
    /// <summary>
    /// Parsed MQTT endpoint URL produced by
    /// <see cref="MqttEndpointParser"/>. Carries the materialised
    /// host / port plus a flag selecting plaintext vs TLS so
    /// transport call sites do not re-parse the URL.
    /// </summary>
    /// <remarks>
    /// Implements the addressing surface of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4">
    /// Part 14 §7.3.4 Broker transport (MQTT)</see>. The
    /// <c>mqtt</c> / <c>mqtts</c> URI distinction is the only
    /// scheme-derived signal for TLS; per Part 14 §7.3.4.4 the
    /// negotiated TLS layer is independent of the MQTT protocol
    /// version selection.
    /// </remarks>
    /// <param name="Uri">Parsed broker URI.</param>
    /// <param name="UseTls">
    /// <see langword="true"/> when the URI scheme was <c>mqtts</c>.
    /// </param>
    public readonly record struct MqttEndpoint(Uri Uri, bool UseTls)
    {
        /// <summary>
        /// Convenience accessor — host portion of <see cref="Uri"/>.
        /// </summary>
        public string Host => Uri.Host;

        /// <summary>
        /// Convenience accessor — port portion of <see cref="Uri"/>.
        /// </summary>
        public int Port => Uri.Port;
    }
}
