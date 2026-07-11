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
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Transcoding;
using Opc.Ua.Tests;
using static Opc.Ua.PubSub.Tests.Transcoding.TranscodingTestUtilities;

namespace Opc.Ua.PubSub.Tests.Transcoding
{
    /// <summary>
    /// Tests for the declarative, reloadable transcoding configuration: the
    /// route-to-descriptor factory, the change signature, the configuration
    /// binding, and the reload coordinator's incremental reconfiguration.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    public class TranscodeConfigurationTests
    {
        private static TranscodeRouteOptions FullRoute()
        {
            return new TranscodeRouteOptions
            {
                Name = "r1",
                Source = "src",
                Target = "tgt",
                TargetEncoding = TranscodeEncoding.Json,
                Topic = "topic/x",
                FieldEncoding = PubSubFieldEncoding.Variant,
                JsonSingleMessage = true,
                PreserveMetaDataVersion = false,
                AllowInsecureCrossEncoding = true,
                DropKeepAlive = true,
                RenameFields = new Dictionary<string, string> { ["a"] = "b" },
                SelectFields = ["b"],
                PromoteFields = ["b"],
                PromotedFieldPrefix = "p_",
                KeepMessageTypes =
                [
                    PubSubDataSetMessageType.KeyFrame
                ],
                RemapIds = new TranscodeIdRemapOptions
                {
                    PublisherIdNumber = 7,
                    WriterGroupId = 3,
                    DataSetClassId = "0f8fad5b-d9cb-469f-a165-70867728950e",
                    DataSetWriterIds = new Dictionary<ushort, ushort> { [1] = 2 }
                }
            };
        }

        [Test]
        public void Factory_Create_BuildsDescriptor()
        {
            TranscodingBridgeDescriptor descriptor =
                TranscodeRouteOptionsFactory.Create(FullRoute());

            Assert.Multiple(() =>
            {
                Assert.That(descriptor.SourceConnectionName, Is.EqualTo("src"));
                Assert.That(descriptor.TargetConnectionName, Is.EqualTo("tgt"));
                Assert.That(descriptor.AllowInsecureCrossEncoding, Is.True);
                Assert.That(descriptor.Spec.TargetEncoding, Is.EqualTo(TranscodeEncoding.Json));
                Assert.That(descriptor.Spec.TargetOptions.JsonSingleMessageMode, Is.True);
                Assert.That(descriptor.Spec.TargetOptions.PreserveMetaDataVersion, Is.False);
                Assert.That(descriptor.Spec.Promotion, Is.Not.Null);
                Assert.That(descriptor.Spec.Promotion!.FieldNames.Count, Is.EqualTo(1));
                Assert.That(descriptor.Spec.Promotion!.FieldNames[0], Is.EqualTo("b"));
                Assert.That(descriptor.Spec.Promotion!.PropertyKeyPrefix, Is.EqualTo("p_"));
                Assert.That(descriptor.Spec.Transforms.Count, Is.GreaterThanOrEqualTo(4));
                Assert.That(descriptor.TopicSelector, Is.Not.Null);
                Assert.That(
                    descriptor.TopicSelector!(new ReceivedNetworkMessage()),
                    Is.EqualTo("topic/x"));
            });
        }

        [Test]
        public void Factory_Create_MissingFields_Throw()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => TranscodeRouteOptionsFactory.Create(
                        new TranscodeRouteOptions { Source = "s", Target = "t" }),
                    Throws.InvalidOperationException);
                Assert.That(
                    () => TranscodeRouteOptionsFactory.Create(
                        new TranscodeRouteOptions { Name = "n", Target = "t" }),
                    Throws.InvalidOperationException);
                Assert.That(
                    () => TranscodeRouteOptionsFactory.Create(
                        new TranscodeRouteOptions { Name = "n", Source = "s" }),
                    Throws.InvalidOperationException);
            });
        }

        [Test]
        public void Factory_ComputeSignature_DetectsChanges()
        {
            TranscodeRouteOptions a = FullRoute();
            TranscodeRouteOptions b = FullRoute();
            string signatureA = TranscodeRouteOptionsFactory.ComputeSignature(a);
            string signatureB = TranscodeRouteOptionsFactory.ComputeSignature(b);
            b.TargetEncoding = TranscodeEncoding.Uadp;
            string signatureChanged = TranscodeRouteOptionsFactory.ComputeSignature(b);

            Assert.Multiple(() =>
            {
                Assert.That(signatureA, Is.EqualTo(signatureB));
                Assert.That(signatureChanged, Is.Not.EqualTo(signatureA));
            });
        }

        [Test]
        public void AddTranscoding_BindsRoutesFromConfiguration()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Routes:0:Name"] = "r1",
                    ["Routes:0:Source"] = "sub",
                    ["Routes:0:Target"] = "pub",
                    ["Routes:0:TargetEncoding"] = "Json",
                    ["Routes:0:Topic"] = "t/1",
                    ["Routes:0:PromoteFields:0"] = "Temperature",
                    ["Routes:0:PromotedFieldPrefix"] = "opc_",
                    ["Routes:0:RenameFields:a"] = "b",
                    ["Routes:0:RemapIds:WriterGroupId"] = "5"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddLogging();
            services.AddOpcUa().AddPubSub(pubsub => pubsub.AddTranscoding(configuration));
            using ServiceProvider provider = services.BuildServiceProvider();

            PubSubTranscodingOptions options = provider
                .GetRequiredService<IOptionsMonitor<PubSubTranscodingOptions>>()
                .CurrentValue;

            Assert.That(options.Routes, Has.Count.EqualTo(1));
            TranscodeRouteOptions route = options.Routes[0];
            Assert.Multiple(() =>
            {
                Assert.That(route.Name, Is.EqualTo("r1"));
                Assert.That(route.Source, Is.EqualTo("sub"));
                Assert.That(route.Target, Is.EqualTo("pub"));
                Assert.That(route.TargetEncoding, Is.EqualTo(TranscodeEncoding.Json));
                Assert.That(route.Topic, Is.EqualTo("t/1"));
                Assert.That(route.PromoteFields, Has.Count.EqualTo(1));
                Assert.That(route.PromoteFields![0], Is.EqualTo("Temperature"));
                Assert.That(route.PromotedFieldPrefix, Is.EqualTo("opc_"));
                Assert.That(route.RenameFields!["a"], Is.EqualTo("b"));
                Assert.That(route.RemapIds!.WriterGroupId, Is.EqualTo((ushort)5));
            });
        }

        [Test]
        public void AddTranscoding_NullArguments_Throw()
        {
            IConfiguration configuration = new ConfigurationBuilder().Build();
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddLogging();
            IPubSubBuilder? captured = null;
            services.AddOpcUa().AddPubSub(pubsub => captured = pubsub);

            Assert.Multiple(() =>
            {
                Assert.That(() => captured!.AddTranscoding(null!), Throws.ArgumentNullException);
                Assert.That(
                    () => PubSubTranscodingBuilderExtensions.AddTranscoding(null!, configuration),
                    Throws.ArgumentNullException);
            });
        }

        [Test]
        public async Task ReloadCoordinator_ReconfiguresOnlyChangedRoutes()
        {
            await using PubSubConnection source = NewConnection("src");
            await using PubSubConnection target = NewConnection("tgt");
            IServiceProvider provider = BuildApplicationProvider(source, target);
            var monitor = new TestOptionsMonitor<PubSubTranscodingOptions>(
                Options(
                    Route("a", TranscodeEncoding.Uadp),
                    Route("b", TranscodeEncoding.Uadp)));
            await using var coordinator = new PubSubTranscodingReloadCoordinator(
                provider, monitor, NUnitTelemetryContext.Create());

            await coordinator.StartAsync().ConfigureAwait(false);
            Assert.That(coordinator.ActiveRouteCount, Is.EqualTo(2));
            PubSubTranscodingBridge? bridgeA = coordinator.GetActiveBridge("a");
            PubSubTranscodingBridge? bridgeB = coordinator.GetActiveBridge("b");

            monitor.Set(Options(
                Route("a", TranscodeEncoding.Uadp),
                Route("b", TranscodeEncoding.Json)));
            await coordinator.ReloadNowAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(coordinator.GetActiveBridge("a"), Is.SameAs(bridgeA));
                Assert.That(coordinator.GetActiveBridge("b"), Is.Not.SameAs(bridgeB));
                Assert.That(coordinator.ActiveRouteCount, Is.EqualTo(2));
            });
        }

        [Test]
        public async Task ReloadCoordinator_RemovesDeletedRoutes()
        {
            await using PubSubConnection source = NewConnection("src");
            await using PubSubConnection target = NewConnection("tgt");
            IServiceProvider provider = BuildApplicationProvider(source, target);
            var monitor = new TestOptionsMonitor<PubSubTranscodingOptions>(
                Options(
                    Route("a", TranscodeEncoding.Uadp),
                    Route("b", TranscodeEncoding.Uadp)));
            await using var coordinator = new PubSubTranscodingReloadCoordinator(
                provider, monitor, NUnitTelemetryContext.Create());
            await coordinator.StartAsync().ConfigureAwait(false);

            monitor.Set(Options(Route("a", TranscodeEncoding.Uadp)));
            await coordinator.ReloadNowAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(coordinator.ActiveRouteCount, Is.EqualTo(1));
                Assert.That(coordinator.GetActiveBridge("a"), Is.Not.Null);
                Assert.That(coordinator.GetActiveBridge("b"), Is.Null);
            });
        }

        [Test]
        public async Task ReloadCoordinator_OnChange_AddsNewRoute()
        {
            await using PubSubConnection source = NewConnection("src");
            await using PubSubConnection target = NewConnection("tgt");
            IServiceProvider provider = BuildApplicationProvider(source, target);
            var monitor = new TestOptionsMonitor<PubSubTranscodingOptions>(
                Options(Route("a", TranscodeEncoding.Uadp)));
            await using var coordinator = new PubSubTranscodingReloadCoordinator(
                provider, monitor, NUnitTelemetryContext.Create());
            await coordinator.StartAsync().ConfigureAwait(false);

            monitor.Set(Options(
                Route("a", TranscodeEncoding.Uadp),
                Route("b", TranscodeEncoding.Uadp)));

            await WaitForAsync(() => coordinator.ActiveRouteCount == 2, TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            Assert.That(coordinator.ActiveRouteCount, Is.EqualTo(2));
        }

        [Test]
        public void ReloadCoordinator_NullArguments_Throw()
        {
            IServiceProvider provider = BuildApplicationProvider();
            var monitor = new TestOptionsMonitor<PubSubTranscodingOptions>(
                new PubSubTranscodingOptions());
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new PubSubTranscodingReloadCoordinator(null!, monitor, telemetry),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => new PubSubTranscodingReloadCoordinator(provider, null!, telemetry),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => new PubSubTranscodingReloadCoordinator(provider, monitor, null!),
                    Throws.ArgumentNullException);
            });
        }

        private static PubSubTranscodingOptions Options(params TranscodeRouteOptions[] routes)
        {
            return new PubSubTranscodingOptions { Routes = [.. routes] };
        }

        private static TranscodeRouteOptions Route(string name, TranscodeEncoding encoding)
        {
            return new TranscodeRouteOptions
            {
                Name = name,
                Source = "src",
                Target = "tgt",
                TargetEncoding = encoding
            };
        }

        private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }
                await Task.Delay(25).ConfigureAwait(false);
            }
        }

        private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
        {
            private readonly List<Action<T, string?>> m_listeners = [];
            private T m_current;

            public TestOptionsMonitor(T initial)
            {
                m_current = initial;
            }

            public T CurrentValue => m_current;

            public T Get(string? name)
            {
                return m_current;
            }

            public IDisposable OnChange(Action<T, string?> listener)
            {
                m_listeners.Add(listener);
                return new Subscription(m_listeners, listener);
            }

            public void Set(T value)
            {
                m_current = value;
                foreach (Action<T, string?> listener in m_listeners.ToArray())
                {
                    listener(value, null);
                }
            }

            private sealed class Subscription : IDisposable
            {
                private readonly List<Action<T, string?>> m_list;
                private readonly Action<T, string?> m_listener;

                public Subscription(List<Action<T, string?>> list, Action<T, string?> listener)
                {
                    m_list = list;
                    m_listener = listener;
                }

                public void Dispose()
                {
                    m_list.Remove(m_listener);
                }
            }
        }
    }
}
