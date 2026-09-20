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
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
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
