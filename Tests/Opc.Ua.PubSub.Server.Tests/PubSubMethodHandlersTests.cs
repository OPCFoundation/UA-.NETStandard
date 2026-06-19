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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Server.Tests
{
    /// <summary>
    /// Coverage for <see cref="PubSubMethodHandlers"/>: standard
    /// PublishSubscribe method handlers wired by the
    /// <see cref="PubSubNodeManager"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("9.1.3.4", Summary = "AddConnection")]
    [TestSpec("9.1.3.5", Summary = "RemoveConnection")]
    [TestSpec("9.1.10.2", Summary = "Status.Enable")]
    [TestSpec("9.1.10.3", Summary = "Status.Disable")]
    [TestSpec("8.3.1", Summary = "SecurityGroup add/remove")]
    [TestSpec("8.3.2", Summary = "GetSecurityKeys")]
    public class PubSubMethodHandlersTests
    {
        [Test]
        public void OnEnable_StartsApplicationAndReturnsGood()
        {
            PubSubMethodHandlers handlers = CreateHandlers(out _, out _);
            var outputs = new List<Variant>();

            ServiceResult result = handlers.OnEnable(
                BuildContext(), method: null!, inputArguments: default, outputArguments: outputs);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public void OnDisable_StopsApplicationAndReturnsGood()
        {
            PubSubMethodHandlers handlers = CreateHandlers(out _, out _);
            var outputs = new List<Variant>();

            ServiceResult result = handlers.OnDisable(
                BuildContext(), method: null!, inputArguments: default, outputArguments: outputs);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public void OnAddConnection_NoArgs_ReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = CreateHandlers(out _, out _);
            var outputs = new List<Variant>();

            ServiceResult result = handlers.OnAddConnection(
                BuildContext(), method: null!, inputArguments: default, outputArguments: outputs);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void OnAddConnection_WhenConfigurationMethodsDisabled_ReturnsAccessDenied()
        {
            PubSubMethodHandlers handlers = CreateHandlers(
                out _,
                out _,
                opt => opt.ExposeConfigurationMethods = false);
            var outputs = new List<Variant>();

            ServiceResult result = handlers.OnAddConnection(
                BuildContext(), method: null!, inputArguments: default, outputArguments: outputs);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void OnRemoveConnection_NoArgs_ReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = CreateHandlers(out _, out _);
            var outputs = new List<Variant>();

            ServiceResult result = handlers.OnRemoveConnection(
                BuildContext(), method: null!, inputArguments: default, outputArguments: outputs);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void OnRemoveConnection_WhenConfigurationMethodsDisabled_ReturnsAccessDenied()
        {
            PubSubMethodHandlers handlers = CreateHandlers(
                out _,
                out _,
                opt => opt.ExposeConfigurationMethods = false);
            var outputs = new List<Variant>();

            ServiceResult result = handlers.OnRemoveConnection(
                BuildContext(), method: null!, inputArguments: default, outputArguments: outputs);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void OnAddSecurityGroup_RoundtripsGroupAndReturnsNodeId()
        {
            PubSubMethodHandlers handlers = CreateHandlers(
                out _,
                out InMemoryPubSubKeyServiceServer sks,
                opt => opt.ExposeSecurityKeyService = true);
            var inputs = BuildArray(
                Variant.From("group-a"),
                Variant.From(60_000.0),
                Variant.From(PubSubSecurityPolicyUri.PubSubAes128Ctr),
                Variant.From(4U),
                Variant.From(2U));
            var outputs = new List<Variant>();

            ServiceResult result = handlers.OnAddSecurityGroup(
                BuildContext(), method: null!, inputArguments: inputs, outputArguments: outputs);

            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
                Assert.That(outputs, Has.Count.EqualTo(2));
                Assert.That(outputs[0].TryGetValue(out string? groupId), Is.True);
                Assert.That(groupId, Is.EqualTo("group-a"));
                Assert.That(outputs[1].TryGetValue(out NodeId nodeId), Is.True);
                Assert.That(nodeId.IsNull, Is.False);
            });
            Assert.That(((string[]?)sks.SecurityGroupIds) ?? [], Contains.Item("group-a"));
        }

        [Test]
        public void OnAddSecurityGroup_WhenKeyServiceMissing_ReturnsServiceUnsupported()
        {
            PubSubMethodHandlers handlers = CreateHandlers(out _, out _);
            ServiceResult result = handlers.OnAddSecurityGroup(
                BuildContext(),
                method: null!,
                inputArguments: BuildArray(Variant.From("g")),
                outputArguments: new List<Variant>());
            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadServiceUnsupported));
        }

        [Test]
        public void OnAddSecurityGroup_RejectsTooFewArguments()
        {
            PubSubMethodHandlers handlers = CreateHandlers(
                out _,
                out _,
                opt => opt.ExposeSecurityKeyService = true);

            ServiceResult result = handlers.OnAddSecurityGroup(
                BuildContext(),
                method: null!,
                inputArguments: BuildArray(Variant.From("g")),
                outputArguments: new List<Variant>());

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [TestCase("", 60_000.0, PubSubSecurityPolicyUri.PubSubAes128Ctr, 4U, 2U)]
        [TestCase("g", 0.0, PubSubSecurityPolicyUri.PubSubAes128Ctr, 4U, 2U)]
        [TestCase("g", 60_000.0, "", 4U, 2U)]
        public void OnAddSecurityGroup_RejectsBadArguments(
            string name, double lifetime, string policy, uint maxFuture, uint maxPast)
        {
            PubSubMethodHandlers handlers = CreateHandlers(
                out _,
                out _,
                opt => opt.ExposeSecurityKeyService = true);

            ServiceResult result = handlers.OnAddSecurityGroup(
                BuildContext(),
                method: null!,
                inputArguments: BuildArray(
                    Variant.From(name),
                    Variant.From(lifetime),
                    Variant.From(policy),
                    Variant.From(maxFuture),
                    Variant.From(maxPast)),
                outputArguments: new List<Variant>());

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task OnAddSecurityGroup_DuplicateName_PropagatesSksException()
        {
            PubSubMethodHandlers handlers = CreateHandlers(
                out _,
                out InMemoryPubSubKeyServiceServer sks,
                opt => opt.ExposeSecurityKeyService = true);
            await sks.AddSecurityGroupAsync(new SksSecurityGroup(
                "g-dup",
                PubSubSecurityPolicyUri.PubSubAes128Ctr,
                TimeSpan.FromMinutes(1),
                2,
                2,
                Array.Empty<PubSubSecurityKey>()));

            ServiceResult result = handlers.OnAddSecurityGroup(
                BuildContext(),
                method: null!,
                inputArguments: BuildArray(
                    Variant.From("g-dup"),
                    Variant.From(60_000.0),
                    Variant.From(PubSubSecurityPolicyUri.PubSubAes128Ctr),
                    Variant.From(2U),
                    Variant.From(2U)),
                outputArguments: new List<Variant>());

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadAlreadyExists));
        }

        [Test]
        public void OnRemoveSecurityGroup_RoundTrip()
        {
            PubSubMethodHandlers handlers = CreateHandlers(
                out _,
                out InMemoryPubSubKeyServiceServer sks,
                opt => opt.ExposeSecurityKeyService = true);
            ServiceResult addResult = handlers.OnAddSecurityGroup(
                BuildContext(),
                method: null!,
                inputArguments: BuildArray(
                    Variant.From("g-x"),
                    Variant.From(60_000.0),
                    Variant.From(PubSubSecurityPolicyUri.PubSubAes128Ctr),
                    Variant.From(2U),
                    Variant.From(2U)),
                outputArguments: new List<Variant>());
            Assert.That(StatusCode.IsGood(addResult.StatusCode), Is.True);
            NodeId? nodeId = handlers.TryGetSecurityGroupNodeId("g-x");
            Assert.That(nodeId, Is.Not.Null);
            NodeId resolved = nodeId!.Value;

            ServiceResult result = handlers.OnRemoveSecurityGroup(
                BuildContext(),
                method: null!,
                inputArguments: BuildArray(Variant.From(resolved)),
                outputArguments: new List<Variant>());

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(((string[]?)sks.SecurityGroupIds) ?? [], Does.Not.Contain("g-x"));
        }

        [Test]
        public void OnRemoveSecurityGroup_UnknownNodeId_ReturnsBadNodeIdUnknown()
        {
            PubSubMethodHandlers handlers = CreateHandlers(
                out _,
                out _,
                opt => opt.ExposeSecurityKeyService = true);

            // Numeric NodeId that is not in the handler's allocated map
            // and cannot be parsed back to a securityGroupId string.
            ServiceResult result = handlers.OnRemoveSecurityGroup(
                BuildContext(),
                method: null!,
                inputArguments: BuildArray(Variant.From(new NodeId(424242u))),
                outputArguments: new List<Variant>());

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void OnRemoveSecurityGroup_NoKeyService_ReturnsServiceUnsupported()
        {
            PubSubMethodHandlers handlers = CreateHandlers(out _, out _);
            ServiceResult result = handlers.OnRemoveSecurityGroup(
                BuildContext(),
                method: null!,
                inputArguments: BuildArray(Variant.From(new NodeId("x", 0))),
                outputArguments: new List<Variant>());
            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadServiceUnsupported));
        }

        [Test]
        public void OnRemoveSecurityGroup_MissingArg_ReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = CreateHandlers(
                out _, out _, opt => opt.ExposeSecurityKeyService = true);
            ServiceResult result = handlers.OnRemoveSecurityGroup(
                BuildContext(), method: null!, inputArguments: default, outputArguments: new List<Variant>());
            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void OnRemoveSecurityGroup_NullNodeId_ReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = CreateHandlers(
                out _, out _, opt => opt.ExposeSecurityKeyService = true);
            ServiceResult result = handlers.OnRemoveSecurityGroup(
                BuildContext(),
                method: null!,
                inputArguments: BuildArray(Variant.From(NodeId.Null)),
                outputArguments: new List<Variant>());
            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task OnGetSecurityKeys_ReturnsGoodAndKeyMaterial()
        {
            PubSubMethodHandlers handlers = CreateHandlers(
                out _,
                out InMemoryPubSubKeyServiceServer sks,
                opt => opt.ExposeSecurityKeyService = true);
            await sks.AddSecurityGroupAsync(new SksSecurityGroup(
                "grp",
                PubSubSecurityPolicyUri.PubSubAes128Ctr,
                TimeSpan.FromMinutes(1),
                3,
                1,
                Array.Empty<PubSubSecurityKey>(),
                ["user"]));

            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnGetSecurityKeys(
                BuildContext("user"),
                method: null!,
                objectId: ObjectIds.PublishSubscribe,
                inputArguments: BuildArray(
                    Variant.From("grp"),
                    Variant.From(0U),
                    Variant.From(2U)),
                outputArguments: outputs);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(outputs, Has.Count.EqualTo(5));
        }

        [Test]
        public void OnGetSecurityKeys_NoKeyService_ReturnsServiceUnsupported()
        {
            PubSubMethodHandlers handlers = CreateHandlers(out _, out _);
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnGetSecurityKeys(
                BuildContext("u"),
                method: null!,
                objectId: ObjectIds.PublishSubscribe,
                inputArguments: BuildArray(Variant.From("grp"), Variant.From(0U), Variant.From(1U)),
                outputArguments: outputs);
            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadServiceUnsupported));
        }

        [Test]
        public void Constructor_NullArgs_Throw()
        {
            IPubSubApplication app = CreateApplication();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var options = new PubSubServerOptions();

            Assert.Multiple(() =>
            {
                Assert.That(() => new PubSubMethodHandlers(null!, null, options, telemetry),
                    Throws.ArgumentNullException);
                Assert.That(() => new PubSubMethodHandlers(app, null, null!, telemetry),
                    Throws.ArgumentNullException);
                Assert.That(() => new PubSubMethodHandlers(app, null, options, null!),
                    Throws.ArgumentNullException);
            });
        }

        [Test]
        public void TryGetSecurityGroupNodeId_EmptyId_ReturnsNull()
        {
            PubSubMethodHandlers handlers = CreateHandlers(out _, out _);
            Assert.That(handlers.TryGetSecurityGroupNodeId(string.Empty), Is.Null);
        }

        [Test]
        public void DefaultPolicyUri_FallsBackToBuiltInAes256()
        {
            PubSubMethodHandlers handlers = CreateHandlers(out _, out _);
            Assert.That(handlers.DefaultPolicyUri,
                Is.EqualTo("http://opcfoundation.org/UA/SecurityPolicy#PubSub-Aes256-CTR"));
        }

        [Test]
        public void DefaultPolicyUri_HonoursConfiguredOverride()
        {
            PubSubMethodHandlers handlers = CreateHandlers(
                out _,
                out _,
                opt => opt.DefaultSecurityPolicyUri = PubSubSecurityPolicyUri.PubSubAes128Ctr);
            Assert.That(handlers.DefaultPolicyUri, Is.EqualTo(PubSubSecurityPolicyUri.PubSubAes128Ctr));
        }

        private static PubSubMethodHandlers CreateHandlers(
            out IPubSubApplication application,
            out InMemoryPubSubKeyServiceServer sksServer,
            Action<PubSubServerOptions>? configureOptions = null)
        {
            application = CreateApplication();
            sksServer = new InMemoryPubSubKeyServiceServer();
            var options = new PubSubServerOptions();
            configureOptions?.Invoke(options);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            return new PubSubMethodHandlers(
                application,
                options.ExposeSecurityKeyService ? sksServer : null,
                options,
                telemetry);
        }

        private static IPubSubApplication CreateApplication()
        {
            return new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithApplicationId("test-handlers")
                .UseConfiguration(new PubSubConfigurationDataType
                {
                    Connections = [],
                    PublishedDataSets = []
                })
                .UseAllStandardEncoders()
                .AddTransportFactory(new StubTransportFactory())
                .Build();
        }

        private static SystemContext BuildContext(string? userId = null)
        {
            return new SystemContext(NUnitTelemetryContext.Create())
            {
                UserId = userId
            };
        }

        private static ArrayOf<Variant> BuildArray(params Variant[] values)
        {
            return new ArrayOf<Variant>(values);
        }

        private sealed class StubTransportFactory : IPubSubTransportFactory
        {
            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public IPubSubTransport Create(
                PubSubConnectionDataType connection,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
            {
                _ = connection;
                _ = telemetry;
                _ = timeProvider;
                return new StubTransport();
            }
        }

        private sealed class StubTransport : IPubSubTransport
        {
            private bool m_isConnected;

            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public PubSubTransportDirection Direction => PubSubTransportDirection.SendReceive;

            public bool IsConnected => m_isConnected;

            public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged
            {
                add { }
                remove { }
            }

            public ValueTask OpenAsync(CancellationToken cancellationToken = default)
            {
                _ = cancellationToken;
                m_isConnected = true;
                return default;
            }

            public ValueTask CloseAsync(CancellationToken cancellationToken = default)
            {
                _ = cancellationToken;
                m_isConnected = false;
                return default;
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload,
                string? topic = null,
                CancellationToken cancellationToken = default)
            {
                _ = payload;
                _ = topic;
                _ = cancellationToken;
                return default;
            }

            public IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                CancellationToken cancellationToken = default)
            {
                _ = cancellationToken;
                return TestAsyncEnumerable.Empty<PubSubTransportFrame>();
            }

            public ValueTask DisposeAsync()
            {
                m_isConnected = false;
                return default;
            }
        }
    }
}
