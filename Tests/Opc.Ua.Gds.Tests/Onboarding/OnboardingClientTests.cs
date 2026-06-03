/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Gds.Client;

#nullable enable

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Tests for <see cref="OnboardingClient"/>.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("Onboarding")]
    public sealed class OnboardingClientTests
    {
        private static readonly NodeId kRegistrarId = new NodeId("Reg", 2);

        private static Mock<ISession> CreateSessionMock()
        {
            var mock = new Mock<ISession>();
            var nsTable = new NamespaceTable();
            // Seed with the GDS namespace (which carries the Onboarding
            // ObjectTypes after the model relocation). OnboardingClient
            // resolves method NodeIds via browse-path translation, not
            // by namespace-constant lookup, so this is only here to
            // give the mocked session a non-empty namespace table.
            nsTable.GetIndexOrAppend(global::Opc.Ua.Gds.Namespaces.OpcUaGds);
            mock.SetupGet(s => s.NamespaceUris).Returns(nsTable);
            return mock;
        }

        private static ITelemetryContext NullTelemetry()
        {
            return new Mock<ITelemetryContext>().Object;
        }

        private static void SetupTranslateToMethod(
            Mock<ISession> sessionMock, NodeId methodId)
        {
            sessionMock
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = new BrowsePathResult[]
                    {
                        new BrowsePathResult
                        {
                            StatusCode = StatusCodes.Good,
                            Targets = new BrowsePathTarget[]
                            {
                                new BrowsePathTarget
                                {
                                    TargetId = new ExpandedNodeId(methodId)
                                }
                            }.ToArrayOf()
                        }
                    }.ToArrayOf()
                });
        }

        private static void SetupCallReturnsStatuses(
            Mock<ISession> sessionMock, int[] statuses)
        {
            sessionMock
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results = new CallMethodResult[]
                    {
                        new CallMethodResult
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments = new Variant[]
                            {
                                new Variant(statuses.ToArrayOf())
                            }.ToArrayOf()
                        }
                    }.ToArrayOf()
                });
        }

        [Test]
        public void ConstructorRejectsNullSession()
        {
            Assert.Throws<ArgumentNullException>(
                () => new OnboardingClient(null!, kRegistrarId, NullTelemetry()));
        }

        [Test]
        public void ConstructorRejectsNullRegistrarId()
        {
            Mock<ISession> s = CreateSessionMock();
            Assert.Throws<ArgumentException>(
                () => new OnboardingClient(s.Object, NodeId.Null, NullTelemetry()));
        }

        [Test]
        public void ConstructorRejectsNullTelemetry()
        {
            Mock<ISession> s = CreateSessionMock();
            Assert.Throws<ArgumentNullException>(
                () => new OnboardingClient(s.Object, kRegistrarId, null!));
        }

        [Test]
        public async Task RegisterTicketsReturnsStatusArray()
        {
            Mock<ISession> session = CreateSessionMock();
            SetupTranslateToMethod(session, new NodeId("Reg_Register", 2));
            SetupCallReturnsStatuses(session, new[]
            {
                (int)(uint)StatusCodes.Good,
                (int)(uint)StatusCodes.BadEntryExists
            });

            var client = new OnboardingClient(
                session.Object, kRegistrarId, NullTelemetry());

            int[] statuses = await client.RegisterTicketsAsync(new[]
            {
                new byte[] { 1, 2 },
                new byte[] { 3, 4 }
            }).ConfigureAwait(false);

            Assert.That(statuses, Has.Length.EqualTo(2));
            Assert.That(statuses[0], Is.EqualTo((int)(uint)StatusCodes.Good));
            Assert.That(statuses[1], Is.EqualTo((int)(uint)StatusCodes.BadEntryExists));
        }

        [Test]
        public async Task UnregisterTicketsReturnsStatusArray()
        {
            Mock<ISession> session = CreateSessionMock();
            SetupTranslateToMethod(session, new NodeId("Reg_Unregister", 2));
            SetupCallReturnsStatuses(session, new[]
            {
                (int)(uint)StatusCodes.Good
            });

            var client = new OnboardingClient(
                session.Object, kRegistrarId, NullTelemetry());

            int[] statuses = await client.UnregisterTicketsAsync(new[]
            {
                new byte[] { 9 }
            }).ConfigureAwait(false);

            Assert.That(statuses, Has.Length.EqualTo(1));
            Assert.That(statuses[0], Is.EqualTo((int)(uint)StatusCodes.Good));
        }

        [Test]
        public void RegisterTicketsThrowsOnUnresolvedMethod()
        {
            Mock<ISession> session = CreateSessionMock();
            session
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = new BrowsePathResult[]
                    {
                        new BrowsePathResult
                        {
                            StatusCode = StatusCodes.BadNotFound,
                            Targets = global::Opc.Ua.ArrayOf.Empty<BrowsePathTarget>()
                        }
                    }.ToArrayOf()
                });

            var client = new OnboardingClient(
                session.Object, kRegistrarId, NullTelemetry());

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.RegisterTicketsAsync(new[]
                {
                    new byte[] { 1 }
                }).ConfigureAwait(false));
        }

        [Test]
        public void RegisterTicketsRejectsNullArray()
        {
            Mock<ISession> session = CreateSessionMock();
            var client = new OnboardingClient(
                session.Object, kRegistrarId, NullTelemetry());

            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await client.RegisterTicketsAsync(null!).ConfigureAwait(false));
        }
    }
}
