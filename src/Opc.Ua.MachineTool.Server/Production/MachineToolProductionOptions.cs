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

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Configures the bounded retained-production surface of one MachineTool or derived companion instance.
    /// </summary>
    public sealed record MachineToolProductionOptions
    {
        /// <summary>
        /// Gets explicitly enabled production facets; monitoring feature bits are ignored by this module.
        /// </summary>
        public MachineToolFeatures Features { get; init; }

        /// <summary>
        /// Gets the maximum number of ordered jobs, between 1 and 65536.
        /// </summary>
        public int MaximumJobs { get; init; } = 64;

        /// <summary>
        /// Gets the maximum number of ordered programs per job, between 1 and 65536.
        /// </summary>
        public int MaximumProgramsPerJob { get; init; } = 128;

        /// <summary>
        /// Gets the maximum number of part sets per job.
        /// </summary>
        public int MaximumPartSetsPerJob { get; init; } = 32;

        /// <summary>
        /// Gets the maximum number of part slots per set.
        /// </summary>
        public int MaximumPartsPerSet { get; init; } = 1024;

        /// <summary>
        /// Gets the maximum number of retained interruption conditions per job.
        /// </summary>
        public int MaximumInterruptionsPerJob { get; init; } = 32;

        /// <summary>
        /// Gets the maximum combined number of jobs, programs, sets, parts and conditions.
        /// </summary>
        public int MaximumElements { get; init; } = 4096;

        /// <summary>
        /// Gets the maximum length of each provider identifier, name and reason.
        /// </summary>
        public int MaximumTextLength { get; init; } = 1024;

        /// <summary>
        /// Gets the optional typed job factory. The returned unpublished node can be a generated GMSJob subtype.
        /// Any returned resource becomes owned by this module.
        /// </summary>
        public Func<ISystemContext, NodeState, QualifiedName, CancellationToken,
            ValueTask<MachineToolProductionNode<ProductionJobState>>>? CreateJobAsync { get; init; }

        /// <summary>
        /// Gets the optional typed part factory. The returned unpublished node can be a generated GMSPart subtype.
        /// Any returned resource becomes owned by this module.
        /// </summary>
        public Func<ISystemContext, NodeState, QualifiedName, CancellationToken,
            ValueTask<MachineToolProductionNode<ProductionPartState>>>? CreatePartAsync { get; init; }

        /// <summary>
        /// Gets the required delivery barrier for dynamic removal. It must complete only after the final
        /// observations of the supplied subtrees have been included in every active data subscription's
        /// Publish response. This is a server publication boundary, not a transport acknowledgement.
        /// A failure or cancellation leaves those nodes and their resources present. No timer or successful
        /// no-op is substituted when the transport cannot supply this guarantee.
        /// </summary>
        public Func<ArrayOf<NodeState>, CancellationToken, ValueTask>? PublishFinalObservationsAsync { get; init; }
    }

    /// <summary>
    /// Transfers a typed unpublished generated node and its optional companion-specific resource to the module.
    /// </summary>
    /// <typeparam name="TState">The generated base type accepted by the production module.</typeparam>
    public sealed class MachineToolProductionNode<TState> where TState : BaseObjectState
    {
        /// <summary>
        /// Creates an owned node result. The factory remains responsible for cleanup if it throws before returning.
        /// </summary>
        public MachineToolProductionNode(TState state, IAsyncDisposable? resource = null)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Resource = resource;
        }

        /// <summary>
        /// Gets the actual generated node, retaining any derived companion type.
        /// </summary>
        public TState State { get; }

        /// <summary>
        /// Gets a resource released on rollback, removal or module disposal.
        /// </summary>
        public IAsyncDisposable? Resource { get; }
    }
}
