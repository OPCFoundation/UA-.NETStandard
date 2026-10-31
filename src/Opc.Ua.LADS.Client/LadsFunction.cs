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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions.Streaming;

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// A handle to a LADS function that offers the read, observe and control
    /// operations of <see cref="LadsClient"/> fluently.
    /// </summary>
    public sealed class LadsFunction
    {
        /// <summary>
        /// Creates a handle.
        /// </summary>
        internal LadsFunction(
            LadsClient client,
            NodeId nodeId,
            string name,
            string displayName,
            LadsFunctionKind kind,
            string path)
        {
            Client = client;
            NodeId = nodeId;
            Name = name;
            DisplayName = displayName;
            Kind = kind;
            Path = path;
        }

        /// <summary>
        /// The client the handle belongs to.
        /// </summary>
        public LadsClient Client { get; }

        /// <summary>
        /// The NodeId of the function.
        /// </summary>
        public NodeId NodeId { get; }

        /// <summary>
        /// The browse name of the function.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The display name of the function.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// The most specific standard function type of the function.
        /// </summary>
        public LadsFunctionKind Kind { get; }

        /// <summary>
        /// The path of browse names from the functional unit, such as
        /// <c>Heater/Temperature</c>.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Whether the function is a sensor function.
        /// </summary>
        public bool IsSensor => LadsFunctionKinds.IsSensor(Kind);

        /// <summary>
        /// Whether the function is a control function.
        /// </summary>
        public bool IsControl => LadsFunctionKinds.IsControl(Kind);

        /// <summary>
        /// Reads a snapshot of the function.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The snapshot.</returns>
        public ValueTask<LadsFunctionSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            return Client.ReadFunctionAsync(this, cancellationToken);
        }

        /// <summary>
        /// Observes the sensor, control and state variables of the function.
        /// </summary>
        /// <param name="streaming">
        /// The streaming subscription to monitor with; <c>null</c> to use the
        /// default streaming subscription of a <see cref="ManagedSession"/>.
        /// </param>
        /// <param name="cancellationToken">The cancellation token that ends the observation.</param>
        /// <returns>The changes, starting with the current values.</returns>
        public IAsyncEnumerable<LadsValueChange> ObserveAsync(
            IStreamingSubscription? streaming = null,
            CancellationToken cancellationToken = default)
        {
            return Client.ObserveFunctionAsync(NodeId, streaming, cancellationToken);
        }

        /// <summary>
        /// Gets the functions nested in the function.
        /// </summary>
        /// <param name="recursive">Whether to include the functions nested deeper.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The functions.</returns>
        public ValueTask<ArrayOf<LadsFunction>> GetFunctionsAsync(
            bool recursive = true,
            CancellationToken cancellationToken = default)
        {
            return Client.GetFunctionsAsync(NodeId, recursive, cancellationToken);
        }

        /// <summary>
        /// Starts the control function.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            return Client.StartFunctionAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Starts the control function with a new target value.
        /// </summary>
        /// <param name="targetValue">The target value.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask StartAsync(Variant targetValue, CancellationToken cancellationToken = default)
        {
            return Client.StartFunctionWithTargetValueAsync(NodeId, targetValue, cancellationToken);
        }

        /// <summary>
        /// Stops the control function.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            return Client.StopFunctionAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Aborts the control function.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask AbortAsync(CancellationToken cancellationToken = default)
        {
            return Client.AbortFunctionAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Changes the target value of an analog control function with a relative
        /// target value by an offset.
        /// </summary>
        /// <param name="value">The offset to add to the target value.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask ModifyTargetValueByAsync(double value, CancellationToken cancellationToken = default)
        {
            return Client.ModifyTargetValueByAsync(NodeId, value, cancellationToken);
        }

        /// <summary>
        /// Resets the totalizer of an analog control function with a totalizer.
        /// </summary>
        /// <param name="value">The value to reset the totalizer to.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask ResetTotalizerAsync(double value = 0, CancellationToken cancellationToken = default)
        {
            return Client.ResetTotalizerAsync(NodeId, value, cancellationToken);
        }

        /// <summary>
        /// Opens the cover.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask OpenAsync(CancellationToken cancellationToken = default)
        {
            return Client.OpenCoverAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Closes the cover.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask CloseAsync(CancellationToken cancellationToken = default)
        {
            return Client.CloseCoverAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Locks the cover.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask LockAsync(CancellationToken cancellationToken = default)
        {
            return Client.LockCoverAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Unlocks the cover.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask UnlockAsync(CancellationToken cancellationToken = default)
        {
            return Client.UnlockCoverAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Resets the cover from an error.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask ResetAsync(CancellationToken cancellationToken = default)
        {
            return Client.ResetCoverAsync(NodeId, cancellationToken);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Path;
        }
    }
}
