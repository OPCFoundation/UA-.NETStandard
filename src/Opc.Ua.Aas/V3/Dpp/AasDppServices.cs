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

namespace Opc.Ua.Aas.V3
{
    /// <summary>
    /// Constructs DPP template semantic identifier IRIs.
    /// </summary>
    public interface IAasDppIdentifierFactory
    {
        /// <summary>
        /// Constructs an IRI by applying the first matching DPP clause 3 rule.
        /// </summary>
        /// <param name="identifier">The identifier as written in the template.</param>
        /// <returns>The constructed identifier result.</returns>
        AasDppIdentifierResult Construct(string identifier);
    }

    /// <summary>
    /// Looks up identifiers in the embedded DPP SSSOM mapping set.
    /// </summary>
    public interface IAasDppMappingSet
    {
        /// <summary>
        /// Looks up an identifier in the embedded mapping set.
        /// </summary>
        /// <param name="subjectId">The template identifier, before or after clause 3 trimming.</param>
        /// <param name="row">The mapping row when the return value is <c>true</c>.</param>
        /// <returns><c>true</c> when a mapping row exists.</returns>
        bool TryFind(string subjectId, out AasDppMappingRow? row);
    }

    /// <summary>
    /// Default injectable DPP identifier factory.
    /// </summary>
    public sealed class AasDppIdentifierFactory : IAasDppIdentifierFactory
    {
        /// <inheritdoc/>
        public AasDppIdentifierResult Construct(string identifier)
        {
            return AasDppIdentifier.Construct(identifier);
        }
    }

    /// <summary>
    /// Default injectable DPP mapping set lookup.
    /// </summary>
    public sealed class AasDppMappingSetProvider : IAasDppMappingSet
    {
        /// <inheritdoc/>
        public bool TryFind(string subjectId, out AasDppMappingRow? row)
        {
            return AasDppMappingSet.TryFindEmbedded(subjectId, out row);
        }
    }
}
