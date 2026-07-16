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
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Server.RuntimeNodeSet
{
    /// <summary>
    /// Configuration options for <see cref="RuntimeNodeSetNodeManagerFactory"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Sources"/> lists the NodeSet2 documents the factory will
    /// load. They are parsed in dependency order during
    /// <see cref="RuntimeNodeSetNodeManagerFactory.CreateAsync"/>; any
    /// inter-source dependency cycle detected at that point causes a
    /// <see cref="InvalidOperationException"/>.
    /// </para>
    /// <para>
    /// <see cref="DefaultNamespaceUri"/> controls the namespace index that
    /// <see cref="INodeManagerBuilder"/> browse-path lookups use when no
    /// explicit <c>ns=N;</c> prefix is given. If left <c>null</c> and
    /// exactly one top-level model is loaded (i.e. one source whose model
    /// namespace is not a dependency of any other included source), that
    /// model's URI is used automatically. If <see cref="Configure"/> is
    /// non-<c>null</c> and the namespace cannot be resolved uniquely,
    /// server startup fails with a clear error.
    /// </para>
    /// </remarks>
    public sealed class RuntimeNodeSetOptions
    {
        /// <summary>
        /// Gets or sets the ordered collection of NodeSet2 sources that
        /// will be imported into the server's address space. The factory
        /// derives import order from the RequiredModel dependency graph;
        /// the list order here is used only when two sources are
        /// otherwise unordered.
        /// </summary>
        public ArrayOf<RuntimeNodeSetSource> Sources { get; set; } = [];

        /// <summary>
        /// Gets or sets the model namespace URI used as the default
        /// namespace for untyped <see cref="INodeManagerBuilder"/>
        /// browse-path lookups (i.e. paths without an explicit
        /// <c>ns=N;</c> prefix).
        /// </summary>
        /// <remarks>
        /// When <c>null</c> the factory infers the default namespace
        /// automatically: if exactly one model is a "leaf" (not a
        /// dependency of any other loaded source) that model's URI is
        /// used. If the inference is ambiguous and <see cref="Configure"/>
        /// is non-<c>null</c>, the factory throws
        /// <see cref="InvalidOperationException"/> at startup.
        /// </remarks>
        public string? DefaultNamespaceUri { get; set; }

        /// <summary>
        /// Gets or sets an optional callback invoked with the fluent
        /// <see cref="INodeManagerBuilder"/> after all NodeSet2 nodes
        /// have been added to the address space.
        /// </summary>
        /// <remarks>
        /// Use this callback to wire simulation loops, event sources,
        /// value callbacks, or other runtime behaviours on top of the
        /// statically defined address-space nodes.
        /// </remarks>
        public Action<INodeManagerBuilder>? Configure { get; set; }
    }
}
