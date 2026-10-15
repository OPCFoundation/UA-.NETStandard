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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Plugins.Companions;

namespace UaLens.Tests.Companions
{

    internal sealed class CompanionPreparedOperationTestContext : IAsyncDisposable
    {
        public CompanionPreparedOperationTestContext(
            CompanionOperationSafety safety = CompanionOperationSafety.ReadOnly,
            string endpoint = "opc.tcp://localhost:4840/Sample",
            ITelemetryContext? telemetry = null,
            ArrayOf<ICompanionProvider> additionalProviders = default,
            ICompanionDeploymentPolicy? deploymentPolicy = null)
        {
            Telemetry = telemetry ?? DefaultTelemetry.Create(static _ => { });
            Endpoint = new EndpointDescription
            {
                EndpointUrl = endpoint,
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
            Session.SetupGet(item => item.Connected).Returns(() => Connected);
            Session.SetupGet(item => item.SessionId).Returns(() => SessionId);
            Session.SetupGet(item => item.Identity).Returns(() => Identity);
            Session.SetupGet(item => item.NamespaceUris).Returns(NamespaceUris);
            Session.SetupGet(item => item.Endpoint).Returns(Endpoint);
            Session.SetupGet(item => item.MessageContext).Returns(ServiceMessageContext.Create(Telemetry));
            Clock.Setup(item => item.GetUtcNow()).Returns(() => UtcNow);
            Operation = new CompanionOperation("apply", "Apply recipe", safety, "Recipe input");
            Targets = [Target];
            Inspection = new CompanionInspection(
                [new CompanionValue("Temperature", Variant.From(42.5))], [Operation], "Inspection complete.");
            Provider.SetupGet(item => item.Descriptor)
                .Returns(new CompanionDescriptor("sample", "Sample", "urn:ualens:test", "Test model"));
            Provider.Setup(item => item.DiscoverAsync(It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult(Targets));
            Provider.Setup(item => item.InspectAsync(
                    It.IsAny<CompanionContext>(), Target, It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult(Inspection));
            Provider.Setup(item => item.ExecuteAsync(
                    It.IsAny<CompanionContext>(), Target, "apply",
                    It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult(Result));
            Workspace = new CompanionWorkspace(
                [Provider.Object, .. additionalProviders], Telemetry, Clock.Object, deploymentPolicy);
        }

        public ITelemetryContext Telemetry { get; }

        public Mock<ISession> Session { get; } = new(MockBehavior.Strict);

        public Mock<ICompanionProvider> Provider { get; } = new(MockBehavior.Strict);

        public Mock<TimeProvider> Clock { get; } = new(MockBehavior.Strict);

        public CompanionWorkspace Workspace { get; }

        public CompanionTarget Target { get; } = new("sample", new NodeId(1234u, 2), "Sample device", "Device");

        public CompanionOperation Operation { get; }

        public ArrayOf<CompanionTarget> Targets { get; set; }

        public CompanionInspection Inspection { get; set; }

        public CompanionOperationResult Result { get; } =
            new("Operation complete.", [new CompanionValue("Processed", Variant.From(17u))]);

        public NamespaceTable NamespaceUris { get; } =
            new([Namespaces.OpcUa, "urn:ualens:server", "urn:ualens:test"]);

        public EndpointDescription Endpoint { get; }

        public bool Connected { get; set; } = true;

        public NodeId SessionId { get; set; } = new(101u);

        public IUserIdentity Identity { get; set; } = new Mock<IUserIdentity>(MockBehavior.Strict).Object;

        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

        public async Task InitializeAsync()
        {
            await Workspace.BindAsync(Session.Object).ConfigureAwait(false);
            await Workspace.DiscoverAsync("sample").ConfigureAwait(false);
            await Workspace.InspectAsync(Target).ConfigureAwait(false);
        }

        public void VerifyInspectionCount(int count)
        {
            Provider.Verify(item => item.InspectAsync(
                    It.Is<CompanionContext>(value => ReferenceEquals(value.Session, Session.Object)),
                    Target, It.IsAny<CancellationToken>()),
                Times.Exactly(count));
        }

        public void VerifyExecution(string? input)
        {
            Provider.Verify(item => item.ExecuteAsync(
                    It.Is<CompanionContext>(value => ReferenceEquals(value.Session, Session.Object)),
                    Target, "apply", input, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        public void VerifyExecutionCount(int count)
        {
            Provider.Verify(item => item.ExecuteAsync(
                    It.IsAny<CompanionContext>(), It.IsAny<CompanionTarget>(),
                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
                Times.Exactly(count));
        }

        public ValueTask DisposeAsync()
        {
            return Workspace.DisposeAsync();
        }
    }
}
