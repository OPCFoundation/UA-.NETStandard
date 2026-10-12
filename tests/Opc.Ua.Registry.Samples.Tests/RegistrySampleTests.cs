using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.EndpointRegistry;
using Opc.Ua.EndpointRegistry.Client;
using Opc.Ua.SchemaRegistry.Client;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry;

namespace Opc.Ua.Registry.Samples.Tests
{
    /// <summary>Runs the standalone sample's public hosting and client entry points over secured TCP.</summary>
    [TestFixture]
    [NonParallelizable]
    [Category("RegistrySamples")]
    public sealed class RegistrySampleTests
    {
        [Test]
        public async Task NativeDemoSurvivesDurableRestartAsync()
        {
            string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                "registry-sample-" + Guid.NewGuid().ToString("N"));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var options = new RegistrySampleServerOptions
            {
                EndpointUrl = $"opc.tcp://localhost:{port}/Registry",
                StateDirectory = Path.Combine(directory, "state"),
                PkiDirectory = Path.Combine(directory, "server-pki"),
                UserName = "sysadmin",
                Password = "demo",
                EnableSchema = true,
                AutoAccept = true
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try
            {
                uint epoch;
                await using (RegistrySampleServer server = await RegistrySampleServer.StartAsync(options, timeout.Token)
                    .ConfigureAwait(false))
                {
                    RegistryDemoResult result = await RegistryDemo.ConnectAndRunAsync(options.EndpointUrl,
                        "sysadmin", "demo", Path.Combine(directory, "client-pki"), autoAccept: true,
                        includeSchema: true, cancellationToken: timeout.Token).ConfigureAwait(false);
                    epoch = result.Epoch;
                    Assert.Multiple(() =>
                    {
                        Assert.That(result.Protocol, Is.EqualTo("MQTT/5.0"));
                        Assert.That(result.Topic, Is.EqualTo("factory/line1/temperature"));
                        Assert.That(result.SchemaReference, Is.EqualTo("urn:registry-sample:temperature"));
                        Assert.That(result.Description, Is.EqualTo(RegistryDemo.LargeDescription));
                        Assert.That(result.GroupName, Is.EqualTo("Updated reusable Message Group"));
                        Assert.That(result.ResolvedContentType, Is.EqualTo("application/json"));
                        Assert.That(result.SchemaFingerprint.IsNull, Is.False);
                        Assert.That(result.SnapshotParts, Is.GreaterThan(1));
                    });
                    RegistryDemoResult repeated = await RegistryDemo.ConnectAndRunAsync(options.EndpointUrl,
                        "sysadmin", "demo", Path.Combine(directory, "client-pki"), autoAccept: true,
                        includeSchema: true, cancellationToken: timeout.Token).ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(repeated.Epoch, Is.EqualTo(epoch));
                        Assert.That(repeated.GroupName, Is.EqualTo("Updated reusable Message Group"));
                        Assert.That(repeated.Description, Is.EqualTo(RegistryDemo.LargeDescription));
                    });
                }

