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

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Binds a user-authored partial class to an OPC UA model design for
    /// source-generated node authoring.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The user-authored untyped <c>Configure</c> implementation selects the
    /// generated runtime type:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///   <c>Configure(INodeGraphBuilder)</c> generates a compositional
    ///   <see cref="Nodes.INodeSource"/>.
    ///   </description></item>
    ///   <item><description>
    ///   <c>Configure(INodeManagerBuilder)</c> generates a
    ///   <see cref="FluentNodeManagerBase"/> and, by default, a matching
    ///   <see cref="INodeManagerFactory"/>.
    ///   </description></item>
    /// </list>
    /// <para>
    /// If neither untyped overload is implemented, node-manager generation is
    /// selected for compatibility. Implementing both overloads is invalid.
    /// The generated typed <c>Configure(I{ClassName}Builder)</c> overload does
    /// not select the runtime type.
    /// The MSBuild property
    /// <c>ModelSourceGeneratorGenerateNodeManager</c> remains as a
    /// project-wide fallback that produces conventionally-named managers
    /// (<c>{Prefix}NodeManager</c> in <c>namespace {Prefix}</c>) for
    /// designs that have no attributed class.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class NodeManagerAttribute : Attribute
    {
        /// <summary>
        /// Namespace URI of the model design this manager binds to.
        /// </summary>
        /// <remarks>
        /// Required when the project contains more than one design.
        /// Optional when there is exactly one design — in that case the
        /// generator binds to it automatically.
        /// </remarks>
        public string NamespaceUri { get; set; } = null!;

        /// <summary>
        /// Optional design file logical name (the file name without
        /// extension). Alternative selector when matching by
        /// <see cref="NamespaceUri"/> is inconvenient.
        /// </summary>
        public string Design { get; set; } = null!;

        /// <summary>
        /// When <c>true</c> (default) the generator also emits a
        /// <c>{ClassName}Factory</c> sibling implementing
        /// <see cref="INodeManagerFactory"/> when node-manager generation is
        /// selected. Set to <c>false</c> to author the factory by hand.
        /// This setting has no effect for node-source generation.
        /// </summary>
        public bool GenerateFactory { get; set; } = true;

        /// <summary>
        /// Additional namespace URIs the generated authoring type owns beyond
        /// the model's own namespace — typically a separate instance namespace (e.g.
        /// <c>"http://opcfoundation.org/UA/Boiler/Instance"</c>).
        /// </summary>
        /// <remarks>
        /// A generated node manager passes these to its base constructor and
        /// advertises them from its factory. A generated node source includes
        /// them in <see cref="Nodes.INodeSource.NamespaceUris"/>. In both cases
        /// the master node manager can route the namespaces from initial
        /// registration.
        /// </remarks>
        public string[] AdditionalNamespaceUris { get; set; } = null!;
    }
}
