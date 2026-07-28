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

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// A version-neutral personnel, equipment or physical-asset resource entry
    /// captured by the in-memory engine. The same structure describes a job-order
    /// requirement and a job-response actual for all three resource kinds in both
    /// Job Control V1 and V2. The V1 <c>UoM</c> string and the V2
    /// <c>EngineeringUnits</c> structure are unified onto
    /// <see cref="EngineeringUnits"/>.
    /// </summary>
    internal sealed record Isa95ResourceRequirement
    {
        /// <summary>
        /// The identifier of the resource.
        /// </summary>
        public string? Id { get; init; }

        /// <summary>
        /// The localized descriptions of the resource. V1 carries a single string
        /// description which is normalized to a single element here.
        /// </summary>
        public ArrayOf<LocalizedText> Description { get; init; } = [];

        /// <summary>
        /// The use of the resource (the V1/V2 <c>PersonnelUse</c>,
        /// <c>EquipmentUse</c> or <c>PhysicalAssetUse</c> field).
        /// </summary>
        public string? Use { get; init; }

        /// <summary>
        /// The quantity of the resource.
        /// </summary>
        public string? Quantity { get; init; }

        /// <summary>
        /// The engineering unit of measure as the version-neutral core
        /// <see cref="EUInformation"/> structure.
        /// </summary>
        public EUInformation? EngineeringUnits { get; init; }

        /// <summary>
        /// The properties of the resource.
        /// </summary>
        public ArrayOf<Isa95Property> Properties { get; init; } = [];
    }
}
