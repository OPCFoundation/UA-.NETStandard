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
 *
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

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Resolves an engineering unit's authority and identifier into explicit semantic annotations.
    /// </summary>
    public interface IWotUnitMappingProvider
    {
        /// <summary>
        /// Tries to resolve a unit IRI and an optional quantity-kind IRI without interpreting display names.
        /// </summary>
        /// <param name="namespaceUri">The EUInformation unit authority.</param>
        /// <param name="unitId">The identifier in that authority.</param>
        /// <param name="unitIri">The unit IRI when a mapping exists.</param>
        /// <param name="quantityKindIri">The explicitly mapped quantity kind, or null when ambiguous.</param>
        /// <returns>Whether an explicit unit mapping exists.</returns>
        bool TryGetMapping(string namespaceUri, int unitId, out string unitIri, out string? quantityKindIri);
    }
}
