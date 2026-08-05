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
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Client.Hosting;

namespace Opc.Ua.WotCon.Tests.Hosting
{
    /// <summary>
    /// Unit tests for the WoT Connectivity 1.1 registry client
    /// dependency-injection surface:
    /// <c>IOpcUaBuilder.AddWotRegistryClient(...)</c>.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class OpcUaWotRegistryClientBuilderExtensionsTests
    {
        [Test]
        public void AddWotRegistryClientThrowsForNullArgs()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(() => ((IOpcUaBuilder)null!)
                .AddWotRegistryClient(configure: null),
                Throws.ArgumentNullException);

            Assert.That(() => builder.AddWotRegistryClient(
                (Microsoft.Extensions.Configuration.IConfiguration)null!),
                Throws.ArgumentNullException);

            Assert.That(() => builder.AddWotRegistryClient(
                (Microsoft.Extensions.Configuration.IConfigurationSection)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task AddWotRegistryClientRegistersClientFactoryAsync()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            builder.AddWotRegistryClient();

            await using ServiceProvider sp = services.BuildServiceProvider();

            Func<CancellationToken, Task<WotRegistryClient>>? factory =
                sp.GetService<Func<CancellationToken, Task<WotRegistryClient>>>();
            Func<ManagedSession, CancellationToken, Task<WotRegistryClient>>? wrapperFactory =
                sp.GetService<Func<ManagedSession, CancellationToken, Task<WotRegistryClient>>>();
            Assert.That(factory, Is.Not.Null);
            Assert.That(wrapperFactory, Is.Not.Null);
        }

        [Test]
        public void AddWotRegistryClientChainsFromClientBuilder()
        {
            IServiceCollection services = new ServiceCollection();

            IOpcUaClientBuilder builder = services.AddOpcUa()
                .AddClient(_ => { })
                .AddWotRegistryClient();

            Assert.That(builder.Services, Is.SameAs(services));
            Assert.That(services.Any(d => d.ServiceType ==
                typeof(Func<CancellationToken, Task<WotRegistryClient>>)), Is.True);
        }

        [Test]
        public async Task LazyConnectFalseDoesNotResolveManagedSessionAsync()
        {
            IServiceCollection services = new ServiceCollection();

            services.AddOpcUa().AddWotRegistryClient(options => options.LazyConnect = false);

            await using ServiceProvider sp = services.BuildServiceProvider();
            Func<CancellationToken, Task<WotRegistryClient>> factory =
                sp.GetRequiredService<Func<CancellationToken, Task<WotRegistryClient>>>();

            InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await factory(CancellationToken.None).ConfigureAwait(false))!;
            Assert.That(ex.Message, Does.Contain(nameof(WotRegistryClientOptions.LazyConnect)));
        }

        [Test]
        public void AddWotRegistryClientReturnsBuilder()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IOpcUaBuilder returned = builder.AddWotRegistryClient(o => o.LazyConnect = false);

            Assert.That(returned, Is.SameAs(builder));
        }

        [Test]
        public async Task AddWotRegistryClientAndAddWotConClientCoexistAsync()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            builder.AddWotConClient();
            builder.AddWotRegistryClient();

