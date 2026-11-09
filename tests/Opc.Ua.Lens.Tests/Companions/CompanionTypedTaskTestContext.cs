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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Plugins.Companions;

namespace UaLens.Tests.Companions
{

    internal sealed class CompanionTypedTaskTestContext : IAsyncDisposable
    {
        public CompanionTypedTaskTestContext(
            ITelemetryContext? telemetry = null,
            CompanionOperationSafety safety = CompanionOperationSafety.ReadOnly,
            string endpoint = "opc.tcp://localhost:4840/Sample")
        {
            Telemetry = telemetry ?? DefaultTelemetry.Create(static _ => { });
            Endpoint = new EndpointDescription
            {
                EndpointUrl = endpoint,
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
            Session.SetupGet(value => value.Connected).Returns(() => Connected);
            Session.SetupGet(value => value.SessionId).Returns(() => SessionId);
            Session.SetupGet(value => value.Identity).Returns(() => Identity);
            Session.SetupGet(value => value.NamespaceUris).Returns(NamespaceUris);
            Session.SetupGet(value => value.Endpoint).Returns(Endpoint);
            Session.SetupGet(value => value.MessageContext).Returns(ServiceMessageContext.Create(Telemetry));
            Clock.Setup(value => value.GetUtcNow()).Returns(() => UtcNow);
            Operation = new CompanionOperation("typed", "Apply typed settings", safety)
            {
                Inputs =
                [
                    new("label", "Label", BuiltInType.String, "Package label"),
                    new("enabled", "Enabled", BuiltInType.Boolean, "Enable the option"),
                    new("count", "Count", BuiltInType.UInt32, "Unsigned count"),
                    new("delta", "Delta", BuiltInType.Int32, "Signed adjustment"),
                    new("gain", "Gain", BuiltInType.Double, "Invariant gain")
                ]
            };
            Inspection = new CompanionInspection(
                [new("Current count", Variant.From(7u))], [Operation], "Typed inspection.");
            Provider.SetupGet(value => value.Descriptor).Returns(
                new CompanionDescriptor("typed-provider", "Typed model", "urn:typed", "Test model"));
            Provider.Setup(value => value.DiscoverAsync(
                    It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult<ArrayOf<CompanionTarget>>([Target]));
            Provider.Setup(value => value.InspectAsync(
                    It.IsAny<CompanionContext>(), Target, It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult(Inspection));
            Provider.Setup(value => value.PrepareInputAsync(
                    It.IsAny<CompanionContext>(), Target, "typed",
                    It.IsAny<ArrayOf<CompanionValue>>(), It.IsAny<CancellationToken>()))
                .Returns((CompanionContext _, CompanionTarget _, string _, ArrayOf<CompanionValue> inputs,
                    CancellationToken token) =>
                {
                    CapturedInputs = inputs;
                    PrepareToken = token;
                    return PrepareHandler?.Invoke(inputs, token) ?? ValueTask.FromResult(PreparedInput);
                });
            Provider.Setup(value => value.ExecutePreparedAsync(
                    It.IsAny<CompanionContext>(), Target, "typed", It.IsAny<CompanionTaskInput>(),
                    It.IsAny<IProgress<CompanionTaskProgress>?>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult(Result));
            Workspace = new CompanionWorkspace([Provider.Object], Telemetry, Clock.Object);
        }

        public ITelemetryContext Telemetry { get; }

        public Mock<ISession> Session { get; } = new(MockBehavior.Strict);

        public Mock<IPreparedCompanionProvider> Provider { get; } = new(MockBehavior.Strict);

        public Mock<TimeProvider> Clock { get; } = new(MockBehavior.Strict);

        public CompanionWorkspace Workspace { get; }

        public CompanionTarget Target { get; } = new("typed-provider", new NodeId(1234u, 2), "Typed device", "Device");

        public CompanionOperation Operation { get; }

        public CompanionInspection Inspection { get; set; }

        public CompanionTaskInput PreparedInput { get; set; } = new CompanionTestTaskInput("Reviewed typed settings.");

        public CompanionOperationResult Result { get; } =
            new("Typed operation completed.", [new("Accepted count", Variant.From(19u))]);

        public ArrayOf<CompanionValue> Inputs { get; } =
        [
            new("label", Variant.From("unreviewed-label")),
            new("enabled", Variant.From(true)),
            new("count", Variant.From(19u)),
            new("delta", Variant.From(-3)),
            new("gain", Variant.From(2.5))
        ];

        public ArrayOf<CompanionValue> CapturedInputs { get; private set; }

        public CancellationToken PrepareToken { get; private set; }

        public Func<ArrayOf<CompanionValue>, CancellationToken,
            ValueTask<CompanionTaskInput>>? PrepareHandler
        { get; set; }

        public NamespaceTable NamespaceUris { get; } = new([Namespaces.OpcUa, "urn:server", "urn:typed"]);

        public EndpointDescription Endpoint { get; }

        public bool Connected { get; set; } = true;

        public NodeId SessionId { get; set; } = new(101u);

        public IUserIdentity Identity { get; set; } = new Mock<IUserIdentity>(MockBehavior.Strict).Object;

        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

        public async Task InitializeAsync()
        {
            await Workspace.BindAsync(Session.Object).ConfigureAwait(false);
            await Workspace.DiscoverAsync("typed-provider").ConfigureAwait(false);
            await Workspace.InspectAsync(Target).ConfigureAwait(false);
        }

        public Task<CompanionOperationDraft> PrepareAsync()
        {
            return Workspace.PrepareTaskAsync(Target, "typed", Inputs);
        }

        public void VerifyCalls(int preparations, int executions)
        {
            Provider.Verify(value => value.PrepareInputAsync(
                It.IsAny<CompanionContext>(), It.IsAny<CompanionTarget>(), It.IsAny<string>(),
                It.IsAny<ArrayOf<CompanionValue>>(), It.IsAny<CancellationToken>()), Times.Exactly(preparations));
            Provider.Verify(value => value.ExecutePreparedAsync(
                It.IsAny<CompanionContext>(), It.IsAny<CompanionTarget>(), It.IsAny<string>(),
                It.IsAny<CompanionTaskInput>(), It.IsAny<IProgress<CompanionTaskProgress>?>(),
                It.IsAny<CancellationToken>()), Times.Exactly(executions));
            Provider.Verify(value => value.ExecuteAsync(
                It.IsAny<CompanionContext>(), It.IsAny<CompanionTarget>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        public void VerifyInspections(int count)
        {
            Provider.Verify(value => value.InspectAsync(
                It.IsAny<CompanionContext>(), Target, It.IsAny<CancellationToken>()), Times.Exactly(count));
        }

        public ValueTask DisposeAsync()
        {
            return Workspace.DisposeAsync();
        }
    }
}
