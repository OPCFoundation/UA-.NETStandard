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
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Transports;
using UaLens.Plugins.PubSub;

namespace UaLens.Tests.PubSub;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class PubSubCommissioningRuntimeTests
{
    [TestCase("token")]
    [TestCase("future")]
    [TestCase("expiry")]
    [TestCase("signing")]
    [TestCase("encryption")]
    [TestCase("nonce")]
    public async Task InvalidActiveKeyFailsBeforeNetworkStartAndReleasesLeasesInReverseOrderAsync(string fault)
    {
        var clock = new PubSubTestClock();
        DateTimeOffset issuedAt = clock.GetUtcNow();
        if (fault == "future")
        {
            issuedAt = issuedAt.AddTicks(1);
        }
        else if (fault == "expiry")
        {
            issuedAt = issuedAt.AddMinutes(-1);
        }
        using var key = new PubSubSecurityKey(
            fault == "token" ? 0u : 17u,
            ByteString.From(RandomNumberGenerator.GetBytes(fault == "signing" ? 31 : 32)),
            ByteString.From(RandomNumberGenerator.GetBytes(fault == "encryption" ? 15 : 32)),
            ByteString.From(RandomNumberGenerator.GetBytes(fault == "nonce" ? 3 : 4)),
            DateTimeUtc.From(issuedAt), TimeSpan.FromMinutes(1));
        var released = new List<string>();
        var transport = new Mock<IPubSubTransportFactory>(MockBehavior.Strict);
        transport.SetupGet(value => value.TransportProfileUri).Returns(Profiles.PubSubUdpUadpTransport);
        var transportProvider = new ConfiguredPubSubTransportProvider(
            "configured", [PubSubProfile.UdpUadp],
            _ => new PubSubPrerequisite("Transport", PubSubReadiness.Ready, "Test binding"),
            (_, _, _) => ValueTask.FromResult(new PubSubTransportLease(
                transport.Object, new ReleaseOwner(() => released.Add("transport")))));
        var keys = new Mock<IPubSubSecurityKeyProvider>(MockBehavior.Strict);
        keys.SetupGet(value => value.SecurityGroupId).Returns("group");
        keys.Setup(value => value.GetCurrentKeyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(key);
        var keyProvider = new ConfiguredPubSubKeyProvider("keys",
            (_, _) => ValueTask.FromResult(new PubSubKeyProviderLease(
                keys.Object, new ReleaseOwner(() => released.Add("keys")))));
        var factory = new PubSubRuntimeFactory(DefaultTelemetry.Create(static _ => { }), clock,
            [transportProvider], [keyProvider]);
        PubSubConfiguration configuration = PubSubTestRuntime.Configuration with
        {
            TransportProviderId = "configured",
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityGroupId = "group",
            SecurityProviderId = "keys",
            KeySource = PubSubKeySource.ConfiguredProvider
        };
        await Assert.ThatAsync(() => factory.CreateAsync(
            configuration, new(), null, new PubSubObservationStore(2), CancellationToken.None).AsTask(),
            Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadSecurityChecksFailed)).ConfigureAwait(false);
        Assert.That(released, Is.EqualTo(s_releaseOrder));
        keys.Verify(value => value.GetCurrentKeyAsync(It.IsAny<CancellationToken>()), Times.Once);
        transport.Verify(value => value.Create(
            It.IsAny<PubSubConnectionDataType>(), It.IsAny<ITelemetryContext>(), It.IsAny<TimeProvider>()),
            Times.Never);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ProviderReferenceCannotRetargetTheKeySourceOrAuthorityAsync(bool sourceMismatch)
    {
        int acquired = 0;
        var provider = new ConfiguredPubSubKeyProvider("keys", "opc.tcp://localhost:4840/trusted-sks",
            (_, _) =>
            {
                acquired++;
                throw new InvalidOperationException("Acquisition must not occur.");
            });
        var factory = new PubSubRuntimeFactory(DefaultTelemetry.Create(static _ => { }), keyProviders: [provider]);
        PubSubConfiguration configuration = PubSubTestRuntime.Configuration with
        {
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityGroupId = "group",
            SecurityProviderId = "keys",
            KeySource = sourceMismatch ? PubSubKeySource.ConfiguredProvider : PubSubKeySource.SecurityKeyService,
            SecurityKeyServiceEndpoint = sourceMismatch ? string.Empty : "opc.tcp://localhost:4840/another-sks"
        };
        Assert.That(factory.Inspect(configuration, false).Contains(value =>
            value.Area == "Security" && value.Readiness == PubSubReadiness.RequiresConfiguration), Is.True);
        await Assert.ThatAsync(() => factory.CreateAsync(
            configuration, new(), null, new PubSubObservationStore(2), CancellationToken.None).AsTask(),
            Throws.InvalidOperationException).ConfigureAwait(false);
        Assert.That(acquired, Is.Zero);
    }

    [TestCase("name")]
    [TestCase("type")]
    [TestCase("status")]
    [TestCase("partial")]
    [TestCase("null")]
    [TestCase("array")]
    [TestCase("nonfinite")]
    public async Task IncompatibleWriteBackDataNeverReachesTheMappedUaWriterAsync(string fault)
    {
        var target = new Mock<ISubscribedDataSetSink>(MockBehavior.Strict);
        var local = new PubSubObservationStore(4);
        PubSubConfiguration configuration = PubSubTestRuntime.Configuration with
        {
            Fields = [new() { Name = "Temperature", Type = BuiltInType.Double, TargetNodeId = "i=2258" }]
        };
        var controlled = new PubSubControlledWriteBack(target.Object, local, configuration, new PubSubTestClock());
        await using (controlled.ConfigureAwait(false))
        {
            var field = new DataSetField
            {
                Name = "Temperature", Value = Variant.From(42.5), StatusCode = StatusCodes.Good
            };
            field = fault switch
            {
                "name" => field with { Name = "Other" },
                "type" => field with { Value = Variant.From(42) },
                "status" => field with { StatusCode = StatusCodes.BadOutOfService },
                "partial" => field with { FieldIndex = 0 },
                "null" => field with { Value = Variant.Null },
                "array" => field with { Value = Variant.From((ArrayOf<double>)[42.5]) },
                "nonfinite" => field with { Value = Variant.From(double.NaN) },
                _ => throw new ArgumentOutOfRangeException(nameof(fault))
            };
            await Assert.ThatAsync(() => controlled.WriteAsync([field]).AsTask(),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
            target.Verify(value => value.WriteAsync(
                It.IsAny<IReadOnlyList<DataSetField>>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(local.Snapshot().AcceptedDataSets, Is.Zero);
            Assert.That(local.Snapshot().Evidence.Count, Is.GreaterThan(0));
        }
    }

    [TestCase("target")]
    [TestCase("correlation")]
    [TestCase("output")]
    public async Task InvalidActionResponseDiscardsThePreviousSuccessfulEvidenceAsync(string invalid)
    {
        var runtime = new PubSubTestRuntime();
        bool corrupt = false;
        runtime.Application.Setup(value => value.InvokeActionAsync(
            It.IsAny<PubSubActionRequest>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns((PubSubActionRequest request, TimeSpan _, CancellationToken _) => ValueTask.FromResult(
                new PubSubActionResponse
                {
                    Target = request.Target with
                    {
                        ActionTargetId = corrupt && invalid == "target" ? (ushort)999 : request.Target.ActionTargetId
                    },
                    RequestId = 17,
                    CorrelationData = corrupt && invalid == "correlation" ? ByteString.Empty : ByteString.From([1, 2]),
                    StatusCode = StatusCodes.Good,
                    ActionState = ActionState.Done,
                    OutputFields = corrupt && invalid == "output"
                        ? [PubSubTestRuntime.Field(1, "Duplicate"), PubSubTestRuntime.Field(2, "Duplicate")]
                        : [PubSubTestRuntime.Field(42, "Answer")]
                }));
        var workspace = runtime.CreateWorkspace();
        await using (workspace.ConfigureAwait(false))
        {
            await workspace.ConfigureAsync(PubSubTestRuntime.Configuration).ConfigureAwait(false);
            await workspace.StartAsync(PubSubTestRuntime.ReceiveAuthorization).ConfigureAwait(false);
            var request = new PubSubActionRequest
            {
                Target = new PubSubActionTarget { DataSetWriterId = 2, ActionTargetId = 1 }
            };
            PubSubActionResult previous = await workspace.InvokeActionAsync(request, true, TimeSpan.FromSeconds(2))
                .ConfigureAwait(false);
            Assert.That(previous.Outputs[0].Value.TryGetValue(out int answer), Is.True);
            Assert.That(answer, Is.EqualTo(42));
            corrupt = true;
            await Assert.ThatAsync(() => workspace.InvokeActionAsync(request, true, TimeSpan.FromSeconds(2)),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadDecodingError)).ConfigureAwait(false);
            Assert.That(workspace.Snapshot().Action, Is.Null);
            runtime.Application.Verify(value => value.InvokeActionAsync(
                It.IsAny<PubSubActionRequest>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }
    }

    private static readonly string[] s_releaseOrder = ["keys", "transport"];

    private sealed class ReleaseOwner(Action released) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            released();
            return ValueTask.CompletedTask;
        }
    }
}
