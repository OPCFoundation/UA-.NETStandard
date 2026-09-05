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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Nodes
{
    /// <summary>
    /// Contributes a materialized partition of the server address space.
    /// </summary>
    /// <remarks>
    /// A source is adapted to the existing asynchronous NodeManager engine.
    /// <see cref="BuildAsync"/> runs exactly once for each manager generation
    /// while that generation is being prepared and is not visible to clients.
    /// The supplied builder is sealed when the build succeeds. Each invocation
    /// must create a fresh graph; a source must not reuse mutable
    /// <see cref="NodeState"/> instances across generations.
    /// </remarks>
    public interface INodeSource
    {
        /// <summary>
        /// Gets the namespace URIs owned by this source.
        /// </summary>
        ArrayOf<string> NamespaceUris { get; }

        /// <summary>
        /// Builds and wires the source's node graph.
        /// </summary>
        /// <param name="builder">The graph builder for this generation.</param>
        /// <param name="cancellationToken">The token used to cancel preparation.</param>
        ValueTask BuildAsync(
            INodeGraphBuilder builder,
            CancellationToken cancellationToken = default);
    }
}
