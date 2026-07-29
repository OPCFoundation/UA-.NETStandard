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
    /// A version-neutral material resource entry captured by the in-memory
    /// engine. The same structure describes a job-order material requirement and
    /// a job-response material actual in both Job Control V1 and V2.
    /// </summary>
    internal sealed record Isa95Material
    {
        /// <summary>
        /// The material class identifier.
        /// </summary>
        public string? MaterialClassId { get; init; }

        /// <summary>
        /// The material definition identifier.
        /// </summary>
        public string? MaterialDefinitionId { get; init; }

        /// <summary>
        /// The material lot identifier.
        /// </summary>
        public string? MaterialLotId { get; init; }

        /// <summary>
        /// The material sub-lot identifier.
        /// </summary>
        public string? MaterialSublotId { get; init; }

        /// <summary>
        /// The localized descriptions of the material. V1 carries a single string
        /// description which is normalized to a single element here.
        /// </summary>
        public ArrayOf<LocalizedText> Description { get; init; } = [];

        /// <summary>
        /// The use of the material (the V1/V2 <c>MaterialUse</c> field).
        /// </summary>
        public string? Use { get; init; }

        /// <summary>
        /// The quantity of the material.
        /// </summary>
        public string? Quantity { get; init; }

        /// <summary>
        /// The engineering unit of measure as the version-neutral core
        /// <see cref="EUInformation"/> structure.
        /// </summary>
        public EUInformation? EngineeringUnits { get; init; }

        /// <summary>
        /// The properties of the material.
        /// </summary>
        public ArrayOf<Isa95Property> Properties { get; init; } = [];
    }
}
