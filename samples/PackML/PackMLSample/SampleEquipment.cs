/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.PackML;
using Opc.Ua.PackML.Server;

namespace PackMLSample
{
    /// <summary>
    /// Deterministic simulated equipment. No operation controls physical hardware.
    /// The sample process calls CompleteActingStateAsync after a deliberate delay.
    /// </summary>
    internal sealed class SampleEquipment : IPackMLEquipmentProvider
    {
        /// <summary>
        /// Gets the last accepted request identity, separate from completion.
        /// </summary>
        public long AcceptedRequestId { get; private set; }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> AuthorizeAsync(
            ISystemContext context,
            PackMLRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Operation == PackMLOperation.ResetCounters)
            {
                return new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadUserAccessDenied));
            }
            if (request.Operation is PackMLOperation.SetProduct or PackMLOperation.SetParameter &&
                request.CurrentState != PackMLStateNumbers.Stopped)
            {
                return new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadInvalidState));
            }
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> ExecuteAsync(
            PackMLRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_sync)
            {
                AcceptedRequestId = request.RequestId;
                switch (request.Operation)
                {
                    case PackMLOperation.SetUnitMode:
                        m_mode = request.Mode;
                        break;
                    case PackMLOperation.SetMachSpeed:
                        m_speed = request.MachineSpeed / 2;
                        break;
                    case PackMLOperation.SetInterlock:
                        m_ready[request.InterlockId] = !request.InterlockState;
                        break;
                    case PackMLOperation.RemoteCommand:
                        m_remote = request.RemoteInterfaces.ConvertAll(
                            value => (PackMLRemoteInterfaceDataType)value.Clone());
                        break;
                }
            }
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        /// <inheritdoc/>
        public ValueTask<PackMLEquipmentSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_sync)
            {
                return new ValueTask<PackMLEquipmentSnapshot>(new PackMLEquipmentSnapshot
                {
                    CurrentMode = m_mode,
                    CurrentMachineSpeed = m_speed,
                    EquipmentBlocked = !m_ready[0],
                    MaterialReadiness = ArrayOf.Wrapped((bool[])m_ready.Clone()),
                    RemoteParameters = m_remote.ConvertAll(value => (PackMLRemoteInterfaceDataType)value.Clone()),
                    SourceTimestamp = DateTime.UtcNow,
                    StatusCode = StatusCodes.Good
                });
            }
        }

        /// <summary>Protects the simulated equipment snapshot.</summary>
        private readonly System.Threading.Lock m_sync = new();

        /// <summary>The two simulated material-readiness signals.</summary>
        private readonly bool[] m_ready = [true, true];

        /// <summary>The simulated equipment's current mode.</summary>
        private int m_mode = 1;

        /// <summary>The simulated actual speed, independent of the requested setpoint.</summary>
        private float m_speed;

        /// <summary>The simulated equipment's acknowledged remote interface data.</summary>
        private ArrayOf<PackMLRemoteInterfaceDataType> m_remote;
    }
}
