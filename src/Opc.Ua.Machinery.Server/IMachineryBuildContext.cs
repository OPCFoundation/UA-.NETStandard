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
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Machinery.Server
{
    /// <summary>
    /// Build context shared by Machinery hosting configurators.
    /// </summary>
    public interface IMachineryBuildContext
    {
        /// <summary>
        /// Gets the active DI node manager.
        /// </summary>
        DiNodeManager Manager { get; }

        /// <summary>
        /// Gets the active system context.
        /// </summary>
        ISystemContext Context { get; }

        /// <summary>
        /// Gets the single fluent node-manager builder owned by this context.
        /// </summary>
        INodeManagerBuilder Nodes { get; }

        /// <summary>
        /// Gets the application-owned instance namespace index.
        /// </summary>
        ushort InstanceNamespaceIndex { get; }

        /// <summary>
        /// Gets the OPC 40001 parts this context can build.
        /// </summary>
        MachineryParts Parts { get; }

        /// <summary>
        /// Gets the OPC 40001-1 <c>Machines</c> folder. Machines are
        /// <c>Organizes</c>-referenced from here, and — contrary to the obvious
        /// guess — not from the Device Integration <c>DeviceSet</c>.
        /// </summary>
        NodeState MachinesFolder { get; }

        /// <summary>
        /// Gets the Device Integration <c>DeviceSet</c> node, for a machine
        /// that is also a DI device.
        /// </summary>
        NodeState DeviceSet { get; }

        /// <summary>
        /// Gets the hosting cancellation token.
        /// </summary>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// Starts a machine below the <c>Machines</c> folder. The machine is a
        /// plain object; use
        /// <see cref="AddMachine{TState}(TState, QualifiedName)"/> to place an
        /// instance of a companion-specification type instead.
        /// </summary>
        /// <param name="browseName">
        /// The browse name of the machine. A bare name is qualified with the
        /// application-owned instance namespace.
        /// </param>
        IMachineBuilder<BaseObjectState> AddMachine(QualifiedName browseName);

        /// <summary>
        /// Starts a machine below the <c>Machines</c> folder from an instance a
        /// companion specification created — an OPC 40223 <c>PumpState</c>, for
        /// example. The Machinery building blocks are added to that instance,
        /// so a vendor model keeps its own type definition and still answers
        /// the Machinery browse paths.
        /// </summary>
        /// <typeparam name="TState">The machine's state type.</typeparam>
        /// <param name="machine">The instance to adopt.</param>
        /// <param name="browseName">
        /// The browse name of the machine. Pass
        /// <see cref="QualifiedName.Null"/> to keep the instance's own.
        /// </param>
        IMachineBuilder<TState> AddMachine<TState>(
            TState machine,
            QualifiedName browseName = default)
            where TState : BaseObjectState;

        /// <summary>
        /// Resolves a required application service.
        /// </summary>
        /// <typeparam name="T">The required service contract.</typeparam>
        T GetRequiredService<T>() where T : notnull;

        /// <summary>
        /// Resolves an optional application service.
        /// </summary>
        /// <typeparam name="T">The service contract.</typeparam>
        T? GetService<T>() where T : class;

        /// <summary>
        /// Seals the builder, completes the registrations that could not finish
        /// synchronously and starts configured simulations.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        ValueTask SealAsync(CancellationToken cancellationToken = default);
    }
}
