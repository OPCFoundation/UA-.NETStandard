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

namespace Opc.Ua.Pumps
{
    /// <summary>
    /// The nameplate of an OPC 40223 pump, as published by its
    /// <c>Identification</c> add-in.
    /// </summary>
    /// <remarks>
    /// <c>PumpIdentificationType</c> is the one place OPC 40223 closes the
    /// set: it inherits the OPC 10000-100 and OPC 40001-1 nameplate fields and
    /// adds eleven of its own, and that whole list is fixed by the
    /// specification. Unlike the measurement and design groups it is therefore
    /// modelled as a record rather than a
    /// <see cref="PumpValueSet"/>. Only <see cref="Manufacturer"/> and
    /// <see cref="SerialNumber"/> are mandatory; everything else is
    /// <see langword="null"/> when the server does not publish it.
    /// </remarks>
    public sealed record PumpNameplate
    {
        /// <summary>
        /// The <c>Identification</c> add-in the nameplate was read from.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// Manufacturer. Mandatory in OPC 40223.
        /// </summary>
        public LocalizedText Manufacturer { get; init; }

        /// <summary>
        /// Manufacturer-assigned serial number. Mandatory in OPC 40223.
        /// </summary>
        public string? SerialNumber { get; init; }

        /// <summary>
        /// Globally unique manufacturer URI.
        /// </summary>
        public string? ManufacturerUri { get; init; }

        /// <summary>
        /// Model designation.
        /// </summary>
        public LocalizedText Model { get; init; }

        /// <summary>
        /// Manufacturer-defined product code.
        /// </summary>
        public string? ProductCode { get; init; }

        /// <summary>
        /// Hardware revision level.
        /// </summary>
        public string? HardwareRevision { get; init; }

        /// <summary>
        /// Software revision level.
        /// </summary>
        public string? SoftwareRevision { get; init; }

        /// <summary>
        /// Device class.
        /// </summary>
        public string? DeviceClass { get; init; }

        /// <summary>
        /// Globally unique product-instance URI.
        /// </summary>
        public string? ProductInstanceUri { get; init; }

        /// <summary>
        /// Operator-assigned asset identifier.
        /// </summary>
        public string? AssetId { get; init; }

        /// <summary>
        /// Operator-assigned component name.
        /// </summary>
        public LocalizedText ComponentName { get; init; }

        /// <summary>
        /// Installation location.
        /// </summary>
        public string? Location { get; init; }

        /// <summary>
        /// Date the pump was first put into operation.
        /// </summary>
        public DateTime? InitialOperationDate { get; init; }

        /// <summary>
        /// Year of construction.
        /// </summary>
        public ushort? YearOfConstruction { get; init; }

        /// <summary>
        /// Month of construction.
        /// </summary>
        public byte? MonthOfConstruction { get; init; }

        /// <summary>
        /// Day of construction.
        /// </summary>
        public int? DayOfConstruction { get; init; }

        /// <summary>
        /// Manufacturer's article number.
        /// </summary>
        public string? ArticleNumber { get; init; }

        /// <summary>
        /// Product code used when ordering.
        /// </summary>
        public string? OrderProductCode { get; init; }

        /// <summary>
        /// Type of product.
        /// </summary>
        public string? TypeOfProduct { get; init; }

        /// <summary>
        /// Supplier, where it differs from the manufacturer.
        /// </summary>
        public string? Supplier { get; init; }

        /// <summary>
        /// Country of origin.
        /// </summary>
        public string? CountryOfOrigin { get; init; }

        /// <summary>
        /// Manufacturer's fabrication number.
        /// </summary>
        public string? FabricationNumber { get; init; }

        /// <summary>
        /// GTIN (Global Trade Item Number) code.
        /// </summary>
        public string? GTINCode { get; init; }

        /// <summary>
        /// NATO/national stock number.
        /// </summary>
        public string? NationalStockNumber { get; init; }

        /// <summary>
        /// The physical fieldbus address of the pump, when it publishes one.
        /// </summary>
        public PhysicalAddressDataType? PhysicalAddress { get; init; }

        /// <summary>
        /// The <c>Markings</c> folder, or <see cref="NodeId.Null"/> when the
        /// pump publishes none. OPC 40223 leaves its contents open - each
        /// marking is a vendor-defined object below it - so it is exposed as a
        /// NodeId to browse rather than a decoded value.
        /// </summary>
        public NodeId MarkingsFolderId { get; init; }
    }
}
