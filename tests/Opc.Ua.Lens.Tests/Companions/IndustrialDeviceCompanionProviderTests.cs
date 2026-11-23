// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using Di = Opc.Ua.Di;
using static UaLens.Tests.Companions.IndustrialCompanionTestSession;

namespace UaLens.Tests.Companions;

[TestFixture]
[Category("IndustrialCompanions")]
public sealed class IndustrialDeviceCompanionProviderTests
{
    [Test]
    public async Task IdentificationPreservesLocalizedTextAndReportsOptionalAbsenceAsync()
    {
        var session = new IndustrialCompanionTestSession();
        CompanionTarget target = Device(session);
        var manufacturer = new LocalizedText("de-DE", "Beispielhersteller");
        session.AddProperty(target.NodeId, Di.Namespaces.OpcUaDi, "Manufacturer", Variant.From(manufacturer));
        session.AddProperty(target.NodeId, Di.Namespaces.OpcUaDi, "SoftwareRevision", Variant.From("2.7"));

        CompanionInspection inspection = await new DeviceCompanionProvider()
            .InspectAsync(session.Context(), target, CancellationToken.None).ConfigureAwait(false);

        Assert.That(Field(inspection.Values, "Manufacturer").TryGetValue(out LocalizedText actual), Is.True);
        Assert.That(actual, Is.EqualTo(manufacturer));
        Assert.That(Field(inspection.Values, "SoftwareRevision").TryGetValue(out string? revision), Is.True);
        Assert.That(revision, Is.EqualTo("2.7"));
        Assert.That(Field(inspection.Values, "Model").TryGetValue(out StatusCode absent), Is.True);
        Assert.That(absent, Is.EqualTo(StatusCodes.BadNotFound));
        Assert.That(session.Calls.IsEmpty, Is.True);
        Assert.That(inspection.Operations, Has.Count.EqualTo(1));
        Assert.That(inspection.Operations[0].Safety, Is.EqualTo(CompanionOperationSafety.ReadOnly));
    }

