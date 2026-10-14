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
    /// Host authority shared by semantic tools, generic UA guards and named runtime ownership.
    /// Server permissions and network-target policy remain additional checks.
    /// </summary>
    public interface IMcpOperationPolicy
    {
        /// <summary>
        /// Reports catalogue-level host enablement, not caller authorization.
        /// </summary>
        bool IsEnabled(McpCapability capability);

        /// <summary>
        /// Requires an enabled capability and an exact trusted caller grant.
        /// </summary>
        McpCallerIdentity Require(McpCapability capability);

        /// <summary>
        /// Additionally requires the exact named UA session in the caller's grant.
        /// </summary>
        McpCallerIdentity RequireSession(McpCapability capability, string sessionName);

        /// <summary>
        /// Additionally requires an exact host-configured secret alias in the caller's grant.
        /// </summary>
        McpCallerIdentity RequireSecret(McpCapability capability, string alias);

        /// <summary>
        /// Gets the trusted current identity or refuses an unidentified caller.
        /// </summary>
        McpCallerIdentity RequireCaller();
    }
}
