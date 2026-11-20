/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

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
