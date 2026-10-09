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

namespace Opc.Ua.Machinery.Server.Builders
{
    /// <summary>
    /// Mutable property bag for the OPC 40001-1 identification add-in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>MachineryItemIdentificationType</c> derives from the Device
    /// Integration <c>FunctionalGroupType</c>, so an identification add-in is a
    /// functional group rather than a free-standing object; the builder places
    /// it accordingly. Two of its properties are mandatory —
    /// <see cref="Manufacturer"/> and <see cref="SerialNumber"/> — and a
    /// machine adds <see cref="ProductInstanceUri"/> on top.
    /// </para>
    /// <para>
    /// Members left unset are skipped: the builder writes only what the
    /// configuration delegate assigned, so a partial nameplate does not
    /// overwrite values a companion model already wrote.
    /// </para>
    /// </remarks>
    public sealed class MachineryIdentificationData
    {
        /// <summary>
        /// Publishes the writable nameplate members on every instance and
        /// leaves them writable, satisfying OPC 40001-1's
        /// <c>… Identification Writable</c> units.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The units require <c>2:AssetId</c>, <c>2:ComponentName</c> and — on
        /// a machine — <c>Location</c> to be present on <em>all</em> instances,
        /// not only where a value was supplied, and to stay writable. The
        /// builder otherwise skips a member the configuration delegate left
        /// unset, so the presence guarantee has to be asked for.
        /// </para>
        /// <para>
        /// A server that sets this is accepting client writes to its
        /// nameplate; the model declares the three with
        /// <c>AccessLevel = CurrentRead | CurrentWrite</c>, and this is what
        /// makes the instance honour that declaration.
        /// </para>
        /// </remarks>
        public bool Writable { get; set; }

        /// <summary>
        /// Manufacturer of the machinery item. Mandatory in OPC 40001-1.
        /// </summary>
        public LocalizedText Manufacturer { get; set; }

        /// <summary>
        /// Manufacturer-assigned serial number. Mandatory in OPC 40001-1.
        /// </summary>
        public string? SerialNumber { get; set; }

        /// <summary>
        /// Globally unique product-instance URI. Mandatory on a machine
        /// (<c>MachineIdentificationType</c>), optional on a component.
        /// </summary>
        public string? ProductInstanceUri { get; set; }

        /// <summary>
        /// Globally unique manufacturer URI.
        /// </summary>
        public string? ManufacturerUri { get; set; }

        /// <summary>
        /// Model designation.
        /// </summary>
        public LocalizedText Model { get; set; }

        /// <summary>
        /// Manufacturer-defined product code.
        /// </summary>
        public string? ProductCode { get; set; }

        /// <summary>
        /// Hardware revision level.
        /// </summary>
        public string? HardwareRevision { get; set; }

        /// <summary>
        /// Software revision level.
        /// </summary>
        public string? SoftwareRevision { get; set; }

        /// <summary>
        /// Overall device revision level. Only
        /// <c>MachineryComponentIdentificationType</c> declares it, so it is
        /// ignored for a machine.
        /// </summary>
        public string? DeviceRevision { get; set; }

        /// <summary>
        /// Device class — <c>"Pump"</c> or <c>"Generator"</c>, for example.
        /// </summary>
        public string? DeviceClass { get; set; }

        /// <summary>
        /// Operator-assigned asset identifier.
        /// </summary>
        public string? AssetId { get; set; }

        /// <summary>
        /// Operator-assigned component name.
        /// </summary>
        public LocalizedText ComponentName { get; set; }

        /// <summary>
        /// Location of the machine. Only <c>MachineIdentificationType</c>
        /// declares it, so it is ignored for a component.
        /// </summary>
        public string? Location { get; set; }

        /// <summary>
        /// Date the machinery item was first put into operation.
        /// </summary>
        public DateTime? InitialOperationDate { get; set; }

        /// <summary>
        /// Year of construction.
        /// </summary>
        public ushort? YearOfConstruction { get; set; }

        /// <summary>
        /// Month of construction, 1-12.
        /// </summary>
        public byte? MonthOfConstruction { get; set; }

        internal void Validate()
        {
            if (Manufacturer.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "OPC 40001-1 requires Manufacturer on every identification add-in.");
            }
            if (string.IsNullOrEmpty(SerialNumber))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "OPC 40001-1 requires SerialNumber on every identification add-in.");
            }
            if (MonthOfConstruction is < 1 or > 12)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "MonthOfConstruction must be between 1 and 12 but was {0}.",
                    MonthOfConstruction);
            }
        }
    }
}
