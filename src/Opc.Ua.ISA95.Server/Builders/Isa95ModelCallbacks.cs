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

#pragma warning disable IDE0005 // Imports are required by target frameworks without matching implicit global usings.
using System.Threading;
using System.Threading.Tasks;
#pragma warning restore IDE0005

namespace Opc.Ua.ISA95.Server.Builders
{
    /// <summary>
    /// Asynchronously registers a newly created node with the hosting node
    /// manager (for example by adding it to the address space). Supplied to the
    /// <see cref="Isa95ModelBuilder"/> so the builder does not depend on a
    /// concrete node manager type.
    /// </summary>
    /// <param name="node">
    /// The node to register.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel the operation.
    /// </param>
    /// <returns>
    /// A task that completes when the node has been registered.
    /// </returns>
    public delegate ValueTask Isa95RegisterNodeAsync(
        NodeState node,
        CancellationToken cancellationToken);

    /// <summary>
    /// Asynchronously removes a previously registered node from the hosting node
    /// manager. Supplied to the <see cref="Isa95ModelBuilder"/> so the builder
    /// does not depend on a concrete node manager type.
    /// </summary>
    /// <param name="node">
    /// The node to remove.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel the operation.
    /// </param>
    /// <returns>
    /// A task that completes when the node has been removed.
    /// </returns>
    public delegate ValueTask Isa95RemoveNodeAsync(
        NodeState node,
        CancellationToken cancellationToken);
}
