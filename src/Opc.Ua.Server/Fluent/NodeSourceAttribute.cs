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

using System;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Marks a user-authored partial class as the source-generated
    /// <see cref="Nodes.INodeSource"/> for an OPC UA model design.
    /// </summary>
    /// <remarks>
    /// The generator completes the annotated partial class with
    /// <see cref="Nodes.INodeSource"/>, typed graph traversal, and typed
    /// NodeSet import factories. The generated source is hosted with
    /// <c>AddNodeSource&lt;TSource&gt;()</c>.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class NodeSourceAttribute : Attribute
    {
        /// <summary>
        /// Namespace URI of the model design this source binds to.
        /// </summary>
        /// <remarks>
        /// Required when the project contains more than one design.
        /// Optional when there is exactly one design.
        /// </remarks>
        public string NamespaceUri { get; set; } = null!;

        /// <summary>
        /// Optional design file logical name, without its extension.
        /// </summary>
        public string Design { get; set; } = null!;

        /// <summary>
        /// Additional namespace URIs owned by the source beyond the model
        /// namespace, such as a separate instance namespace.
        /// </summary>
        public string[] AdditionalNamespaceUris { get; set; } = null!;
    }
}
