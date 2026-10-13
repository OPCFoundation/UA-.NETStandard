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
using Opc.Ua.MachineTool;

namespace Opc.Ua.GMS.Contracts
{
    /// <summary>
    /// Explicit optional GMS observations. The default retains all mandatory inherited and GMS structure.
    /// </summary>
    [Flags]
    public enum GmsFeatures
    {
        /// <summary>
        /// Mandatory identification, equipment, monitoring, production and result management only.
        /// </summary>
        None = 0,

        /// <summary>
        /// The identification's optional workspace and device subclass.
        /// </summary>
        Identification = 1,

        /// <summary>
        /// Native SensorType and MultiSensorType tools, always including the qualification countdown.
        /// </summary>
        Sensors = 2,

        /// <summary>
        /// Dynamic sensor inventories using the shared MachineTool publication and lifetime machinery.
        /// </summary>
        DynamicSensors = 4,

        /// <summary>
        /// Native part-loading observations.
        /// </summary>
        Loading = 8,

        /// <summary>
        /// References to the actual currently active sensor tools.
        /// </summary>
        ToolMonitoring = 16,

        /// <summary>
        /// Native rotary tables and sensor and tip exchange racks.
        /// </summary>
        Accessories = 32,

        /// <summary>
        /// Analog additional sensors with their native classification and engineering units.
        /// </summary>
        AdditionalSensors = 64,

        /// <summary>
        /// Observed calibration history, state and forecast, not a physical calibration command.
        /// </summary>
        Calibration = 128,

        /// <summary>
        /// Native GMS jobs and parts on the retained MachineTool production model.
        /// This does not claim the undefined GMS_ProductionStatisticsType conformance unit.
        /// </summary>
        Production = 256,

        /// <summary>
        /// Dynamic retained production plans.
        /// </summary>
        DynamicProduction = 512,

        /// <summary>
        /// Retained standard SensorWarningAlarmType conditions.
        /// </summary>
        SensorWarnings = 1024,

        /// <summary>
        /// All implemented GMS observation surfaces; each still requires an explicit provider observation.
        /// </summary>
        All = Identification | Sensors | DynamicSensors | Loading | ToolMonitoring | Accessories |
            AdditionalSensors | Calibration | Production | DynamicProduction | SensorWarnings
    }

    /// <summary>
    /// One immutable equipment acquisition. MachineTool supplies the inherited observations and revision;
    /// the GMS adapter supplies the derived model without introducing measurement or correction commands.
    /// </summary>
    public sealed partial record GmsSystemSnapshot
    {
        /// <summary>
        /// Gets inherited monitoring, retained production and any explicitly selected generic prognoses.
        /// Native GMS sensors are supplied separately rather than as untyped base tools.
        /// </summary>
        public MachineToolSnapshot MachineTool { get; init; } = new();
    }

    /// <summary>
    /// Supplies a complete coherent GMS observation, independent of OPC UA transport and server ownership.
    /// </summary>
    public interface IGmsProvider
    {
        /// <summary>
        /// Acquires observations without actuating the equipment. Returned graphs must remain immutable.
        /// </summary>
        ValueTask<GmsSystemSnapshot> ReadAsync(CancellationToken cancellationToken = default);
    }
}
