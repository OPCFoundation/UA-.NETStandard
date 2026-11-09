/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root.
 * ======================================================================*/

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PlasticsRubber.Server;

namespace Opc.Ua.PlasticsRubber.Runtime.Tests
{
    public sealed partial class CommonBindingTests
    {
        /// <summary>
        /// Construction validates dependencies and never invents unselected list methods.
        /// </summary>
        [Test]
        public async Task JobsConstructorValidatesDependenciesCapacityAndOptionalMethods()
        {
            JobsState node = CreateJobExchange();
            var provider = new Mock<IJobListProvider>(MockBehavior.Strict);
            IPlasticsRubberAccessPolicy access = AllowAccess();
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(() =>
                    new JobsBinding(null!, m_context, provider.Object, access));
                Assert.Throws<ArgumentNullException>(() =>
                    new JobsBinding(node, null!, provider.Object, access));
                Assert.Throws<ArgumentNullException>(() =>
                    new JobsBinding(node, m_context, null!, access));
                Assert.Throws<ArgumentNullException>(() =>
                    new JobsBinding(node, m_context, provider.Object, null!));
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    new JobsBinding(node, m_context, provider.Object, access, 0));
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    new JobsBinding(node, m_context, provider.Object, access, -1));
            });

            for (int selection = 0; selection < 4; selection++)
            {
                JobsState optional = CreateJobExchange(
                    (selection & 1) != 0, (selection & 2) != 0);
                await using var binding = new JobsBinding(optional, m_context, provider.Object, access, 1);
                Assert.Multiple(() =>
                {
                    Assert.That(binding.Node, Is.SameAs(optional));
                    Assert.That(optional.SendJobList?.OnCallAsync != null, Is.EqualTo((selection & 1) != 0));
                    Assert.That(optional.SendCyclicJobList?.OnCallAsync != null, Is.EqualTo((selection & 2) != 0));
                });
            }
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Generated wire dispatch forwards even an empty list, preserving order and caller identity.
        /// </summary>
        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(false, 2)]
        [TestCase(false, 3)]
        [TestCase(true, 0)]
        [TestCase(true, 1)]
        [TestCase(true, 2)]
        [TestCase(true, 3)]
        public async Task JobsGeneratedCallsPreserveOrderedListsAndRequestContextAsync(bool cyclic, int count)
        {
            JobsState node = CreateJobExchange();
            var provider = new Mock<IJobListProvider>(MockBehavior.Strict);
            var access = new Mock<IPlasticsRubberAccessPolicy>(MockBehavior.Strict);
            MethodState method = cyclic ? node.SendCyclicJobList! : node.SendJobList!;
            access.Setup(value => value.AuthorizeAsync(
                It.Is<PlasticsRubberRequestContext>(request =>
                    ReferenceEquals(request.SystemContext, m_context) && request.TargetId == node.NodeId),
                method.NodeId, It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            JobListElementType[] sent = CreateExchangeRows(cyclic).Take(count).ToArray();
            JobListElementType[]? received = null;
            PlasticsRubberRequestContext captured = default;
            if (cyclic)
            {
                provider.Setup(value => value.SendCyclicJobsAsync(
                    It.IsAny<PlasticsRubberRequestContext>(),
                    It.IsAny<ArrayOf<CyclicJobListElementType>>(), It.IsAny<CancellationToken>()))
                    .Callback((PlasticsRubberRequestContext request, ArrayOf<CyclicJobListElementType> rows,
                        CancellationToken _) =>
                    {
                        captured = request;
                        received = rows.ToArray();
                    }).Returns(default(ValueTask));
            }
            else
            {
                provider.Setup(value => value.SendJobsAsync(
                    It.IsAny<PlasticsRubberRequestContext>(),
                    It.IsAny<ArrayOf<JobListElementType>>(), It.IsAny<CancellationToken>()))
                    .Callback((PlasticsRubberRequestContext request, ArrayOf<JobListElementType> rows,
                        CancellationToken _) =>
                    {
                        captured = request;
                        received = rows.ToArray();
                    }).Returns(default(ValueTask));
            }
            await using var binding = new JobsBinding(node, m_context, provider.Object, access.Object, 3);
            Variant payload = cyclic
                ? Variant.FromStructure(sent.Cast<CyclicJobListElementType>().ToArray().ToArrayOf())
                : Variant.FromStructure(sent.ToArrayOf());

            ServiceResult result = await method.CallAsync(
                m_context, node.NodeId, [payload], [], [], CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good), result.ToString());
            JobListElementType[] actual = received
                ?? throw new AssertionException("The provider did not receive the list.");
            string[] expectedNames = ["A", "a", "third"];
            string[] expectedDescriptions = ["first batch", "second batch", "third batch"];
            string[] expectedPriorities = ["urgent/customer", "-17", "free form"];
            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(captured.SystemContext, Is.SameAs(m_context));
                Assert.That(captured.TargetId, Is.EqualTo(node.NodeId));
                Assert.That(actual, Has.Length.EqualTo(count));
                Assert.That(actual.Select(row => row.JobName), Is.EqualTo(expectedNames.Take(count)));
                Assert.That(actual.Select(row => row.JobDescription), Is.EqualTo(expectedDescriptions.Take(count)));
                Assert.That(actual.Select(row => row.JobPriority), Is.EqualTo(expectedPriorities.Take(count)));
                if (cyclic)
                {
                    Assert.That(actual.Cast<CyclicJobListElementType>().Select(row => row.ExpectedCycleTime),
                        Is.EqualTo(new[] { 12.5, 0, double.MaxValue }.Take(count)));
                    Assert.That(actual.Cast<CyclicJobListElementType>().Select(row => row.NominalParts),
                        Is.EqualTo(new[] { 4294967296UL, 1UL, ulong.MaxValue }.Take(count)));
                }
                AssertJobObservationsUnchanged(node);
            });
            access.Verify(value => value.AuthorizeAsync(
                It.IsAny<PlasticsRubberRequestContext>(), method.NodeId, It.IsAny<CancellationToken>()), Times.Once);
            provider.VerifyAll();
            Assert.That(provider.Invocations, Has.Count.EqualTo(1));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Each invalid-entry witness is below capacity, and each overflow contains only valid names.
        /// </summary>
        [TestCase(false, "null-entry", false)]
        [TestCase(false, "null-name", false)]
        [TestCase(false, "empty", false)]
        [TestCase(false, "whitespace", false)]
        [TestCase(false, "duplicate", false)]
        [TestCase(false, "four", true)]
        [TestCase(false, "five", true)]
        [TestCase(true, "null-entry", false)]
        [TestCase(true, "null-name", false)]
        [TestCase(true, "empty", false)]
        [TestCase(true, "whitespace", false)]
        [TestCase(true, "duplicate", false)]
        [TestCase(true, "four", true)]
        [TestCase(true, "five", true)]
        public async Task JobsRejectInvalidEntriesAndOverflowBeforeProviderAsync(
            bool cyclic, string invalid, bool capacityExceeded)
        {
            JobsState node = CreateJobExchange();
            var provider = new Mock<IJobListProvider>(MockBehavior.Strict);
            await using var binding = new JobsBinding(node, m_context, provider.Object, AllowAccess(), 3);
            JobListElementType[] rows = CreateExchangeRows(cyclic);
            rows = invalid switch
            {
                "null-entry" => [rows[0], null!],
                "duplicate" => [rows[0], rows[0]],
                "four" => [rows[0], rows[1], rows[2], NewExchangeRow(cyclic, "fourth")],
                "five" => [rows[0], rows[1], rows[2], NewExchangeRow(cyclic, "fourth"), NewExchangeRow(cyclic,
                    "fifth")],
                _ => [NewExchangeRow(cyclic, invalid switch
                {
                    "null-name" => null!,
                    "empty" => string.Empty,
                    _ => " \t "
                })]
            };

            ServiceResult result = await InvokeJobListHandlerAsync(node, cyclic, rows).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(
                    capacityExceeded ? StatusCodes.BadTooManyOperations : StatusCodes.BadInvalidArgument));
                AssertJobObservationsUnchanged(node);
            });
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Cyclic-list durations distinguish their BadOutOfRange contract from configuration errors.
        /// </summary>
        [TestCase(-double.Epsilon, false)]
        [TestCase(-12.5, false)]
        [TestCase(double.NaN, false)]
        [TestCase(double.NegativeInfinity, false)]
        [TestCase(double.PositiveInfinity, false)]
        [TestCase(0.0, true)]
        [TestCase(double.Epsilon, true)]
        [TestCase(27.25, true)]
        [TestCase(double.MaxValue, true)]
        public async Task CyclicJobListsEnforceDurationBoundariesAsync(double duration, bool accepted)
        {
            JobsState node = CreateJobExchange();
            var job = new CyclicJobListElementType { JobName = "batch-17", ExpectedCycleTime = duration };
            var provider = new Mock<IJobListProvider>(MockBehavior.Strict);
            if (accepted)
            {
                provider.Setup(value => value.SendCyclicJobsAsync(
                    It.IsAny<PlasticsRubberRequestContext>(),
                    It.Is<ArrayOf<CyclicJobListElementType>>(rows =>
                        rows.Count == 1 && rows[0].JobName == "batch-17" && rows[0].ExpectedCycleTime == duration),
                    It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            }
            await using var binding = new JobsBinding(node, m_context, provider.Object, AllowAccess(), 3);

            ServiceResult result = await InvokeJobListHandlerAsync(node, true, [job]).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(accepted ? StatusCodes.Good : StatusCodes.BadOutOfRange));
                AssertJobObservationsUnchanged(node);
            });
            provider.Verify(value => value.SendCyclicJobsAsync(
                It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<ArrayOf<CyclicJobListElementType>>(),
                It.IsAny<CancellationToken>()), accepted ? Times.Once() : Times.Never());
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// The member-specific policy precedes validation, but never sees a foreign target.
        /// </summary>
        [TestCase(false, "foreign")]
        [TestCase(false, "denied-valid")]
        [TestCase(false, "denied-invalid")]
        [TestCase(true, "foreign")]
        [TestCase(true, "denied-valid")]
        [TestCase(true, "denied-invalid")]
        public async Task JobsGuardsRejectForeignTargetsAndPreserveAuthorizationResultsAsync(
            bool cyclic, string guard)
        {
            JobsState node = CreateJobExchange();
            MethodState method = cyclic ? node.SendCyclicJobList! : node.SendJobList!;
            var provider = new Mock<IJobListProvider>(MockBehavior.Strict);
            var policy = new Mock<IPlasticsRubberAccessPolicy>(MockBehavior.Strict);
            ServiceResultException denied = ServiceResultException.Create(
                StatusCodes.BadUserAccessDenied, "The job channel is locked.");
            bool foreign = guard == "foreign";
            if (!foreign)
            {
                policy.Setup(value => value.AuthorizeAsync(
                    It.Is<PlasticsRubberRequestContext>(request =>
                        ReferenceEquals(request.SystemContext, m_context) && request.TargetId == node.NodeId),
                    method.NodeId, It.IsAny<CancellationToken>())).Throws(denied);
            }
            await using var binding = new JobsBinding(node, m_context, provider.Object, policy.Object, 3);
            JobListElementType[] jobs = guard == "denied-invalid"
                ? [NewExchangeRow(cyclic, " ")]
                : CreateExchangeRows(cyclic);

            ServiceResult result = await InvokeJobListHandlerAsync(
                node, cyclic, jobs, foreign ? new NodeId("other-jobs", 1) : node.NodeId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(
                    foreign ? StatusCodes.BadNodeIdInvalid : StatusCodes.BadUserAccessDenied));
                if (!foreign)
                {
                    Assert.That(result, Is.SameAs(denied.Result));
                }
                AssertJobObservationsUnchanged(node);
            });
            policy.Verify(value => value.AuthorizeAsync(
                It.IsAny<PlasticsRubberRequestContext>(), method.NodeId, It.IsAny<CancellationToken>()),
                foreign ? Times.Never() : Times.Once());
            policy.VerifyNoOtherCalls();
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Service failures are mapped without swallowing ordinary failures or caller cancellation.
        /// </summary>
        [TestCase(false, "service")]
        [TestCase(false, "exception")]
        [TestCase(false, "cancel")]
        [TestCase(true, "service")]
        [TestCase(true, "exception")]
        [TestCase(true, "cancel")]
        public async Task JobsPropagateProviderFailuresAndLinkedCancellationAsync(bool cyclic, string outcome)
        {
            JobsState node = CreateJobExchange();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancellationObserved = new
                TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var caller = new CancellationTokenSource();
            CancellationToken providerToken = default;
            ServiceResultException serviceFailure = ServiceResultException.Create(
                StatusCodes.BadResourceUnavailable, "MES storage rejected the batch.");
            var ordinaryFailure = new InvalidOperationException("The provider failed.");
            async ValueTask CommitAsync(PlasticsRubberRequestContext request, CancellationToken token)
            {
                Assert.That(request.SystemContext, Is.SameAs(m_context));
                Assert.That(request.TargetId, Is.EqualTo(node.NodeId));
                providerToken = token;
                using CancellationTokenRegistration registration = token.Register(
                    () => cancellationObserved.TrySetResult(true));
                entered.TrySetResult(true);
                await completion.Task.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (outcome == "service")
                {
                    throw serviceFailure;
                }
                throw ordinaryFailure;
            }
            var provider = new Mock<IJobListProvider>(MockBehavior.Strict);
            if (cyclic)
            {
                provider.Setup(value => value.SendCyclicJobsAsync(
                    It.IsAny<PlasticsRubberRequestContext>(),
                    It.IsAny<ArrayOf<CyclicJobListElementType>>(), It.IsAny<CancellationToken>()))
                    .Returns((PlasticsRubberRequestContext request, ArrayOf<CyclicJobListElementType> _,
                        CancellationToken token) =>
                        CommitAsync(request, token));
            }
            else
            {
                provider.Setup(value => value.SendJobsAsync(
                    It.IsAny<PlasticsRubberRequestContext>(),
                    It.IsAny<ArrayOf<JobListElementType>>(), It.IsAny<CancellationToken>()))
                    .Returns((PlasticsRubberRequestContext request, ArrayOf<JobListElementType> _,
                        CancellationToken token) =>
                        CommitAsync(request, token));
            }
            await using var binding = new JobsBinding(node, m_context, provider.Object, AllowAccess(), 3);
            Task<ServiceResult> pending = InvokeJobListHandlerAsync(
                node, cyclic, CreateExchangeRows(cyclic), cancellationToken: caller.Token).AsTask();
            try
            {
                Task first = await Task.WhenAny(entered.Task, pending).ConfigureAwait(false);
                Assert.That(first, Is.SameAs(entered.Task));
                Assert.That(pending.IsCompleted, Is.False);
                AssertJobObservationsUnchanged(node);
                if (outcome == "cancel")
                {
                    caller.Cancel();
                    await cancellationObserved.Task.ConfigureAwait(false);
                    Assert.That(providerToken.IsCancellationRequested, Is.True);
                }
            }
            finally
            {
                completion.TrySetResult(true);
            }
            if (outcome == "service")
            {
                ServiceResult result = await pending.ConfigureAwait(false);
                Assert.That(result, Is.SameAs(serviceFailure.Result));
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
            }
            else if (outcome == "cancel")
            {
                await Assert.CatchAsync<OperationCanceledException>(async () => await pending.ConfigureAwait(false));
            }
            else
            {
                InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await pending.ConfigureAwait(false));
                Assert.That(error, Is.SameAs(ordinaryFailure));
            }
            AssertJobObservationsUnchanged(node);
            provider.VerifyAll();
            Assert.That(provider.Invocations, Has.Count.EqualTo(1));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Captured typed handlers honor disposal even though they can still be invoked directly.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task JobsDisposalInvalidatesCapturedHandlersAsync(bool cyclic)
        {
            JobsState node = CreateJobExchange();
            var provider = new Mock<IJobListProvider>(MockBehavior.Strict);
            var policy = new Mock<IPlasticsRubberAccessPolicy>(MockBehavior.Strict);
            var binding = new JobsBinding(node, m_context, provider.Object, policy.Object);
            SendJobListMethodStateMethodAsyncCallHandler standard = node.SendJobList!.OnCallAsync!;
            SendCyclicJobListMethodStateMethodAsyncCallHandler periodic = node.SendCyclicJobList!.OnCallAsync!;

            await binding.DisposeAsync().ConfigureAwait(false);
            await binding.DisposeAsync().ConfigureAwait(false);
            ServiceResult result = cyclic
                ? (await periodic(m_context, node.SendCyclicJobList, node.NodeId, [], CancellationToken.None)
                    .ConfigureAwait(false)).ServiceResult
                : (await standard(m_context, node.SendJobList, node.NodeId, [], CancellationToken.None)
                    .ConfigureAwait(false)).ServiceResult;

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(node.SendJobList.Executable, Is.False);
                Assert.That(node.SendJobList.UserExecutable, Is.False);
                Assert.That(node.SendCyclicJobList.Executable, Is.False);
                Assert.That(node.SendCyclicJobList.UserExecutable, Is.False);
                AssertJobObservationsUnchanged(node);
            });
            policy.VerifyNoOtherCalls();
            provider.VerifyNoOtherCalls();
        }

        private JobsState CreateJobExchange(bool standard = true, bool cyclic = true)
        {
            // Detached contexts have no loaded type hierarchy. Real method argument validation
            // needs the same declared Structure -> JobList -> CyclicJobList ancestry as the model.
            var types = (TypeTable)m_context.TypeTable;
            NodeId jobType = ExpandedNodeId.ToNodeId(DataTypeIds.JobListElementType, m_context.NamespaceUris);
            if (!types.IsKnown(jobType))
            {
                types.AddSubtype(Opc.Ua.DataTypeIds.BaseDataType, NodeId.Null);
                types.AddSubtype(Opc.Ua.DataTypeIds.Structure, Opc.Ua.DataTypeIds.BaseDataType);
                types.AddSubtype(jobType, Opc.Ua.DataTypeIds.Structure);
                types.AddSubtype(
                    ExpandedNodeId.ToNodeId(DataTypeIds.CyclicJobListElementType, m_context.NamespaceUris), jobType);
            }
            JobsState node = m_context.CreateInstanceOfJobsType(Parent(), new QualifiedName("Jobs", 1));
            if (standard)
            {
                node.AddSendJobList(m_context);
            }
            if (cyclic)
            {
                node.AddSendCyclicJobList(m_context);
            }
            node.ActiveJob!.JobName!.Value = "already-running";
            node.ActiveJobValues = m_context.CreateInstanceOfActiveCyclicJobValuesType(
                node, new QualifiedName("ActiveJobValues", 1));
            node.ActiveJobValues.JobStatus!.Value = JobStatusEnumeration.SET_UP_FINISHED;
            node.ActiveJobValues.JobStatus.StatusCode = StatusCodes.UncertainDataSubNormal;
            ((ActiveCyclicJobValuesState)node.ActiveJobValues).JobCycleCounter!.Value = 91;
            return node;
        }

        private static void AssertJobObservationsUnchanged(JobsState node)
        {
            Assert.That(node.ActiveJob!.JobName!.Value, Is.EqualTo("already-running"));
            Assert.That(node.ActiveJobValues!.JobStatus!.Value, Is.EqualTo(JobStatusEnumeration.SET_UP_FINISHED));
            Assert.That(node.ActiveJobValues.JobStatus.StatusCode, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
            Assert.That(((ActiveCyclicJobValuesState)node.ActiveJobValues).JobCycleCounter!.Value, Is.EqualTo(91));
        }

        private static JobListElementType NewExchangeRow(bool cyclic, string name)
        {
            return cyclic
                ? new CyclicJobListElementType { JobName = name }
                : new JobListElementType { JobName = name };
        }

        private static JobListElementType[] CreateExchangeRows(bool cyclic)
        {
            string[] names = ["A", "a", "third"];
            string[] descriptions = ["first batch", "second batch", "third batch"];
            string[] priorities = ["urgent/customer", "-17", "free form"];
            double[] durations = [12.5, 0, double.MaxValue];
            ulong[] counts = [4294967296UL, 1UL, ulong.MaxValue];
            return names.Select((name, index) =>
            {
                JobListElementType row = NewExchangeRow(cyclic, name);
                row.JobDescription = descriptions[index];
                row.JobPriority = priorities[index];
                if (row is CyclicJobListElementType cyclicRow)
                {
                    cyclicRow.ExpectedCycleTime = durations[index];
                    cyclicRow.NominalParts = counts[index];
                }
                return row;
            }).ToArray();
        }

        private async ValueTask<ServiceResult> InvokeJobListHandlerAsync(
            JobsState node,
            bool cyclic,
            JobListElementType[] rows,
            NodeId? target = null,
            CancellationToken cancellationToken = default)
        {
            if (cyclic)
            {
                SendCyclicJobListMethodState method = node.SendCyclicJobList!;
                SendCyclicJobListMethodStateResult result = await method.OnCallAsync!(
                    m_context, method, target ?? node.NodeId,
                    rows.Cast<CyclicJobListElementType>().ToArray().ToArrayOf(),
                        cancellationToken).ConfigureAwait(false);
                return result.ServiceResult;
            }
            else
            {
                SendJobListMethodState method = node.SendJobList!;
                SendJobListMethodStateResult result = await method.OnCallAsync!(
                    m_context, method, target ?? node.NodeId, rows.ToArrayOf(),
                        cancellationToken).ConfigureAwait(false);
                return result.ServiceResult;
            }
        }
    }
}
