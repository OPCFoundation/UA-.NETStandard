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

using System.Collections.Generic;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// A single page of history values returned by an
    /// <see cref="IHistorianProvider"/> read.
    /// </summary>
    /// <typeparam name="T">
    /// The page payload type — typically
    /// <see cref="HistoricalDataValue"/>, <see cref="ModifiedDataValue"/>,
    /// <see cref="DataValue"/> or <see cref="Annotation"/>.
    /// </typeparam>
    /// <remarks>
    /// <para>
    /// The page <see cref="Values"/> collection is returned in chronological
    /// order (ascending by <c>SourceTimestamp</c>; for reverse-time reads
    /// the framework reverses the order). When the page does not exhaust
    /// the requested time window the provider supplies a non-empty
    /// <see cref="NextToken"/> so the framework can issue the follow-up
    /// page read on behalf of the client via a HistoryRead continuation
    /// point.
    /// </para>
    /// </remarks>
    public readonly record struct HistorianPage<T>(
        IReadOnlyList<T> Values,
        HistorianResumeToken NextToken = default)
    {
        /// <summary>An empty page with no continuation token.</summary>
        public static HistorianPage<T> Empty { get; } = new([], default);

        /// <summary>Returns <c>true</c> when this page exhausts the request.</summary>
        public bool IsFinal => NextToken.IsEmpty;
    }
}
