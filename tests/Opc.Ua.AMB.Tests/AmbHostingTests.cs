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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using Opc.Ua.AMB.Server;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Configuration;
using Opc.Ua.AMB.Server.Hosting;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Covers the hosting surface: the registrations <c>AddAssetManagement</c>
    /// makes, its composition with the managers that own the Device
    /// Integration address space, and the setup delegates.
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class AmbHostingTests
    {
        [Test]
        public void AddAssetManagementRegistersFactoryRegistryAndMemoryStore()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();

            builder.AddAssetManagement();

            using ServiceProvider provider = builder.Services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(
                    builder.Services.Any(descriptor => descriptor.ServiceType == typeof(AmbNodeManagerFactory)),
                    Is.True);
                Assert.That(
                    builder.Services.Any(descriptor => descriptor.ServiceType == typeof(DiAddressSpaceOwnership)),
                    Is.False,
                    "the AMB manager is a sidecar and claims no Device Integration ownership");
                Assert.That(
                    provider.GetRequiredService<IAssetManagement>(),
                    Is.SameAs(provider.GetRequiredService<AssetManagement>()));
                Assert.That(
                    provider.GetRequiredService<IAssetConfigurationStore>(),
                    Is.InstanceOf<MemoryAssetConfigurationStore>());
                Assert.That(
                    provider.GetRequiredService<AssetManagement>().Store,
                    Is.SameAs(provider.GetRequiredService<IAssetConfigurationStore>()));
            });
        }

        [Test]
        public void UseFileSystemStoresRegistersAPersistentStore()
        {
            string directory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(UseFileSystemStoresRegistersAPersistentStore),
                Guid.NewGuid().ToString("N"));
            IOpcUaServerBuilder builder = CreateServerBuilder();

            builder.AddAssetManagement(options => options.UseFileSystemStores(directory));

            using ServiceProvider provider = builder.Services.BuildServiceProvider();
            IAssetConfigurationStore store = provider.GetRequiredService<IAssetConfigurationStore>();
            Assert.Multiple(() =>
            {
                Assert.That(store, Is.InstanceOf<FileSystemAssetConfigurationStore>());
                Assert.That(store.IsPersistent, Is.True);
                Assert.That(Directory.Exists(directory), Is.True);
            });
        }

        [Test]
        public void AnApplicationStoreIsUsed()
        {
            var store = new MemoryAssetConfigurationStore();
            IOpcUaServerBuilder builder = CreateServerBuilder();
            builder.Services.AddSingleton<IAssetConfigurationStore>(store);

            builder.AddAssetManagement(options => options.UseFileSystemStores("ignored"));

            using ServiceProvider provider = builder.Services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<AssetManagement>().Store, Is.SameAs(store));
        }

        [Test]
        public void ASecondAddAssetManagementIsRefused()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();
            builder.AddAssetManagement();

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(() => builder.AddAssetManagement())!;
            Assert.That(exception.Message, Does.Contain("AddAssetManagement"));
        }

        [Test]
        public void InvalidOptionsAreRefused()
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => CreateServerBuilder()
                    .AddAssetManagement(options => options.MaxAssetIdLength = 39));
                Assert.Throws<ArgumentException>(() => CreateServerBuilder()
                    .AddAssetManagement(options => options.InstanceNamespaceUri = Namespaces.AMB));
                Assert.Throws<ArgumentException>(() => CreateServerBuilder()
                    .AddAssetManagement(options => options.InstanceNamespaceUri = "not a uri"));
                Assert.Throws<ArgumentException>(() => CreateServerBuilder()
                    .AddAssetManagement(options => options.TypeNamespaceUri = "not a uri"));
                Assert.Throws<ArgumentException>(() => CreateServerBuilder()
                    .AddAssetManagement(options => options.TypeNamespaceUri = Namespaces.AMB));
                Assert.Throws<ArgumentException>(() => CreateServerBuilder()
                    .AddAssetManagement(options => options.TypeNamespaceUri = options.InstanceNamespaceUri));
                Assert.Throws<ArgumentException>(() => CreateServerBuilder()
                    .AddAssetManagement(options => options.AdditionalNamespaceUris = [string.Empty]));
                Assert.Throws<ArgumentException>(() => new AmbServerOptions().UseFileSystemStores(" "));
            });
        }

        [Test]
        public void NullArgumentsAreRefused()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(
                    () => OpcUaAmbServerBuilderExtensions.AddAssetManagement(null!));
                Assert.Throws<ArgumentNullException>(() => OpcUaAmbServerBuilderExtensions
                    .ConfigureAssetManagement(null!, _ => default));
                Assert.Throws<ArgumentNullException>(() => builder
                    .ConfigureAssetManagement((Func<IAssetManagementSetupContext, ValueTask>)null!));
                Assert.Throws<ArgumentNullException>(() => builder
                    .ConfigureAssetManagement((Action<IAssetManagementSetupContext>)null!));
                Assert.Throws<ArgumentNullException>(() => _ = new AmbNodeManagerFactory(null!));
            });
        }

        [Test]
        public void FactoryPublishesTheInstanceModelAndTypeNamespaces()
        {
            var options = new AmbServerOptions
            {
                InstanceNamespaceUri = "urn:test:amb",
                AdditionalNamespaceUris = ["urn:test:extra", Namespaces.AMB]
            };
            var factory = new AmbNodeManagerFactory(new AssetManagement(options));

            Assert.That(
                factory.NamespacesUris.ToArray(),
                Is.EqualTo(new[]
                {
                    "urn:test:amb",
                    Namespaces.AMB,
                    AmbServerOptions.DefaultTypeNamespaceUri,
                    AmbServerOptions.IrdiNamespaceUri,
                    "urn:test:extra"
                }));
        }

        [Test]
        public void RegisteringWithoutTheNodeManagerFails()
        {
            var assets = new AssetManagement();
            var node = new Mock<INodeBuilder<BaseObjectState>>();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await assets.RegisterAssetAsync(node.Object).ConfigureAwait(false))!;
            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(exception.Message, Does.Contain("AddAssetManagement()"));
                Assert.ThrowsAsync<ArgumentNullException>(async () => await assets.RegisterAssetAsync(null!).ConfigureAwait(false));
            });
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ConfigureAssetManagementRunsInsideTheNodeManagerAsync(bool synchronous)
        {
            IAssetManagementSetupContext? captured = null;
            IAssetManagement? resolved = null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(ConfigureAssetManagementRunsInsideTheNodeManagerAsync) + synchronous,
                builder =>
                {
                    builder.AddAssetManagement();
                    if (synchronous)
                    {
                        builder.ConfigureAssetManagement(context =>
                        {
                            captured = context;
                            resolved = context.GetRequiredService<IAssetManagement>();
                        });
                    }
                    else
                    {
                        builder.ConfigureAssetManagement(context =>
                        {
                            captured = context;
                            resolved = context.GetRequiredService<IAssetManagement>();
                            return default;
                        });
                    }
                }).ConfigureAwait(false);

            Assert.That(captured, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(captured!.Manager, Is.SameAs(server.Manager));
                Assert.That(captured.Assets, Is.SameAs(server.AssetManagement));
                Assert.That(resolved, Is.SameAs(server.AssetManagement));
                Assert.That(captured.Builder, Is.Not.Null);
                Assert.That(captured.GetService<IDisposable>(), Is.Null);
                Assert.That(captured.CancellationToken.IsCancellationRequested, Is.False);
            });
        }

        [Test]
        public async Task AFailingSetupDelegateAbortsTheStartupAsync()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(options =>
            {
                options.ApplicationName = nameof(AFailingSetupDelegateAbortsTheStartupAsync);
                options.ApplicationUri = "urn:localhost:" + nameof(AFailingSetupDelegateAbortsTheStartupAsync);
                options.PkiRoot = Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    nameof(AFailingSetupDelegateAbortsTheStartupAsync),
                    Guid.NewGuid().ToString("N"));
                options.AutoAcceptUntrustedCertificates = true;
                options.EndpointUrls.Clear();
                options.EndpointUrls.Add("opc.tcp://localhost:0/" + nameof(AFailingSetupDelegateAbortsTheStartupAsync));
            });
            bool ran = false;
            builder.AddAssetManagement()
                .ConfigureAssetManagement(_ =>
                {
                    ran = true;
                    throw new InvalidDataException("broken setup");
                });

            await using ServiceProvider provider = services.BuildServiceProvider();
            var hosted = (BackgroundService)provider.GetServices<IHostedService>().Single();

            // The hosted service starts the server in the background, so the
            // failure surfaces on the task that runs it.
            await hosted.StartAsync(CancellationToken.None).ConfigureAwait(false);
            Task execute = hosted.ExecuteTask!;
            Task finished = await Task.WhenAny(execute, Task.Delay(TimeSpan.FromSeconds(60)))
                .ConfigureAwait(false);
            await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);

            // The server reduces the cause to BadInternalError and logs it.
            Assert.Multiple(() =>
            {
                Assert.That(ran, Is.True);
                Assert.That(finished, Is.SameAs(execute), "the startup must fail");
                Assert.That(execute.IsFaulted, Is.True);
                Assert.That(
                    execute.Exception!.InnerException,
                    Is.InstanceOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo((StatusCode)StatusCodes.BadInternalError));
            });
        }

        private static IOpcUaServerBuilder CreateServerBuilder()
        {
            var services = new ServiceCollection();
            return services.AddOpcUa().AddServer(options =>
            {
                options.ApplicationName = nameof(AmbHostingTests);
                options.ApplicationUri = "urn:localhost:OPCFoundation:AmbHostingTests";
            });
        }
    }
}
