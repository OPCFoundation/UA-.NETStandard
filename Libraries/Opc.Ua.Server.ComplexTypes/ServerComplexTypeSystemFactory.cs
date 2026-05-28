/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.ComplexTypes;

namespace Opc.Ua.Server.ComplexTypes
{
    /// <summary>
    /// Dependency-injected factory that produces a fresh
    /// <see cref="ServerComplexTypeSystem"/> bound to a caller-supplied
    /// <see cref="IServerInternal"/> and the host's
    /// <see cref="ITelemetryContext"/>.
    /// </summary>
    /// <remarks>
    /// Registered as a singleton by
    /// <c>IOpcUaServerBuilder.AddComplexTypes()</c>. Server hosts resolve
    /// this factory and call <see cref="Create(IServerInternal)"/> when a
    /// concrete <see cref="IServerInternal"/> becomes available
    /// (typically inside the server's <c>CreateMasterNodeManager</c> or
    /// after server start-up) to obtain a type-loader scoped to that
    /// server instance.
    /// </remarks>
    public sealed class ServerComplexTypeSystemFactory
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="telemetry">The shared telemetry context.</param>
        /// <exception cref="ArgumentNullException"><paramref name="telemetry"/> is <c>null</c>.</exception>
        public ServerComplexTypeSystemFactory(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Creates a new <see cref="ServerComplexTypeSystem"/> bound to
        /// <paramref name="server"/> and the host's
        /// <see cref="ITelemetryContext"/>, using the AOT-friendly
        /// <see cref="DefaultComplexTypeFactory"/>.
        /// </summary>
        /// <param name="server">The hosted server.</param>
        /// <returns>A fresh <see cref="ServerComplexTypeSystem"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="server"/> is <c>null</c>.</exception>
        public ServerComplexTypeSystem Create(IServerInternal server)
        {
            return Create(server, new DefaultComplexTypeFactory());
        }

        /// <summary>
        /// Creates a new <see cref="ServerComplexTypeSystem"/> bound to
        /// <paramref name="server"/> using a caller-supplied
        /// <see cref="IComplexTypeFactory"/> (for example the
        /// Reflection.Emit-based <c>ComplexTypeBuilderFactory</c>).
        /// </summary>
        /// <param name="server">The hosted server.</param>
        /// <param name="factory">The complex type builder factory.</param>
        /// <returns>A fresh <see cref="ServerComplexTypeSystem"/>.</returns>
        /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
        public ServerComplexTypeSystem Create(
            IServerInternal server,
            IComplexTypeFactory factory)
        {
            if (server is null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            return new ServerComplexTypeSystem(server, factory, m_telemetry);
        }

        private readonly ITelemetryContext m_telemetry;
    }
}
