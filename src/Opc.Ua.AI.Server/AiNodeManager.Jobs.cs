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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.AI;
using AIRefs = Opc.Ua.AI.ReferenceTypeIds;
using BrowseNames = Opc.Ua.AI.BrowseNames;
using ObjectIds = Opc.Ua.ObjectIds;
using ReferenceTypeIds = Opc.Ua.ReferenceTypeIds;

namespace Opc.Ua.AI.Server
{
    public sealed partial class AINodeManager
    {
        /// <summary>
        /// Starts an inference that outlives the call that asked for it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The job NodeId comes back immediately and the result arrives on the job.
        /// This is what makes a long inference usable from a client that cannot hold
        /// a call open for minutes - and, more importantly, what makes the result
        /// survive the client that requested it disconnecting.
        /// </para>
        /// <para>
        /// <c>AiJobType</c> is a Part 10 <c>ProgramStateMachineType</c>, so the
        /// lifecycle is the one clients already know: Ready, Running, and then
        /// Halted whether it succeeded or not. Nothing new to learn, which is the
        /// reason the specification reused it.
        /// </para>
        /// </remarks>
        private async ValueTask<InvokeAsyncMethodStateResult> StartJobAsync(
            NodeId objectId,
            ByteString payload,
            string payloadUri,
            string contentType,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            DeploymentState? deployment = FindDeployment(objectId);
            if (deployment is null)
            {
                return new InvokeAsyncMethodStateResult
                {
                    ServiceResult = StatusCodes.BadNodeIdUnknown,
                    Job = NodeId.Null
                };
            }

            // Clause 8.4, as for Invoke: exactly one of Payload and PayloadUri.
            if (payload.IsNull == string.IsNullOrEmpty(payloadUri))
            {
                return new InvokeAsyncMethodStateResult
                {
                    ServiceResult = StatusCodes.BadInvalidArgument,
                    Job = NodeId.Null
                };
            }

            InferenceJobState job;
            NodeId? stale = null;
            byte[] body = payload.Memory.ToArray();

            lock (m_sync)
            {
                // The same bound transfers already have. Without it any session that
                // can call this Method can grow the address space indefinitely, and
                // each job retains its request and response payloads - so the cost
                // is not only nodes but the bytes they hold.
                if (m_jobs.Count >= m_options.MaxRetainedJobs)
                {
                    stale = ReclaimOldestJob();
                }

                string jobId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

                job = new InferenceJobState(null);
                job.Create(
                    SystemContext,
                    NodeId.Null,
                    new QualifiedName("Job_" + jobId, NamespaceIndex),
                    new LocalizedText("Job " + jobId),
                    true);

                Child<PropertyState<string>>(job, BrowseNames.JobId).Value = jobId;
                Child<PropertyState<NodeId>>(job, BrowseNames.Deployment).Value =
                    deployment.NodeId;
                Child<PropertyState<ByteString>>(job, BrowseNames.RequestPayload).Value = payload;
                Child<PropertyState<string>>(job, BrowseNames.RequestContentType).Value =
                    contentType;
                Child<PropertyState<DateTimeUtc>>(job, BrowseNames.StartedAt).Value =
                    DateTime.UtcNow;
                Child<PropertyState<double>>(job, BrowseNames.Progress).Value = 0;

                // Materialised before the node is indexed, for the same reason the
                // transfer does it: a member created after the fact is invisible to
                // a client, and a job whose result never appears is a worse failure
                // than one that fails.
                Child<PropertyState<ByteString>>(job, BrowseNames.ResponsePayload);
                Child<PropertyState<string>>(job, BrowseNames.ResponseContentType);
                Child<PropertyState<NodeId>>(job, BrowseNames.ModelUsed);
                Child<PropertyState<UsageDataType>>(job, BrowseNames.Usage);
                Child<PropertyState<FinishReasonEnum>>(job, BrowseNames.FinishReason);
                Child<PropertyState<LocalizedText>>(job, BrowseNames.LastError);
                Child<PropertyState<DateTimeUtc>>(job, BrowseNames.FinishedAt);

                job.CurrentState!.Value = new LocalizedText("Running");
                job.CurrentState!.Id!.Value = Opc.Ua.ObjectIds.ProgramStateMachineType_Running;

                Child<FolderState>(m_root!, BrowseNames.Jobs).AddChild(job);
                AddPredefinedNodeSynchronously(job);
                m_jobs.Add(job.NodeId);
            }

            if (stale is not null)
            {
                // Outside the lock: only DeleteNodeAsync exists, and holding a lock
                // across it is what the transfer path had to be fixed for.
                await DeleteNodeAsync(SystemContext, stale.Value, ct).ConfigureAwait(false);
            }

            // Fire and forget by design: the caller has its NodeId and the result
            // belongs to the job. Faults are recorded on the job rather than thrown
            // into a void, so an unobserved task cannot swallow one.
            _ = Task.Run(
                () => RunJobAsync(job, deployment, body, contentType),
                CancellationToken.None);

            return new InvokeAsyncMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Job = job.NodeId
            };
        }