                await using (RegistrySampleServer restarted = await RegistrySampleServer.StartAsync(options, timeout.Token)
                    .ConfigureAwait(false))
                {
                    RegistryDemoResult persisted = await RegistryDemo.ConnectAndRunAsync(options.EndpointUrl,
                        "sysadmin", "demo", Path.Combine(directory, "client-pki"), autoAccept: true,
                        includeSchema: true, verifyOnly: true, cancellationToken: timeout.Token).ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(persisted.Epoch, Is.EqualTo(epoch));
                        Assert.That(persisted.Topic, Is.EqualTo("factory/line1/temperature"));
                        Assert.That(persisted.Description, Is.EqualTo(RegistryDemo.LargeDescription));
                        Assert.That(persisted.GroupName, Is.EqualTo("Updated reusable Message Group"));
                        Assert.That(persisted.ResolvedContentType, Is.EqualTo("application/json"));
                    });
                }
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Test]
        public async Task NativeFailuresKeepDiagnosticsAndOptionalSchemaIsNotInventedAsync()
        {
            string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                "registry-errors-" + Guid.NewGuid().ToString("N"));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var options = new RegistrySampleServerOptions
            {
                EndpointUrl = $"opc.tcp://localhost:{port}/Registry",
                StateDirectory = Path.Combine(directory, "state"),
                PkiDirectory = Path.Combine(directory, "server"),
                UserName = "sysadmin",
                Password = "demo",
                AutoAccept = true
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                RegistrySampleServer server = await RegistrySampleServer.StartAsync(options, timeout.Token)
                    .ConfigureAwait(false);
                await using (server.ConfigureAwait(false))
                {
                    ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                    var fixture = new ClientFixture(telemetry);
                    await using (fixture.ConfigureAwait(false))
                    {
                        await fixture.LoadClientConfigurationAsync(Path.Combine(directory, "client"))
                            .ConfigureAwait(false);
                        using ISession session = await fixture.ConnectAsync(new Uri(options.EndpointUrl),
                            SecurityPolicies.Basic256Sha256, userIdentity: new UserIdentity("sysadmin", "demo"u8))
                            .ConfigureAwait(false);
                        var services = new ServiceCollection();
                        services.AddSingleton(telemetry);
                        services.AddEndpointRegistryClient().AddEndpointRegistryClient();
                        services.AddSchemaRegistryClient().AddSchemaRegistryClient();
                        using ServiceProvider provider = services.BuildServiceProvider();
                        EndpointRegistryClient generic = provider.GetRequiredService<EndpointRegistryClientFactory>()
                            .Create(session);
                        EndpointRegistryClient media = new(session, telemetry, media: true);
                        RegistryReadResultDataType missing = await generic.ReadDocumentAsync(new RegistryReadRequestDataType
                        {
                            TargetXid = "/messagegroups/missing",
                            DocumentKind = "metadata",
                            View = 1,
                            MaxItems = 10
                        }, timeout.Token).ConfigureAwait(false);
                        Assert.That(missing.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
                        RegistryMutationResultDataType written = await generic.WriteDocumentAsync(new RegistryWriteRequestDataType
                        {
                            TargetXid = "/messagegroups/test",
                            Definition = generic.Canonicalize(new MessageGroupDataType
                            {
                                PresentFields = ["MessageGroupId"],
                                MessageGroupId = "test"
                            })
                        }, timeout.Token).ConfigureAwait(false);
                        Assert.That(written.StatusCode, Is.EqualTo(StatusCodes.Good));
                        RegistryMutationResultDataType stale = await generic.ApplyChangesAsync(new RegistryChangeRequestDataType
                        {
                            TargetXid = "/messagegroups/test",
                            ExpectedEpoch = written.Epoch + 1,
                            Changes =
                            [
                                new RegistryChangeDataType
                                        {
                                            Operation = 0,
                                            Path = [new RegistryPathElementDataType { Kind = 0, Name = "name" }],
                                            Value = new RegistryStringValueDataType { Kind = 2, Value = "stale" }
                                        }
                            ]
                        }, timeout.Token).ConfigureAwait(false);
                        Assert.Multiple(() =>
                        {
                            Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                            Assert.That(stale.Issues[0].Code, Is.EqualTo("E_EPOCH_CONFLICT"));
                            Assert.That(generic.RegistryNodeId, Is.Not.EqualTo(media.RegistryNodeId));
                            Assert.That(generic.TypedAccessNodeId, Is.Not.EqualTo(media.TypedAccessNodeId));
                        });
                        RegistryReadResultDataType isolated = await media.ReadDocumentAsync(new RegistryReadRequestDataType
                        {
                            TargetXid = "/endpoints/missing",
                            DocumentKind = "metadata",
                            View = 1,
                            MaxItems = 10
                        }, timeout.Token).ConfigureAwait(false);
                        Assert.That(isolated.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
                        ServiceResultException absent = Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await provider.GetRequiredService<SchemaRegistryClientFactory>().DiscoverAsync(session,
                                timeout.Token).ConfigureAwait(false))!;
                        Assert.That(absent.StatusCode, Is.AnyOf(StatusCodes.BadNodeIdUnknown, StatusCodes.BadNoMatch));
                    }
                }
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }
    }
}