            await using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(
                sp.GetService<Func<CancellationToken, Task<WotConnectivityClient>>>(), Is.Not.Null);
            Assert.That(
                sp.GetService<Func<CancellationToken, Task<WotRegistryClient>>>(), Is.Not.Null);
        }

        [Test]
        public void RegistryAccessorDoesNotOwnManagedSession()
        {
            Type accessor = typeof(OpcUaWotConClientBuilderExtensions)
                .GetNestedType("WotRegistryClientAccessor", BindingFlags.NonPublic)!;

            FieldInfo[] managedSessionFields = accessor
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(field => field.FieldType == typeof(ManagedSession))
                .ToArray();

            Assert.That(managedSessionFields, Is.Empty);
        }

        [Test]
        public async Task RegistryFactoryRetriesAfterFaultedLazyConnectAsync()
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
            services.AddOpcUa().AddWotRegistryClient();

            await using ServiceProvider sp = services.BuildServiceProvider();
            Func<CancellationToken, Task<WotRegistryClient>> factory =
                sp.GetRequiredService<Func<CancellationToken, Task<WotRegistryClient>>>();

            InvalidOperationException first = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await factory(CancellationToken.None).ConfigureAwait(false))!;
            InvalidOperationException second = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await factory(CancellationToken.None).ConfigureAwait(false))!;

            Assert.That(first.Message, Is.EqualTo("attempt 1"));
            Assert.That(second.Message, Is.EqualTo("attempt 2"));
            Assert.That(calls, Is.EqualTo(2));
        }

        [Test]
        public async Task RegistryFactorySharesInFlightLazyConnectAsync()
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
            services.AddOpcUa().AddWotRegistryClient();

            await using ServiceProvider sp = services.BuildServiceProvider();
            Func<CancellationToken, Task<WotRegistryClient>> factory =
                sp.GetRequiredService<Func<CancellationToken, Task<WotRegistryClient>>>();

            Task<WotRegistryClient> first = factory(CancellationToken.None);
            Task<WotRegistryClient> second = factory(CancellationToken.None);
            pending.SetException(new InvalidOperationException("shared"));

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(first, Is.SameAs(second));
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await first.ConfigureAwait(false));
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await second.ConfigureAwait(false));
        }

        [Test]
        public async Task AddWotRegistryClientBindsConfigurationAsync()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IOpcUaBuilder returned = builder.AddWotRegistryClient(CreateConfiguration());

            await using ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(returned, Is.SameAs(builder));
            Assert.That(ResolveOptions(sp).LazyConnect, Is.False);
        }

        [Test]
        public async Task AddWotRegistryClientBindsConfigurationSectionAsync()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IOpcUaBuilder returned = builder.AddWotRegistryClient(
                CreateConfiguration().GetSection(
                    OpcUaWotConClientBuilderExtensions.DefaultRegistryConfigurationSection));

            await using ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(returned, Is.SameAs(builder));
            Assert.That(ResolveOptions(sp).LazyConnect, Is.False);
        }

        [Test]
        public void AddWotRegistryClientOnBuilderThrowsForANullBuilderWithASection()
        {
            IConfigurationSection section = CreateConfiguration().GetSection(
                OpcUaWotConClientBuilderExtensions.DefaultRegistryConfigurationSection);

            Assert.That(
                () => ((IOpcUaBuilder)null!).AddWotRegistryClient(section),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
        }

        [Test]
        public void AddWotRegistryClientOnClientBuilderThrowsForNullArgs()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(_ => { });
            IConfiguration configuration = CreateConfiguration();
            IConfigurationSection section = configuration.GetSection(
                OpcUaWotConClientBuilderExtensions.DefaultRegistryConfigurationSection);

            Assert.That(
                () => ((IOpcUaClientBuilder)null!).AddWotRegistryClient(configure: null),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
            Assert.That(
                () => ((IOpcUaClientBuilder)null!).AddWotRegistryClient(configuration),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
            Assert.That(
                () => ((IOpcUaClientBuilder)null!).AddWotRegistryClient(section),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
            Assert.That(
                () => builder.AddWotRegistryClient((IConfiguration)null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configuration"));
            Assert.That(
                () => builder.AddWotRegistryClient((IConfigurationSection)null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("section"));
        }

        [Test]
        public async Task AddWotRegistryClientOnClientBuilderBindsConfigurationAsync()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(_ => { });

            IOpcUaClientBuilder returned = builder.AddWotRegistryClient(CreateConfiguration());

            await using ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(returned, Is.SameAs(builder));
            Assert.That(ResolveOptions(sp).LazyConnect, Is.False);
        }

        [Test]
        public async Task AddWotRegistryClientOnClientBuilderBindsConfigurationSectionAsync()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(_ => { });

            IOpcUaClientBuilder returned = builder.AddWotRegistryClient(
                CreateConfiguration().GetSection(
                    OpcUaWotConClientBuilderExtensions.DefaultRegistryConfigurationSection));

            await using ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(returned, Is.SameAs(builder));
            Assert.That(ResolveOptions(sp).LazyConnect, Is.False);
        }

        [Test]
        public async Task RegistryAccessorRejectsConnectAfterDisposeAsync()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa().AddWotRegistryClient();

            await using ServiceProvider sp = services.BuildServiceProvider();
            Func<CancellationToken, Task<WotRegistryClient>> factory =
                sp.GetRequiredService<Func<CancellationToken, Task<WotRegistryClient>>>();

            Assert.That(factory.Target, Is.InstanceOf<IAsyncDisposable>());
            var accessor = (IAsyncDisposable)factory.Target!;
            await accessor.DisposeAsync().ConfigureAwait(false);
            await accessor.DisposeAsync().ConfigureAwait(false);

            Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await factory(CancellationToken.None).ConfigureAwait(false));
        }

        [Test]
        public async Task RegistryFactoryFailsWhenTheSessionFactoryYieldsNoSessionAsync()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(
                _ => _ => Task.FromResult<ManagedSession>(null!));
            services.AddOpcUa().AddWotRegistryClient();

            await using ServiceProvider sp = services.BuildServiceProvider();
            Func<CancellationToken, Task<WotRegistryClient>> factory =
                sp.GetRequiredService<Func<CancellationToken, Task<WotRegistryClient>>>();

            ArgumentNullException ex = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await factory(CancellationToken.None).ConfigureAwait(false))!;

            Assert.That(ex.ParamName, Is.EqualTo("session"));
        }

        private static IConfiguration CreateConfiguration()
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(s_configuration)
                .Build();
        }

        private static WotRegistryClientOptions ResolveOptions(IServiceProvider sp)
        {
            return sp.GetRequiredService<IOptions<WotRegistryClientOptions>>().Value;
        }

        private static readonly KeyValuePair<string, string?>[] s_configuration =
        [
            new(
                OpcUaWotConClientBuilderExtensions.DefaultRegistryConfigurationSection +
                    ":" + nameof(WotRegistryClientOptions.LazyConnect),
                "false")
        ];
    }
}
