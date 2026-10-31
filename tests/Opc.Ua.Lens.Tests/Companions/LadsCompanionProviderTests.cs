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
 *
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
using Opc.Ua.LADS;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Tests.Companions;

/// <summary>
/// Tests LADS provider boundaries using the in-memory companion session.
/// </summary>
[TestFixture]
public sealed class LadsCompanionProviderTests
{
    /// <summary>
    /// The provider identifies the LADS model and exposes explicit task safety classifications.
    /// </summary>
    [Test]
    public async Task ProviderIdentifiesLadsModelAndRejectsUnknownTargetAsync()
    {
        var session = new IndustrialCompanionTestSession(includeModels: false);
        session.NamespaceUris.GetIndexOrAppend(LadsModel.NamespaceUri);
        var provider = new LadsCompanionProvider();
        NodeId device = session.Id("device");
        var target = new CompanionTarget("lads", device, "Device", "LADSDevice");
        var context = session.Context();

        Assert.That(provider.Descriptor.ModelUri, Is.EqualTo(LadsModel.NamespaceUri));
        await Assert.ThatAsync(
            async () => await provider.InspectAsync(context, target, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                .EqualTo(StatusCodes.BadNodeIdUnknown))
            .ConfigureAwait(false);
        Assert.That(session.BrowseCalls.IsEmpty, Is.True);
    }

    /// <summary>
    /// Discovery rejects a session that does not publish the LADS namespace before browsing.
    /// </summary>
    [Test]
    public async Task DiscoveryRejectsSessionsWithoutLadsNamespaceAsync()
    {
        var session = new IndustrialCompanionTestSession(includeModels: false);
        var provider = new LadsCompanionProvider();

        await Assert.ThatAsync(
            async () => await provider.DiscoverAsync(session.Context(), CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                .EqualTo(StatusCodes.BadNotSupported))
            .ConfigureAwait(false);
        Assert.That(session.BrowseCalls.IsEmpty, Is.True);
    }

    /// <summary>
    /// Unsupported operations cannot be used as an alternate path to control a LADS device.
    /// </summary>
    [Test]
    public async Task ExecuteRejectsUnlistedCommandsBeforeServiceCallsAsync()
    {
        var session = new IndustrialCompanionTestSession(includeModels: false);
        var provider = new LadsCompanionProvider();
        var target = new CompanionTarget("lads", session.Id("device"), "Device", "LADSDevice");

        await Assert.ThatAsync(
            async () => await provider.ExecuteAsync(
                session.Context(), target, "start", null, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                .EqualTo(StatusCodes.BadNotSupported))
            .ConfigureAwait(false);
        Assert.That(session.BrowseCalls.IsEmpty, Is.True);
        Assert.That(session.Calls.IsEmpty, Is.True);
    }

    /// <summary>
    /// A program is prepared by its published template name, not a NodeId.
    /// </summary>
    [Test]
    public async Task ProgramPreparationAcceptsPublishedTemplateNameAsync()
    {
        var fixture = new LadsTaskFixture();
        CompanionTaskInput prepared = await fixture.Provider.PrepareInputAsync(
            fixture.Services.Context(), fixture.Target, "start-program",
            fixture.Inputs("start-program"), CancellationToken.None).ConfigureAwait(false);

        Assert.That(prepared.Review, Is.Not.Empty);
        Assert.That(fixture.Services.Calls.IsEmpty, Is.True);
        await Assert.ThatAsync(
            async () => await fixture.Provider.PrepareInputAsync(
                fixture.Services.Context(), fixture.Target, "start-program",
                [
                    new("functionalUnitNodeId", Variant.From(fixture.Unit.ToString())),
                    new("templateId", Variant.From("Missing"))
                ],
                CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                .EqualTo(StatusCodes.BadNotFound)).ConfigureAwait(false);
    }

    /// <summary>
    /// Remote commands require deployment authorization even when no policy is configured.
    /// </summary>
    [TestCase("start-program")]
    [TestCase("function-control")]
    public async Task RemoteCommandsAreDeploymentMutationsAndDeniedByDefaultAsync(string operationId)
    {
        var fixture = new LadsTaskFixture();
        await using var workspace = new CompanionWorkspace([fixture.Provider], fixture.Services.Telemetry);
        CompanionTarget target = await fixture.InspectAsync(workspace).ConfigureAwait(false);
        CompanionInspection inspection = await workspace.InspectAsync(target).ConfigureAwait(false);
        CompanionOperation? operation = null;
        foreach (CompanionOperation candidate in inspection.Operations)
        {
            if (candidate.Id == operationId)
            {
                operation = candidate;
            }
        }

        Assert.That(operation?.Safety, Is.EqualTo(CompanionOperationSafety.DeploymentMutation));
        await Assert.ThatAsync(
            () => workspace.PrepareTaskAsync(target, operationId, fixture.Inputs(operationId)),
            Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
        Assert.That(fixture.Services.Calls.IsEmpty, Is.True);
    }

    /// <summary>
    /// Remote commands require an encrypted channel and confirmation.
    /// </summary>
    [TestCase("start-program")]
    [TestCase("function-control")]
    public async Task RemoteCommandsRequireEncryptedChannelAndConfirmationAsync(string operationId)
    {
        var fixture = new LadsTaskFixture();
        var policy = CreateGrantPolicy(() => "1");
        fixture.Endpoint.SecurityMode = MessageSecurityMode.None;
        await using var workspace = new CompanionWorkspace(
            [fixture.Provider], fixture.Services.Telemetry, deploymentPolicy: policy.Object);
        CompanionTarget target = await fixture.InspectAsync(workspace).ConfigureAwait(false);
        await Assert.ThatAsync(
            () => workspace.PrepareTaskAsync(target, operationId, fixture.Inputs(operationId)),
            Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);

        fixture.Endpoint.SecurityMode = MessageSecurityMode.SignAndEncrypt;
        CompanionOperationDraft draft = await workspace.PrepareTaskAsync(
            target, operationId, fixture.Inputs(operationId)).ConfigureAwait(false);
        await Assert.ThatAsync(
            () => workspace.ExecuteTaskAsync(draft, false, false, null),
            Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
        Assert.That(fixture.Services.Calls.IsEmpty, Is.True);
    }

    /// <summary>
    /// An authorization grant is checked again before the remote command runs.
    /// </summary>
    [TestCase("start-program")]
    [TestCase("function-control")]
    public async Task RemoteCommandsRevalidateThePolicyGrantBeforeExecutionAsync(string operationId)
    {
        var fixture = new LadsTaskFixture();
        string revision = "1";
        var policy = CreateGrantPolicy(() => revision);
        await using var workspace = new CompanionWorkspace(
            [fixture.Provider], fixture.Services.Telemetry, deploymentPolicy: policy.Object);
        CompanionTarget target = await fixture.InspectAsync(workspace).ConfigureAwait(false);
        CompanionOperationDraft draft = await workspace.PrepareTaskAsync(
            target, operationId, fixture.Inputs(operationId)).ConfigureAwait(false);
        revision = "2";

        await Assert.ThatAsync(
            () => workspace.ExecuteTaskAsync(draft, false, true, null),
            Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
        Assert.That(fixture.Services.Calls.IsEmpty, Is.True);
    }

    /// <summary>
    /// An authorized and confirmed command runs once with the reviewed input.
    /// </summary>
    [TestCase("start-program")]
    [TestCase("function-control")]
    public async Task AuthorizedRemoteCommandsExecuteOnceAsync(string operationId)
    {
        var fixture = new LadsTaskFixture();
        fixture.EnableExecution();
        var policy = CreateGrantPolicy(() => "1");
        await using var workspace = new CompanionWorkspace(
            [fixture.Provider], fixture.Services.Telemetry, deploymentPolicy: policy.Object);
        CompanionTarget target = await fixture.InspectAsync(workspace).ConfigureAwait(false);
        CompanionOperationDraft draft = await workspace.PrepareTaskAsync(
            target, operationId, fixture.Inputs(operationId)).ConfigureAwait(false);

        CompanionOperationResult result = await workspace.ExecuteTaskAsync(
            draft, false, true, null).ConfigureAwait(false);

        Assert.That(result.Summary, Does.Contain("accepted"));
        Assert.That(fixture.Services.Calls.Count, Is.EqualTo(1));
        CallMethodRequest call = fixture.Services.Calls[0];
        Assert.That(call.ObjectId, Is.EqualTo(
            operationId == "start-program" ? fixture.UnitState : fixture.FunctionState));
        if (operationId == "start-program")
        {
            Assert.That(call.InputArguments[0].TryGetValue(out string? template), Is.True);
            Assert.That(template, Is.EqualTo("Incubation"));
        }
        await Assert.ThatAsync(
            () => workspace.ExecuteTaskAsync(draft, false, true, null),
            Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
        Assert.That(fixture.Services.Calls.Count, Is.EqualTo(1));
        policy.Verify(value => value.AuthorizeAsync(
                It.IsAny<CompanionContext>(), It.IsAny<CompanionOperationDraft>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    /// <summary>
    /// Creates a policy granting the requested revision until the draft expires.
    /// </summary>
    private static Mock<ICompanionDeploymentPolicy> CreateGrantPolicy(Func<string> revision)
    {
        var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
        policy.Setup(value => value.AuthorizeAsync(
                It.IsAny<CompanionContext>(), It.IsAny<CompanionOperationDraft>(), It.IsAny<CancellationToken>()))
            .Returns((CompanionContext _, CompanionOperationDraft draft, CancellationToken _) =>
                ValueTask.FromResult(new CompanionDeploymentGrant("lads-test", revision(), draft.ExpiresAt)));
        return policy;
    }

    /// <summary>
    /// Provides a LADS device and functions through an in-memory session.
    /// </summary>
    private sealed class LadsTaskFixture
    {
        /// <summary>
        /// Creates the device, functional unit and program template.
        /// </summary>
        public LadsTaskFixture()
        {
            Services.NamespaceUris.GetIndexOrAppend(LadsModel.NamespaceUri);
            Services.NamespaceUris.GetIndexOrAppend(Opc.Ua.Machinery.Namespaces.Machinery);
            Services.Session.SetupGet(session => session.Connected).Returns(true);
            Services.Session.SetupGet(session => session.SessionId).Returns(new NodeId(1234u));
            Services.Session.SetupGet(session => session.Identity).Returns(new UserIdentity());
            Services.Session.SetupGet(session => session.Endpoint).Returns(Endpoint);
            var cache = new Mock<INodeCache>(MockBehavior.Strict);
            NodeId analogType = Services.Resolve(Opc.Ua.LADS.ObjectTypeIds.AnalogControlFunctionType);
            NodeId functionType = Services.Resolve(Opc.Ua.LADS.ObjectTypeIds.FunctionType);
            cache.Setup(value => value.IsTypeOfAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId actual, NodeId expected, CancellationToken _) =>
                    ValueTask.FromResult(actual == analogType && expected == functionType));
            Services.Session.SetupGet(session => session.NodeCache).Returns(cache.Object);

            NodeId device = Services.Id("device");
            NodeId root = Services.Resolve(Opc.Ua.Di.ObjectIds.DeviceSet);
            NodeId unitSet = Services.Id("units");
            Unit = Services.Id("unit");
            NodeId functionSet = Services.Id("functions");
            Function = Services.Id("temperature");
            UnitState = Services.Id("unit-state");
            FunctionState = Services.Id("function-state");
            NodeId manager = Services.Id("manager");
            NodeId templates = Services.Id("templates");
            NodeId template = Services.Id("incubation");
            Services.AddObject(device, Opc.Ua.LADS.ObjectTypeIds.LADSDeviceType, "Incubator");
            Services.AddObject(unitSet, Opc.Ua.ObjectTypeIds.FolderType, "FunctionalUnitSet");
            Services.AddObject(Unit, Opc.Ua.LADS.ObjectTypeIds.FunctionalUnitType, "Chamber");
            Services.AddObject(functionSet, Opc.Ua.ObjectTypeIds.FolderType, "FunctionSet");
            Services.AddObject(Function, Opc.Ua.LADS.ObjectTypeIds.AnalogControlFunctionType, "Temperature");
            Services.AddObject(manager, Opc.Ua.ObjectTypeIds.FolderType, "ProgramManager");
            Services.AddObject(templates, Opc.Ua.ObjectTypeIds.FolderType, "ProgramTemplateSet");
            Services.AddObject(template, Opc.Ua.ObjectTypeIds.FolderType, "Incubation");
            Services.AddChild(root, device);
            Services.AddChild(device, unitSet, LadsModel.NamespaceUri, "FunctionalUnitSet");
            Services.AddChild(unitSet, Unit);
            Services.AddChild(Unit, functionSet, LadsModel.NamespaceUri, "FunctionSet");
            Services.AddChild(functionSet, Function);
            Services.AddChild(Unit, manager, LadsModel.NamespaceUri, "ProgramManager");
            Services.AddChild(manager, templates, LadsModel.NamespaceUri, "ProgramTemplateSet");
            Services.AddChild(templates, template);
            Target = new CompanionTarget("lads", device, "Incubator", "LADSDevice");
        }

        /// <summary>
        /// Gets the in-memory service responses.
        /// </summary>
        public IndustrialCompanionTestSession Services { get; } = new(maximumBrowseReferences: 1000);

        /// <summary>
        /// Gets the provider under test.
        /// </summary>
        public LadsCompanionProvider Provider { get; } = new();

        /// <summary>
        /// Gets the session endpoint.
        /// </summary>
        public EndpointDescription Endpoint { get; } = new("opc.tcp://localhost:4840/Lads")
        {
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
        };

        /// <summary>
        /// Gets the device target.
        /// </summary>
        public CompanionTarget Target { get; }

        /// <summary>
        /// Gets the functional unit NodeId.
        /// </summary>
        public NodeId Unit { get; }

        /// <summary>
        /// Gets the control function NodeId.
        /// </summary>
        public NodeId Function { get; }

        /// <summary>
        /// Gets the functional unit state machine NodeId.
        /// </summary>
        public NodeId UnitState { get; }

        /// <summary>
        /// Gets the control function state machine NodeId.
        /// </summary>
        public NodeId FunctionState { get; }

        /// <summary>
        /// Gets the typed inputs offered by the selected operation.
        /// </summary>
        public ArrayOf<CompanionValue> Inputs(string operationId)
        {
            return operationId == "start-program"
                ?
                [
                    new("functionalUnitNodeId", Variant.From(Unit.ToString())),
                    new("templateId", Variant.From("Incubation"))
                ]
                :
                [
                    new("functionNodeId", Variant.From(Function.ToString())),
                    new("command", Variant.From("stop")),
                    new("value", Variant.From(string.Empty))
                ];
        }

        /// <summary>
        /// Enables the state-machine method calls used by execution tests.
        /// </summary>
        public void EnableExecution()
        {
            Services.AddObject(UnitState, Opc.Ua.ObjectTypeIds.FolderType, "FunctionalUnitState");
            Services.AddObject(FunctionState, Opc.Ua.ObjectTypeIds.FolderType, "ControlFunctionState");
            Services.AddChild(Unit, UnitState, LadsModel.NamespaceUri, "FunctionalUnitState");
            Services.AddChild(Function, FunctionState, LadsModel.NamespaceUri, "ControlFunctionState");
            Services.CallHandler = (request, _) => request.ObjectId == UnitState
                ? IndustrialCompanionTestSession.Good([Variant.From("run-1")])
                : IndustrialCompanionTestSession.Good();
        }

        /// <summary>
        /// Binds the workspace and inspects its discovered device.
        /// </summary>
        public async Task<CompanionTarget> InspectAsync(CompanionWorkspace workspace)
        {
            await workspace.BindAsync(Services.Session.Object).ConfigureAwait(false);
            ArrayOf<CompanionTarget> targets = await workspace.DiscoverAsync("lads").ConfigureAwait(false);
            Assert.That(targets.Count, Is.EqualTo(1));
            await workspace.InspectAsync(targets[0]).ConfigureAwait(false);
            return targets[0];
        }
    }
}
