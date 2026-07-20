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

namespace Opc.Ua.Server.SchemaRegistry
{
    /// <summary>
    /// Options for the optional, dependency-injectable in-server Schema Registry feature. The
    /// defaults reproduce the well-known xRegistry base and Schema Registry companion namespaces
    /// (<see cref="SchemaRegistryWellKnown"/>); a host may override the namespace URIs to run the
    /// registry under a private namespace.
    /// </summary>
    [Experimental("UA_NETStandard_Encoders")]
    public sealed class SchemaRegistryOptions
    {
        /// <summary>
        /// The abstract xRegistry base companion namespace URI. Defaults to
        /// <see cref="SchemaRegistryWellKnown.XRegistryNamespaceUri"/>.
        /// </summary>
        public string XRegistryNamespaceUri { get; set; } =
            SchemaRegistryWellKnown.XRegistryNamespaceUri;

        /// <summary>
        /// The Schema Registry companion namespace URI. Defaults to
        /// <see cref="SchemaRegistryWellKnown.SchemaRegistryNamespaceUri"/>.
        /// </summary>
        public string SchemaRegistryNamespaceUri { get; set; } =
            SchemaRegistryWellKnown.SchemaRegistryNamespaceUri;

        /// <summary>
        /// When <c>true</c> (default) the fast-path node manager pre-publishes its well-known seed
        /// schema so a fresh server can resolve at least one content-addressed schema before any
        /// registration; set <c>false</c> to start with an empty registry.
        /// </summary>
        public bool PublishSeedSchema { get; set; } = true;

        /// <summary>
        /// When <c>true</c> (default) the federation node manager publishes a proxy for a schema
        /// hosted by a remote registry, proving the federation model; set <c>false</c> to omit it.
        /// </summary>
        public bool PublishFederationProxy { get; set; } = true;
    }
}
