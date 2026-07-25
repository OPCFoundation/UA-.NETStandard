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

using Opc.Ua.Client;

namespace Opc.Ua.XRegistry.Client
{
    /// <summary>
    /// The plain xRegistry client. It binds to any registry companion namespace and drives it
    /// purely through the base-model ObjectType proxies, so it also works against a domain registry
    /// whose types are subtypes of the xRegistry base types.
    /// </summary>
    /// <remarks>
    /// Domain registries do not wrap this class — they derive from <see cref="XRegistryClient"/>
    /// and add their own naming and defaults.
    /// </remarks>
    public sealed class GenericXRegistryClient : XRegistryClient
    {
        /// <summary>
        /// Initializes a generic registry client.
        /// </summary>
        /// <param name="session">The connected session whose server hosts the registry.</param>
        /// <param name="registryNamespaceUri">The registry companion namespace URI.</param>
        /// <param name="telemetry">Telemetry context used by the generated proxies.</param>
        public GenericXRegistryClient(
            ISession session,
            string registryNamespaceUri,
            ITelemetryContext telemetry)
            : base(session, registryNamespaceUri, telemetry)
        {
        }

        /// <summary>
        /// Initializes a generic client bound to the abstract xRegistry base namespace.
        /// </summary>
        /// <param name="session">The connected session whose server hosts the registry.</param>
        /// <param name="telemetry">Telemetry context used by the generated proxies.</param>
        public GenericXRegistryClient(ISession session, ITelemetryContext telemetry)
            : base(session, XRegistryWellKnown.XRegistryNamespaceUri, telemetry)
        {
        }
    }
}
