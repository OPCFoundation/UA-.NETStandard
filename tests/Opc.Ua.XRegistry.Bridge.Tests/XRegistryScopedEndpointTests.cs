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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using Opc.Ua.XRegistry.Bridge.Tests.Sync;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Tests
{
    [TestFixture]
    public sealed class XRegistryScopedEndpointTests
    {
        [Test]
        public async Task PreparationPinsTheSelectedEndpointThroughCredentialRotationAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            using XRegistryTransactionalEndpoint first = Provider("first");
            using XRegistryTransactionalEndpoint second = Provider("second");
            IXRegistryEndpoint selected = first;
            int acquisitions = 0;
            int releases = 0;
            var resolver = new XRegistryEndpointResolver((caller, _) =>
            {
                acquisitions++;
                return new ValueTask<IXRegistryEndpointLease>(new XRegistryEndpointLease(selected, caller, () =>
                {
                    releases++;
                    return default;
                }));
            });
            var endpoint = new XRegistryScopedEndpoint(resolver, fixture.Telemetry);
            IXRegistryPreparedOperation operation =
                await endpoint.PrepareAsync(new XRegistryRequest(XRegistryAction.Merge, "/")
                {
                    Context = XRegistrySyncFixture.Writer,
                    Metadata = XRegistrySyncFixture.Json(/*lang=json,strict*/ """{"name":"pinned"}""")
                }).ConfigureAwait(false);
            selected = second;
            await using (operation.ConfigureAwait(false))
            {
                var snapshot = (IXRegistryPreparedSnapshot)operation;
                XRegistryResponse candidate =
                    await snapshot.ReadCandidateAsync(new XRegistryRequest(XRegistryAction.Read, "/")
                    {
                        Context = new XRegistryCallContext("another-caller")
                    }).ConfigureAwait(false);
                Assert.That(candidate.Metadata.GetProperty("name").GetString(), Is.EqualTo("pinned"));
                Assert.That(acquisitions, Is.EqualTo(1));
                Assert.That(releases, Is.Zero);
                Assert.That((await operation.CommitAsync().ConfigureAwait(false)).StatusCode, Is.EqualTo(200));
            }
            XRegistryResponse committed = await first.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            XRegistryResponse untouched = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/")
            { Context = XRegistrySyncFixture.Writer }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(committed.Metadata.GetProperty("name").GetString(), Is.EqualTo("pinned"));
                Assert.That(untouched.Metadata.TryGetProperty("name", out _), Is.False);
                Assert.That(acquisitions, Is.EqualTo(2));
                Assert.That(releases, Is.EqualTo(2));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RevokedPreparationCannotPublishAsync(bool signal)
        {
            await using var fixture = new XRegistrySyncFixture();
            using XRegistryTransactionalEndpoint provider = Provider("registry");
            using var revoked = new CancellationTokenSource();
            bool authorized = true;
            int releases = 0;
            var resolver = new XRegistryEndpointResolver((caller, _) =>
                new ValueTask<IXRegistryEndpointLease>(new XRegistryEndpointLease(provider, caller, () =>
                {
                    releases++;
                    return default;
                }, _ => new ValueTask<bool>(authorized), revoked.Token)));
            var endpoint = new XRegistryScopedEndpoint(resolver, fixture.Telemetry);
            IXRegistryPreparedOperation operation =
                await endpoint.PrepareAsync(new XRegistryRequest(XRegistryAction.Merge, "/")
                {
                    Context = XRegistrySyncFixture.Writer,
                    Metadata = XRegistrySyncFixture.Json(/*lang=json,strict*/ """{"name":"must-not-publish"}""")
                }).ConfigureAwait(false);
            await using (operation.ConfigureAwait(false))
            {
                authorized = false;
                if (signal)
                {
                    await revoked.CancelAsync().ConfigureAwait(false);
                }
                Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                    await operation.CommitAsync().ConfigureAwait(false));
            }
            XRegistryResponse root = await provider.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(root.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                Assert.That(releases, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task IncapableEndpointIsNotAdvertisedAsPreparedAndCannotMutateAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            var backend = new Mock<IXRegistryEndpoint>(MockBehavior.Strict);
            backend.Setup(value => value.InspectAsync(XRegistrySyncFixture.Writer, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new XRegistryEndpointDescription("read-only")
                {
                    SupportsPreparedMutations = true,
                    SupportsPreparedSnapshots = true,
                    SupportsOperationReplay = true
                });
            var endpoint =
                new XRegistryScopedEndpoint(XRegistryEndpointResolver.Borrow(backend.Object), fixture.Telemetry);
            XRegistryEndpointDescription description = await endpoint.InspectAsync(XRegistrySyncFixture.Writer)
                .ConfigureAwait(false);
            XRegistryResponse response = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Merge, "/")
            { Context = XRegistrySyncFixture.Writer }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(description.SupportsPreparedMutations, Is.False);
                Assert.That(description.SupportsPreparedSnapshots, Is.False);
                Assert.That(description.SupportsOperationReplay, Is.False);
                Assert.That(response.StatusCode, Is.EqualTo(405));
            });
            backend.Verify(
                value => value.ExecuteAsync(It.IsAny<XRegistryRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task CleanupFailureDoesNotRewriteKnownCommitAsARejectionAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            using XRegistryTransactionalEndpoint provider = Provider("registry");
            var resolver = new XRegistryEndpointResolver((caller, _) =>
                new ValueTask<IXRegistryEndpointLease>(new XRegistryEndpointLease(provider, caller,
                    () => throw new IOException("Injected release failure."))));
            var endpoint = new XRegistryScopedEndpoint(resolver, fixture.Telemetry);
            XRegistryResponse response = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Merge, "/")
            {
                Context = XRegistrySyncFixture.Writer,
                Metadata = XRegistrySyncFixture.Json(/*lang=json,strict*/ """{"name":"committed"}""")
            }).ConfigureAwait(false);
            XRegistryResponse root = await provider.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(200));
                Assert.That(root.Metadata.GetProperty("name").GetString(), Is.EqualTo("committed"));
            });
        }

        private static XRegistryTransactionalEndpoint Provider(string id)
        {
            return new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
            {
                RegistryId = id,
                Model = XRegistrySyncFixture.Json(/*lang=json,strict*/ """{"groups":{}}""")
            }, new InMemoryXRegistryTransactionStore());
        }
    }
}
