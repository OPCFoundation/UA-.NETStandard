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
    /// Server side helpers that build dynamic stand-in encodeables for the
    /// custom DataTypes of a server address space and register them into the
    /// server's <see cref="IEncodeableFactory"/>. This lets a server that
    /// loaded a NodeSet at runtime encode and decode instances of DataTypes
    /// for which no compiled .NET type exists, reusing exactly the same
    /// NativeAOT friendly path as the client
    /// (<see cref="ComplexTypeSystem"/>).
    /// </summary>
    public static class ServerComplexTypeSystem
    {
        /// <summary>
        /// Builds stand-in encodeables for the custom DataTypes in the server
        /// address space (those with a <c>DataTypeDefinition</c> attribute that
        /// are not already backed by a compiled .NET type), registers them into
        /// the server's encodeable factory, and returns an
        /// <see cref="IDataTypeDefinitionResolver"/> backed by that factory.
        /// </summary>
        /// <remarks>
        /// DataTypes that are already known to the encodeable factory (for
        /// example the compiled, source-generated types) are skipped; only
        /// runtime-loaded DataTypes are turned into stand-ins. The returned
        /// resolver derives its data type definitions directly from the primed
        /// <see cref="IEncodeableFactory"/> (generated types and runtime
        /// stand-ins expose their definition via
        /// <see cref="IDataTypeDefinitionSource"/>), so no separate registry or
        /// address-space walk is required. When a <paramref name="registry"/> is
        /// supplied it is composed as a fallback for schema-only types that have
        /// no encodeable in the factory.
        /// </remarks>
        /// <param name="server">The server whose address space is inspected.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="options">The load options, or <c>null</c> for the defaults.</param>
        /// <param name="registry">An optional supplementary registry for
        /// schema-only types.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A resolver backed by the primed encodeable factory.</returns>
        /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
        public static async ValueTask<IDataTypeDefinitionResolver> LoadComplexTypesAsync(
            this IServerInternal server,
            ITelemetryContext telemetry,
            ServerComplexTypeOptions? options = null,
            DataTypeDefinitionRegistry? registry = null,
            CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            options ??= new ServerComplexTypeOptions();

            var resolver = new AddressSpaceComplexTypeResolver(server);
            var complexTypeSystem = new ComplexTypeSystem(resolver, telemetry)
            {
                // The server builds types from the DataTypeDefinition attribute
                // only; the OPC Binary/XML dictionary type system is not used.
                DisableDataTypeDictionary = true
            };

            await complexTypeSystem
                .LoadAsync(options.OnlyEnumTypes, options.ThrowOnError, cancellationToken)
                .ConfigureAwait(false);

            // The primed encodeable factory is the source of truth for data type
            // definitions; expose it as the resolver rather than materializing a
            // separate registry from an address-space walk.
#pragma warning disable UA_NETStandard_1
            IDataTypeDefinitionResolver factorySource =
                new EncodeableFactoryDefinitionSource(server.Factory, server.NamespaceUris);
#pragma warning restore UA_NETStandard_1
            if (registry != null)
            {
                return new CompositeDataTypeDefinitionResolver([factorySource, registry]);
            }
            return factorySource;
        }

        internal static async ValueTask<IDataTypeDefinitionResolver>
            LoadComplexTypesAsync(
                this IServerInternal server,
                ITelemetryContext telemetry,
                ServerComplexTypeOptions? options,
                DataTypeDefinitionRegistry? registry,
                IAsyncNodeManager additionalNodeManager,
                CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (additionalNodeManager == null)
            {
                throw new ArgumentNullException(nameof(additionalNodeManager));
            }

            options ??= new ServerComplexTypeOptions();
            var resolver = new AddressSpaceComplexTypeResolver(
                server,
                additionalNodeManager);
            var complexTypeSystem = new ComplexTypeSystem(resolver, telemetry)
            {
                DisableDataTypeDictionary = true
            };

            await complexTypeSystem
                .LoadAsync(
                    options.OnlyEnumTypes,
                    options.ThrowOnError,
                    cancellationToken)
                .ConfigureAwait(false);

#pragma warning disable UA_NETStandard_1
            IDataTypeDefinitionResolver factorySource =
                new EncodeableFactoryDefinitionSource(
                    server.Factory,
                    server.NamespaceUris);
#pragma warning restore UA_NETStandard_1
            if (registry != null)
            {
                return new CompositeDataTypeDefinitionResolver(
                    [factorySource, registry]);
            }
            return factorySource;
        }
    }
}
