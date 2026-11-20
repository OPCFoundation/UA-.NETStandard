// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