        /// <summary>
        /// Removes the oldest job to make room, and returns its NodeId to delete.
        /// </summary>
        /// <remarks>
        /// Oldest first rather than oldest finished, because a job that has been
        /// running long enough to be the oldest of a full set is not going to
        /// finish. A cap that only reclaimed completed jobs would be no cap at all
        /// against the case it exists for.
        /// </remarks>
        private NodeId? ReclaimOldestJob()
        {
            if (m_jobs.Count == 0)
            {
                return null;
            }

            NodeId oldest = m_jobs[0];
            m_jobs.RemoveAt(0);

            if (FindPredefinedNode<InferenceJobState>(oldest) is { } node)
            {
                Child<FolderState>(m_root!, BrowseNames.Jobs).RemoveChild(node);
            }

            return oldest;
        }

        private async Task RunJobAsync(
            InferenceJobState job,
            DeploymentState deployment,
            byte[] payload,
            string contentType)
        {
            try
            {
                // The delay exists so a client can observe Running before Halted.
                // A job that completes before its NodeId reaches the caller would
                // demonstrate nothing about the lifecycle it is here to show.
                if (m_options.AsyncInferenceDelay > TimeSpan.Zero)
                {
                    await Task.Delay(m_options.AsyncInferenceDelay).ConfigureAwait(false);
                }

                InferenceOutcome outcome = await RunWithFallbackAsync(
                    deployment,
                    payload,
                    contentType,
                    m_options.TransferInferenceTimeout.TotalMilliseconds,
                    CancellationToken.None).ConfigureAwait(false);

                lock (m_sync)
                {
                    if (outcome.Result.Ok)
                    {
                        Child<PropertyState<ByteString>>(job, BrowseNames.ResponsePayload)
                            .Value = new ByteString(outcome.Result.Payload.ToArray());
                        Child<PropertyState<string>>(job, BrowseNames.ResponseContentType)
                            .Value = outcome.Result.ContentType;
                        Child<PropertyState<NodeId>>(job, BrowseNames.ModelUsed).Value =
                            outcome.ModelUsed;
                        Child<PropertyState<UsageDataType>>(job, BrowseNames.Usage).Value =
                            ToUsage(outcome.Result);
                        Child<PropertyState<FinishReasonEnum>>(job, BrowseNames.FinishReason)
                            .Value = ToFinishReason(outcome.Result.Finish);
                    }
                    else
                    {
                        Child<PropertyState<LocalizedText>>(job, BrowseNames.LastError).Value =
                            new LocalizedText(outcome.Result.Message ?? "Inference failed.");
                    }

                    CompleteJob(job);
                }
            }
#pragma warning disable CA1031 // a background job records its fault rather than crashing the Server
            catch (Exception ex)
#pragma warning restore CA1031
            {
                m_logger.LogError(ex, "Asynchronous inference job failed.");

                lock (m_sync)
                {
                    Child<PropertyState<LocalizedText>>(job, BrowseNames.LastError).Value =
                        new LocalizedText(ex.Message);
                    CompleteJob(job);
                }
            }
        }

        /// <summary>
        /// Moves a job to Halted and stamps when it finished.
        /// </summary>
        /// <remarks>
        /// Halted regardless of outcome, which is the Part 10 lifecycle rather than
        /// an opinion about the result: whether the inference succeeded is answered
        /// by whether <c>ResponsePayload</c> or <c>LastError</c> is set, not by the
        /// state the program ended in.
        /// </remarks>
        private void CompleteJob(InferenceJobState job)
        {
            Child<PropertyState<double>>(job, BrowseNames.Progress).Value = 100;
            Child<PropertyState<DateTimeUtc>>(job, BrowseNames.FinishedAt).Value =
                DateTime.UtcNow;

            job.CurrentState!.Value = new LocalizedText("Halted");
            job.CurrentState!.Id!.Value = Opc.Ua.ObjectIds.ProgramStateMachineType_Halted;

            job.ClearChangeMasks(SystemContext, true);
        }
    }
}
