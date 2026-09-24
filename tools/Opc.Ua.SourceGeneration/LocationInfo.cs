/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// A value-equatable snapshot of a source <see cref="Location"/>.
    /// </summary>
    /// <remarks>
    /// A <see cref="Location"/> holds its <see cref="SyntaxTree"/>, which is a
    /// new instance after every edit of the file, so a pipeline value that
    /// carries one is never equal to its previous value and keeps old syntax
    /// trees alive in the incremental cache. The snapshot carries only the
    /// path and spans and re-creates the location when a diagnostic is
    /// reported.
    /// </remarks>
    internal readonly record struct LocationInfo(
        string FilePath,
        TextSpan TextSpan,
        LinePositionSpan LineSpan)
    {
        /// <summary>
        /// Snapshot a location. <see cref="Location.None"/>, a
        /// <c>null</c> location and a location outside source map to
        /// <c>default</c>.
        /// </summary>
        public static LocationInfo From(Location location)
        {
            if (location == null || !location.IsInSource)
            {
                return default;
            }
            return new LocationInfo(
                location.SourceTree.FilePath,
                location.SourceSpan,
                location.GetLineSpan().Span);
        }

        /// <summary>
        /// Re-create the location, or <see cref="Location.None"/> for
        /// <c>default</c>.
        /// </summary>
        public Location ToLocation()
        {
            return FilePath == null
                ? Location.None
                : Location.Create(FilePath, TextSpan, LineSpan);
        }
    }
}
