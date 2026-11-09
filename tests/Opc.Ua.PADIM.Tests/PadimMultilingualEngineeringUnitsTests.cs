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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.PADIM.Client;
using Opc.Ua.PADIM.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.PADIM.Tests
{
    /// <summary>
    /// Observes engineering-unit translations and their semantic notifications over a real UA connection.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class PadimMultilingualEngineeringUnitsTests
    {
        /// <summary>
        /// Checks locale selection and both nondefault and default translation changes independently.
        /// </summary>
        [TestCase("en-US", false)]
        [TestCase("de-DE", false)]
        [TestCase("mul", false)]
        [TestCase("en-US", true)]
        [TestCase("de-DE", true)]
        [TestCase("mul", true)]
        public async Task HostedEngineeringUnitsHonorsPreferredLocalesAndReportsTranslationChanges(
            string locale, bool description)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            string endpoint = $"opc.tcp://localhost:{port.ToString(CultureInfo.InvariantCulture)}/PadimLocales";
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "padim-locales",
                Guid.NewGuid().ToString("N"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var configured = new TaskCompletionSource<PadimValueBinding>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            provider.Setup(value => value.ReadAsync("measurement", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataValue(Variant.From(21.5f), StatusCodes.Good));
            var services = new ServiceCollection();
            services.AddLogging();
            var started = new ServerStartedSignal();
            services.AddSingleton<IServerStartupTask>(started);
            services.AddOpcUa().AddServer<StandardServer>(options =>
            {
                options.ApplicationName = "PadimLocales";
                options.ApplicationUri = "urn:localhost:PadimLocales";
                options.ProductUri = "urn:OPCFoundation:PadimLocales";
                options.PkiRoot = Path.Combine(root, "server-pki");
                options.AutoAcceptUntrustedCertificates = true;
                options.IncludeUnsecurePolicyNone = true;
                options.EndpointUrls.Clear();
                options.EndpointUrls.Add(endpoint);
            }).AddPadim().ConfigurePadim(async context =>
            {
                try
                {
                    var builder = await context.Manager.CreatePadimDeviceAsync(
                        new QualifiedName("Device", context.Manager.InstanceNamespaceIndex));
                    PadimDevice device = builder.BindPadim();
                    AnalogSignalObjectState signal = await device.AddMeasurementAsync(
                        new QualifiedName("Measurement", context.Manager.InstanceNamespaceIndex),
                        PadimMeasurementKind.Temperature, "PV");
                    PadimValueBinding binding = device.BindValue(signal.AnalogSignal!, "measurement", provider.Object);
                    binding.WithEngineeringUnits(Units("English", "Deutsch"), new Range(high: 100, low: 0));
                    await device.RegisterAsync(context.CancellationToken);
                    await device.RefreshAsync(context.CancellationToken);
                    configured.TrySetResult(binding);
                }
                catch (Exception exception)
                {
                    configured.TrySetException(exception);
                    throw;
                }
            });
            await using ServiceProvider container = services.BuildServiceProvider();
            IHostedService host = container.GetServices<IHostedService>().Single();
            await host.StartAsync(timeout.Token);
            try
            {
                await WaitForServerStartAsync(host, started.Task, configured.Task, timeout.Token);
                using (timeout.Token.Register(() => configured.TrySetCanceled()))
                {
                    PadimValueBinding binding = await configured.Task.ConfigureAwait(false);
                    using var fixture = new ClientFixture(telemetry)
                    {
                        SessionFactory = new ManagedSessionFactory(telemetry)
                    };
                    await fixture.LoadClientConfigurationAsync(Path.Combine(root, "client-pki"));
                    using ISession session = await fixture.ConnectAsync(new Uri(endpoint), SecurityPolicies.None);
                    await session.ChangePreferredLocalesAsync([locale], timeout.Token);
                    var managed = (ManagedSession)session;
                    var client = new PadimClient(session, telemetry);
                    NodeId owner = binding.Variable.NodeId;
                    var analog = (AnalogSignalVariableState)binding.Variable;
                    LocalizedText initial = await ReadTextAsync();
                    (LocalizedText germanWire, bool germanEvent, bool germanStatus) =
                        await ObserveChangeAsync("English", "DeutschChanged");
                    (LocalizedText controlWire, bool controlEvent, bool controlStatus) =
                        await ObserveChangeAsync("EnglishChanged", "DeutschChanged");

                    TestContext.Progress.WriteLine(
                        $"locale={locale}; description={description}; " +
                        $"German event/status={germanEvent}/{germanStatus}; " +
                        $"default control event/status={controlEvent}/{controlStatus}");
                    Assert.Multiple(() =>
                    {
                        AssertWire(initial, "English", "Deutsch");
                        AssertWire(germanWire, "English", "DeutschChanged");
                        AssertWire(controlWire, "EnglishChanged", "DeutschChanged");
                        Assert.That(germanEvent, Is.True, "The German-only update must report the affected owner.");
                        Assert.That(germanStatus, Is.True, "The German-only update must set SemanticsChanged.");
                        Assert.That(controlEvent, Is.True,
                            "The default-translation positive control must report an event.");
                        Assert.That(controlStatus, Is.True,
                            "The default-translation positive control must set the bit.");
                    });

                    async Task<LocalizedText> ReadTextAsync()
                    {
                        (ArrayOf<DataValue> values, ArrayOf<ServiceResult> errors) =
                            await session.ReadValuesAsync([analog.EngineeringUnits!.NodeId], timeout.Token);
                        Assert.That(errors.Count, Is.EqualTo(1));
                        Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
                        Assert.That(values.Count, Is.EqualTo(1));
                        Assert.That(StatusCode.IsGood(values[0].StatusCode), Is.True);
                        EUInformation units = values[0].WrappedValue.TryGetStructure<EUInformation>(out var decoded)
                            && decoded is not null ? decoded
                            : throw new InvalidOperationException("The wire value must decode as EUInformation.");
                        Assert.That(units.NamespaceUri, Is.EqualTo("urn:padim:locale-test"));
                        Assert.That(units.UnitId, Is.EqualTo(1));
                        return description ? units.Description : units.DisplayName;
                    }

                    async Task<(LocalizedText Wire, bool Event, bool Status)> ObserveChangeAsync(
                        string english, string german)
                    {
                        using var phase = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                        var filter = new EventFilter
                        {
                            SelectClauses =
                            [
                                new SimpleAttributeOperand
                                {
                                    TypeDefinitionId = Opc.Ua.ObjectTypeIds.SemanticChangeEventType,
                                    BrowsePath = [new QualifiedName(Opc.Ua.BrowseNames.Changes)],
                                    AttributeId = Attributes.Value
                                }
                            ]
                        };
                        filter.WhereClause.Push(FilterOperator.OfType,
                            Variant.From(Opc.Ua.ObjectTypeIds.SemanticChangeEventType));
                        await using IAsyncEnumerator<DataValueChange> samples =
                            client.ObserveValueAsync(owner, phase.Token).GetAsyncEnumerator(phase.Token);
                        Assert.That(await samples.MoveNextAsync(), Is.True);
                        bool ready = false;
                        await using IAsyncEnumerator<EventNotification> events = managed.DefaultStreaming
                            .SubscribeEventsWhenReadyAsync(new NodeId(2253u), filter, _ =>
                            {
                                binding.WithEngineeringUnits(Units(english, german), new Range(high: 100, low: 0));
                                ready = true;
                                phase.CancelAfter(TimeSpan.FromSeconds(5));
                                return default;
                            }, ct: phase.Token).GetAsyncEnumerator(phase.Token);
                        bool affected = false;
                        Task<bool> nextStatus = ObserveStatusAsync();
                        try
                        {
                            while (await events.MoveNextAsync())
                            {
                                if (events.Current.Fields[0].TryGetStructure(
                                    out ArrayOf<SemanticChangeStructureDataType> changes))
                                {
                                    for (int i = 0; i < changes.Count; i++)
                                    {
                                        affected |= changes[i].Affected == owner;
                                    }
                                }
                                if (affected)
                                {
                                    break;
                                }
                            }
                        }
                        catch (OperationCanceledException) when (
                            ready && phase.IsCancellationRequested && !timeout.IsCancellationRequested)
                        {
                            TestContext.Progress.WriteLine("The phase observation deadline expired after mutation.");
                        }
                        Assert.That(ready, Is.True, "Successful event-item creation must precede mutation.");
                        bool semanticsChanged = await nextStatus;
                        return (await ReadTextAsync(), affected, semanticsChanged);

                        async Task<bool> ObserveStatusAsync()
                        {
                            try
                            {
                                return await samples.MoveNextAsync() &&
                                    samples.Current.Value.StatusCode.SemanticsChanged;
                            }
                            catch (OperationCanceledException) when (
                                ready && phase.IsCancellationRequested && !timeout.IsCancellationRequested)
                            {
                                return false;
                            }
                        }
                    }

                    void AssertWire(LocalizedText text, string english, string german)
                    {
                        if (locale == "mul" && text.IsMultiLanguage)
                        {
                            IReadOnlyDictionary<string, string>? translations = text.Translations;
                            Assert.That(translations, Is.Not.Null);
                            bool hasEnglish = translations is not null
                                && translations.TryGetValue("en-US", out _);
                            bool hasGerman = translations is not null
                                && translations.TryGetValue("de-DE", out _);
                            Assert.That(hasEnglish, Is.True, "The wire translations must contain en-US.");
                            Assert.That(hasGerman, Is.True, "The wire translations must contain de-DE.");
                            if (translations is not null
                                && translations.TryGetValue("en-US", out string? actualEnglish))
                            {
                                Assert.That(actualEnglish, Is.EqualTo(english));
                            }
                            if (translations is not null
                                && translations.TryGetValue("de-DE", out string? actualGerman))
                            {
                                Assert.That(actualGerman, Is.EqualTo(german));
                            }
                        }
                        else if (locale == "mul")
                        {
                            Assert.That(text.Locale, Is.AnyOf("en-US", "de-DE"),
                                "Part 4 permits an available single-locale fallback for mul.");
                            Assert.That(text.Text, Is.EqualTo(text.Locale == "de-DE" ? german : english));
                        }
                        else
                        {
                            Assert.That(text.Locale, Is.EqualTo(locale));
                            Assert.That(text.Text, Is.EqualTo(locale == "de-DE" ? german : english));
                        }
                    }

                }
            }
            finally
            {
                await host.StopAsync(CancellationToken.None);
            }

            EUInformation Units(string english, string german)
            {
                var translations = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["en-US"] = english,
                    ["de-DE"] = german
                };
                var text = new LocalizedText(translations, new TranslationInfo("unit", "en-US", english));
                return new EUInformation
                {
                    NamespaceUri = "urn:padim:locale-test",
                    UnitId = 1,
                    DisplayName = description ? new LocalizedText("fixed") : text,
                    Description = description ? text : new LocalizedText("fixed")
                };
            }
        }

        /// <summary>
        /// Waits for configuration and server startup, reporting host failures before the test timeout.
        /// </summary>
        private static async Task WaitForServerStartAsync(
            IHostedService host,
            Task started,
            Task configured,
            CancellationToken cancellationToken)
        {
            Task readiness = Task.WhenAll(started, configured);
            if (host is BackgroundService { ExecuteTask: { } execute })
            {
                Task completed = await Task.WhenAny(readiness, execute).WaitAsync(cancellationToken);
                await completed.WaitAsync(cancellationToken);
                if (execute.IsCompleted)
                {
                    await execute;
                    throw new InvalidOperationException(
                        "The hosted server stopped before readiness was observed.");
                }
            }
            await readiness.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// Signals completion of the hosted server's startup.
        /// </summary>
        private sealed class ServerStartedSignal : IServerStartupTask
        {
            /// <summary>
            /// Gets the task that completes after the server starts.
            /// </summary>
            internal Task Task => m_started.Task;

            /// <inheritdoc/>
            public ValueTask OnServerStartedAsync(
                IServerContext server,
                CancellationToken cancellationToken = default)
            {
                m_started.TrySetResult(true);
                return default;
            }

            /// <summary>
            /// The server startup completion signal.
            /// </summary>
            private readonly TaskCompletionSource<bool> m_started =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
