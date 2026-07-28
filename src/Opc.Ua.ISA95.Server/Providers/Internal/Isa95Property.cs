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
    /// A version-neutral property captured by the in-memory engine. Properties
    /// appear on the personnel, equipment, physical-asset and material
    /// requirement and actual structures of both Job Control V1 and V2.
    /// </summary>
    internal sealed record Isa95Property
    {
        /// <summary>
        /// The identifier of the property.
        /// </summary>
        public string? Id { get; init; }

        /// <summary>
        /// The value of the property.
        /// </summary>
        public Variant Value { get; init; }

        /// <summary>
        /// The localized descriptions of the property. V1 carries a single
        /// string description which is normalized to a single element here.
        /// </summary>
        public ArrayOf<LocalizedText> Description { get; init; } = [];

        /// <summary>
        /// The engineering unit of measure of the property as the version-neutral
        /// core <see cref="EUInformation"/> structure. V1 stores a plain string
        /// (<c>UoM</c>) projected onto <see cref="EUInformation.DisplayName"/>.
        /// </summary>
        public EUInformation? EngineeringUnits { get; init; }

        /// <summary>
        /// The nested sub-properties.
        /// </summary>
        public ArrayOf<Isa95Property> Subproperties { get; init; } = [];
    }
}
