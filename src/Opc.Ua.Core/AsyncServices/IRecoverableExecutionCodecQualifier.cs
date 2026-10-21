/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Explicitly qualifies a custom codec's immutable interpretation for persistent recoverable records.
    /// </summary>
    /// <remarks>
    /// A nonempty revision is an application authority's promise that this exact registration's codec
    /// remains immutable during a captured context's lifetime and has the same encode/decode semantics
    /// whenever that revision is used after restart. Change it whenever those semantics change.
    /// Declared data type metadata, CLR names and matching identifiers alone cannot prove that promise.
    /// Missing metadata is rejected even when a revision is supplied. Return null or empty bytes to deny.
    /// </remarks>
    public interface IRecoverableExecutionCodecQualifier
    {
        /// <summary>
        /// Returns a bounded, non-secret codec revision for the exact registration, or denies qualification.
        /// </summary>
        ValueTask<ByteString> GetRevisionAsync(
            ExpandedNodeId registrationId, IType codec, CancellationToken ct = default);
    }
}
