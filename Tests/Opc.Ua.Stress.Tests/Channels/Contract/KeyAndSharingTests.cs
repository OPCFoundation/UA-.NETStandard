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

using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Stress.Tests.Channels.Fakes;

namespace Opc.Ua.Stress.Tests.Channels.Contract
{
    /// <summary>
    /// Layer-1 contract tests for channel-manager key equivalence and lease sharing.
    /// </summary>
    [TestFixture]
    [Category("Contract")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class KeyAndSharingTests : ContractTestBase
    {
        [Test]
        public async Task L1Key1SameEndpointSameCertificateForwardSharesLeaseAsync()
        {
            ContractHarness fixture = CreateHarness();
            await using ConfiguredAsyncDisposable fixtureAsyncDisposable = fixture.ConfigureAwait(false);
            ByteString serverCertificate = CreateServerCertificateBlob();
            ConfiguredEndpoint endpoint = CreateEndpoint(
                DefaultEndpointUrl,
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.SignAndEncrypt,
                serverCertificate);
            var firstParticipant = new FakeParticipant(endpoint);
            var secondParticipant = new FakeParticipant(endpoint);

            IManagedTransportChannel firstChannel = await fixture.Manager
                .GetAsync(firstParticipant, CancellationToken.None)
                .ConfigureAwait(false);
            try
            {
                IManagedTransportChannel secondChannel = await fixture.Manager
                    .GetAsync(secondParticipant, CancellationToken.None)
                    .ConfigureAwait(false);
                try
                {
                    Assert.That(firstChannel.Key, Is.EqualTo(secondChannel.Key));
                    AssertSingleDiagnostic(fixture.Manager, firstChannel.Key, expectedRefcount: 2);
                    Assert.That(fixture.Bindings.Created, Has.Count.EqualTo(1));
                }
                finally
                {
                    await secondChannel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                await firstChannel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task L1Key2DifferentReverseConnectIdentityCreatesDistinctKeysAsync()
        {
            ContractHarness fixture = CreateHarness();
            await using ConfiguredAsyncDisposable fixtureAsyncDisposable = fixture.ConfigureAwait(false);
            ConfiguredEndpoint endpoint = CreateEndpoint();
            var firstParticipant = new FakeParticipant(endpoint);
            var secondParticipant = new FakeParticipant(endpoint);
            var firstReverseConnection = new Mock<ITransportWaitingConnection>();
            var secondReverseConnection = new Mock<ITransportWaitingConnection>();

            IManagedTransportChannel firstChannel = await fixture.Manager
                .GetAsync(firstParticipant, firstReverseConnection.Object, CancellationToken.None)
                .ConfigureAwait(false);
            try
            {
                IManagedTransportChannel secondChannel = await fixture.Manager
                    .GetAsync(secondParticipant, secondReverseConnection.Object, CancellationToken.None)
                    .ConfigureAwait(false);
                try
                {
                    Assert.That(firstChannel.Key, Is.Not.EqualTo(secondChannel.Key));
                    AssertDistinctDiagnostics(fixture.Manager, firstChannel.Key, secondChannel.Key);
                    Assert.That(fixture.Bindings.Created, Has.Count.EqualTo(2));
                }
                finally
                {
                    await secondChannel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                await firstChannel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task L1Key3SameReverseWaitingConnectionSharesLeaseAsync()
        {
            ContractHarness fixture = CreateHarness();
            await using ConfiguredAsyncDisposable fixtureAsyncDisposable = fixture.ConfigureAwait(false);
            ConfiguredEndpoint endpoint = CreateEndpoint();
            var firstParticipant = new FakeParticipant(endpoint);
            var secondParticipant = new FakeParticipant(endpoint);
            var reverseConnection = new Mock<ITransportWaitingConnection>();

            IManagedTransportChannel firstChannel = await fixture.Manager
                .GetAsync(firstParticipant, reverseConnection.Object, CancellationToken.None)
                .ConfigureAwait(false);
            try
            {
                IManagedTransportChannel secondChannel = await fixture.Manager
                    .GetAsync(secondParticipant, reverseConnection.Object, CancellationToken.None)
                    .ConfigureAwait(false);
                try
                {
                    Assert.That(firstChannel.Key, Is.EqualTo(secondChannel.Key));
                    AssertSingleDiagnostic(fixture.Manager, firstChannel.Key, expectedRefcount: 2);
                    Assert.That(fixture.Bindings.Created, Has.Count.EqualTo(1));
                }
                finally
                {
                    await secondChannel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                await firstChannel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task L1Key4ForwardAndReverseNeverShareAsync()
        {
            ContractHarness fixture = CreateHarness();
            await using ConfiguredAsyncDisposable fixtureAsyncDisposable = fixture.ConfigureAwait(false);
            ConfiguredEndpoint endpoint = CreateEndpoint();
            var forwardParticipant = new FakeParticipant(endpoint);
            var reverseParticipant = new FakeParticipant(endpoint);
            var reverseConnection = new Mock<ITransportWaitingConnection>();

            IManagedTransportChannel forwardChannel = await fixture.Manager
                .GetAsync(forwardParticipant, CancellationToken.None)
                .ConfigureAwait(false);
            try
            {
                IManagedTransportChannel reverseChannel = await fixture.Manager
                    .GetAsync(reverseParticipant, reverseConnection.Object, CancellationToken.None)
                    .ConfigureAwait(false);
                try
                {
                    Assert.That(forwardChannel.Key, Is.Not.EqualTo(reverseChannel.Key));
                    AssertDistinctDiagnostics(fixture.Manager, forwardChannel.Key, reverseChannel.Key);
                    Assert.That(fixture.Bindings.Created, Has.Count.EqualTo(2));
                }
                finally
                {
                    await reverseChannel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                await forwardChannel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task L1Key5DifferentEndpointUrlCreatesDistinctKeysAsync()
        {
            ContractHarness fixture = CreateHarness();
            await using ConfiguredAsyncDisposable fixtureAsyncDisposable = fixture.ConfigureAwait(false);
            ConfiguredEndpoint firstEndpoint = CreateEndpoint("opc.tcp://hostA");
            ConfiguredEndpoint secondEndpoint = CreateEndpoint("opc.tcp://hostB");
            var firstParticipant = new FakeParticipant(firstEndpoint);
            var secondParticipant = new FakeParticipant(secondEndpoint);

            IManagedTransportChannel firstChannel = await fixture.Manager
                .GetAsync(firstParticipant, CancellationToken.None)
                .ConfigureAwait(false);
            try
            {
                IManagedTransportChannel secondChannel = await fixture.Manager
                    .GetAsync(secondParticipant, CancellationToken.None)
                    .ConfigureAwait(false);
                try
                {
                    Assert.That(firstChannel.Key, Is.Not.EqualTo(secondChannel.Key));
                    AssertDistinctDiagnostics(fixture.Manager, firstChannel.Key, secondChannel.Key);
                    Assert.That(fixture.Bindings.Created, Has.Count.EqualTo(2));
                }
                finally
                {
                    await secondChannel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                await firstChannel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task L1Key6SameUrlDifferentSecurityPolicyCreatesDistinctKeysAsync()
        {
            ContractHarness fixture = CreateHarness();
            await using ConfiguredAsyncDisposable fixtureAsyncDisposable = fixture.ConfigureAwait(false);
            ConfiguredEndpoint firstEndpoint = CreateEndpoint(
                DefaultEndpointUrl,
                SecurityPolicies.None,
                MessageSecurityMode.None);
            ConfiguredEndpoint secondEndpoint = CreateEndpoint(
                DefaultEndpointUrl,
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.None);
            var firstParticipant = new FakeParticipant(firstEndpoint);
            var secondParticipant = new FakeParticipant(secondEndpoint);

            IManagedTransportChannel firstChannel = await fixture.Manager
                .GetAsync(firstParticipant, CancellationToken.None)
                .ConfigureAwait(false);
            try
            {
                IManagedTransportChannel secondChannel = await fixture.Manager
                    .GetAsync(secondParticipant, CancellationToken.None)
                    .ConfigureAwait(false);
                try
                {
                    Assert.That(firstChannel.Key, Is.Not.EqualTo(secondChannel.Key));
                    AssertDistinctDiagnostics(fixture.Manager, firstChannel.Key, secondChannel.Key);
                    Assert.That(fixture.Bindings.Created, Has.Count.EqualTo(2));
                }
                finally
                {
                    await secondChannel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                await firstChannel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task L1Key7SameUrlAndSecurityPolicyDifferentSecurityModeCreatesDistinctKeysAsync()
        {
            ContractHarness fixture = CreateHarness();
            await using ConfiguredAsyncDisposable fixtureAsyncDisposable = fixture.ConfigureAwait(false);
            ConfiguredEndpoint firstEndpoint = CreateEndpoint(
                DefaultEndpointUrl,
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.None);
            ConfiguredEndpoint secondEndpoint = CreateEndpoint(
                DefaultEndpointUrl,
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.SignAndEncrypt);
            var firstParticipant = new FakeParticipant(firstEndpoint);
            var secondParticipant = new FakeParticipant(secondEndpoint);

            IManagedTransportChannel firstChannel = await fixture.Manager
                .GetAsync(firstParticipant, CancellationToken.None)
                .ConfigureAwait(false);
            try
            {
                IManagedTransportChannel secondChannel = await fixture.Manager
                    .GetAsync(secondParticipant, CancellationToken.None)
                    .ConfigureAwait(false);
                try
                {
                    Assert.That(firstChannel.Key, Is.Not.EqualTo(secondChannel.Key));
                    AssertDistinctDiagnostics(fixture.Manager, firstChannel.Key, secondChannel.Key);
                    Assert.That(fixture.Bindings.Created, Has.Count.EqualTo(2));
                }
                finally
                {
                    await secondChannel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                await firstChannel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Test]
        public void L1Key8ManagedChannelKeyEqualityIsValueBased()
        {
            var reverseIdentity = new object();
            ManagedChannelKey firstKey = CreateManagedChannelKey(reverseIdentity);
            ManagedChannelKey equivalentKey = CreateManagedChannelKey(reverseIdentity);
            var differentKey = new ManagedChannelKey(
                "opc.tcp://localhost:4841",
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.SignAndEncrypt,
                ByteString.Create([1, 2, 3]),
                42,
                ByteString.Create([4, 5, 6]),
                reverseIdentity);

            Assert.Multiple(() =>
            {
                Assert.That(firstKey.Equals(equivalentKey), Is.True);
                Assert.That(firstKey, Is.EqualTo(equivalentKey));
                Assert.That(firstKey.GetHashCode(), Is.EqualTo(equivalentKey.GetHashCode()));
                Assert.That(firstKey, Is.Not.EqualTo(differentKey));
            });
        }

        private static ManagedChannelKey CreateManagedChannelKey(object reverseIdentity)
        {
            return new ManagedChannelKey(
                "opc.tcp://localhost:4840",
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.SignAndEncrypt,
                ByteString.Create([1, 2, 3]),
                42,
                ByteString.Create([4, 5, 6]),
                reverseIdentity);
        }
    }
}
