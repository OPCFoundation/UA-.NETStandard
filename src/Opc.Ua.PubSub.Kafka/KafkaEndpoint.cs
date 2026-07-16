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

namespace Opc.Ua.PubSub.Kafka
{
    /// <summary>
    /// Parsed Kafka endpoint produced by
    /// <see cref="KafkaEndpointParser"/>. Carries the normalised
    /// comma-separated bootstrap server list plus a flag selecting
    /// plaintext vs TLS so transport call sites do not re-parse the URL.
    /// </summary>
    /// <remarks>
    /// Implements the addressing surface of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/Annex-B.2">
    /// Part 14 Annex B.2 Apache Kafka transport</see>. Kafka clients
    /// connect to a list of bootstrap brokers rather than a single host,
    /// so the endpoint carries the full <c>host:port</c> list; the
    /// <c>kafka</c> / <c>kafkas</c> scheme is the only scheme-derived
    /// signal for TLS.
    /// </remarks>
    /// <param name="BootstrapServers">
    /// Normalised comma-separated <c>host:port</c> bootstrap server list.
    /// </param>
    /// <param name="UseTls">
    /// <see langword="true"/> when the URL scheme was <c>kafkas</c>.
    /// </param>
    public readonly record struct KafkaEndpoint(string BootstrapServers, bool UseTls);
}
