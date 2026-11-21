// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PlasticsRubber.Server
{
    /// <summary>
    /// Receives common job-list exchanges without substituting another companion's job protocol.
    /// </summary>
    public interface IJobListProvider
    {
        /// <summary>
        /// Commits the complete common job list received from MES.
        /// </summary>
        ValueTask SendJobsAsync(
            PlasticsRubberRequestContext context,
            ArrayOf<JobListElementType> jobs,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits the complete common cyclic job list received from MES.
        /// </summary>
        ValueTask SendCyclicJobsAsync(
            PlasticsRubberRequestContext context,
            ArrayOf<CyclicJobListElementType> jobs,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Binds the common JobsType list methods with bounded, typed, authorized exchanges.
    /// </summary>
    /// <remarks>
    /// Active/preparation metadata remain provider-owned. Bind their actual active-job controls
    /// separately with ActiveJobBinding; receiving a list never starts a job.
    /// </remarks>
    public sealed class JobsBinding : IAsyncDisposable
    {
        /// <summary>
        /// Connects selected generated list methods to an explicit MES exchange provider.
        /// </summary>
        public JobsBinding(
            JobsState node,
            ISystemContext context,
            IJobListProvider provider,
            IPlasticsRubberAccessPolicy accessPolicy,
            int maximumJobs = 1024)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (maximumJobs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumJobs));
            }
            m_binding = new PlasticsRubberMethodBinding(node, context, accessPolicy);
            if (node.SendJobList != null)
            {
                node.SendJobList.OnCallAsync = async (call, method, target, jobs, ct) =>
                    new SendJobListMethodStateResult
                    {
                        ServiceResult = await m_binding.ExecuteAsync(
                            call, target, method.NodeId,
                            (request, token) =>
                            {
                                ValidateJobs(jobs, maximumJobs);
                                return provider.SendJobsAsync(request, jobs, token);
                            },
                            ct).ConfigureAwait(false)
                    };
                m_binding.Track(node.SendJobList);
            }
            if (node.SendCyclicJobList != null)
            {
                node.SendCyclicJobList.OnCallAsync = async (call, method, target, jobs, ct) =>
                    new SendCyclicJobListMethodStateResult
                    {
                        ServiceResult = await m_binding.ExecuteAsync(
                            call, target, method.NodeId,
                            (request, token) =>
                            {
                                ValidateJobs(jobs, maximumJobs);
                                foreach (CyclicJobListElementType job in jobs)
                                {
                                    if (job.ExpectedCycleTime < 0 || double.IsNaN(job.ExpectedCycleTime) ||
                                        double.IsInfinity(job.ExpectedCycleTime))
                                    {
                                        throw ServiceResultException.Create(
                                            StatusCodes.BadOutOfRange,
                                                "Expected cycle duration must be finite and nonnegative.");
                                    }
                                }
                                return provider.SendCyclicJobsAsync(request, jobs, token);
                            },
                            ct).ConfigureAwait(false)
                    };
                m_binding.Track(node.SendCyclicJobList);
            }
        }

        /// <summary>
        /// Gets the native common job container.
        /// </summary>
        public JobsState Node { get; }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_binding.DisposeAsync();
        }

        /// <summary>
        /// Validates list bounds and identifiers without interpreting free-form MES priorities.
        /// </summary>
        /// <typeparam name="T">The actual common job-list structure or its cyclic derivative.</typeparam>
        private static void ValidateJobs<T>(ArrayOf<T> jobs, int maximumJobs)
            where T : JobListElementType
        {
            if (jobs.Count > maximumJobs)
            {
                throw ServiceResultException.Create(StatusCodes.BadTooManyOperations,
                    "The job list exceeds its configured bound.");
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (T job in jobs)
            {
                if (job == null || string.IsNullOrWhiteSpace(job.JobName) || !names.Add(job.JobName))
                {
                    throw ServiceResultException.Create(StatusCodes.BadInvalidArgument,
                        "Job names must be nonempty and unique.");
                }
            }
        }

        /// <summary>
        /// The common operation lifetime and authorization guard.
        /// </summary>
        private readonly PlasticsRubberMethodBinding m_binding;
    }
}
