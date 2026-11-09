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

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PlasticsRubber.Server
{
    /// <summary>
    /// Carries the original caller and the concrete instance targeted by an operation.
    /// </summary>
    public readonly record struct PlasticsRubberRequestContext
    {
        /// <summary>
        /// Creates an operation context without replacing the authenticated caller.
        /// </summary>
        public PlasticsRubberRequestContext(ISystemContext systemContext, NodeId targetId)
        {
            SystemContext = systemContext ?? throw new ArgumentNullException(nameof(systemContext));
            if (targetId.IsNull)
            {
                throw new ArgumentException("An instance NodeId is required.", nameof(targetId));
            }
            TargetId = targetId;
        }

        /// <summary>
        /// Gets the original service context, including its session and user identity.
        /// </summary>
        public ISystemContext SystemContext { get; }

        /// <summary>
        /// Gets the instance targeted by the command.
        /// </summary>
        public NodeId TargetId { get; }
    }

    /// <summary>
    /// Authorizes a common-model operation before any provider or mutable state is accessed.
    /// </summary>
    public interface IPlasticsRubberAccessPolicy
    {
        /// <summary>
        /// Throws a service-result exception when the original caller may not access the member.
        /// </summary>
        ValueTask AuthorizeAsync(
            PlasticsRubberRequestContext context,
            NodeId memberId,
            CancellationToken cancellationToken = default);
    }
}
