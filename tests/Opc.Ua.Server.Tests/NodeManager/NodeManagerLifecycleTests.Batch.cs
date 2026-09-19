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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchOwnsItsChangesAcrossAwaitAsync(bool mutateCallerArray)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            NodeManagerRegistration unrelated = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kReadinessProbeNamespaceUri, 707), null, timeout.Token).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var originalFactory = new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue));
            var delayedFactory = new Mock<IAsyncNodeManagerFactory>();
            delayedFactory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken token) =>
                    CreateDelayedAsync(server, configuration, token));
            NodeManagerBatchChange[] changes =
            [
                NodeManagerBatchChange.Add(delayedFactory.Object),
                NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))
            ];
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            Task<IPreparedNodeManagerBatch> pending = lifecycle.PrepareAsync(changes, timeout.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (mutateCallerArray)
                {
                    changes[1] = NodeManagerBatchChange.Remove(unrelated);
                }
            }
            finally
            {
                release.TrySetResult(true);
            }
            await using IPreparedNodeManagerBatch prepared = await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
            NodeManagerBatchResult result = await prepared.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
            DataValue second = await ReadValueAsync(new NodeId(kValueNodeId,
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri))).ConfigureAwait(false);
            DataValue original = await ReadValueAsync(new NodeId(kValueNodeId,
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kReadinessProbeNamespaceUri))).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Registrations.Count, Is.EqualTo(2));
                Assert.That(lifecycle.Registrations.Count, Is.EqualTo(3));
                Assert.That(lifecycle.Registrations.ToList(), Does.Contain(unrelated));
                Assert.That(result.Retired, Is.Zero);
                Assert.That(second.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(second.WrappedValue, Is.EqualTo(new Variant(kSecondRegistrationValue)));
                Assert.That(original.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(original.WrappedValue, Is.EqualTo(new Variant(707)));
            }

            async ValueTask<IAsyncNodeManager> CreateDelayedAsync(
                IServerInternal server, ApplicationConfiguration configuration, CancellationToken token)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                return await originalFactory.CreateAsync(server, configuration, token).ConfigureAwait(false);
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task PreparedBatchPostdecisionFailuresStillReconcileAsync(
            bool failPublication, bool failReadiness)
        {
            TrackingLifecycleNodeManager originalManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, manager => originalManager = manager), null)
                .ConfigureAwait(false);
            var publicationFailure = new IOException("Post-decision publication callback failed.");
            var readinessFailure = new InvalidOperationException("Committed readiness failed.");
            ReadinessLifecycleNodeManager replacement = null;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken _) =>
                {
                    replacement = new ReadinessLifecycleNodeManager(server, configuration, m_logger, kGeneration2Value)
                    {
                        ReadinessCallback = token =>
                        {
                            Assert.That(token.CanBeCanceled, Is.False);
                            if (failReadiness)
                            {
                                throw readinessFailure;
                            }
                            return default;
                        }
                    };
                    return new ValueTask<IAsyncNodeManager>(replacement);
                });
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Replace(original, factory.Object)]).ConfigureAwait(false);
            int decisions = 0;
            int publications = 0;
            NodeManagerBatchResult result = null;
            IOException escaped = null;
            try
            {
                result = await prepared.CommitAsync(
                    _ =>
                    {
                        decisions++;
                        return default;
                    },
                    () =>
                    {
                        publications++;
                        Assert.That(lifecycle.Registrations[0], Is.SameAs(prepared.Registrations[0]));
                        if (failPublication)
                        {
                            throw publicationFailure;
                        }
                    }).ConfigureAwait(false);
            }
            catch (IOException failure)
            {
                escaped = failure;
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(escaped, Is.Null, "A post-decision callback failure must remain a committed outcome.");
                Assert.That(result, Is.Not.Null);
                Assert.That(prepared.IsCommitted, Is.True);
                Assert.That(decisions, Is.EqualTo(1));
                Assert.That(publications, Is.EqualTo(1));
                Assert.That(lifecycle.Registrations.Count, Is.EqualTo(1));
                Assert.That(lifecycle.Registrations[0].Generation, Is.EqualTo(original.Generation + 1));
                Assert.That(replacement.ReadinessCount, Is.EqualTo(1));
                Assert.That(replacement.ReadinessCompletedCount, Is.EqualTo(failReadiness ? 0 : 1));
                Assert.That(originalManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
                if (result is not null)
                {
                    Assert.That(result.Retired, Is.EqualTo(1u));
                    if (failPublication || failReadiness)
                    {
                        Assert.That(result.CleanupFailure, Is.TypeOf<AggregateException>());
                        var failures = ((AggregateException)result.CleanupFailure).Flatten().InnerExceptions;
                        Assert.That(failures, Has.Count.EqualTo((failPublication ? 1 : 0) + (failReadiness ? 1 : 0)));
                        if (failPublication)
                        {
                            Assert.That(failures, Does.Contain(publicationFailure));
                        }
                        if (failReadiness)
                        {
                            Assert.That(failures, Does.Contain(readinessFailure));
                        }
                    }
                    else
                    {
                        Assert.That(result.CleanupFailure, Is.Null);
                    }
                }
            }
            await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value).ConfigureAwait(false);
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedBatchRejectsChangedRoutingInputsOverNativeTcp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var client = new ClientFixture(false, true, telemetry);
            await client.LoadClientConfigurationAsync(m_pkiRoot);
            Assert.That(client.SessionFactory, Is.TypeOf<DefaultSessionFactory>());
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None);
            try
            {
                var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
                await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [
                        NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                            CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))
                    ]);
                NodeManagerRegistration intervening = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                    CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue), null);
                int decisions = 0;

                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    await prepared.CommitAsync(_ =>
                    {
                        decisions++;
                        return default;
                    });
                });

                Assert.That(decisions, Is.Zero);
                Assert.That(prepared.IsCommitted, Is.False);
                Assert.That(m_server.NodeManagerLifecycle.Registrations.Count, Is.EqualTo(1));
                Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList(), Does.Contain(intervening));
                ArrayOf<ReadValueId> nodes =
                [
                    new ReadValueId
                    {
                        NodeId = new NodeId(kValueNodeId,
                            (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)),
                        AttributeId = Attributes.Value
                    },
                    new ReadValueId
                    {
                        NodeId = new NodeId(kValueNodeId,
                            (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri)),
                        AttributeId = Attributes.Value
                    }
                ];
                ReadResponse response = await session.ReadAsync(
                    null, 0, TimestampsToReturn.Neither, nodes, CancellationToken.None);
                Assert.That(response.Results.Count, Is.EqualTo(2));
                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(response.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results[1].WrappedValue.TryGetValue(out int number), Is.True);
                Assert.That(number, Is.EqualTo(202));
            }
            finally
            {
                await session.CloseAsync();
            }
        }

        [Test]
        public async Task PreparedBatchRejectsChangedRoutingInputsBeforeDecision()
        {
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))
                ]);
            NodeManagerRegistration intervening = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue), null);
            int decisions = 0;

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                });
            });

            Assert.That(decisions, Is.Zero);
            Assert.That(prepared.IsCommitted, Is.False);
            Assert.That(m_server.NodeManagerLifecycle.Registrations.Count, Is.EqualTo(1));
            Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList(), Does.Contain(intervening));
            DataValue unpublished = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
            DataValue retained = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri)));
            Assert.That(unpublished.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(retained.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(retained.WrappedValue.TryGetValue(out int number), Is.True);
            Assert.That(number, Is.EqualTo(202));
        }

        [Test]
        public async Task PreparedBatchDecisionFailureLeavesBothExistingGenerationsActive()
        {
            NodeManagerRegistration first = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue), null);
            NodeManagerRegistration second = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue), null);
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Replace(first, new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, 303))),
                    NodeManagerBatchChange.Replace(second, new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, 404)))
                ]);

            Assert.ThrowsAsync<IOException>(async () =>
            {
                await prepared.CommitAsync(_ => throw new IOException("Confirmed decision noncommit."));
            });

            Assert.That(prepared.IsCommitted, Is.False);
            Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList(), Does.Contain(first));
            Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList(), Does.Contain(second));
            DataValue firstValue = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
            DataValue secondValue = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri)));
            Assert.That(firstValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(secondValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(firstValue.WrappedValue.TryGetValue(out int firstNumber), Is.True);
            Assert.That(secondValue.WrappedValue.TryGetValue(out int secondNumber), Is.True);
            Assert.That(firstNumber, Is.EqualTo(101));
            Assert.That(secondNumber, Is.EqualTo(202));
        }

        [Test]
        public async Task PreparedBatchRejectsStaleRegistrationBeforeDecision()
        {
            NodeManagerRegistration first = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue), null);
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Replace(first, new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, 303)))
                ]);
            NodeManagerRegistration winner = await m_server.NodeManagerLifecycle.ShadowReloadRuntimeNodeSetAsync(
                first, CreateOptions(kModelNamespaceUri, 505));
            int decisions = 0;

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                });
            });

            Assert.That(decisions, Is.Zero);
            Assert.That(prepared.IsCommitted, Is.False);
            Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList(), Does.Contain(winner));
            DataValue value = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out int number), Is.True);
            Assert.That(number, Is.EqualTo(505));
        }

        [Test]
        public async Task PreparedBatchPredecisionCancellationLeavesNoPublishedOwner()
        {
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))
                ]);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            int decisions = 0;

            Assert.CatchAsync<OperationCanceledException>(async () =>
            {
                await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }, cancellation.Token);
            });

            Assert.That(decisions, Is.Zero);
            Assert.That(prepared.IsCommitted, Is.False);
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.Empty);
            DataValue value = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task PreparedBatchPostdecisionCancellationKeepsCommittedOwner()
        {
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))
                ]);
            using var cancellation = new CancellationTokenSource();
            NodeManagerBatchResult result = await prepared.CommitAsync(_ =>
            {
                cancellation.Cancel();
                return default;
            }, cancellation.Token);

            Assert.That(prepared.IsCommitted, Is.True);
            Assert.That(result.Registrations.Count, Is.EqualTo(1));
            Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList(), Does.Contain(result.Registrations[0]));
            DataValue value = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out int number), Is.True);
            Assert.That(number, Is.EqualTo(101));
        }

        [Test]
        public async Task PreparedBatchRemainsPrivateUntilDecision()
        {
            Assert.That(m_server.NodeManagerLifecycle, Is.InstanceOf<INodeManagerBatchLifecycle>());
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, kFirstRegistrationValue))),
                    NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))
                ]);
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.Empty);
            int decisions = 0;
            NodeManagerBatchResult result = await prepared.CommitAsync(async _ =>
            {
                decisions++;
                DataValue first = await ReadValueAsync(new NodeId(
                    kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
                DataValue second = await ReadValueAsync(new NodeId(
                    kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri)));
                Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(second.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.Empty);
            });
            Assert.That(decisions, Is.EqualTo(1));
            Assert.That(result.Registrations.Count, Is.EqualTo(2));
            Assert.That(m_server.NodeManagerLifecycle.Registrations.Count, Is.EqualTo(2));
            DataValue publishedFirst = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
            DataValue publishedSecond = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri)));
            Assert.That(publishedFirst.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(publishedSecond.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(publishedFirst.WrappedValue.TryGetValue(out int firstValue), Is.True);
            Assert.That(publishedSecond.WrappedValue.TryGetValue(out int secondValue), Is.True);
            Assert.That(firstValue, Is.EqualTo(kFirstRegistrationValue));
            Assert.That(secondValue, Is.EqualTo(kSecondRegistrationValue));
        }
    }
}
