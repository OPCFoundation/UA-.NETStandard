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

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Explicit host contract for registered custom sessions that do not change identity during generic mutations.
    /// </summary>
    /// <remarks>
    /// Register an implementation through dependency injection only for sessions whose host guarantees no
    /// reconnect, retarget, authentication change, or namespace/type/ownership metadata change between
    /// classification and dispatch. Returning true grants no operation authority and supplies no SDK generation gate.
    /// Unknown or independently mutable sessions must return false. Stack ManagedSession instances always
    /// use the SDK's generation-bound context instead, regardless of this contract.
    /// </remarks>
    public interface IMcpFixedSessionContract
    {
        /// <summary>
        /// Confirms the fixed-session contract for this exact named registration.
        /// The contract is checked before classification and again immediately before mutation dispatch.
        /// A known change to the host's classification-data epoch must withdraw the contract.
        /// </summary>
        bool IsFixed(OpcUaSessionManager.SessionInfo registration);
    }
}
