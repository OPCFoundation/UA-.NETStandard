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
    /// Marks a user-authored partial class as the source-generated
    /// <see cref="CustomNodeManager2"/> target for an OPC UA model design.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The OPC UA source generator scans for this attribute. When found,
    /// it emits a companion <c>partial class</c> (in the same namespace
    /// and with the same name as the attributed class) that derives from
    /// <see cref="CustomNodeManager2"/>, loads the predefined nodes for
    /// the matching design, and exposes the
    /// <c>partial void Configure(INodeManagerBuilder builder)</c> hook.
    /// A matching <c>{ClassName}Factory</c> implementing
    /// <see cref="INodeManagerFactory"/> is also emitted unless
    /// <see cref="GenerateFactory"/> is set to <c>false</c>.
    /// </para>
    /// <para>
    /// This is the recommended opt-in mechanism. The MSBuild property
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
        public string NamespaceUri { get; set; }

        /// <summary>
        /// Optional design file logical name (the file name without
        /// extension). Alternative selector when matching by
        /// <see cref="NamespaceUri"/> is inconvenient.
        /// </summary>
        public string Design { get; set; }

        /// <summary>
        /// When <c>true</c> (default) the generator also emits a
        /// <c>{ClassName}Factory</c> sibling implementing
        /// <see cref="INodeManagerFactory"/>. Set to <c>false</c> to
        /// author the factory by hand.
        /// </summary>
        public bool GenerateFactory { get; set; } = true;
    }
}
