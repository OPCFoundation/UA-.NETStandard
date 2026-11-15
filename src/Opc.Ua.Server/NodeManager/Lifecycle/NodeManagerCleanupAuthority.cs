/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Identifies the host adapters bound to a lifecycle before server startup.
    /// </summary>
    /// <param name="authorization">The trusted host authorization policy, when configured.</param>
    /// <param name="verifier">The trusted exact-resource cleanup verifier, when configured.</param>
    /// <param name="audit">The trusted host audit adapter, when configured.</param>
    internal sealed class NodeManagerCleanupAuthority(
        INodeManagerCleanupAuthorization? authorization,
        INodeManagerCleanupVerifier? verifier,
        INodeManagerCleanupAudit? audit)
    {
        /// <summary>
        /// Gets the trusted host authorization policy, if configured.
        /// </summary>
        internal INodeManagerCleanupAuthorization? Authorization { get; } = authorization;

        /// <summary>
        /// Gets the trusted cleanup verifier, if configured.
        /// </summary>
        internal INodeManagerCleanupVerifier? Verifier { get; } = verifier;

        /// <summary>
        /// Gets the trusted host audit adapter, if configured.
        /// </summary>
        internal INodeManagerCleanupAudit? Audit { get; } = audit;

        /// <summary>
        /// Checks that another binding refers to exactly the same host adapters.
        /// </summary>
        internal bool Matches(NodeManagerCleanupAuthority other)
        {
            return ReferenceEquals(Authorization, other.Authorization) &&
                ReferenceEquals(Verifier, other.Verifier) &&
                ReferenceEquals(Audit, other.Audit);
        }
    }
}
