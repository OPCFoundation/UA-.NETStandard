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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Schema;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A <see cref="StandardServer"/> that automatically builds and registers
    /// dynamic stand-in encodeables for runtime-loaded custom DataTypes once
    /// the address space is available (before the server begins accepting
    /// connections). It is used by the dependency-injection hosting when
    /// complex type support is enabled and can also be used directly as a
    /// drop-in replacement for <see cref="StandardServer"/>.
    /// </summary>
    public sealed class ComplexTypeStandardServer : StandardServer
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="timeProvider">The time provider, or <c>null</c> for the system provider.</param>
        /// <param name="options">The complex type load options, or <c>null</c> for the defaults.</param>
        /// <param name="registry">An optional schema registry to populate.</param>
        public ComplexTypeStandardServer(
            ITelemetryContext telemetry,
            TimeProvider? timeProvider = null,
            ServerComplexTypeOptions? options = null,
            DataTypeDefinitionRegistry? registry = null)
            : base(telemetry, timeProvider)
        {
            m_options = options;
            m_registry = registry;
        }

        /// <summary>
        /// The dependency-injection resolver holder that is filled with the
        /// factory-backed resolver once the complex types have been loaded.
        /// </summary>
        internal ServerDataTypeDefinitionResolver? ResolverHolder { get; set; }

        /// <inheritdoc/>
        protected override async ValueTask OnNodeManagerStartedAsync(
            IServerInternal server,
            CancellationToken cancellationToken = default)
        {
            await base.OnNodeManagerStartedAsync(server, cancellationToken)
                .ConfigureAwait(false);

            IDataTypeDefinitionResolver resolver = await server
                .LoadComplexTypesAsync(server.Telemetry, m_options, m_registry, cancellationToken)
                .ConfigureAwait(false);

            ResolverHolder?.SetResolver(resolver);
        }

        private readonly ServerComplexTypeOptions? m_options;
        private readonly DataTypeDefinitionRegistry? m_registry;
    }
}
