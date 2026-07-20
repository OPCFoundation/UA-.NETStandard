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
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.PubSub.Server.SchemaRegistry
{
    /// <summary>
    /// The PubSub Schema Registry registration node manager: the generic
    /// <see cref="XRegistryRegistrationNodeManager"/> configured with the Schema Registry namespace
    /// and the schema content-id provider so a <c>Close</c> auto-bootstraps the schema's Opaque
    /// SchemaId-NodeId (§5.2, §10.1).
    /// </summary>
    [Experimental("UA_NETStandard_Encoders")]
    public sealed class SchemaRegistryRegistrationNodeManager : XRegistryRegistrationNodeManager
    {
        /// <summary>
        /// Initializes the schema registration node manager.
        /// </summary>
        /// <param name="server">The server that owns the node manager.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="options">The Schema Registry feature options.</param>
        public SchemaRegistryRegistrationNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            SchemaRegistryOptions? options)
            : base(server, configuration, (options ?? new SchemaRegistryOptions()).ToServerOptions())
        {
        }
    }
}
