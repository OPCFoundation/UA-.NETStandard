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

namespace Opc.Ua
{
    /// <summary>
    /// Renders a <see cref="GeoLocationSample"/> as the location literals a
    /// text-valued location variable carries.
    /// </summary>
    /// <remarks>
    /// OPC 10030 (ISA-95) models a location as text rather than as coordinates,
    /// so a structured sample has to be projected before it can be published.
    /// Replace the default <see cref="WktGeoLocationTextFormatter"/> to publish
    /// a different geometry encoding or a site-specific literal.
    /// </remarks>
    public interface IGeoLocationTextFormatter
    {
        /// <summary>
        /// Renders a sample as location literals.
        /// </summary>
        /// <param name="sample">The sample to render.</param>
        /// <returns>
        /// The literals to publish, most specific first. May be empty when the
        /// sample carries no location at all.
        /// </returns>
        ArrayOf<string> Format(in GeoLocationSample sample);
    }
}
