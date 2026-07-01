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

namespace Opc.Ua.PubSub.MetaData
{
    /// <summary>
    /// Outcome of an <see cref="IDataSetMetaDataRegistry.TryGet"/> lookup.
    /// Distinguishes the three reasons a lookup may not return a
    /// directly-usable entry: nothing registered, a backward-compatible
    /// version drift, or a breaking version drift.
    /// </summary>
    /// <remarks>
    /// Implements the metadata-version reconciliation rules of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.9.4">
    /// Part 14 §6.2.9.4 DataSetReader DataSetMetaData</see>.
    /// </remarks>
    public enum MetaDataMatchResult
    {
        /// <summary>
        /// An entry exists with the exact MajorVersion and at least the
        /// requested MinorVersion; the returned <see cref="DataSetMetaDataType"/>
        /// is safe to use to decode payloads bound to the key.
        /// </summary>
        Match,

        /// <summary>
        /// An entry exists for the key but its MinorVersion differs from
        /// the requested MinorVersion. Per Part 14 §6.2.9.4 this is a
        /// soft-update path: the registered metadata may still decode the
        /// payload but the registry should be refreshed at the earliest
        /// opportunity.
        /// </summary>
        MinorVersionMismatch,

        /// <summary>
        /// An entry exists for the key tuple but its MajorVersion differs
        /// from the requested MajorVersion. This is a breaking change;
        /// callers must reject the payload and trigger a metadata
        /// re-acquisition before continuing.
        /// </summary>
        MajorVersionMismatch,

        /// <summary>
        /// No entry exists for the requested key tuple.
        /// </summary>
        NotFound
    }
}
