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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Aas.Server
{
    /// <summary>
    /// Serves read and write access to AAS element value variables.
    /// </summary>
    public interface IAasValueProvider
    {
        /// <summary>
        /// Reads the current value of an element value variable.
        /// </summary>
        ValueTask<AasValueReadResult> ReadValueAsync(
            NodeId valueNodeId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes the current value of an element value variable.
        /// </summary>
        ValueTask<ServiceResult> WriteValueAsync(
            NodeId valueNodeId,
            Variant value,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A value read result.
    /// </summary>
    public readonly record struct AasValueReadResult(
        ServiceResult Result,
        Variant Value,
        StatusCode StatusCode,
        DateTimeUtc SourceTimestamp);
}
