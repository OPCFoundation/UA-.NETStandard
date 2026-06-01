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
    /// <c>IOpcUaServerBuilder.AddComplexTypes()</c>. By default the
    /// factory produces type loaders backed by the AOT-friendly
    /// <see cref="DefaultComplexTypeFactory"/>. Hosts that need
    /// runtime concrete .NET classes for custom DataTypes register the
    /// Reflection.Emit-based <c>ComplexTypeBuilderFactory</c> from
    /// <c>Opc.Ua.ComplexTypes.Emit</c> via
    /// <c>AddComplexTypesWithReflectionEmit()</c>.
    /// </remarks>
    public sealed class ServerComplexTypeSystemFactory
    {
        /// <summary>
        /// Initializes a new instance backed by
        /// <see cref="DefaultComplexTypeFactory"/>.
        /// </summary>
        /// <param name="telemetry">The shared telemetry context.</param>
        /// <exception cref="ArgumentNullException"><paramref name="telemetry"/> is <c>null</c>.</exception>
        public ServerComplexTypeSystemFactory(ITelemetryContext telemetry)
            : this(telemetry, static () => new DefaultComplexTypeFactory())
        {
        }

        /// <summary>
        /// Initializes a new instance backed by a caller-supplied
        /// <see cref="IComplexTypeFactory"/> source. The
        /// <paramref name="complexTypeFactoryFactory"/> delegate is
        /// invoked once per <see cref="Create(IServerInternal)"/> call
        /// so each <see cref="ServerComplexTypeSystem"/> gets its own
        /// builder factory.
        /// </summary>
        /// <param name="telemetry">The shared telemetry context.</param>
        /// <param name="complexTypeFactoryFactory">Delegate that
        /// produces a fresh <see cref="IComplexTypeFactory"/> per
        /// server instance.</param>
        /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
        public ServerComplexTypeSystemFactory(
            ITelemetryContext telemetry,
            Func<IComplexTypeFactory> complexTypeFactoryFactory)
        {
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_complexTypeFactoryFactory = complexTypeFactoryFactory ??
                throw new ArgumentNullException(nameof(complexTypeFactoryFactory));
        }

        /// <summary>
        /// Creates a new <see cref="ServerComplexTypeSystem"/> bound to
        /// <paramref name="server"/> and the host's
        /// <see cref="ITelemetryContext"/>.
        /// </summary>
        /// <param name="server">The hosted server.</param>
        /// <returns>A fresh <see cref="ServerComplexTypeSystem"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="server"/> is <c>null</c>.</exception>
        public ServerComplexTypeSystem Create(IServerInternal server)
        {
            return Create(server, m_complexTypeFactoryFactory());
        }

        /// <summary>
        /// Creates a new <see cref="ServerComplexTypeSystem"/> bound to
        /// <paramref name="server"/> using a caller-supplied
        /// <see cref="IComplexTypeFactory"/>. This overload bypasses
        /// the configured factory source and is useful when callers
        /// already hold a built factory.
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
        private readonly Func<IComplexTypeFactory> m_complexTypeFactoryFactory;
    }
}
