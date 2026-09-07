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

using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Positioning.Server.Hosting;
using Opc.Ua.Server;

namespace Opc.Ua.Positioning.Server
{
    /// <summary>
    /// Creates standalone <see cref="PositioningNodeManager"/> instances.
    /// </summary>
    public sealed class PositioningNodeManagerFactory : IAsyncNodeManagerFactory
    {
        private readonly IPositioningPostSetupRunner? m_runner;

        /// <summary>
        /// Creates a factory without dependency-injection configurators.
        /// </summary>
        public PositioningNodeManagerFactory()
            : this(null)
        {
        }

        /// <summary>
        /// Creates a factory with Positioning post-setup support.
        /// </summary>
        public PositioningNodeManagerFactory(IPositioningPostSetupRunner? runner)
        {
            m_runner = runner;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris =>
        [
            Rsl.Namespaces.RSL,
            Gpos.Namespaces.GPOS
        ];

        /// <inheritdoc/>
        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Ownership is transferred to the server.")]
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            IAsyncNodeManager manager =
                new PositioningNodeManager(server, configuration, m_runner);
            return new ValueTask<IAsyncNodeManager>(manager);
        }
    }
}
