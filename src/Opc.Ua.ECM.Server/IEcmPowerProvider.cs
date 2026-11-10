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

namespace Opc.Ua.ECM.Server
{
    /// <summary>
    /// Supplies device-owned standby state and asynchronous control.
    /// The caller retains provider ownership; accepting a command does not imply arrival.
    /// </summary>
    public interface IEcmStandbyProvider
    {
        /// <summary>
        /// Reads a coherent state and current mode characteristics.
        /// </summary>
        ValueTask<EcmPowerSnapshot> ReadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Requests a device-selected mode for the pause duration in milliseconds.
        /// </summary>
        ValueTask<EcmControlResult> StartPauseAsync(double pauseTime, CancellationToken cancellationToken = default);

        /// <summary>
        /// Requests return to operation.
        /// </summary>
        ValueTask<EcmControlResult> EndPauseAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Requests a specific energy-saving mode.
        /// </summary>
        ValueTask<EcmControlResult> SwitchToEnergySavingModeAsync(
            byte modeId,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Optional device-owned WOL sleep capability. No wake transport is implied.
    /// </summary>
    public interface IEcmWolProvider
    {
        /// <summary>
        /// Gets the six-byte MAC address, not a preassembled wake packet.
        /// </summary>
        ByteString MacAddress { get; }

        /// <summary>
        /// Gets the regular recovery duration in milliseconds.
        /// </summary>
        double RegularTimeToOperate { get; }

        /// <summary>
        /// Gets the minimum sleep pause in milliseconds.
        /// </summary>
        double TimeMinPause { get; }

        /// <summary>
        /// Gets the sleep power in kW, with the UInt32 representation required by OPC 34100.
        /// </summary>
        uint ModePowerConsumption { get; }

        /// <summary>
        /// Requests WOL sleep; the device may disconnect after accepting the request.
        /// </summary>
        ValueTask<EcmControlResult> SwitchOffWolAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Coherent device state. Durations use milliseconds, power kW and energy kWh.
    /// </summary>
    /// <param name="Status">The standardized standby status.</param>
    /// <param name="SourceModeId">The source mode ID.</param>
    /// <param name="DestinationModeId">The destination mode ID.</param>
    /// <param name="PauseTime">The accepted pause duration.</param>
    /// <param name="CurrentTimeToDestination">The remaining transition estimate.</param>
    /// <param name="CurrentTimeToOperate">The remaining return-to-operation estimate.</param>
    /// <param name="RegularTimeToOperate">The regular recovery estimate.</param>
    /// <param name="ModePowerConsumption">The current mode power.</param>
    /// <param name="EnergyConsumptionToDestination">The transition energy estimate.</param>
    /// <param name="Quality">The acquisition quality.</param>
    /// <param name="SourceTimestamp">The acquisition timestamp.</param>
    /// <param name="Modes">The current mode characteristics.</param>
    public sealed record EcmPowerSnapshot(
        EcmStandbyStatus Status,
        byte SourceModeId,
        byte DestinationModeId,
        double PauseTime,
        double CurrentTimeToDestination,
        double CurrentTimeToOperate,
        double RegularTimeToOperate,
        float ModePowerConsumption,
        float EnergyConsumptionToDestination,
        StatusCode Quality,
        DateTime SourceTimestamp,
        ArrayOf<EcmEnergySavingMode> Modes);
}
