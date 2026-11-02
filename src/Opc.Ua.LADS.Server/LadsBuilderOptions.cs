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
using Opc.Ua.Machinery.Server.StateMachines;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// The identification of a LADS device or component (OPC 30500-1 §7.1, OPC 10000-100 §5.6).
    /// </summary>
    public sealed class LadsIdentification
    {
        /// <summary>
        /// Gets or sets the manufacturer.
        /// </summary>
        public LocalizedText Manufacturer { get; set; }

        /// <summary>
        /// Gets or sets the URI of the manufacturer.
        /// </summary>
        public string? ManufacturerUri { get; set; }

        /// <summary>
        /// Gets or sets the model.
        /// </summary>
        public LocalizedText Model { get; set; }

        /// <summary>
        /// Gets or sets the serial number.
        /// </summary>
        public string? SerialNumber { get; set; }

        /// <summary>
        /// Gets or sets the hardware revision.
        /// </summary>
        public string? HardwareRevision { get; set; }

        /// <summary>
        /// Gets or sets the software revision.
        /// </summary>
        public string? SoftwareRevision { get; set; }

        /// <summary>
        /// Gets or sets the device revision.
        /// </summary>
        public string? DeviceRevision { get; set; }

        /// <summary>
        /// Gets or sets the address of the device manual.
        /// </summary>
        public string? DeviceManual { get; set; }

        /// <summary>
        /// Gets or sets the globally unique product instance URI.
        /// </summary>
        public string? ProductInstanceUri { get; set; }

        /// <summary>
        /// Gets or sets the product code.
        /// </summary>
        public string? ProductCode { get; set; }

        /// <summary>
        /// Gets or sets the device class.
        /// </summary>
        public string? DeviceClass { get; set; }

        /// <summary>
        /// Gets or sets the asset id assigned by the user.
        /// </summary>
        public string? AssetId { get; set; }

        /// <summary>
        /// Gets or sets the component name assigned by the user.
        /// </summary>
        public LocalizedText ComponentName { get; set; }

        /// <summary>
        /// Gets or sets the date the device was first operated.
        /// </summary>
        public DateTimeUtc InitialOperationDate { get; set; }
    }

    /// <summary>
    /// The configuration of an analog control function.
    /// </summary>
    public sealed class LadsAnalogControlOptions
    {
        /// <summary>
        /// Gets or sets the variant of the analog control function type.
        /// </summary>
        public LadsAnalogControlKind Kind { get; set; } = LadsAnalogControlKind.Plain;

        /// <summary>
        /// Gets or sets the engineering units.
        /// </summary>
        public EUInformation? EngineeringUnits { get; set; }

        /// <summary>
        /// Gets or sets the range of the values.
        /// </summary>
        public Range? Range { get; set; }

        /// <summary>
        /// Gets or sets the initial target value.
        /// </summary>
        public double InitialTargetValue { get; set; }

        /// <summary>
        /// Gets or sets the initial current value.
        /// </summary>
        public double InitialCurrentValue { get; set; }

        /// <summary>
        /// Gets or sets the increase rate of a relative control function.
        /// </summary>
        public double? IncreaseRate { get; set; }

        /// <summary>
        /// Gets or sets the decrease rate of a relative control function.
        /// </summary>
        public double? DecreaseRate { get; set; }

        /// <summary>
        /// Gets or sets the names of the target values of a composed control function.
        /// </summary>
        public ArrayOf<string> ComposedTargetValues { get; set; }
    }

    /// <summary>
    /// The configuration of a multi-mode analog control function.
    /// </summary>
    public sealed class LadsMultiModeAnalogControlOptions
    {
        /// <summary>
        /// Gets or sets the names of the controller modes; at least one is required.
        /// </summary>
        public ArrayOf<string> Modes { get; set; }

        /// <summary>
        /// Gets or sets the initially selected mode.
        /// </summary>
        public uint InitialMode { get; set; }

        /// <summary>
        /// Gets or sets the engineering units of the controller parameters.
        /// </summary>
        public EUInformation? EngineeringUnits { get; set; }

        /// <summary>
        /// Gets or sets the range of the controller parameters.
        /// </summary>
        public Range? Range { get; set; }
    }

    /// <summary>
    /// The configuration of a two-state discrete control or sensor function.
    /// </summary>
    public sealed class LadsTwoStateDiscreteOptions
    {
        /// <summary>
        /// Gets or sets the text of the true state.
        /// </summary>
        public LocalizedText TrueState { get; set; } = new("On");

        /// <summary>
        /// Gets or sets the text of the false state.
        /// </summary>
        public LocalizedText FalseState { get; set; } = new("Off");

        /// <summary>
        /// Gets or sets the initial value.
        /// </summary>
        public bool InitialValue { get; set; }
    }

    /// <summary>
    /// The configuration of a multi-state discrete control or sensor function.
    /// </summary>
    public sealed class LadsMultiStateDiscreteOptions
    {
        /// <summary>
        /// Gets or sets the texts of the states; at least one is required.
        /// </summary>
        public ArrayOf<LocalizedText> States { get; set; }

        /// <summary>
        /// Gets or sets the initial value.
        /// </summary>
        public uint InitialValue { get; set; }
    }

    /// <summary>
    /// The configuration of a timer control function.
    /// </summary>
    public sealed class LadsTimerOptions
    {
        /// <summary>
        /// Gets or sets the engineering units, milliseconds by default.
        /// </summary>
        public EUInformation? EngineeringUnits { get; set; }

        /// <summary>
        /// Gets or sets the range of the values.
        /// </summary>
        public Range? Range { get; set; }

        /// <summary>
        /// Gets or sets the initial target duration in milliseconds.
        /// </summary>
        public double InitialTargetValue { get; set; }
    }

    /// <summary>
    /// The configuration of an analog scalar or array sensor function.
    /// </summary>
    public sealed class LadsAnalogSensorOptions
    {
        /// <summary>
        /// Gets or sets the engineering units.
        /// </summary>
        public EUInformation? EngineeringUnits { get; set; }

        /// <summary>
        /// Gets or sets the range of the values.
        /// </summary>
        public Range? Range { get; set; }

        /// <summary>
        /// Gets or sets the initial value of a scalar sensor.
        /// </summary>
        public double InitialValue { get; set; }

        /// <summary>
        /// Gets or sets the initial values of an array sensor.
        /// </summary>
        public ArrayOf<double> InitialValues { get; set; }

        /// <summary>
        /// Gets or sets whether a scalar sensor exposes a compensation value.
        /// </summary>
        public bool WithCompensation { get; set; }
    }

    /// <summary>
    /// The configuration of a cover function.
    /// </summary>
    public sealed class LadsCoverOptions
    {
        /// <summary>
        /// Gets or sets the initial state.
        /// </summary>
        public LadsCoverStateValue InitialState { get; set; } = LadsCoverStateValue.Closed;

        /// <summary>
        /// Gets or sets whether the cover can be locked.
        /// </summary>
        public bool Lockable { get; set; } = true;
    }

    /// <summary>
    /// The configuration of a maintenance task.
    /// </summary>
    public sealed class LadsMaintenanceTaskOptions
    {
        /// <summary>
        /// Gets or sets the message of the maintenance condition.
        /// </summary>
        public LocalizedText Message { get; set; }

        /// <summary>
        /// Gets or sets the interval the task recurs in.
        /// </summary>
        public TimeSpan? RecurrencePeriod { get; set; }

        /// <summary>
        /// Gets or sets the planned date.
        /// </summary>
        public DateTimeUtc PlannedDate { get; set; }

        /// <summary>
        /// Gets or sets the estimated downtime.
        /// </summary>
        public TimeSpan? EstimatedDowntime { get; set; }
    }
}
