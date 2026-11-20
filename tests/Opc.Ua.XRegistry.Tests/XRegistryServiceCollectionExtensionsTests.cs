/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    /// <summary>
    /// Verifies the dependency injection wiring for the xRegistry server pieces. Direct
    /// construction remains supported, so these only cover the container path.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryServiceCollectionExtensionsTests
    {
        /// <summary>
        /// Both composition paths use the supplied host for publication and removal.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RefreshEnginePublishesAndRetiresWithSuppliedHostAsync(bool useContainer)
        {
            const string xid = "/groups/example/resources/pump";
            var member = new XRegistryRefreshMember(xid)
            {
                VersionId = "v1",
                ContentDigest = ByteString.From(new byte[] { 1, 2, 3 })
            };
            var closure = new XRegistryDependencyClosure(
                xid, [member], [member], [], [], hasCycle: false, hasMissingDependency: false);
            var document = new XRegistryProjectionDocument(
                xid, [new XRegistryProjectionSource("pump", ["urn:example:pump"], ByteString.Empty)]);
            var handle = new XRegistryProjectionHandle(xid, 1, null, [], 1);
            var strategy = new Mock<IXRegistryRefreshStrategy>(MockBehavior.Strict);
            var host = new Mock<IXRegistryProjectionHost>(MockBehavior.Strict);

            var sequence = new MockSequence();
            strategy.InSequence(sequence).Setup(value => value.EnumerateClosuresAsync(
                    It.IsAny<XRegistryRefreshRequest>(), CancellationToken.None))
                .ReturnsAsync([closure]);
            strategy.InSequence(sequence).Setup(value => value.PrepareClosureAsync(
                    It.Is<XRegistryClosurePreparationContext>(context => ReferenceEquals(context.Closure, closure)),
                    CancellationToken.None))
                .ReturnsAsync(XRegistryClosurePreparation.Ready(document));
            host.InSequence(sequence).Setup(value => value.AddAsync(document, CancellationToken.None))
                .ReturnsAsync(handle);
            strategy.InSequence(sequence).Setup(value => value.CommitClosureAsync(
                    It.Is<XRegistryClosureCommitContext>(context => ReferenceEquals(context.Handle, handle)),
                    CancellationToken.None))
                .ReturnsAsync(new XRegistryClosureCommitResult());
            strategy.InSequence(sequence).Setup(value => value.ApplyResultsAsync(
                    It.Is<ArrayOf<XRegistryRefreshItemResult>>(items =>
                        items.Count == 1 && items[0].Outcome == XRegistryRefreshOutcome.Success),
                    CancellationToken.None))
                .Returns(default(ValueTask));
            host.InSequence(sequence).Setup(value => value.RemoveAsync(handle, CancellationToken.None))
                .Returns(default(ValueTask));

            using ServiceProvider? provider = useContainer
                ? new ServiceCollection()
                    .AddXRegistryServer()
                    .AddSingleton(strategy.Object)
                    .AddSingleton(host.Object)
                    .AddScoped(static services => new XRegistryRefreshEngine(
                        services.GetRequiredService<IXRegistryRefreshStrategy>(),
                        services.GetRequiredService<IXRegistryProjectionHost>()))
                    .BuildServiceProvider()
                : null;
            using IServiceScope? scope = provider?.CreateScope();
            using XRegistryRefreshEngine? direct = useContainer ? null : new(strategy.Object, host.Object);
            XRegistryRefreshEngine engine = direct ?? scope!.ServiceProvider.GetRequiredService<XRegistryRefreshEngine>();

            XRegistryRefreshResult result = await engine.RefreshAsync(new XRegistryRefreshRequest())
                .ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Summary.Outcome, Is.EqualTo(XRegistryRefreshOutcome.Success));
                Assert.That(result.NewGeneration, Is.EqualTo(1u));
                Assert.That(result.Results.Count, Is.EqualTo(1));
                Assert.That(result.Results[0].Xid, Is.EqualTo(xid));
            }

            await engine.RemoveAllAsync().ConfigureAwait(false);
            await engine.RemoveAllAsync().ConfigureAwait(false);
            host.Verify(value => value.AddAsync(document, CancellationToken.None), Times.Once);
            host.Verify(value => value.RemoveAsync(handle, CancellationToken.None), Times.Once);
        }

        [Test]
        public void AddXRegistryServerSuppliesTheDefaultInProcessStore()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddXRegistryServer()
                .BuildServiceProvider();

            var options = provider.GetRequiredService<XRegistryServerOptions>();
            Assert.Multiple(() =>
            {
                Assert.That(provider.GetRequiredService<IXRegistryResourceStore>(),
                    Is.InstanceOf<InMemoryResourceStore>());
                Assert.That(options.ResourceStore, Is.InstanceOf<InMemoryResourceStore>());
                Assert.That(options.ContentIdProvider, Is.Null,
                    "A concrete registry supplies the identity provider.");
            });
        }

        [Test]
        public void AddXRegistryServerAppliesTheConfigureCallback()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddXRegistryServer(options =>
                {
                    options.RequireEncryptionForReads = true;
                    options.RegistryId = "urn:example:registry";
                })
                .BuildServiceProvider();

            var options = provider.GetRequiredService<XRegistryServerOptions>();
            Assert.Multiple(() =>
            {
                Assert.That(options.RequireEncryptionForReads, Is.True);
                Assert.That(options.RegistryId, Is.EqualTo("urn:example:registry"));
            });
        }

        [Test]
        public void AFileSystemStoreRegisteredFirstWins()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddXRegistryFileSystemResourceStore("resources")
                .AddXRegistryServer()
                .BuildServiceProvider();

            Assert.That(provider.GetRequiredService<IXRegistryResourceStore>(),
                Is.InstanceOf<FileSystemResourceStore>(),
                "TryAdd must not overwrite a store the application already supplied.");
        }

        [Test]
        public void TheContentIdProviderFlowsIntoTheOptions()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddXRegistryContentIdProvider<XRegistryServerTestHarness.FakeContentIdProvider>()
                .AddXRegistryServer()
                .BuildServiceProvider();

            var options = provider.GetRequiredService<XRegistryServerOptions>();
            Assert.That(options.ContentIdProvider,
                Is.InstanceOf<XRegistryServerTestHarness.FakeContentIdProvider>());
        }

        [Test]
        public void TheRegistrationsAreSingletons()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddXRegistryServer()
                .BuildServiceProvider();

            var firstOptions = provider.GetRequiredService<XRegistryServerOptions>();
            var secondOptions = provider.GetRequiredService<XRegistryServerOptions>();
            var firstStore = provider.GetRequiredService<IXRegistryResourceStore>();
            var secondStore = provider.GetRequiredService<IXRegistryResourceStore>();

            Assert.Multiple(() =>
            {
                Assert.That(secondOptions, Is.SameAs(firstOptions));
                Assert.That(secondStore, Is.SameAs(firstStore));
            });
        }

        [Test]
        public void NullArgumentsAreRejected()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => ((IServiceCollection)null!).AddXRegistryServer(),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => ((IServiceCollection)null!).AddXRegistryFileSystemResourceStore("x"),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => ((IServiceCollection)null!)
                        .AddXRegistryContentIdProvider<XRegistryServerTestHarness.FakeContentIdProvider>(),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => new ServiceCollection().AddXRegistryFileSystemResourceStore(string.Empty),
                    Throws.TypeOf<ArgumentException>());
            });
        }
    }
}
