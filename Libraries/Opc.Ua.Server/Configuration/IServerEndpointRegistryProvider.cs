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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional opt-in interface that surfaces the server's active
    /// <see cref="EndpointDescription"/> set. Implemented by
    /// <see cref="ServerInternalData"/> and populated by
    /// <see cref="StandardServer"/> once the endpoints are created. Consumers
    /// resolve the registry via
    /// <c>(server as IServerEndpointRegistryProvider)?.ServerEndpoints</c> so
    /// external/mocked <see cref="IServerInternal"/> implementations remain
    /// binary-compatible.
    /// </summary>
    /// <remarks>
    /// Primary consumer: <see cref="ConfigurationNodeManager"/> needs the
    /// endpoints to enforce OPC UA Part 12 §7.10.7 — a <c>DeleteCertificate</c>
    /// that would remove a certificate still referenced by an
    /// <c>EndpointDescription</c> must be rejected when <c>ApplyChanges</c>
    /// is called.
    /// </remarks>
    public interface IServerEndpointRegistryProvider
    {
        /// <summary>
        /// A snapshot of the endpoints currently advertised by the server.
        /// Each entry's <see cref="EndpointDescription.SecurityPolicyUri"/>
        /// identifies which certificate/type that endpoint presents; the
        /// certificate itself is resolved live from the active certificate
        /// registry at check time (so a rotated certificate is honored even
        /// though the entry's <see cref="EndpointDescription.ServerCertificate"/>
        /// blob was captured at startup). Returns an empty array before the
        /// endpoints are created.
        /// </summary>
        ArrayOf<EndpointDescription> ServerEndpoints { get; }
    }
}