    [Test]
    public void IdentificationPropagatesPerPropertyReadFailure()
    {
        var session = new IndustrialCompanionTestSession();
        CompanionTarget target = Device(session);
        NodeId manufacturer = session.AddProperty(
            target.NodeId, Di.Namespaces.OpcUaDi, "Manufacturer", Variant.From(new LocalizedText("Vendor")));
        session.SetValue(manufacturer, Variant.Null, status: StatusCodes.BadUserAccessDenied);
        Assert.That(
            async () => await new DeviceCompanionProvider()
                .InspectAsync(session.Context(), target, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                .EqualTo(StatusCodes.BadUserAccessDenied));
        Assert.That(session.Calls.IsEmpty, Is.True);
    }

    [Test]
    public void IdentificationDoesNotTreatATranslateFailureAsOptionalAbsence()
    {
        var session = new IndustrialCompanionTestSession();
        CompanionTarget target = Device(session);
        session.TranslateHandler = (_, _) => new BrowsePathResult { StatusCode = StatusCodes.BadTimeout };
        Assert.That(
            async () => await new DeviceCompanionProvider()
                .InspectAsync(session.Context(), target, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadTimeout));
    }

    [Test]
    public void IdentificationHonorsTheConfiguredFieldLimit()
    {
        var session = new IndustrialCompanionTestSession();
        CompanionTarget target = Device(session);
        Assert.That(
            async () => await new DeviceCompanionProvider()
                .InspectAsync(session.Context(maxFields: 7), target, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public async Task SoftwareUpdateInspectionReadsTypedStateWithoutCallingPrepareAsync()
    {
        var session = new IndustrialCompanionTestSession();
        CompanionTarget target = Update(session, out NodeId prepare, out _, out _);
        CompanionInspection inspection = await new DeviceCompanionProvider()
            .InspectAsync(session.Context(), target, CancellationToken.None).ConfigureAwait(false);

        Assert.That(Field(inspection.Values, "PrepareForUpdate state").TryGetValue(out LocalizedText state), Is.True);
        Assert.That(state.Text, Is.EqualTo("Ready"));
        Assert.That(
            Field(inspection.Values, "PrepareForUpdate state machine").TryGetValue(out NodeId machine), Is.True);
        Assert.That(machine, Is.EqualTo(prepare));
        Assert.That(Field(inspection.Values, "Installation available").TryGetValue(out bool installed), Is.True);
        Assert.That(installed, Is.False);
        Assert.That(inspection.Operations.Contains(operation =>
            operation.Id == "prepare-sample" && operation.Safety == CompanionOperationSafety.DeploymentMutation),
            Is.True);
        Assert.That(inspection.Summary, Does.Contain("application-specific"));
        Assert.That(session.Calls.IsEmpty, Is.True);
    }

    [Test]
    public async Task SamplePrepareUsesTypedHelperAndReturnsTheObservedStateAsync()
    {
        var session = new IndustrialCompanionTestSession();
        CompanionTarget target = Update(session, out NodeId prepare, out NodeId current, out NodeId currentId);
        NodeId preparingId = session.Id("preparing-state");
        using var cancellation = new CancellationTokenSource();
        session.CallHandler = (request, token) =>
        {
            Assert.That(token, Is.EqualTo(cancellation.Token));
            Assert.That(request.ObjectId, Is.EqualTo(prepare));
            Assert.That(request.MethodId, Is.EqualTo(session.Resolve(
                Di.MethodIds.PrepareForUpdateStateMachineType_Prepare)));
            Assert.That(request.InputArguments.IsEmpty, Is.True);
            session.SetValue(current, Variant.From(new LocalizedText("Preparing")));
            session.SetValue(currentId, Variant.From(preparingId));
            return Good();
        };
        CompanionOperationResult result = await new DeviceCompanionProvider()
            .ExecuteAsync(session.Context(), target, "prepare-sample", null, cancellation.Token).ConfigureAwait(false);

        Assert.That(Field(result.Values, "PrepareForUpdate state").TryGetValue(out LocalizedText state), Is.True);
        Assert.That(state.Text, Is.EqualTo("Preparing"));
        Assert.That(Field(result.Values, "PrepareForUpdate state NodeId").TryGetValue(out NodeId stateId), Is.True);
        Assert.That(stateId, Is.EqualTo(preparingId));
        Assert.That(session.Calls, Has.Count.EqualTo(1));
        Assert.That(result.Summary, Does.Contain("No package"));
    }

    [Test]
    public void SamplePrepareDoesNotHideServerRejection()
    {
        var session = new IndustrialCompanionTestSession();
        CompanionTarget target = Update(session, out _, out _, out _);
        session.CallHandler = (_, _) => new CallMethodResult { StatusCode = StatusCodes.BadInvalidState };
        Assert.That(
            async () => await new DeviceCompanionProvider()
                .ExecuteAsync(session.Context(), target, "prepare-sample", null, CancellationToken.None)
                .ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadInvalidState));
        Assert.That(session.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public void AbsentPrepareIsExplicitlyUnsupportedAndNeverInvoked()
    {
        var session = new IndustrialCompanionTestSession();
        NodeId update = session.Id("update-without-prepare");
        session.AddObject(update, Di.ObjectTypeIds.SoftwareUpdateType);
        var target = new CompanionTarget("di", update, "Update", "Software update");
        Assert.That(
            async () => await new DeviceCompanionProvider()
                .ExecuteAsync(session.Context(), target, "prepare-sample", null, CancellationToken.None)
                .ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadNotSupported));
        Assert.That(session.Calls.IsEmpty, Is.True);
    }

    [TestCase("install")]
    [TestCase("upload-package")]
    [TestCase("confirm")]
    public void FirmwareLoadingAndInstallationAreNotExposed(string operation)
    {
        var session = new IndustrialCompanionTestSession();
        CompanionTarget target = Update(session, out _, out _, out _);
        Assert.That(
            async () => await new DeviceCompanionProvider()
                .ExecuteAsync(session.Context(), target, operation, null, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadNotSupported));
        Assert.That(session.Calls.IsEmpty, Is.True);
    }

    [Test]
    public void CancelledPrepareDoesNotReadOrMutateTheDevice()
    {
        var session = new IndustrialCompanionTestSession();
        CompanionTarget target = Update(session, out _, out _, out _);
        Assert.That(
            async () => await new DeviceCompanionProvider()
                .ExecuteAsync(session.Context(), target, "prepare-sample", null, new CancellationToken(true))
                .ConfigureAwait(false),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(session.Calls.IsEmpty, Is.True);
        Assert.That(session.BrowseCalls.IsEmpty, Is.True);
    }

    private static CompanionTarget Device(IndustrialCompanionTestSession session)
    {
        NodeId device = session.Id("device");
        session.AddObject(device, Di.ObjectTypeIds.DeviceType);
        return new CompanionTarget("di", device, "Device", "DI device");
    }

    private static CompanionTarget Update(
        IndustrialCompanionTestSession session,
        out NodeId prepare,
        out NodeId current,
        out NodeId currentId)
    {
        NodeId update = session.Id("update");
        prepare = session.Id("prepare");
        session.AddObject(update, Di.ObjectTypeIds.SoftwareUpdateType);
        session.AddObject(prepare, Di.ObjectTypeIds.PrepareForUpdateStateMachineType);
        session.AddChild(update, prepare, Di.Namespaces.OpcUaDi, "PrepareForUpdate");
        current = session.AddProperty(
            prepare, Namespaces.OpcUa, "CurrentState", Variant.From(new LocalizedText("Ready")));
        currentId = session.AddProperty(current, Namespaces.OpcUa, "Id", Variant.From(session.Id("ready-state")));
        return new CompanionTarget("di", update, "Update", "Software update");
    }
}
