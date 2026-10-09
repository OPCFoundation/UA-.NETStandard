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

namespace Opc.Ua.Scales
{
    /// <summary>
    /// The identification of a scale or scale system, published through the
    /// OPC 40001-1 <c>MachineIdentificationType</c> add-in OPC 40200 requires
    /// (§7.3, §7.4), or of a feeder or printer module through
    /// <c>MachineryComponentIdentificationType</c> (§7.1, §7.2).
    /// </summary>
    /// <remarks>
    /// <see cref="Manufacturer"/> and <see cref="SerialNumber"/> are
    /// mandatory everywhere; <see cref="ProductInstanceUri"/> is mandatory on
    /// a scale and a scale system. A field left <see langword="null"/> is not
    /// materialised: an optional nameplate field that is absent and one that
    /// is published empty are different statements.
    /// </remarks>
    public sealed record ScaleIdentification
    {
        /// <summary>
        /// Gets the manufacturer (DI <c>Manufacturer</c>, mandatory).
        /// </summary>
        public LocalizedText Manufacturer { get; init; }

        /// <summary>
        /// Gets the serial number (DI <c>SerialNumber</c>, mandatory).
        /// </summary>
        public string? SerialNumber { get; init; }

        /// <summary>
        /// Gets the globally unique product instance URI (DI
        /// <c>ProductInstanceUri</c>, mandatory on scales and scale systems).
        /// </summary>
        public string? ProductInstanceUri { get; init; }

        /// <summary>
        /// Gets the manufacturer URI.
        /// </summary>
        public string? ManufacturerUri { get; init; }

        /// <summary>
        /// Gets the model name.
        /// </summary>
        public LocalizedText Model { get; init; }

        /// <summary>
        /// Gets the product code.
        /// </summary>
        public string? ProductCode { get; init; }

        /// <summary>
        /// Gets the hardware revision.
        /// </summary>
        public string? HardwareRevision { get; init; }

        /// <summary>
        /// Gets the software revision.
        /// </summary>
        public string? SoftwareRevision { get; init; }

        /// <summary>
        /// Gets the device class.
        /// </summary>
        public string? DeviceClass { get; init; }

        /// <summary>
        /// Gets the user-assigned asset id (writable in the model).
        /// </summary>
        public string? AssetId { get; init; }

        /// <summary>
        /// Gets the user-assigned component name (writable in the model).
        /// </summary>
        public LocalizedText ComponentName { get; init; }

        /// <summary>
        /// Gets the location (OPC 40001-1 <c>Location</c>, writable in the model).
        /// </summary>
        public string? Location { get; init; }

        /// <summary>
        /// Gets the year of construction.
        /// </summary>
        public ushort? YearOfConstruction { get; init; }

        /// <summary>
        /// Gets the month of construction (1-12).
        /// </summary>
        public byte? MonthOfConstruction { get; init; }

        /// <summary>
        /// Gets the date of initial operation.
        /// </summary>
        public DateTime? InitialOperationDate { get; init; }
    }

    /// <summary>
    /// One weighing range of a scale: an instance of
    /// <c>WeighingRangeElementType</c> (OPC 40200 §7.5). A scale has at least
    /// one.
    /// </summary>
    /// <param name="Low">The lower limit of the range.</param>
    /// <param name="High">The upper limit (the maximum capacity) of the range.</param>
    /// <param name="ActualScaleInterval">
    /// The actual scale interval <c>d</c>: the resolution the weight is
    /// rounded to when the scale is not verified.
    /// </param>
    /// <param name="VerificationScaleInterval">
    /// The verification scale interval <c>e</c>: the resolution the weight is
    /// rounded to when the scale is verified (legal for trade).
    /// </param>
    public sealed record WeighingRangeDefinition(
        double Low,
        double High,
        double ActualScaleInterval,
        double VerificationScaleInterval)
    {
        /// <summary>
        /// Gets the engineering units of the range, or <see langword="null"/>
        /// for the scale's unit.
        /// </summary>
        public EUInformation? EngineeringUnits { get; init; }

        /// <summary>
        /// Checks the range is well-formed.
        /// </summary>
        /// <exception cref="ArgumentException">The range is not.</exception>
        public WeighingRangeDefinition Validate()
        {
            if (double.IsNaN(Low) || double.IsNaN(High) || High <= Low)
            {
                throw new ArgumentException(
                    "A weighing range needs a lower limit below its upper limit.");
            }
            if (!(ActualScaleInterval > 0) || !(VerificationScaleInterval > 0))
            {
                throw new ArgumentException(
                    "The actual and verification scale intervals must be positive.");
            }
            return this;
        }
    }

    /// <summary>
    /// A snapshot of a <c>WeightItemType</c> variable - the current or the
    /// registered weight of a scale (OPC 40200 §9.3).
    /// </summary>
    public sealed record ScaleReading
    {
        /// <summary>
        /// Gets the gross weight.
        /// </summary>
        public double Gross { get; init; }

        /// <summary>
        /// Gets the net weight (gross minus tare).
        /// </summary>
        public double Net { get; init; }

        /// <summary>
        /// Gets the tare.
        /// </summary>
        public double Tare { get; init; }

        /// <summary>
        /// Gets how the tare was set.
        /// </summary>
        public TareMode TareMode { get; init; }

        /// <summary>
        /// Gets whether the maximum capacity is exceeded.
        /// </summary>
        public bool Overload { get; init; }

        /// <summary>
        /// Gets whether the weight is below the minimum.
        /// </summary>
        public bool Underload { get; init; }

        /// <summary>
        /// Gets whether the weight is stable, when published.
        /// </summary>
        public bool? Stable { get; init; }

        /// <summary>
        /// Gets whether the gross weight is within the zero-setting range,
        /// that is, whether <c>SetZero</c> would succeed, when published.
        /// </summary>
        public bool? InsideZero { get; init; }

        /// <summary>
        /// Gets the id of the weighing range the weight is in, when published.
        /// </summary>
        public ushort? CurrentRangeId { get; init; }

        /// <summary>
        /// Gets the weight id, when published.
        /// </summary>
        public string? WeightId { get; init; }

        /// <summary>
        /// Gets the engineering units, when published.
        /// </summary>
        public EUInformation? EngineeringUnits { get; init; }

        /// <summary>
        /// Gets the source timestamp of the reading.
        /// </summary>
        public DateTime Timestamp { get; init; }

        /// <summary>
        /// Gets the status of the reading.
        /// </summary>
        public StatusCode StatusCode { get; init; }
    }
}
