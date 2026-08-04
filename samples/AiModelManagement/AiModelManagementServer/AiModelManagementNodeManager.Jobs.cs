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
using Opc.Ua.AiModelManagement;
using AiRefs = Opc.Ua.AiModelManagement.ReferenceTypeIds;
using BrowseNames = Opc.Ua.AiModelManagement.BrowseNames;
using ObjectIds = Opc.Ua.ObjectIds;
using ReferenceTypeIds = Opc.Ua.ReferenceTypeIds;

namespace AiModelManagement.Server
{
    public sealed partial class AiModelManagementNodeManager
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
        private ValueTask<InvokeAsyncMethodStateResult> StartJobAsync(
            NodeId objectId,
            ByteString payload,
            string contentType,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            DeploymentState? deployment = FindDeployment(objectId);
            if (deployment is null)
            {
                return ValueTask.FromResult(new InvokeAsyncMethodStateResult
                {
                    ServiceResult = StatusCodes.BadNodeIdUnknown,
                    Job = NodeId.Null
                });
            }

            InferenceJobState job;
            byte[] body = payload.Memory.ToArray();

            lock (m_sync)
            {
                string jobId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

                job = new InferenceJobState(m_root!.Jobs);
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

                job.CurrentState!.Value = new LocalizedText("Running");
                job.CurrentState!.Id!.Value = ObjectIds.ProgramStateMachineType_Running;

                Child<FolderState>(m_root, BrowseNames.Jobs).AddChild(job);
                AddPredefinedNodeSynchronously(job);
            }

            // Fire and forget by design: the caller has its NodeId and the result
            // belongs to the job. Faults are recorded on the job rather than thrown
            // into a void, so an unobserved task cannot swallow one.
            _ = Task.Run(
                () => RunJobAsync(job, deployment, body, contentType),
                CancellationToken.None);

            return ValueTask.FromResult(new InvokeAsyncMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Job = job.NodeId
            });
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
            job.CurrentState!.Id!.Value = ObjectIds.ProgramStateMachineType_Halted;

            job.ClearChangeMasks(SystemContext, true);
        }
    }
}
