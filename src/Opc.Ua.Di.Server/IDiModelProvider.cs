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

namespace Opc.Ua.Di.Server
{
    /// <summary>
    /// Contributes compiled predefined model nodes, such as the types of a
    /// DI-based companion specification, to a node manager that owns the
    /// Device Integration address space.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Register a provider with <c>AddDiModel&lt;TProvider&gt;()</c>. Every
    /// DI-owning node manager (the plain <see cref="DiNodeManager"/> as well as
    /// the Machinery and Robotics managers) loads the registered providers after
    /// the DI model, so a companion model needs no knowledge of which manager
    /// hosts it.
    /// </para>
    /// <para>
    /// The DI model is loaded before a provider runs, but a provider can also be
    /// invoked by an owner that has loaded it as part of a larger model, so
    /// <see cref="AddPredefinedNodes"/> must tolerate nodes that already exist.
    /// </para>
    /// </remarks>
    public interface IDiModelProvider
    {
        /// <summary>
        /// Gets the deterministic provider execution order. Providers run in
        /// ascending order, ties broken by type name and registration order.
        /// </summary>
        int Order { get; }

        /// <summary>
        /// Gets the model namespace URIs contributed by this provider. The
        /// owning node manager registers them with the server.
        /// </summary>
        ArrayOf<string> NamespaceUris { get; }

        /// <summary>
        /// Adds compiled predefined nodes to <paramref name="nodes"/>.
        /// </summary>
        /// <param name="nodes">The collection that receives the nodes.</param>
        /// <param name="context">The system context.</param>
        void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context);
    }
}
