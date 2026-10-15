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
        /// The runtime cannot bind a missing configuration method or an implicit authority.
        /// </summary>
        [Test]
        public void CyclicConfigurationRequiresSelectedMethodAndDependencies()
        {
            CyclicJobInformationState node = CreateCyclicConfigurationNode();
            var provider = new Mock<ICyclicJobConfigurationProvider>(MockBehavior.Strict);
            IPlasticsRubberAccessPolicy access = AllowAccess();
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(() =>
                    new CyclicJobInformationBinding(null!, m_context, provider.Object, access));
                Assert.Throws<ArgumentNullException>(() =>
                    new CyclicJobInformationBinding(node, null!, provider.Object, access));
                Assert.Throws<ArgumentNullException>(() =>
                    new CyclicJobInformationBinding(node, m_context, null!, access));
                Assert.Throws<ArgumentNullException>(() =>
                    new CyclicJobInformationBinding(node, m_context, provider.Object, null!));
            });
            node.SetCyclicJobData = null;
            Assert.Throws<ArgumentException>(() =>
                new CyclicJobInformationBinding(node, m_context, provider.Object, access));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// All fourteen fields are snapshotted before commit and published together only on success.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public async Task CyclicConfigurationSnapshotsAllFieldsAndPublishesOnlyAfterCommitAsync(int partition)
        {
            CyclicJobInformationState node = CreateCyclicConfigurationNode();
            CyclicJobConfiguration expected = ConfigurationRequest(partition);
            CyclicJobConfiguration old = SeedOldCyclicConfiguration(node, !expected.ContinueAtJobEnd);
            string[] material = expected.Material.ToArray() ?? [];
            string[] products = expected.ProductName.ToArray() ?? [];
            string[] descriptions = expected.ProductDescription.ToArray() ?? [];
            CyclicJobConfiguration input = expected with
            {
                Material = material.ToArrayOf(),
                ProductName = products.ToArrayOf(),
                ProductDescription = descriptions.ToArrayOf()
            };
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var commit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            CyclicJobConfiguration? received = null;
            var provider = new Mock<ICyclicJobConfigurationProvider>(MockBehavior.Strict);
            provider.Setup(value => value.SetAsync(
                It.Is<PlasticsRubberRequestContext>(request =>
                    ReferenceEquals(request.SystemContext, m_context) && request.TargetId == node.NodeId),
                It.IsAny<CyclicJobConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns(async (PlasticsRubberRequestContext _, CyclicJobConfiguration configuration,
                    CancellationToken _) =>
                {
                    received = configuration;
                    entered.TrySetResult(true);
                    await commit.Task.ConfigureAwait(false);
                });
            var policy = new Mock<IPlasticsRubberAccessPolicy>(MockBehavior.Strict);
            policy.Setup(value => value.AuthorizeAsync(
                It.IsAny<PlasticsRubberRequestContext>(), node.SetCyclicJobData!.NodeId,
                It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            await using var binding = new CyclicJobInformationBinding(node, m_context, provider.Object, policy.Object);
            AssertCyclicPublication(node, old, StatusCodes.UncertainDataSubNormal);

            Task<SetCyclicJobDataMethodStateResult> pending = InvokeCyclicConfigurationAsync(node, input).AsTask();
            try
            {
                Task first = await Task.WhenAny(entered.Task, pending).ConfigureAwait(false);
                Assert.That(first, Is.SameAs(entered.Task));
                Assert.That(pending.IsCompleted, Is.False);
                AssertCyclicPublication(node, old, StatusCodes.UncertainDataSubNormal);
                if (material.Length != 0)
                {
                    material[0] = "mutated-material";
                    products[0] = "mutated-product";
                    descriptions[0] = "mutated-description";
                }
                AssertCyclicRequest(received!, expected);
            }
            finally
            {
                commit.TrySetResult(true);
            }
            SetCyclicJobDataMethodStateResult result = await pending.ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            AssertCyclicRequest(received!, expected);
            AssertCyclicPublication(node, expected, StatusCodes.Good);
            Assert.That(binding.Node, Is.SameAs(node));
            provider.Verify(value => value.SetAsync(
                It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<CyclicJobConfiguration>(),
                It.IsAny<CancellationToken>()), Times.Once);
            provider.VerifyNoOtherCalls();
            policy.Verify(value => value.AuthorizeAsync(
                It.IsAny<PlasticsRubberRequestContext>(), node.SetCyclicJobData!.NodeId,
                It.IsAny<CancellationToken>()), Times.Once);
            policy.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Optional publication nodes stay absent; neither empty nor duplicate arrays are invalid.
        /// </summary>
        [TestCase(false, 0)]
        [TestCase(false, 2)]
        [TestCase(true, 0)]
        [TestCase(true, 2)]
        public async Task CyclicConfigurationPreservesAbsentOptionalPropertiesAsync(bool subset, int partition)
        {
            CyclicJobInformationState node = CreateCyclicConfigurationNode();
            node.ExpectedCycleTime = null;
            node.MouldId = null;
            node.NominalBoxParts = null;
            node.NumCavities = null;
            if (!subset)
            {
                node.JobName = null;
                node.JobDescription = null;
                node.CustomerName = null;
                node.ProductionDatasetName = null;
                node.ProductionDatasetDescription = null;
                node.Material = null;
                node.ProductName = null;
                node.ProductDescription = null;
                node.ContinueAtJobEnd = null;
                node.NominalParts = null;
            }
            CyclicJobConfiguration expected = ConfigurationRequest(partition);
            BaseVariableState?[] selected = CyclicPublicationNodes(node);
            var provider = new Mock<ICyclicJobConfigurationProvider>(MockBehavior.Strict);
            CyclicJobConfiguration? received = null;
            provider.Setup(value => value.SetAsync(
                It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<CyclicJobConfiguration>(),
                It.IsAny<CancellationToken>()))
                .Callback((PlasticsRubberRequestContext request, CyclicJobConfiguration configuration,
                    CancellationToken _) =>
                {
                    Assert.That(request.SystemContext, Is.SameAs(m_context));
                    Assert.That(request.TargetId, Is.EqualTo(node.NodeId));
                    received = configuration;
                }).Returns(default(ValueTask));
            await using var binding = new CyclicJobInformationBinding(node, m_context, provider.Object, AllowAccess());

            SetCyclicJobDataMethodStateResult result = await InvokeCyclicConfigurationAsync(node, expected)
                .ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            AssertCyclicRequest(received!, expected);
            Assert.That(CyclicPublicationNodes(node), Is.EqualTo(selected));
            Assert.That(CyclicPublicationNodes(node).Count(value => value != null), Is.EqualTo(subset ? 10 : 0));
            AssertCyclicPublication(node, expected, StatusCodes.Good);
            provider.Verify(value => value.SetAsync(
                It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<CyclicJobConfiguration>(),
                It.IsAny<CancellationToken>()), Times.Once);
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Invalid configuration names and durations have one specific error and never reach commit.
        /// </summary>
        [TestCase(null, 17.5)]
        [TestCase("", 17.5)]
        [TestCase(" \t ", 17.5)]
        [TestCase("valid", -double.Epsilon)]
        [TestCase("valid", -17.5)]
        [TestCase("valid", double.NaN)]
        [TestCase("valid", double.NegativeInfinity)]
        [TestCase("valid", double.PositiveInfinity)]
        public async Task CyclicConfigurationRejectsInvalidNamesAndDurationsAsync(string? name, double duration)
        {
            CyclicJobInformationState node = CreateCyclicConfigurationNode();
            CyclicJobConfiguration old = SeedOldCyclicConfiguration(node, false);
            var provider = new Mock<ICyclicJobConfigurationProvider>(MockBehavior.Strict);
            await using var binding = new CyclicJobInformationBinding(node, m_context, provider.Object, AllowAccess());
            CyclicJobConfiguration input = ConfigurationRequest(2) with { JobName = name!,
                ExpectedCycleTime = duration };

            SetCyclicJobDataMethodStateResult result = await InvokeCyclicConfigurationAsync(node, input)
                .ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            AssertCyclicPublication(node, old, StatusCodes.UncertainDataSubNormal);
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Guards, rejected commits, cancellation and disposal preserve every old field and its quality.
        /// </summary>
        [TestCase("foreign")]
        [TestCase("denied-valid")]
        [TestCase("denied-invalid")]
        [TestCase("service")]
        [TestCase("exception")]
        [TestCase("cancel")]
        [TestCase("disposed")]
        public async Task CyclicConfigurationGuardsAndProviderFailuresPreservePublicationAsync(string outcome)
        {
            CyclicJobInformationState node = CreateCyclicConfigurationNode();
            CyclicJobConfiguration old = SeedOldCyclicConfiguration(node, true);
            CyclicJobConfiguration input = ConfigurationRequest(2);
            if (outcome == "denied-invalid")
            {
                input = input with { JobName = string.Empty, ExpectedCycleTime = double.NaN };
            }
            var provider = new Mock<ICyclicJobConfigurationProvider>(MockBehavior.Strict);
            var policy = new Mock<IPlasticsRubberAccessPolicy>(MockBehavior.Strict);
            ServiceResultException denied = ServiceResultException.Create(StatusCodes.BadUserAccessDenied, "Denied.");
            ServiceResultException rejected = ServiceResultException.Create(StatusCodes.BadResourceUnavailable,
                "Rejected.");
            var error = new InvalidOperationException("Configuration store failed.");
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var commit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var caller = new CancellationTokenSource();
            CancellationToken linked = default;
            bool reachesProvider = outcome is "service" or "exception" or "cancel";
            bool reachesPolicy = outcome is not "foreign" and not "disposed";
            if (reachesPolicy)
            {
                var authorization = policy.Setup(value => value.AuthorizeAsync(
                    It.Is<PlasticsRubberRequestContext>(request =>
                        ReferenceEquals(request.SystemContext, m_context) && request.TargetId == node.NodeId),
                    node.SetCyclicJobData!.NodeId, It.IsAny<CancellationToken>()));
                if (reachesProvider)
                {
                    authorization.Returns(default(ValueTask));
                }
                else
                {
                    authorization.Throws(denied);
                }
            }
            if (reachesProvider)
            {
                provider.Setup(value => value.SetAsync(
                    It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<CyclicJobConfiguration>(),
                    It.IsAny<CancellationToken>()))
                    .Returns(async (PlasticsRubberRequestContext request, CyclicJobConfiguration configuration,
                        CancellationToken token) =>
                    {
                        Assert.That(request.SystemContext, Is.SameAs(m_context));
                        Assert.That(request.TargetId, Is.EqualTo(node.NodeId));
                        AssertCyclicRequest(configuration, input);
                        linked = token;
                        using CancellationTokenRegistration registration = token.Register(()
                            => cancelled.TrySetResult(true));
                        entered.TrySetResult(true);
                        await commit.Task.ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        if (outcome == "service")
                        {
                            throw rejected;
                        }
                        throw error;
                    });
            }
            await using var binding = new CyclicJobInformationBinding(node, m_context, provider.Object, policy.Object);
            SetCyclicJobDataMethodState method = node.SetCyclicJobData!;
            SetCyclicJobDataMethodStateMethodAsyncCallHandler handler = method.OnCallAsync!;
            if (outcome == "disposed")
            {
                await binding.DisposeAsync().ConfigureAwait(false);
            }

            Task<SetCyclicJobDataMethodStateResult> pending = handler(
                m_context, method, outcome == "foreign" ? new NodeId("other-configuration", 1) : node.NodeId,
                input.JobName, input.JobDescription!, input.CustomerName!,
                input.ProductionDatasetName!, input.ProductionDatasetDescription!,
                input.Material, input.ProductName, input.ProductDescription, input.ContinueAtJobEnd,
                input.NominalParts, input.NominalBoxParts, input.ExpectedCycleTime,
                input.MouldId!, input.NumCavities, caller.Token).AsTask();
            try
            {
                if (reachesProvider)
                {
                    Task first = await Task.WhenAny(entered.Task, pending).ConfigureAwait(false);
                    Assert.That(first, Is.SameAs(entered.Task));
                    Assert.That(pending.IsCompleted, Is.False);
                    AssertCyclicPublication(node, old, StatusCodes.UncertainDataSubNormal);
                    if (outcome == "cancel")
                    {
                        caller.Cancel();
                        await cancelled.Task.ConfigureAwait(false);
                        Assert.That(linked.IsCancellationRequested, Is.True);
                    }
                }
            }
            finally
            {
                commit.TrySetResult(true);
            }
            if (outcome == "exception")
            {
                Assert.That(Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await pending.ConfigureAwait(false)), Is.SameAs(error));
            }
            else if (outcome == "cancel")
            {
                Assert.CatchAsync<OperationCanceledException>(async () => await pending.ConfigureAwait(false));
            }
            else
            {
                SetCyclicJobDataMethodStateResult result = await pending.ConfigureAwait(false);
                StatusCode expected = outcome switch
                {
                    "foreign" => StatusCodes.BadNodeIdInvalid,
                    "disposed" => StatusCodes.BadInvalidState,
                    "service" => StatusCodes.BadResourceUnavailable,
                    _ => StatusCodes.BadUserAccessDenied
                };
                Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(expected));
                if (outcome.StartsWith("denied", StringComparison.Ordinal))
                {
                    Assert.That(result.ServiceResult, Is.SameAs(denied.Result));
                }
                if (outcome == "service")
                {
                    Assert.That(result.ServiceResult, Is.SameAs(rejected.Result));
                }
            }
            AssertCyclicPublication(node, old, StatusCodes.UncertainDataSubNormal);
            Assert.That(method.Executable, Is.EqualTo(outcome != "disposed"));
            Assert.That(method.UserExecutable, Is.EqualTo(outcome != "disposed"));
            policy.Verify(value => value.AuthorizeAsync(
                It.IsAny<PlasticsRubberRequestContext>(), method.NodeId, It.IsAny<CancellationToken>()),
                reachesPolicy ? Times.Once() : Times.Never());
            provider.Verify(value => value.SetAsync(
                It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<CyclicJobConfiguration>(),
                It.IsAny<CancellationToken>()), reachesProvider ? Times.Once() : Times.Never());
            policy.VerifyNoOtherCalls();
            provider.VerifyNoOtherCalls();
        }

        private CyclicJobInformationState CreateCyclicConfigurationNode()
        {
            CyclicJobInformationState node = m_context.CreateInstanceOfCyclicJobInformationType(
                Parent(), new QualifiedName("CyclicConfiguration", 1));
            node.AddExpectedCycleTime(m_context);
            node.AddMouldId(m_context);
            node.AddNominalBoxParts(m_context);
            node.AddNumCavities(m_context);
            return node;
        }

        private ValueTask<SetCyclicJobDataMethodStateResult> InvokeCyclicConfigurationAsync(
            CyclicJobInformationState node,
            CyclicJobConfiguration input,
            NodeId? target = null,
            CancellationToken cancellationToken = default)
        {
            return node.SetCyclicJobData!.OnCallAsync!(
                m_context, node.SetCyclicJobData, target ?? node.NodeId,
                input.JobName, input.JobDescription!, input.CustomerName!,
                input.ProductionDatasetName!, input.ProductionDatasetDescription!,
                input.Material, input.ProductName, input.ProductDescription, input.ContinueAtJobEnd,
                input.NominalParts, input.NominalBoxParts, input.ExpectedCycleTime,
                input.MouldId!, input.NumCavities, cancellationToken);
        }

        private static CyclicJobConfiguration ConfigurationRequest(int partition)
        {
            return new CyclicJobConfiguration
            {
                JobName = "order-721",
                JobDescription = "short-run moulding",
                CustomerName = "customer-42",
                ProductionDatasetName = "recipe-93",
                ProductionDatasetDescription = "dry PA6 recipe",
                Material = partition switch { 0 => [], 1 => ["PA6"], _ => ["PA6", "PA6", "glass"] },
                ProductName = partition switch { 0 => [], 1 => ["housing"], _ => ["housing", "lid", "housing"] },
                ProductDescription = partition switch { 0 => [], 1 => ["front"], _ => ["front", "rear", "front"] },
                ContinueAtJobEnd = partition % 2 != 0,
                NominalParts = partition switch { 0 => 0, 1 => 1, 2 => 4294967296UL, _ => ulong.MaxValue },
                NominalBoxParts = partition switch { 0 => 1, 1 => 0, 2 => ulong.MaxValue, _ => 4294967297UL },
                ExpectedCycleTime = partition switch { 0 => 0, 1 => double.Epsilon, 2 => 21.125, _ => double.MaxValue },
                MouldId = "mould-304",
                NumCavities = partition switch { 0 => 0, 1 => 1, _ => uint.MaxValue }
            };
        }

        private static CyclicJobConfiguration SeedOldCyclicConfiguration(CyclicJobInformationState node,
            bool continueAtEnd)
        {
            var old = new CyclicJobConfiguration
            {
                JobName = "old-order",
                JobDescription = "old-description",
                CustomerName = "old-customer",
                ProductionDatasetName = "old-dataset",
                ProductionDatasetDescription = "old-recipe-description",
                Material = ["old-material"],
                ProductName = ["old-product"],
                ProductDescription = ["old-product-description"],
                ContinueAtJobEnd = continueAtEnd,
                NominalParts = 77,
                NominalBoxParts = 11,
                ExpectedCycleTime = 8.75,
                MouldId = "old-mould",
                NumCavities = 6
            };
            BaseVariableState?[] variables = CyclicPublicationNodes(node);
            Variant[] values = CyclicPublicationValues(old);
            for (int index = 0; index < variables.Length; index++)
            {
                BaseVariableState? variable = variables[index];
                if (variable != null)
                {
                    variable.Value = values[index];
                    variable.StatusCode = StatusCodes.UncertainDataSubNormal;
                }
            }
            return old;
        }

        private static BaseVariableState?[] CyclicPublicationNodes(CyclicJobInformationState node)
        {
            return
            [
                node.JobName, node.JobDescription, node.CustomerName, node.ProductionDatasetName,
                node.ProductionDatasetDescription, node.Material, node.ProductName, node.ProductDescription,
                node.ContinueAtJobEnd, node.NominalParts, node.NominalBoxParts, node.ExpectedCycleTime,
                node.MouldId, node.NumCavities
            ];
        }

        private static Variant[] CyclicPublicationValues(CyclicJobConfiguration configuration)
        {
            return
            [
                Variant.From(configuration.JobName), Variant.From(configuration.JobDescription!),
                Variant.From(configuration.CustomerName!), Variant.From(configuration.ProductionDatasetName!),
                Variant.From(configuration.ProductionDatasetDescription!), Variant.From(configuration.Material),
                Variant.From(configuration.ProductName), Variant.From(configuration.ProductDescription),
                Variant.From(configuration.ContinueAtJobEnd), Variant.From(configuration.NominalParts),
                Variant.From(configuration.NominalBoxParts), Variant.From(configuration.ExpectedCycleTime),
                Variant.From(configuration.MouldId!), Variant.From(configuration.NumCavities)
            ];
        }

        private static void AssertCyclicPublication(
            CyclicJobInformationState node, CyclicJobConfiguration expected, StatusCode status)
        {
            BaseVariableState?[] variables = CyclicPublicationNodes(node);
            Variant[] values = CyclicPublicationValues(expected);
            Assert.Multiple(() =>
            {
                for (int index = 0; index < variables.Length; index++)
                {
                    BaseVariableState? variable = variables[index];
                    if (variable != null)
                    {
                        Assert.That(variable.Value, Is.EqualTo(values[index]), variable.BrowseName.ToString());
                        Assert.That(variable.StatusCode, Is.EqualTo(status), variable.BrowseName.ToString());
                    }
                }
            });
        }

        private static void AssertCyclicRequest(CyclicJobConfiguration actual, CyclicJobConfiguration expected)
        {
            Assert.That(actual, Is.Not.Null, "The configuration must reach the provider.");
            Assert.Multiple(() =>
            {
                Assert.That(actual.JobName, Is.EqualTo(expected.JobName));
                Assert.That(actual.JobDescription, Is.EqualTo(expected.JobDescription));
                Assert.That(actual.CustomerName, Is.EqualTo(expected.CustomerName));
                Assert.That(actual.ProductionDatasetName, Is.EqualTo(expected.ProductionDatasetName));
                Assert.That(actual.ProductionDatasetDescription, Is.EqualTo(expected.ProductionDatasetDescription));
                Assert.That(actual.Material.ToArray(), Is.EqualTo(expected.Material.ToArray()));
                Assert.That(actual.ProductName.ToArray(), Is.EqualTo(expected.ProductName.ToArray()));
                Assert.That(actual.ProductDescription.ToArray(), Is.EqualTo(expected.ProductDescription.ToArray()));
                Assert.That(actual.ContinueAtJobEnd, Is.EqualTo(expected.ContinueAtJobEnd));
                Assert.That(actual.NominalParts, Is.EqualTo(expected.NominalParts));
                Assert.That(actual.NominalBoxParts, Is.EqualTo(expected.NominalBoxParts));
                Assert.That(actual.ExpectedCycleTime, Is.EqualTo(expected.ExpectedCycleTime));
                Assert.That(actual.MouldId, Is.EqualTo(expected.MouldId));
                Assert.That(actual.NumCavities, Is.EqualTo(expected.NumCavities));
            });
        }
    }
}
