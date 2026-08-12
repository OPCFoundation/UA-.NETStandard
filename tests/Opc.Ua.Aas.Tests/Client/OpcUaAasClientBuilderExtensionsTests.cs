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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Aas.Client;
using Opc.Ua.Aas.Client.Hosting;
using Opc.Ua.Aas.Client.Registry;
using Opc.Ua.Client;

namespace Opc.Ua.Aas.Tests.Client
{
    /// <summary>
    /// Exercises the AAS client registrations, including the lazily connecting accessors that the
    /// registered factory delegates are bound to.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public sealed class OpcUaAasClientBuilderExtensionsTests
    {
        /// <summary>
        /// The client-builder overloads must reach the same registrations as the plain builder ones
        /// and must keep returning the client builder so the fluent chain continues.
        /// </summary>
        [Test]
        public async Task AddAasClientOnAClientBuilderRegistersTheSameServicesAsync()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(_ => { });

            IOpcUaClientBuilder returned = builder.AddAasV3Client(
                options => options.InstanceNamespaceUri = "urn:instances");

            await using ServiceProvider sp = services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(returned, Is.SameAs(builder));
                Assert.That(ResolveOptions(sp).InstanceNamespaceUri, Is.EqualTo("urn:instances"));
                Assert.That(sp.GetService<Func<CancellationToken, Task<AasClient>>>(), Is.Not.Null);
                Assert.That(sp.GetService<Func<ManagedSession, CancellationToken, Task<AasClient>>>(),
                    Is.Not.Null);
            });
        }

        /// <summary>
        /// The configuration overload has to read the documented default section, otherwise a
        /// deployment's appsettings entry would be silently ignored.
        /// </summary>
        [Test]
        public async Task AddAasClientOnAClientBuilderBindsTheDefaultSectionAsync()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(_ => { });

            IOpcUaClientBuilder returned = builder.AddAasV3Client(CreateConfiguration());

            await using ServiceProvider sp = services.BuildServiceProvider();
            AasClientOptions options = ResolveOptions(sp);
            Assert.Multiple(() =>
            {
                Assert.That(returned, Is.SameAs(builder));
                Assert.That(options.LazyConnect, Is.False);
                Assert.That(options.InstanceNamespaceUri, Is.EqualTo("urn:configured"));
            });
        }

        /// <summary>
        /// A deployment may nest the options anywhere, so the explicit section overload must bind
        /// from the section it is given rather than the default path.
        /// </summary>
        [Test]
        public async Task AddAasClientOnAClientBuilderBindsAnExplicitSectionAsync()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(_ => { });

            IOpcUaClientBuilder returned = builder.AddAasV3Client(
                CreateConfiguration().GetSection(
                    OpcUaAasClientBuilderExtensions.DefaultConfigurationSection));

            await using ServiceProvider sp = services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(returned, Is.SameAs(builder));
                Assert.That(ResolveOptions(sp).LazyConnect, Is.False);
            });
        }

        /// <summary>
        /// The registry half repeats the metamodel half's registration shape, so it needs the same
        /// guarantee that the client-builder overload reaches it.
        /// </summary>
        [Test]
        public async Task AddAasRegistryClientOnAClientBuilderRegistersTheSameServicesAsync()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(_ => { });

            IOpcUaClientBuilder returned = builder.AddAasV3RegistryClient(
                options => options.LazyConnect = false);

            await using ServiceProvider sp = services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(returned, Is.SameAs(builder));
                Assert.That(ResolveOptions(sp).LazyConnect, Is.False);
                Assert.That(sp.GetService<Func<CancellationToken, Task<AasRegistryClient>>>(),
                    Is.Not.Null);
                Assert.That(
                    sp.GetService<Func<ManagedSession, CancellationToken, Task<AasRegistryClient>>>(),
                    Is.Not.Null);
            });
        }

        /// <summary>
        /// The registry configuration overload shares the default section with the metamodel one.
        /// </summary>
        [Test]
        public async Task AddAasRegistryClientOnAClientBuilderBindsTheDefaultSectionAsync()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(_ => { });

            IOpcUaClientBuilder returned = builder.AddAasV3RegistryClient(CreateConfiguration());

            await using ServiceProvider sp = services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(returned, Is.SameAs(builder));
                Assert.That(ResolveOptions(sp).LazyConnect, Is.False);
            });
        }

        /// <summary>
        /// The registry explicit-section overload binds from the section it is given.
        /// </summary>
        [Test]
        public async Task AddAasRegistryClientOnAClientBuilderBindsAnExplicitSectionAsync()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(_ => { });

            IOpcUaClientBuilder returned = builder.AddAasV3RegistryClient(
                CreateConfiguration().GetSection(
                    OpcUaAasClientBuilderExtensions.DefaultConfigurationSection));

            await using ServiceProvider sp = services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(returned, Is.SameAs(builder));
                Assert.That(ResolveOptions(sp).InstanceNamespaceUri, Is.EqualTo("urn:configured"));
            });
        }

        /// <summary>
        /// The registry registration also has to bind through the plain builder's section overload,
        /// which is the path the configuration overload delegates to.
        /// </summary>
        [Test]
        public async Task AddAasRegistryClientOnABuilderBindsConfigurationAsync()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IOpcUaBuilder returned = builder.AddAasV3RegistryClient(CreateConfiguration());

            await using ServiceProvider sp = services.BuildServiceProvider();
            AasClientOptions options = ResolveOptions(sp);
            Assert.Multiple(() =>
            {
                Assert.That(returned, Is.SameAs(builder));
                Assert.That(options.LazyConnect, Is.False);
                Assert.That(options.InstanceNamespaceUri, Is.EqualTo("urn:configured"));
            });
        }

        /// <summary>
        /// The action overload leaves the documented defaults in place when no configuration is
        /// supplied at all.
        /// </summary>
        [Test]
        public async Task AddAasRegistryClientOnABuilderKeepsTheDefaultsWithoutConfigurationAsync()
        {
            IServiceCollection services = new ServiceCollection();

            services.AddOpcUa().AddAasV3RegistryClient();

            await using ServiceProvider sp = services.BuildServiceProvider();
            AasClientOptions options = ResolveOptions(sp);
            Assert.Multiple(() =>
            {
                Assert.That(options.LazyConnect, Is.True);
                Assert.That(options.InstanceNamespaceUri, Is.EqualTo(Opc.Ua.Aas.V3.Namespaces.AasV3));
            });
        }

        /// <summary>
        /// Every overload is part of the public surface, so a missing builder, configuration or
        /// section has to be named rather than surfacing as a NullReferenceException.
        /// </summary>
        [Test]
        public void EveryOverloadRejectsItsMissingArgument()
        {
            IConfiguration configuration = CreateConfiguration();
            IConfigurationSection section = configuration.GetSection(
                OpcUaAasClientBuilderExtensions.DefaultConfigurationSection);
            IOpcUaBuilder builder = new ServiceCollection().AddOpcUa();
            IServiceCollection clientServices = new ServiceCollection();
            IOpcUaClientBuilder clientBuilder = clientServices.AddOpcUa().AddClient(_ => { });

            Assert.Multiple(() =>
            {
                Assert.That(() => ((IOpcUaBuilder)null!).AddAasV3Client(),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
                Assert.That(() => ((IOpcUaBuilder)null!).AddAasV3Client(section),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
                Assert.That(() => builder.AddAasV3Client((IConfiguration)null!),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configuration"));
                Assert.That(() => builder.AddAasV3Client((IConfigurationSection)null!),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("section"));
                Assert.That(() => ((IOpcUaClientBuilder)null!).AddAasV3Client(),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
                Assert.That(() => ((IOpcUaClientBuilder)null!).AddAasV3Client(configuration),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
                Assert.That(() => ((IOpcUaClientBuilder)null!).AddAasV3Client(section),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
                Assert.That(() => clientBuilder.AddAasV3Client((IConfiguration)null!),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configuration"));
                Assert.That(() => clientBuilder.AddAasV3Client((IConfigurationSection)null!),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("section"));
                Assert.That(() => ((IOpcUaBuilder)null!).AddAasV3RegistryClient(),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
                Assert.That(() => ((IOpcUaBuilder)null!).AddAasV3RegistryClient(section),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
                Assert.That(() => builder.AddAasV3RegistryClient((IConfiguration)null!),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configuration"));
                Assert.That(() => builder.AddAasV3RegistryClient((IConfigurationSection)null!),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("section"));
                Assert.That(() => ((IOpcUaClientBuilder)null!).AddAasV3RegistryClient(),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
                Assert.That(() => ((IOpcUaClientBuilder)null!).AddAasV3RegistryClient(configuration),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
                Assert.That(() => ((IOpcUaClientBuilder)null!).AddAasV3RegistryClient(section),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
                Assert.That(() => clientBuilder.AddAasV3RegistryClient((IConfiguration)null!),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configuration"));
                Assert.That(() => clientBuilder.AddAasV3RegistryClient((IConfigurationSection)null!),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("section"));
            });
        }

        /// <summary>
        /// With eager connect the caller owns the session, so the lazy factory must refuse rather
        /// than silently open one behind the caller's back.
        /// </summary>
        [Test]
        public async Task ClientFactoryRefusesToConnectWhenLazyConnectIsDisabledAsync()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(
                _ => _ => Task.FromResult<ManagedSession>(null!));
            services.AddOpcUa().AddAasV3Client(options => options.LazyConnect = false);

            await using ServiceProvider sp = services.BuildServiceProvider();
            Func<CancellationToken, Task<AasClient>> factory =
                sp.GetRequiredService<Func<CancellationToken, Task<AasClient>>>();

            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await factory(CancellationToken.None).ConfigureAwait(false))!;

            Assert.That(error.Message, Does.Contain("LazyConnect is false"));
        }

        /// <summary>
        /// Without AddClient there is no session factory at all, and the diagnostic has to say so
        /// instead of failing with a missing-service message about a delegate type.
        /// </summary>
        [Test]
        public async Task ClientFactoryReportsTheMissingSessionFactoryAsync()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa().AddAasV3Client();

            await using ServiceProvider sp = services.BuildServiceProvider();
            Func<CancellationToken, Task<AasClient>> factory =
                sp.GetRequiredService<Func<CancellationToken, Task<AasClient>>>();

            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await factory(CancellationToken.None).ConfigureAwait(false))!;

            Assert.That(error.Message, Does.Contain("AddAasV3Client requires AddClient"));
        }

        /// <summary>
        /// A session factory that yields nothing must be reported against the session argument, not
        /// swallowed into a client wrapping a null session.
        /// </summary>
        [Test]
        public async Task ClientFactoryFailsWhenTheSessionFactoryYieldsNoSessionAsync()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(
                _ => _ => Task.FromResult<ManagedSession>(null!));
            services.AddOpcUa().AddAasV3Client();

            await using ServiceProvider sp = services.BuildServiceProvider();
            Func<CancellationToken, Task<AasClient>> factory =
                sp.GetRequiredService<Func<CancellationToken, Task<AasClient>>>();

            ArgumentNullException error = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await factory(CancellationToken.None).ConfigureAwait(false))!;

            Assert.That(error.ParamName, Is.EqualTo("session"));
        }

        /// <summary>
        /// A failed connect must not be cached, otherwise a transient network fault would poison
        /// the accessor for the lifetime of the container.
        /// </summary>
        [Test]
        public async Task ClientFactoryRetriesAfterAFaultedConnectAsync()
        {
            IServiceCollection services = new ServiceCollection();
            int calls = 0;
            services.AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(
                _ => _ =>
                {
                    calls++;
                    return Task.FromException<ManagedSession>(
                        new InvalidOperationException("attempt " + calls));
                });
            services.AddOpcUa().AddAasV3Client();

            await using ServiceProvider sp = services.BuildServiceProvider();
            Func<CancellationToken, Task<AasClient>> factory =
                sp.GetRequiredService<Func<CancellationToken, Task<AasClient>>>();

            InvalidOperationException first = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await factory(CancellationToken.None).ConfigureAwait(false))!;
            InvalidOperationException second = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await factory(CancellationToken.None).ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(first.Message, Is.EqualTo("attempt 1"));
                Assert.That(second.Message, Is.EqualTo("attempt 2"));
                Assert.That(calls, Is.EqualTo(2));
            });
        }

        /// <summary>
        /// Concurrent callers must share one in-flight connect; a second one would open a second
        /// session against the same server.
        /// </summary>
        [Test]
        public async Task ClientFactorySharesAnInFlightConnectAsync()
        {
            IServiceCollection services = new ServiceCollection();
            int calls = 0;
            var pending = new TaskCompletionSource<ManagedSession>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            services.AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(
                _ => _ =>
                {
                    calls++;
                    return pending.Task;
                });
            services.AddOpcUa().AddAasV3Client();

            await using ServiceProvider sp = services.BuildServiceProvider();
            Func<CancellationToken, Task<AasClient>> factory =
                sp.GetRequiredService<Func<CancellationToken, Task<AasClient>>>();

            Task<AasClient> first = factory(CancellationToken.None);
            Task<AasClient> second = factory(CancellationToken.None);
            pending.SetException(new InvalidOperationException("shared"));

            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(first, Is.SameAs(second));
                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await first.ConfigureAwait(false));
                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await second.ConfigureAwait(false));
            });
        }

        /// <summary>
        /// The registry accessor enforces the same eager-connect contract as the metamodel one, and
        /// names its own registration in the diagnostic.
        /// </summary>
        [Test]
        public async Task RegistryFactoryRefusesToConnectWhenLazyConnectIsDisabledAsync()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(
                _ => _ => Task.FromResult<ManagedSession>(null!));
            services.AddOpcUa().AddAasV3RegistryClient(options => options.LazyConnect = false);

            await using ServiceProvider sp = services.BuildServiceProvider();
            Func<CancellationToken, Task<AasRegistryClient>> factory =
                sp.GetRequiredService<Func<CancellationToken, Task<AasRegistryClient>>>();

            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await factory(CancellationToken.None).ConfigureAwait(false))!;

            Assert.That(error.Message, Does.Contain("Task<AasRegistryClient>"));
        }

        /// <summary>
        /// The registry diagnostic has to name AddAasV3RegistryClient so a caller knows which
        /// registration is missing its session factory.
        /// </summary>
        [Test]
        public async Task RegistryFactoryReportsTheMissingSessionFactoryAsync()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa().AddAasV3RegistryClient();

            await using ServiceProvider sp = services.BuildServiceProvider();
            Func<CancellationToken, Task<AasRegistryClient>> factory =
                sp.GetRequiredService<Func<CancellationToken, Task<AasRegistryClient>>>();

            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await factory(CancellationToken.None).ConfigureAwait(false))!;

            Assert.That(error.Message, Does.Contain("AddAasV3RegistryClient requires AddClient"));
        }

        /// <summary>
        /// A registry client over a null session is meaningless, so resolution must fail on the
        /// session argument rather than browse against nothing.
        /// </summary>
        [Test]
        public async Task RegistryFactoryFailsWhenTheSessionFactoryYieldsNoSessionAsync()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(
                _ => _ => Task.FromResult<ManagedSession>(null!));
            services.AddOpcUa().AddAasV3RegistryClient();

            await using ServiceProvider sp = services.BuildServiceProvider();
            Func<CancellationToken, Task<AasRegistryClient>> factory =
                sp.GetRequiredService<Func<CancellationToken, Task<AasRegistryClient>>>();

            ArgumentNullException error = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await factory(CancellationToken.None).ConfigureAwait(false))!;

            Assert.That(error.ParamName, Is.EqualTo("session"));
        }

        /// <summary>
        /// The registry accessor must not cache a failure either.
        /// </summary>
        [Test]
        public async Task RegistryFactoryRetriesAfterAFaultedConnectAsync()
        {
            IServiceCollection services = new ServiceCollection();
            int calls = 0;
            services.AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(
                _ => _ =>
                {
                    calls++;
                    return Task.FromException<ManagedSession>(
                        new InvalidOperationException("attempt " + calls));
                });
            services.AddOpcUa().AddAasV3RegistryClient();

            await using ServiceProvider sp = services.BuildServiceProvider();
            Func<CancellationToken, Task<AasRegistryClient>> factory =
                sp.GetRequiredService<Func<CancellationToken, Task<AasRegistryClient>>>();

            InvalidOperationException first = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await factory(CancellationToken.None).ConfigureAwait(false))!;
            InvalidOperationException second = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await factory(CancellationToken.None).ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(first.Message, Is.EqualTo("attempt 1"));
                Assert.That(second.Message, Is.EqualTo("attempt 2"));
                Assert.That(calls, Is.EqualTo(2));
            });
        }

        /// <summary>
        /// The registry accessor shares one in-flight connect across concurrent callers.
        /// </summary>
        [Test]
        public async Task RegistryFactorySharesAnInFlightConnectAsync()
        {
            IServiceCollection services = new ServiceCollection();
            int calls = 0;
            var pending = new TaskCompletionSource<ManagedSession>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            services.AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(
                _ => _ =>
                {
                    calls++;
                    return pending.Task;
                });
            services.AddOpcUa().AddAasV3RegistryClient();

            await using ServiceProvider sp = services.BuildServiceProvider();
            Func<CancellationToken, Task<AasRegistryClient>> factory =
                sp.GetRequiredService<Func<CancellationToken, Task<AasRegistryClient>>>();

            Task<AasRegistryClient> first = factory(CancellationToken.None);
            Task<AasRegistryClient> second = factory(CancellationToken.None);
            pending.SetException(new InvalidOperationException("shared"));

            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(first, Is.SameAs(second));
                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await first.ConfigureAwait(false));
                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await second.ConfigureAwait(false));
            });
        }

        /// <summary>
        /// The session-scoped factory is the documented eager-connect path, and it too has to refuse
        /// a null session instead of producing an unusable client.
        /// </summary>
        [Test]
        public async Task SessionScopedFactoriesRejectAMissingSessionAsync()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa().AddAasV3Client().AddAasV3RegistryClient();

            await using ServiceProvider sp = services.BuildServiceProvider();
            var clientFactory =
                sp.GetRequiredService<Func<ManagedSession, CancellationToken, Task<AasClient>>>();
            var registryFactory =
                sp.GetRequiredService<Func<ManagedSession, CancellationToken, Task<AasRegistryClient>>>();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => clientFactory(null!, CancellationToken.None),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("session"));
                Assert.That(
                    () => registryFactory(null!, CancellationToken.None),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("session"));
            });
        }

        /// <summary>
        /// A cancelled token must be observed before any session work starts.
        /// </summary>
        [Test]
        public async Task SessionScopedClientFactoryObservesCancellationAsync()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa().AddAasV3Client();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await using ServiceProvider sp = services.BuildServiceProvider();
            var clientFactory =
                sp.GetRequiredService<Func<ManagedSession, CancellationToken, Task<AasClient>>>();

            Assert.That(
                () => clientFactory(null!, cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());
        }

        private static IConfiguration CreateConfiguration()
        {
            return new ConfigurationBuilder().AddInMemoryCollection(s_configuration).Build();
        }

        private static AasClientOptions ResolveOptions(IServiceProvider sp)
        {
            return sp.GetRequiredService<IOptions<AasClientOptions>>().Value;
        }

        private static readonly KeyValuePair<string, string?>[] s_configuration =
        [
            new(
                OpcUaAasClientBuilderExtensions.DefaultConfigurationSection + ":" +
                    nameof(AasClientOptions.LazyConnect),
                "false"),
            new(
                OpcUaAasClientBuilderExtensions.DefaultConfigurationSection + ":" +
                    nameof(AasClientOptions.InstanceNamespaceUri),
                "urn:configured")
        ];
    }
}
