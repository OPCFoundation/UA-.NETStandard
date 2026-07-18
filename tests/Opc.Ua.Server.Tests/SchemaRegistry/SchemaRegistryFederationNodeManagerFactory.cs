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

namespace Opc.Ua.Server.Tests.SchemaRegistry
{
    /// <summary>
    /// <see cref="INodeManagerFactory"/> for the <see cref="SchemaRegistryFederationNodeManager"/>.
    /// It declares the Schema Registry namespace so the federated schema proxy lives alongside
    /// the runtime-loaded companion NodeSet.
    /// </summary>
    internal sealed class SchemaRegistryFederationNodeManagerFactory : INodeManagerFactory
    {
        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris =>
            [SchemaRegistryTestServer.SchemaRegistryNamespaceUri];

        /// <inheritdoc/>
        public INodeManager Create(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            // Ownership of the node manager is transferred to the server.
#pragma warning disable CA2000 // Ownership of the node manager is transferred to the server.
            return new SchemaRegistryFederationNodeManager(server, configuration);
#pragma warning restore CA2000
        }
    }
}
