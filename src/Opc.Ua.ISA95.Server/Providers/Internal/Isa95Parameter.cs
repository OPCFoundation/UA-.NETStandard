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
    /// A version-neutral parameter captured by the in-memory engine. The engine
    /// stores this reduced projection of the Job Control V1/V2
    /// <c>ISA95ParameterDataType</c> so that no generated type leaks into the
    /// engine. Conversions between this record and the generated types are
    /// intentionally loss-aware (see <see cref="Isa95JobControlConversions"/>).
    /// </summary>
    internal sealed record Isa95Parameter
    {
        /// <summary>
        /// The identifier of the parameter.
        /// </summary>
        public string? Id { get; init; }

        /// <summary>
        /// The value of the parameter.
        /// </summary>
        public Variant Value { get; init; }

        /// <summary>
        /// The localized descriptions of the parameter. V1 carries a single
        /// string description which is normalized to a single element here.
        /// </summary>
        public ArrayOf<LocalizedText> Description { get; init; } = [];

        /// <summary>
        /// The engineering unit of measure of the parameter as the version-neutral
        /// core <see cref="EUInformation"/> structure. V1 stores a plain string
        /// (<c>UoM</c>) which is projected onto <see cref="EUInformation.DisplayName"/>;
        /// V2 stores the full structure which is preserved as-is.
        /// </summary>
        public EUInformation? EngineeringUnits { get; init; }

        /// <summary>
        /// The nested sub-parameters.
        /// </summary>
        public ArrayOf<Isa95Parameter> Subparameters { get; init; } = [];
    }
}
