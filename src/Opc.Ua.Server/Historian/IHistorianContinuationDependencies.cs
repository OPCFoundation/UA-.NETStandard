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

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Optional provider capability declaring all local Node dependencies of a paginated history read.
    /// </summary>
    /// <remarks>
    /// Dynamic sources require a complete declaration before their first continuation is saved.
    /// The source and requested Node are retained automatically. An empty successful declaration means
    /// no additional NodeManagers are needed for any remaining page. Returning false rejects pagination
    /// with BadNotSupported, without returning partial data. Non-paginated reads and legacy providers
    /// on sources outside the dynamic lifecycle do not require this capability.
    /// </remarks>
    public interface IHistorianContinuationDependencies
    {
        /// <summary>
        /// Declares every additional local Node needed by this token and all its successor pages.
        /// The framework never interprets the opaque token. Dependencies must not grow on later pages.
        /// </summary>
        bool TryGetContinuationDependencies(
            NodeId sourceNodeId,
            HistorianResumeToken resumeToken,
            out ArrayOf<NodeId> dependencies);
    }
}
