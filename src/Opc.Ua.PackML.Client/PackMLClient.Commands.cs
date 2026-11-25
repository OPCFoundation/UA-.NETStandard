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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PackML.Client
{
    /// <summary>
    /// A PackML state command and its owning nested state machine.
    /// </summary>
    public enum PackMLStateCommand
    {
        /// <summary>
        /// Requests abort on the base state machine.
        /// </summary>
        Abort,

        /// <summary>
        /// Requests clear on the base state machine.
        /// </summary>
        Clear,

        /// <summary>
        /// Requests stop on the machine state machine.
        /// </summary>
        Stop,

        /// <summary>
        /// Requests reset on the machine state machine.
        /// </summary>
        Reset,

        /// <summary>
        /// Requests reset on the execute state machine.
        /// </summary>
        ResetExecute,

        /// <summary>
        /// Requests start on the execute state machine.
        /// </summary>
        Start,

        /// <summary>
        /// Requests hold on the execute state machine.
        /// </summary>
        Hold,

        /// <summary>
        /// Requests unhold on the execute state machine.
        /// </summary>
        Unhold,

        /// <summary>
        /// Requests suspend on the execute state machine.
        /// </summary>
        Suspend,

        /// <summary>
        /// Requests completion on the execute state machine.
        /// </summary>
        ToComplete,

        /// <summary>
        /// Requests unsuspend on the execute state machine.
        /// </summary>
        Unsuspend
    }

    public sealed partial class PackMLClient
    {
        /// <summary>
        /// Gets the generated unit proxy over the borrowed session.
        /// Successful calls indicate provider acceptance, never equipment completion.
        /// </summary>
        public PackMLBaseObjectTypeClient GetUnitProxy(NodeId unit)
        {
            if (unit.IsNull)
            {
                throw new ArgumentException("A PackML instance NodeId is required.", nameof(unit));
            }
            return new PackMLBaseObjectTypeClient(Session, unit, Telemetry);
        }

        /// <summary>
        /// Requests a vendor-defined unit mode. Observe UnitModeCurrent separately to determine completion.
        /// Cancellation or transport failure after sending a request leaves its outcome uncertain; do not replay it.
        /// </summary>
        public ValueTask SetUnitModeAsync(NodeId unit, int requestedMode, CancellationToken cancellationToken = default)
        {
            return GetUnitProxy(unit).SetUnitModeAsync(requestedMode, cancellationToken);
        }

        /// <summary>
        /// Requests machine speed. Provider acceptance does not mean the measured speed has reached the target.
        /// </summary>
        public ValueTask SetMachSpeedAsync(
            NodeId unit,
            float requestedSpeed,
            CancellationToken cancellationToken = default)
        {
            return GetUnitProxy(unit).SetMachSpeedAsync(requestedSpeed, cancellationToken);
        }

        /// <summary>
        /// Requests product definitions through the generated proxy, without retrying uncertain outcomes.
        /// </summary>
        public ValueTask SetProductAsync(
            NodeId unit,
            ArrayOf<PackMLProductDataType> products,
            CancellationToken cancellationToken = default)
        {
            return GetUnitProxy(unit).SetProductAsync(products, cancellationToken);
        }

        /// <summary>
        /// Requests parameter definitions through the generated proxy.
        /// </summary>
        public ValueTask SetParameterAsync(
            NodeId unit,
            ArrayOf<PackMLDescriptorDataType> parameters,
            CancellationToken cancellationToken = default)
        {
            return GetUnitProxy(unit).SetParameterAsync(parameters, cancellationToken);
        }

        /// <summary>
        /// Requests start with the standard descriptor payload.
        /// </summary>
        public async ValueTask StartAsync(
            NodeId unit,
            ArrayOf<PackMLDescriptorDataType> parameters,
            CancellationToken cancellationToken = default)
        {
            NodeId node = await RequirePathAsync(
                unit, "BaseStateMachine/MachineState/ExecuteState", cancellationToken).ConfigureAwait(false);
            var proxy = new PackMLExecuteStateMachineTypeClient(Session, node, Telemetry);
            await proxy.StartAsync(parameters, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Invokes the optional remote command method. Missing support produces BadNotSupported.
        /// </summary>
        public async ValueTask RemoteCommandAsync(
            NodeId unit,
            ArrayOf<PackMLRemoteInterfaceDataType> remoteInterfaces,
            CancellationToken cancellationToken = default)
        {
            await RequirePathAsync(unit, "RemoteCommand", cancellationToken).ConfigureAwait(false);
            await GetUnitProxy(unit).RemoteCommandAsync(remoteInterfaces, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Invokes the optional interlock method using the Int32 InterlockId declared by OPC 30050.
        /// </summary>
        public async ValueTask SetInterlockAsync(
            NodeId unit,
            int interlockId,
            bool state,
            CancellationToken cancellationToken = default)
        {
            await RequirePathAsync(unit, "SetInterlock", cancellationToken).ConfigureAwait(false);
            await GetUnitProxy(unit).SetInterlockAsync(interlockId, state, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a typed state command to its owning nested machine through a generated proxy.
        /// The server decides whether the command is permitted in the active mode/state subset.
        /// A successful call means acceptance only; use ReadStatusAsync, ObserveAsync or WaitForAsync for completion.
        /// No transport, timeout or cancellation failure is retried.
        /// </summary>
        public async ValueTask CommandAsync(
            NodeId unit,
            PackMLStateCommand command,
            CancellationToken cancellationToken = default)
        {
            switch (command)
            {
                case PackMLStateCommand.Abort:
                case PackMLStateCommand.Clear:
                {
                    NodeId node = await RequirePathAsync(unit, "BaseStateMachine", cancellationToken)
                        .ConfigureAwait(false);
                    var proxy = new PackMLBaseStateMachineTypeClient(Session, node, Telemetry);
                    if (command == PackMLStateCommand.Abort)
                    {
                        await proxy.AbortAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await proxy.ClearAsync(cancellationToken).ConfigureAwait(false);
                    }
                    break;
                }
                case PackMLStateCommand.Stop:
                case PackMLStateCommand.Reset:
                {
                    NodeId node = await RequirePathAsync(unit, "BaseStateMachine/MachineState", cancellationToken)
                        .ConfigureAwait(false);
                    var proxy = new PackMLMachineStateMachineTypeClient(Session, node, Telemetry);
                    if (command == PackMLStateCommand.Stop)
                    {
                        await proxy.StopAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await proxy.ResetAsync(cancellationToken).ConfigureAwait(false);
                    }
                    break;
                }
                case PackMLStateCommand.ResetExecute:
                case PackMLStateCommand.Start:
                case PackMLStateCommand.Hold:
                case PackMLStateCommand.Unhold:
                case PackMLStateCommand.Suspend:
                case PackMLStateCommand.ToComplete:
                case PackMLStateCommand.Unsuspend:
                {
                    NodeId node = await RequirePathAsync(
                        unit, "BaseStateMachine/MachineState/ExecuteState", cancellationToken).ConfigureAwait(false);
                    var proxy = new PackMLExecuteStateMachineTypeClient(Session, node, Telemetry);
                    switch (command)
                    {
                        case PackMLStateCommand.ResetExecute:
                            await proxy.ResetAsync(cancellationToken).ConfigureAwait(false);
                            break;
                        case PackMLStateCommand.Start:
                            await proxy.StartAsync([], cancellationToken).ConfigureAwait(false);
                            break;
                        case PackMLStateCommand.Hold:
                            await proxy.HoldAsync(cancellationToken).ConfigureAwait(false);
                            break;
                        case PackMLStateCommand.Unhold:
                            await proxy.UnholdAsync(cancellationToken).ConfigureAwait(false);
                            break;
                        case PackMLStateCommand.Suspend:
                            await proxy.SuspendAsync(cancellationToken).ConfigureAwait(false);
                            break;
                        case PackMLStateCommand.ToComplete:
                            await proxy.ToCompleteAsync(cancellationToken).ConfigureAwait(false);
                            break;
                        case PackMLStateCommand.Unsuspend:
                            await proxy.UnsuspendAsync(cancellationToken).ConfigureAwait(false);
                            break;
                    }
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(command));
            }
        }

        /// <summary>
        /// Resolves an optional method or nested machine without sending any command.
        /// </summary>
        private async ValueTask<NodeId> RequirePathAsync(NodeId unit, string path, CancellationToken ct)
        {
            ArrayOf<string> paths = [path];
            ArrayOf<NodeId> nodes = await ResolvePathsAsync(unit, paths, ct).ConfigureAwait(false);
            return nodes[0].IsNull
                ? throw new ServiceResultException(StatusCodes.BadNotSupported, "The PackML member is not exposed.")
                : nodes[0];
        }
    }
}
