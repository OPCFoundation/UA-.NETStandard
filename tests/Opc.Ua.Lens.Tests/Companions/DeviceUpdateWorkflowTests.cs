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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using Di = Opc.Ua.Di;
using static UaLens.Tests.Companions.IndustrialCompanionTestSession;

namespace UaLens.Tests.Companions;

[TestFixture]
public sealed class DeviceUpdateWorkflowTests
{
    [Test]
    public async Task InspectionOffersOnlyExplicitTypedUpdateActionsAndKeepsLegacyOperationsAsync()
    {
        var context = new DeviceUpdateTestContext();

        ArrayOf<CompanionTarget> discovered = await context.Provider.DiscoverAsync(
            context.Server.Context(), CancellationToken.None).ConfigureAwait(false);
        CompanionInspection inspection = await context.Provider.InspectAsync(
            context.Server.Context(), context.Target, CancellationToken.None).ConfigureAwait(false);

        Assert.That(discovered.Contains(target => target == context.Target), Is.True);
        Assert.That(inspection.Operations.ToList().Select(operation => operation.Id), Is.EquivalentTo(s_operations));
        CompanionOperation upload = inspection.Operations.ToList()
            .Single(operation => operation.Id == "upload-package-sample");
        Assert.That(upload.Safety, Is.EqualTo(CompanionOperationSafety.SampleMutation));
        Assert.That(upload.HasTypedInput, Is.True);
        Assert.That(upload.Inputs.ConvertAll(field => field.Name),
            Is.EqualTo(s_uploadFields));
        Assert.That(upload.Inputs[0].IsFileSource, Is.True);
        CompanionOperation install = inspection.Operations.ToList()
            .Single(operation => operation.Id == "install-package-sample");
        Assert.That(install.Inputs.ConvertAll(field => field.Name),
            Is.EqualTo(s_installFields));
        Assert.That(install.Inputs[2].IsMultiline, Is.True);
        Assert.That(install.Inputs[2].Required, Is.False);
        CompanionOperation confirm = inspection.Operations.ToList()
            .Single(operation => operation.Id == "confirm-sample");
        Assert.That(confirm.HasTypedInput, Is.True);
        Assert.That(confirm.Inputs.IsEmpty, Is.True);
        CompanionOperation observe = inspection.Operations.ToList()
            .Single(operation => operation.Id == "observe-update");
        Assert.That(observe.Safety, Is.EqualTo(CompanionOperationSafety.ReadOnly));
        Assert.That(observe.Inputs.ToList().Single().DataType, Is.EqualTo(BuiltInType.UInt32));
        Assert.That(inspection.Operations.ToList().Single(operation => operation.Id == "refresh").HasTypedInput,
            Is.False);
        Assert.That(inspection.Operations.ToList().Single(operation => operation.Id == "prepare-sample").HasTypedInput,
            Is.False);
        Assert.That(Field(inspection.Values, "Installation state").TryGetValue(out LocalizedText state), Is.True);
        Assert.That(state.Text, Is.EqualTo("Idle"));
        context.VerifyNoPackageRead();
        Assert.That(context.Server.Calls.IsEmpty, Is.True);
    }

    [TestCase("install-package-sample", "missing")]
    [TestCase("install-package-sample", "executable")]
    [TestCase("install-package-sample", "user")]
    [TestCase("abort-prepare-sample", "missing")]
    [TestCase("abort-prepare-sample", "executable")]
    [TestCase("resume-prepare-sample", "user")]
    [TestCase("resume-installation-sample", "missing")]
    [TestCase("resume-installation-sample", "user")]
    [TestCase("confirm-sample", "missing")]
    [TestCase("confirm-sample", "executable")]
    [TestCase("confirm-sample", "user")]
    public async Task InspectionWithholdsAMutationWithoutItsMethodAndBothPermissionsAsync(
        string operationId, string restriction)
    {
        var context = new DeviceUpdateTestContext();
        DeviceUpdateTestAction action = context.Action(operationId);
        if (restriction == "missing")
        {
            context.HideChild(action.Facet.Machine, Di.Namespaces.OpcUaDi, action.MethodName);
        }
        else
        {
            context.Server.SetValue(action.PermissionNode, Variant.From(false),
                restriction == "user" ? Attributes.UserExecutable : Attributes.Executable);
        }

        CompanionInspection inspection = await context.Provider.InspectAsync(
            context.Server.Context(), context.Target, CancellationToken.None).ConfigureAwait(false);

        Assert.That(inspection.Operations.Contains(operation => operation.Id == operationId), Is.False);
        Assert.That(inspection.Operations.Contains(operation => operation.Id == "observe-update"), Is.True);
        Assert.That(inspection.Operations.Contains(operation => operation.Id == "refresh"), Is.True);
        Assert.That(Field(inspection.Values, action.Facet.Name + " available").TryGetValue(out bool available),
            Is.True);
        Assert.That(available, Is.True);
        Assert.That(context.Server.Calls.IsEmpty, Is.True);
        context.VerifyNoPackageRead();
    }

    [TestCase("Installation", "install-package-sample")]
    [TestCase("PrepareForUpdate", "abort-prepare-sample")]
    [TestCase("Confirmation", "confirm-sample")]
    public async Task AbsentStateMachineCannotAdvertiseItsMutationAsync(string facetName, string operationId)
    {
        var context = new DeviceUpdateTestContext();
        context.HideChild(context.Target.NodeId, Di.Namespaces.OpcUaDi, facetName);

        CompanionInspection inspection = await context.Provider.InspectAsync(
            context.Server.Context(), context.Target, CancellationToken.None).ConfigureAwait(false);

        Assert.That(inspection.Operations.Contains(operation => operation.Id == operationId), Is.False);
        Assert.That(Field(inspection.Values, facetName + " available").TryGetValue(out bool available), Is.True);
        Assert.That(available, Is.False);
        Assert.That(inspection.Values.Contains(value => value.Name == facetName + " state"), Is.False);
        Assert.That(context.Server.Calls.IsEmpty, Is.True);
    }

    [TestCase("loading")]
    [TestCase("transfer")]
    [TestCase("generate-missing")]
    [TestCase("commit-missing")]
    [TestCase("generate-executable")]
    [TestCase("generate-user")]
    [TestCase("commit-executable")]
    [TestCase("commit-user")]
    public async Task UploadIsOfferedOnlyWhenItsCompleteWritableTransferWorkflowIsAvailableAsync(string restriction)
    {
        var context = new DeviceUpdateTestContext();
        switch (restriction)
        {
            case "loading":
                context.HideChild(context.Target.NodeId, Di.Namespaces.OpcUaDi, "Loading");
                break;
            case "transfer":
                context.HideChild(context.Loading, Di.Namespaces.OpcUaDi, "FileTransfer");
                break;
            case "generate-missing":
                context.HideChild(context.Transfer, Namespaces.OpcUa, "GenerateFileForWrite");
                break;
            case "commit-missing":
                context.HideChild(context.Transfer, Namespaces.OpcUa, "CloseAndCommit");
                break;
            default:
                context.Server.SetValue(
                    restriction.StartsWith("generate-", StringComparison.Ordinal) ? context.Generate : context.Commit,
                    Variant.From(false),
                    restriction.EndsWith("-user", StringComparison.Ordinal)
                        ? Attributes.UserExecutable : Attributes.Executable);
                break;
        }

        CompanionInspection inspection = await context.Provider.InspectAsync(
            context.Server.Context(), context.Target, CancellationToken.None).ConfigureAwait(false);

        Assert.That(inspection.Operations.Contains(operation => operation.Id == "upload-package-sample"), Is.False);
        Assert.That(inspection.Operations.Contains(operation => operation.Id == "install-package-sample"), Is.True);
        Assert.That(context.Server.Calls.IsEmpty, Is.True);
        context.VerifyNoPackageRead();
    }

    [TestCase("header")]
    [TestCase("status")]
    [TestCase("count")]
    [TestCase("type")]
    public async Task MalformedOrBadPermissionResponsesCannotBecomeAnExecutableOfferAsync(string fault)
    {
        StatusCode expectedStatus = fault is "header" or "status"
            ? StatusCodes.BadUserAccessDenied
            : StatusCodes.BadDecodingError;
        var context = new DeviceUpdateTestContext();
        NodeId permission = context.Action("install-package-sample").PermissionNode;
        context.Server.Session.Setup(value => value.ReadAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.Is<ArrayOf<ReadValueId>>(reads => reads.Count == 2 && reads[0].NodeId == permission),
                It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(new ReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    ServiceResult = fault == "header" ? StatusCodes.BadUserAccessDenied : StatusCodes.Good
                },
                Results = fault == "count" ? [new DataValue(Variant.From(true))] :
                [
                    new DataValue(fault == "type" ? Variant.From("true") : Variant.From(true),
                        fault == "status" ? StatusCodes.BadUserAccessDenied : StatusCodes.Good),
                    new DataValue(Variant.From(true))
                ]
            }));

        await Assert.ThatAsync(
            () => context.Provider.InspectAsync(context.Server.Context(), context.Target, CancellationToken.None)
                .AsTask(),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(expectedStatus))
            .ConfigureAwait(false);

        Assert.That(context.Server.Calls.IsEmpty, Is.True);
        context.VerifyNoPackageRead();
    }

    [TestCase("upload-package-sample")]
    [TestCase("install-package-sample")]
    [TestCase("abort-prepare-sample")]
    [TestCase("resume-prepare-sample")]
    [TestCase("resume-installation-sample")]
    [TestCase("confirm-sample")]
    [TestCase("observe-update")]
    public async Task LegacyDirectApiRejectsEveryNewTypedOperationAsync(string operationId)
    {
        var context = new DeviceUpdateTestContext();

        await Assert.ThatAsync(
            () => context.Provider.ExecuteAsync(
                context.Server.Context(), context.Target, operationId, null, CancellationToken.None).AsTask(),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadNotSupported))
            .ConfigureAwait(false);

        Assert.That(context.Server.Calls.IsEmpty, Is.True);
        context.VerifyNoPackageRead();
    }

    [TestCase("install-package-sample")]
    [TestCase("abort-prepare-sample")]
    [TestCase("resume-prepare-sample")]
    [TestCase("resume-installation-sample")]
    [TestCase("confirm-sample")]
    public async Task PreparedCauseUsesItsExactTypedHelperAndReturnsTheActualPostCallStateAsync(string operationId)
    {
        var context = new DeviceUpdateTestContext();
        DeviceUpdateTestAction action = context.Action(operationId);
        using var cancellation = new CancellationTokenSource();
        CompanionTaskInput input = await context.Provider.PrepareInputAsync(
            context.Server.Context(), context.Target, operationId, context.Inputs(operationId), cancellation.Token)
            .ConfigureAwait(false);

        Assert.That(input.Review, Does.Contain(
            operationId == "install-package-sample" ? "2.7.9" : action.DisplayName));
        Assert.That(context.Server.Calls.IsEmpty, Is.True);
        context.VerifyNoPackageRead();
        NodeId actualState = context.Server.Id("actual-device-state");
        context.Server.CallHandler = (request, token) =>
        {
            Assert.That(token, Is.EqualTo(cancellation.Token));
            Assert.That(request.ObjectId, Is.EqualTo(action.Facet.Machine));
            Assert.That(request.MethodId, Is.EqualTo(action.MethodId));
            if (operationId == "install-package-sample")
            {
                AssertInstallArguments(request, "urn:manufacturer:requested", "2.7.9", ["patch-1", "patch-2"]);
            }
            else
            {
                Assert.That(request.InputArguments.IsEmpty, Is.True);
            }
            context.SetState(action.Facet, "Still processing", actualState);
            return Good();
        };
        var reports = new List<CompanionTaskProgress>();
        Mock<IProgress<CompanionTaskProgress>> progress = Progress(reports);

        CompanionOperationResult result = await context.Provider.ExecutePreparedAsync(
            context.Server.Context(), context.Target, operationId, input, progress.Object, cancellation.Token)
            .ConfigureAwait(false);

        Assert.That(result.Summary, Does.Contain("read from the device").And.Contain("not inferred"));
        AssertState(result.Values, action.Facet, "Still processing", actualState);
        Assert.That(context.Server.Calls, Has.Count.EqualTo(1));
        Assert.That(reports.Select(report => report.Phase), Is.EqualTo(new[] { "Calling " + action.DisplayName }));
        Assert.That(reports.Single().Percent, Is.Null);
        context.VerifyNoPackageRead();
    }

    [TestCase("null-field")]
    [TestCase("missing")]
    [TestCase("extra")]
    [TestCase("duplicate")]
    [TestCase("reordered")]
    [TestCase("wrong-name")]
    [TestCase("wrong-type")]
    [TestCase("array")]
    [TestCase("null-variant")]
    [TestCase("null-string")]
    public async Task DirectPreparationRejectsMalformedTypedFieldsWithoutMutationAsync(string fault)
    {
        var context = new DeviceUpdateTestContext();
        List<CompanionValue> inputs = context.Inputs("install-package-sample").ToList();
        switch (fault)
        {
            case "null-field":
                inputs[0] = null!;
                break;
            case "missing":
                inputs.RemoveAt(0);
                break;
            case "extra":
                inputs.Add(new CompanionValue("extra", Variant.From("unused")));
                break;
            case "duplicate":
                inputs[1] = inputs[0];
                break;
            case "reordered":
                (inputs[0], inputs[1]) = (inputs[1], inputs[0]);
                break;
            case "wrong-name":
                inputs[0] = inputs[0] with { Name = "Manufacturer" };
                break;
            case "wrong-type":
                inputs[0] = inputs[0] with { Value = Variant.From(17u) };
                break;
            case "array":
                inputs[0] = inputs[0] with { Value = Variant.From((ArrayOf<string>)["urn:manufacturer:requested"]) };
                break;
            case "null-variant":
                inputs[0] = inputs[0] with { Value = Variant.Null };
                break;
            case "null-string":
                inputs[0] = inputs[0] with { Value = Variant.From((string)null!) };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault));
        }

        await Assert.ThatAsync(
            () => context.Provider.PrepareInputAsync(context.Server.Context(), context.Target,
                "install-package-sample", [.. inputs], CancellationToken.None).AsTask(),
            Throws.InstanceOf<ArgumentException>()).ConfigureAwait(false);

        Assert.That(context.Server.Calls.IsEmpty, Is.True);
        context.VerifyNoPackageRead();
    }

    [TestCase("manufacturer", "relative-manufacturer")]
    [TestCase("manufacturer", "https://user@vendor.example/package")]
    [TestCase("manufacturer", "https://vendor.example/package?request=1")]
    [TestCase("manufacturer", "https://vendor.example/package#revision")]
    [TestCase("manufacturer", " ")]
    [TestCase("revision", "")]
    [TestCase("revision", " \t")]
    [TestCase("sha256", "")]
    [TestCase("sha256", "1234")]
    public async Task InvalidInstallFieldsFailDuringPreparationWithoutCallingAnyMethodAsync(
        string fieldName, string text)
    {
        var context = new DeviceUpdateTestContext();
        ArrayOf<CompanionValue> inputs = Replace(context.Inputs("install-package-sample"), fieldName, text);

        await Assert.ThatAsync(
            () => context.Provider.PrepareInputAsync(context.Server.Context(), context.Target,
                "install-package-sample", inputs, CancellationToken.None).AsTask(),
            Throws.ArgumentException).ConfigureAwait(false);

        Assert.That(context.Server.Calls.IsEmpty, Is.True);
        context.VerifyNoPackageRead();
    }

    [TestCase("manufacturer", 2048, true)]
    [TestCase("manufacturer", 2049, false)]
    [TestCase("revision", 128, true)]
    [TestCase("revision", 129, false)]
    [TestCase("patch", 128, true)]
    [TestCase("patch", 129, false)]
    [TestCase("patch-count", 32, true)]
    [TestCase("patch-count", 33, false)]
    [TestCase("patch-text", 4095, true)]
    [TestCase("patch-text", 4096, true)]
    [TestCase("patch-text", 4097, false)]
    public async Task InstallFieldAndPatchLimitsUseTheExactInclusiveBoundariesAsync(
        string field, int limit, bool accepted)
    {
        var context = new DeviceUpdateTestContext();
        string name = field.StartsWith("patch", StringComparison.Ordinal) ? "patches" : field;
        string text = field switch
        {
            "manufacturer" => "urn:manufacturer:" + new string('a', limit - "urn:manufacturer:".Length),
            "revision" or "patch" => new string('r', limit),
            "patch-count" => string.Join('\n', Enumerable.Range(0, limit).Select(index =>
                "patch-" + index.ToString(CultureInfo.InvariantCulture))),
            "patch-text" => string.Join('\n', Enumerable.Range(0, 32).Select(index =>
                new string('p', 127 + (index < limit - 4095 ? 1 : 0)))),
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        ArrayOf<CompanionValue> inputs = Replace(context.Inputs("install-package-sample"), name, text);
        if (field == "patch-text")
        {
            Assert.That(text, Has.Length.EqualTo(limit));
        }

        if (accepted)
        {
            CompanionTaskInput prepared = await context.Provider.PrepareInputAsync(
                context.Server.Context(), context.Target, "install-package-sample", inputs, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(prepared.Review, Does.Contain("Install revision").And.Contain("SHA-256"));
            DeviceUpdateTestAction action = context.Action("install-package-sample");
            context.Server.CallHandler = (request, _) =>
            {
                Assert.That(request.ObjectId, Is.EqualTo(action.Facet.Machine));
                Assert.That(request.MethodId, Is.EqualTo(action.MethodId));
                AssertInstallArguments(request,
                    field == "manufacturer" ? text : "urn:manufacturer:requested",
                    field == "revision" ? text : "2.7.9",
                    name == "patches" ? new ArrayOf<string>(text.Split('\n')) : ["patch-1", "patch-2"]);
                return Good();
            };
            CompanionOperationResult result = await context.Provider.ExecutePreparedAsync(
                context.Server.Context(), context.Target, "install-package-sample", prepared, null,
                CancellationToken.None)
                .ConfigureAwait(false);
            AssertState(result.Values, action.Facet, "Idle", action.Facet.InitialState);
            Assert.That(context.Server.Calls, Has.Count.EqualTo(1));
        }
        else
        {
            await Assert.ThatAsync(
                () => context.Provider.PrepareInputAsync(context.Server.Context(), context.Target,
                    "install-package-sample", inputs, CancellationToken.None).AsTask(),
                Throws.ArgumentException).ConfigureAwait(false);
            Assert.That(context.Server.Calls.IsEmpty, Is.True);
        }
        context.VerifyNoPackageRead();
    }

    [TestCase("")]
    [TestCase(" \r\n \n")]
    public async Task EmptyOptionalPatchTextBecomesAnExplicitEmptyStringArrayAsync(string patches)
    {
        var context = new DeviceUpdateTestContext();
        DeviceUpdateTestAction action = context.Action("install-package-sample");
        CompanionTaskInput prepared = await context.Provider.PrepareInputAsync(
            context.Server.Context(), context.Target, "install-package-sample",
            Replace(context.Inputs("install-package-sample"), "patches", patches), CancellationToken.None)
            .ConfigureAwait(false);
        context.Server.CallHandler = (request, _) =>
        {
            Assert.That(request.MethodId, Is.EqualTo(action.MethodId));
            AssertInstallArguments(request, "urn:manufacturer:requested", "2.7.9", []);
            return Good();
        };

        CompanionOperationResult result = await context.Provider.ExecutePreparedAsync(
            context.Server.Context(), context.Target, "install-package-sample", prepared, null, CancellationToken.None)
            .ConfigureAwait(false);

        AssertState(result.Values, action.Facet, "Idle", action.Facet.InitialState);
        Assert.That(context.Server.Calls, Has.Count.EqualTo(1));
    }

    [TestCase("null")]
    [TestCase("forged")]
    [TestCase("other-operation")]
    public async Task ExecutePreparedRejectsUnpreparedOrMismatchedDomainInputsAsync(string fault)
    {
        var context = new DeviceUpdateTestContext();
        CompanionTaskInput input = fault switch
        {
            "null" => null!,
            "forged" => new CompanionTestTaskInput("Install requested by a fabricated review."),
            "other-operation" => await context.Provider.PrepareInputAsync(
                context.Server.Context(), context.Target, "confirm-sample", [], CancellationToken.None)
                .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };

        await Assert.ThatAsync(
            () => context.Provider.ExecutePreparedAsync(context.Server.Context(), context.Target,
                "install-package-sample", input, null, CancellationToken.None).AsTask(),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("input")).ConfigureAwait(false);

        Assert.That(context.Server.Calls.IsEmpty, Is.True);
        context.VerifyNoPackageRead();
    }

    [TestCase("state")]
    [TestCase("machine")]
    [TestCase("missing-machine")]
    public async Task ChangedStateIdentityOrMachineCannotExecuteAPreparedCauseAsync(string change)
    {
        var context = new DeviceUpdateTestContext();
        DeviceUpdateTestAction action = context.Action("install-package-sample");
        CompanionTaskInput prepared = await context.PrepareAsync("install-package-sample").ConfigureAwait(false);
        switch (change)
        {
            case "state":
                context.SetState(action.Facet, "Idle", context.Server.Id("different-id-with-the-same-state-name"));
                break;
            case "machine":
                context.ReplaceFacet(action.Facet);
                break;
            case "missing-machine":
                context.HideChild(context.Target.NodeId, Di.Namespaces.OpcUaDi, action.Facet.Name);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }

        await Assert.ThatAsync(
            () => context.Provider.ExecutePreparedAsync(context.Server.Context(), context.Target,
                "install-package-sample", prepared, null, CancellationToken.None).AsTask(),
            Throws.InvalidOperationException).ConfigureAwait(false);

        Assert.That(context.Server.Calls.IsEmpty, Is.True);
    }

    [TestCase("executable", false)]
    [TestCase("user", false)]
    [TestCase("missing", false)]
    [TestCase("executable", true)]
    [TestCase("user", true)]
    [TestCase("missing", true)]
    public async Task MethodPermissionIsRequiredDuringBothPreparationAndExecutionAsync(
        string permission, bool afterPreparation)
    {
        var context = new DeviceUpdateTestContext();
        DeviceUpdateTestAction action = context.Action("install-package-sample");
        CompanionTaskInput? prepared = afterPreparation
            ? await context.PrepareAsync("install-package-sample").ConfigureAwait(false)
            : null;
        if (permission == "missing")
        {
            context.HideChild(action.Facet.Machine, Di.Namespaces.OpcUaDi, action.MethodName);
        }
        else
        {
            context.Server.SetValue(action.PermissionNode, Variant.From(false),
                permission == "user" ? Attributes.UserExecutable : Attributes.Executable);
        }

        await Assert.ThatAsync(
            async () =>
            {
                if (afterPreparation)
                {
                    await context.Provider.ExecutePreparedAsync(context.Server.Context(), context.Target,
                        "install-package-sample", prepared!, null, CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    await context.PrepareAsync("install-package-sample").ConfigureAwait(false);
                }
            },
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadNotExecutable))
            .ConfigureAwait(false);

        Assert.That(context.Server.Calls.IsEmpty, Is.True);
        context.VerifyNoPackageRead();
    }

    [TestCase("foreign-provider")]
    [TestCase("null-node")]
    [TestCase("wrong-type")]
    [TestCase("not-object")]
    [TestCase("namespace")]
    public async Task TargetAndNamespaceRechecksPreventAStalePreparedMutationAsync(string change)
    {
        var context = new DeviceUpdateTestContext();
        CompanionTaskInput prepared = await context.PrepareAsync("install-package-sample").ConfigureAwait(false);
        CompanionTarget target = context.Target;
        switch (change)
        {
            case "foreign-provider":
                target = target with { ProviderId = "other" };
                break;
            case "null-node":
                target = target with { NodeId = NodeId.Null };
                break;
            case "wrong-type":
                context.Server.AddObject(target.NodeId, Di.ObjectTypeIds.DeviceType);
                break;
            case "not-object":
                context.Server.SetValue(target.NodeId, Variant.From((int)NodeClass.Variable), Attributes.NodeClass);
                break;
            case "namespace":
                context.Server.NamespaceUris.Update([Namespaces.OpcUa, "urn:replaced-model"]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }

        await Assert.ThatAsync(
            () => context.Provider.ExecutePreparedAsync(context.Server.Context(), target,
                "install-package-sample", prepared, null, CancellationToken.None).AsTask(),
            change is "foreign-provider" or "null-node"
                ? Throws.ArgumentException : Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);

        Assert.That(context.Server.Calls.IsEmpty, Is.True);
        context.VerifyNoPackageRead();
    }

    [TestCase("install-package-sample")]
    [TestCase("abort-prepare-sample")]
    [TestCase("resume-prepare-sample")]
    [TestCase("resume-installation-sample")]
    [TestCase("confirm-sample")]
    public async Task ARejectedCauseCannotReturnAnInferredSuccessfulUpdateAsync(string operationId)
    {
        var context = new DeviceUpdateTestContext();
        CompanionTaskInput prepared = await context.PrepareAsync(operationId).ConfigureAwait(false);
        DeviceUpdateTestAction action = context.Action(operationId);
        context.Server.CallHandler = (request, _) =>
        {
            Assert.That(request.MethodId, Is.EqualTo(action.MethodId));
            return new CallMethodResult { StatusCode = StatusCodes.BadInvalidState };
        };

        await Assert.ThatAsync(
            () => context.Provider.ExecutePreparedAsync(context.Server.Context(), context.Target,
                operationId, prepared, null, CancellationToken.None).AsTask(),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadInvalidState))
            .ConfigureAwait(false);

        Assert.That(context.Server.Calls, Has.Count.EqualTo(1));
        Assert.That(context.Server.Read(new ReadValueId
        {
            NodeId = action.Facet.CurrentId,
            AttributeId = Attributes.Value
        }).WrappedValue.TryGetValue(out NodeId state), Is.True);
        Assert.That(state, Is.EqualTo(action.Facet.InitialState));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task BadStateReadBeforeOrAfterTheCauseIsNotReportedAsSuccessAsync(bool afterCall)
    {
        var context = new DeviceUpdateTestContext();
        DeviceUpdateTestAction action = context.Action("install-package-sample");
        CompanionTaskInput prepared = await context.PrepareAsync("install-package-sample").ConfigureAwait(false);
        if (afterCall)
        {
            context.Server.CallHandler = (request, _) =>
            {
                Assert.That(request.MethodId, Is.EqualTo(action.MethodId));
                context.Server.SetValue(action.Facet.Current, Variant.From(new LocalizedText("Idle")),
                    status: StatusCodes.BadCommunicationError);
                return Good();
            };
        }
        else
        {
            context.Server.SetValue(action.Facet.Current, Variant.From(new LocalizedText("Idle")),
                status: StatusCodes.BadCommunicationError);
        }

        await Assert.ThatAsync(
            () => context.Provider.ExecutePreparedAsync(context.Server.Context(), context.Target,
                "install-package-sample", prepared, null, CancellationToken.None).AsTask(),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                .EqualTo(StatusCodes.BadCommunicationError))
            .ConfigureAwait(false);

        Assert.That(context.Server.Calls.Count, Is.EqualTo(afterCall ? 1 : 0));
    }

    [Test]
    public async Task InstallPreparationRetainsExactArgumentsWhenTheCallerReplacesItsInputArrayAsync()
    {
        var context = new DeviceUpdateTestContext();
        CompanionValue[] values = [.. context.Inputs("install-package-sample")];
        CompanionTaskInput prepared = await context.Provider.PrepareInputAsync(
            context.Server.Context(), context.Target, "install-package-sample",
            ArrayOf.Wrapped(values), CancellationToken.None).ConfigureAwait(false);
        values[0] = new CompanionValue("manufacturer", Variant.From("urn:manufacturer:other"));
        values[1] = new CompanionValue("revision", Variant.From("9.9.9"));
        values[2] = new CompanionValue("patches", Variant.From("different-patch"));
        values[3] = new CompanionValue("sha256", Variant.From(new string('0', 64)));
        DeviceUpdateTestAction action = context.Action("install-package-sample");
        context.Server.CallHandler = (request, _) =>
        {
            AssertInstallArguments(request, "urn:manufacturer:requested", "2.7.9", ["patch-1", "patch-2"]);
            return Good();
        };

        CompanionOperationResult result = await context.Provider.ExecutePreparedAsync(
            context.Server.Context(), context.Target, "install-package-sample", prepared, null, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(prepared.Review, Does.Contain("2.7.9").And.Not.Contain("9.9.9"));
        AssertState(result.Values, action.Facet, "Idle", action.Facet.InitialState);
        Assert.That(context.Server.Calls.ToList().Single().MethodId, Is.EqualTo(action.MethodId));
    }

    [TestCase("a")]
    [TestCase("package-42")]
    [TestCase("abcdefghijklmnopqrstuvwxyz012345")]
    public async Task VerifiedUploadPinsBytesHashAndSampleIdThenCommitsWithoutInstallingAsync(string suffix)
    {
        var context = new DeviceUpdateTestContext();
        using var cancellation = new CancellationTokenSource();
        ArrayOf<CompanionValue> inputs = Replace(context.Inputs("upload-package-sample"), "packageId", suffix);
        CompanionTaskInput prepared = await context.Provider.PrepareInputAsync(
            context.Server.Context(), context.Target, "upload-package-sample", inputs, cancellation.Token)
            .ConfigureAwait(false);

        Assert.That(DeviceCompanionProvider.MaximumPackageBytes, Is.EqualTo(67108864));
        Assert.That(prepared.Review, Does.Contain("3 verified bytes").And.Contain("ualens-sample-" + suffix));
        Assert.That(prepared.Review, Does.Contain(DeviceUpdateTestContext.PackageDigest));
        Assert.That(prepared.Review, Does.Contain("No installation"));
        Assert.That(prepared.Review, Does.Not.Contain(context.PackagePath));
        context.Packages.Verify(value => value.ReadAsync(context.PackagePath, 67108864, cancellation.Token),
            Times.Once);
        Assert.That(context.Server.Calls.IsEmpty, Is.True);
        var reports = new List<CompanionTaskProgress>();

        CompanionOperationResult result = await context.Provider.ExecutePreparedAsync(
            context.Server.Context(), context.Target, "upload-package-sample", prepared, Progress(reports).Object,
            cancellation.Token).ConfigureAwait(false);

        AssertUpload(context, result, "ualens-sample-" + suffix, [0x61, 0x62, 0x63]);
        Assert.That(result.Summary, Does.Contain("No install").And.Contain("must be observed"));
        Assert.That(reports.Single().Phase, Does.Contain("verified package").And.Contain("no installation"));
        context.Packages.Verify(value => value.ReadAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(context.Server.Calls.ToList().All(
            call => call.MethodId != context.Action("install-package-sample").MethodId),
            Is.True);
    }

    [TestCase("sha256-length")]
    [TestCase("sha256-first")]
    [TestCase("sha256-last")]
    [TestCase("suffix-empty")]
    [TestCase("suffix-long")]
    [TestCase("suffix-uppercase")]
    [TestCase("suffix-space")]
    [TestCase("suffix-path")]
    [TestCase("path-empty")]
    [TestCase("path-long")]
    public async Task InvalidUploadFormFailsBeforeReadingAFileOrGeneratingATransferAsync(string fault)
    {
        var context = new DeviceUpdateTestContext();
        (string field, string value) = fault switch
        {
            "sha256-length" => ("sha256", new string('a', 63)),
            "sha256-first" => ("sha256", "g" + new string('a', 63)),
            "sha256-last" => ("sha256", new string('a', 63) + "g"),
            "suffix-empty" => ("packageId", string.Empty),
            "suffix-long" => ("packageId", new string('a', 33)),
            "suffix-uppercase" => ("packageId", "Package"),
            "suffix-space" => ("packageId", "package name"),
            "suffix-path" => ("packageId", @"..\package"),
            "path-empty" => ("path", " \t"),
            "path-long" => ("path", new string('p', 4097)),
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };

        await Assert.ThatAsync(
            () => context.Provider.PrepareInputAsync(context.Server.Context(), context.Target, "upload-package-sample",
                Replace(context.Inputs("upload-package-sample"), field, value), CancellationToken.None).AsTask(),
            Throws.ArgumentException).ConfigureAwait(false);

        context.VerifyNoPackageRead();
        Assert.That(context.Server.Calls.IsEmpty, Is.True);
    }

    [Test]
    public async Task IncorrectExpectedDigestPreventsGenerateFileForWriteAsync()
    {
        var context = new DeviceUpdateTestContext();

        await Assert.ThatAsync(
            () => context.Provider.PrepareInputAsync(context.Server.Context(), context.Target, "upload-package-sample",
                Replace(context.Inputs("upload-package-sample"), "sha256", new string('0', 64)),
                CancellationToken.None).AsTask(),
            Throws.ArgumentException.With.Message.Contains("SHA-256")).ConfigureAwait(false);

        context.Packages.Verify(value => value.ReadAsync(
            context.PackagePath, 67108864, It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(context.Server.Calls.IsEmpty, Is.True);
        Assert.That(context.Uploaded, Is.Empty);
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(67108864, true)]
    [TestCase(67108865, false)]
    public async Task ProviderIndependentlyEnforcesNonemptyPackagesThroughExactlySixtyFourMiBAsync(
        int length, bool accepted)
    {
        var context = new DeviceUpdateTestContext { Package = new ByteString(new byte[length]) };
        string digest = Convert.ToHexString(SHA256.HashData(context.Package.Span));
        ArrayOf<CompanionValue> fields = Replace(context.Inputs("upload-package-sample"), "sha256", digest);

        if (accepted)
        {
            CompanionTaskInput input = await context.Provider.PrepareInputAsync(
                context.Server.Context(), context.Target, "upload-package-sample", fields, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(input.Review,
                Does.Contain(length.ToString(CultureInfo.InvariantCulture) + " verified bytes").And.Contain(digest));
        }
        else
        {
            await Assert.ThatAsync(
                () => context.Provider.PrepareInputAsync(context.Server.Context(), context.Target,
                    "upload-package-sample", fields, CancellationToken.None).AsTask(),
                Throws.ArgumentException).ConfigureAwait(false);
        }

        context.Packages.Verify(value => value.ReadAsync(
            context.PackagePath, 67108864, It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(context.Server.Calls.IsEmpty, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PackageReadFailureOrCancellationCannotGenerateAServerFileAsync(bool cancel)
    {
        var context = new DeviceUpdateTestContext();
        using var cancellation = new CancellationTokenSource();
        context.Packages.Setup(value => value.ReadAsync(context.PackagePath, 67108864, cancellation.Token))
            .Returns(() =>
            {
                if (cancel)
                {
                    cancellation.Cancel();
                    return ValueTask.FromCanceled<ByteString>(cancellation.Token);
                }
                return ValueTask.FromException<ByteString>(new IOException("Selected package could not be read."));
            });

        await Assert.ThatAsync(
            () => context.Provider.PrepareInputAsync(context.Server.Context(), context.Target,
                "upload-package-sample", context.Inputs("upload-package-sample"), cancellation.Token).AsTask(),
            cancel ? Throws.InstanceOf<OperationCanceledException>() : Throws.TypeOf<IOException>())
            .ConfigureAwait(false);

        Assert.That(context.Server.Calls.IsEmpty, Is.True);
        Assert.That(context.Uploaded, Is.Empty);
        context.Packages.Verify(value => value.ReadAsync(context.PackagePath, 67108864, cancellation.Token),
            Times.Once);
    }

    [Test]
    public async Task UploadAfterOnDiskReplacementUsesOnlyTheVerifiedPreparedSnapshotAsync()
    {
        using var file = new CompanionTemporaryPackage();
        await File.WriteAllBytesAsync(file.Path, new byte[] { 0x61, 0x62, 0x63 }).ConfigureAwait(false);
        var context = new DeviceUpdateTestContext(new CompanionPackageReader());
        ArrayOf<CompanionValue> inputs = Replace(context.Inputs("upload-package-sample"), "path", file.Path);
        CompanionTaskInput prepared = await context.Provider.PrepareInputAsync(
            context.Server.Context(), context.Target, "upload-package-sample", inputs, CancellationToken.None)
            .ConfigureAwait(false);
        await File.WriteAllBytesAsync(file.Path, new byte[] { 0x10, 0x20, 0x30, 0x40 }).ConfigureAwait(false);

        CompanionOperationResult result = await context.Provider.ExecutePreparedAsync(
            context.Server.Context(), context.Target, "upload-package-sample", prepared, null, CancellationToken.None)
            .ConfigureAwait(false);

        AssertUpload(context, result, "ualens-sample-package-1", [0x61, 0x62, 0x63]);
        byte[] currentFile = await File.ReadAllBytesAsync(file.Path).ConfigureAwait(false);
        Assert.That(currentFile, Is.EqualTo(new byte[] { 0x10, 0x20, 0x30, 0x40 }));
        Assert.That(prepared.Review, Does.Not.Contain(file.Path));
    }

    [Test]
    public async Task UploadOwnsItsSnapshotEvenWhenTheInjectedReaderReturnsSharedStorageAsync()
    {
        byte[] storage = [0x61, 0x62, 0x63];
        var context = new DeviceUpdateTestContext { Package = new ByteString(storage) };
        CompanionTaskInput prepared = await context.PrepareAsync("upload-package-sample").ConfigureAwait(false);
        storage[0] = 0x7A;
        context.Package = new ByteString(new byte[] { 0x10, 0x20 });

        CompanionOperationResult result = await context.Provider.ExecutePreparedAsync(
            context.Server.Context(), context.Target, "upload-package-sample", prepared, null, CancellationToken.None)
            .ConfigureAwait(false);

        AssertUpload(context, result, "ualens-sample-package-1", [0x61, 0x62, 0x63]);
        Assert.That(storage, Is.EqualTo(new byte[] { 0x7A, 0x62, 0x63 }));
        context.Packages.Verify(value => value.ReadAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase("target")]
    [TestCase("generate")]
    [TestCase("commit")]
    public async Task UploadRechecksTheTransferIdentityAndBothMethodPermissionsAsync(string change)
    {
        var context = new DeviceUpdateTestContext();
        CompanionTaskInput prepared = await context.PrepareAsync("upload-package-sample").ConfigureAwait(false);
        if (change == "target")
        {
            NodeId replacement = context.Server.AddProperty(
                context.Loading, Di.Namespaces.OpcUaDi, "FileTransfer", Variant.Null);
            context.AddMethod(replacement, Namespaces.OpcUa, "GenerateFileForWrite");
            context.AddMethod(replacement, Namespaces.OpcUa, "CloseAndCommit");
        }
        else
        {
            context.Server.SetValue(change == "generate" ? context.Generate : context.Commit,
                Variant.From(false), Attributes.UserExecutable);
        }

        await Assert.ThatAsync(
            () => context.Provider.ExecutePreparedAsync(context.Server.Context(), context.Target,
                "upload-package-sample", prepared, null, CancellationToken.None).AsTask(),
            change == "target"
                ? Throws.InvalidOperationException
                : Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadNotExecutable))
            .ConfigureAwait(false);

        Assert.That(context.Server.Calls.IsEmpty, Is.True);
        context.Packages.Verify(value => value.ReadAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase("GenerateFileForWrite")]
    [TestCase("Open")]
    [TestCase("Write")]
    [TestCase("Close")]
    [TestCase("CloseAndCommit")]
    public async Task FailedUploadCallsPropagateTheirStatusAndNeverProceedToInstallationAsync(string method)
    {
        var context = new DeviceUpdateTestContext { FailingUploadMethod = method };
        CompanionTaskInput prepared = await context.PrepareAsync("upload-package-sample").ConfigureAwait(false);

        await Assert.ThatAsync(
            () => context.Provider.ExecutePreparedAsync(context.Server.Context(), context.Target,
                "upload-package-sample", prepared, null, CancellationToken.None).AsTask(),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadInvalidState))
            .ConfigureAwait(false);

        string[] expected = method switch
        {
            "GenerateFileForWrite" => ["GenerateFileForWrite"],
            "Open" => ["GenerateFileForWrite", "Open"],
            "Write" => ["GenerateFileForWrite", "Open", "Write", "Close"],
            "Close" => ["GenerateFileForWrite", "Open", "Write", "Close", "Close"],
            "CloseAndCommit" => ["GenerateFileForWrite", "Open", "Write", "Close", "CloseAndCommit"],
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };
        Assert.That(context.UploadCallNames, Is.EqualTo(expected));
        Assert.That(context.CommitSucceeded, Is.False);
        Assert.That(context.Server.Calls.ToList().All(
            call => call.MethodId != context.Action("install-package-sample").MethodId),
            Is.True);
    }

    [TestCase("generate")]
    [TestCase("commit")]
    public async Task InvalidUploadMethodOutputCannotBeReportedAsACompletedUploadAsync(string invalidOutput)
    {
        var context = new DeviceUpdateTestContext { InvalidUploadOutput = invalidOutput };
        CompanionTaskInput prepared = await context.PrepareAsync("upload-package-sample").ConfigureAwait(false);

        await Assert.ThatAsync(
            () => context.Provider.ExecutePreparedAsync(context.Server.Context(), context.Target,
                "upload-package-sample", prepared, null, CancellationToken.None).AsTask(),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadDecodingError))
            .ConfigureAwait(false);

        Assert.That(context.Server.Calls.Count, Is.EqualTo(invalidOutput == "generate" ? 1 : 5));
        Assert.That(context.Server.Calls.ToList().All(
            call => call.MethodId != context.Action("install-package-sample").MethodId),
            Is.True);
    }

    [TestCase("opc.tcp://localhost:4840/Sample", false)]
    [TestCase("opc.tcp://remote.example:4840/Server", false)]
    [TestCase("opc.tcp://remote.example:4840/Server", true)]
    public async Task ActualDeviceInstallStillRequiresWorkspaceSampleAuthorizationAsync(string endpoint, bool confirmed)
    {
        var context = new DeviceUpdateTestContext();
        context.Endpoint.EndpointUrl = endpoint;
        var workspace = new CompanionWorkspace([context.Provider], context.Server.Telemetry);
        await using (workspace.ConfigureAwait(false))
        {
            await workspace.BindAsync(context.Server.Session.Object).ConfigureAwait(false);
            await workspace.DiscoverAsync("di").ConfigureAwait(false);
            await workspace.InspectAsync(context.Target).ConfigureAwait(false);
            CompanionOperationDraft draft = await workspace.PrepareTaskAsync(
                context.Target, "install-package-sample", context.Inputs("install-package-sample"))
                .ConfigureAwait(false);
            Assert.That(draft.TaskInput?.Review, Does.Contain("2.7.9"));

            await Assert.ThatAsync(
                () => workspace.ExecuteTaskAsync(draft, confirmed, null),
                Throws.InvalidOperationException).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => workspace.ExecuteAsync(context.Target, "install-package-sample", null, true),
                Throws.InvalidOperationException).ConfigureAwait(false);

            Assert.That(context.Server.Calls.IsEmpty, Is.True);
            context.VerifyNoPackageRead();
        }
    }

    [TestCase(0u)]
    [TestCase(61u)]
    [TestCase(uint.MaxValue)]
    public async Task ObservationDurationOutsideOneThroughSixtySecondsIsRejectedBeforePollingAsync(uint seconds)
    {
        var clock = new DeviceUpdateTestClock();
        var context = new DeviceUpdateTestContext(timeProvider: clock.Provider.Object);

        await Assert.ThatAsync(
            () => context.Provider.PrepareInputAsync(context.Server.Context(), context.Target, "observe-update",
                [new("seconds", Variant.From(seconds))], CancellationToken.None).AsTask(),
            Throws.ArgumentException.With.Message.Contains("1 through 60")).ConfigureAwait(false);

        Assert.That(clock.TimerCount, Is.Zero);
        Assert.That(context.Server.Calls.IsEmpty, Is.True);
        context.VerifyNoPackageRead();
    }

    [TestCase(1u)]
    [TestCase(2u)]
    [TestCase(60u)]
    public async Task ObservationUsesVirtualTimeAndReturnsTheFinalActualSnapshotWithoutMutationAsync(uint seconds)
    {
        var clock = new DeviceUpdateTestClock();
        var context = new DeviceUpdateTestContext(timeProvider: clock.Provider.Object);
        DeviceUpdateTestFacet installation = context.Action("install-package-sample").Facet;
        clock.Tick = tick => context.SetState(installation,
            "Observed " + tick.ToString(CultureInfo.InvariantCulture), context.Server.Id("observed-" + tick));
        CompanionTaskInput prepared = await context.Provider.PrepareInputAsync(
            context.Server.Context(), context.Target, "observe-update",
            [new("seconds", Variant.From(seconds))], CancellationToken.None).ConfigureAwait(false);
        Assert.That(clock.TimerCount, Is.Zero);
        Assert.That(prepared.Review, Does.Contain("no device mutation"));
        var reports = new List<CompanionTaskProgress>();

        CompanionOperationResult result = await context.Provider.ExecutePreparedAsync(
            context.Server.Context(), context.Target, "observe-update", prepared, Progress(reports).Object,
            CancellationToken.None).ConfigureAwait(false);

        Assert.That(clock.TimerCount, Is.EqualTo(seconds));
        Assert.That(result.Summary,
            Does.Contain((seconds + 1).ToString(CultureInfo.InvariantCulture) + " state snapshots")
                .And.Contain("does not prove"));
        AssertState(result.Values, installation, "Observed " + seconds.ToString(CultureInfo.InvariantCulture),
            context.Server.Id("observed-" + seconds));
        Assert.That(reports, Has.Count.EqualTo(seconds));
        Assert.That(reports[0].Phase, Does.Contain("Observed 1 update snapshots"));
        Assert.That(reports[^1].Phase,
            Does.Contain("Observed " + seconds.ToString(CultureInfo.InvariantCulture) + " update snapshots"));
        Assert.That(context.Server.Calls.IsEmpty, Is.True);
        context.VerifyNoPackageRead();
        clock.VerifyDisposedTimers();
    }

    [Test]
    public async Task CancelingAnObservationDisposesItsPendingTimerAndPreventsAnotherReadAsync()
    {
        var clock = new DeviceUpdateTestClock(autoAdvance: false);
        var context = new DeviceUpdateTestContext(timeProvider: clock.Provider.Object);
        DeviceUpdateTestFacet installation = context.Action("install-package-sample").Facet;
        int snapshots = 0;
        context.Server.ReadHandler = (read, _) =>
        {
            if (read.NodeId == installation.Current)
            {
                snapshots++;
            }
            return context.Server.Read(read);
        };
        CompanionTaskInput prepared = await context.Provider.PrepareInputAsync(
            context.Server.Context(), context.Target, "observe-update",
            [new("seconds", Variant.From(60u))], CancellationToken.None).ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        Task<CompanionOperationResult> observing = context.Provider.ExecutePreparedAsync(
            context.Server.Context(), context.Target, "observe-update", prepared, null, cancellation.Token).AsTask();
        try
        {
            await Task.WhenAny(clock.TimerCreated.Task, observing).ConfigureAwait(false);
            Assert.That(clock.TimerCreated.Task.IsCompletedSuccessfully, Is.True);
            Assert.That(observing.IsCompleted, Is.False);
            Assert.That(snapshots, Is.EqualTo(1));
        }
        finally
        {
            cancellation.Cancel();
        }

        await Assert.ThatAsync(() => observing, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

        Assert.That(snapshots, Is.EqualTo(1));
        Assert.That(clock.TimerCount, Is.EqualTo(1));
        Assert.That(context.Server.Calls.IsEmpty, Is.True);
        context.VerifyNoPackageRead();
        clock.VerifyDisposedTimers();
    }

    [Test]
    public async Task ObservationReadFailureStopsPollingInsteadOfReturningTheLastGoodSnapshotAsync()
    {
        var clock = new DeviceUpdateTestClock();
        var context = new DeviceUpdateTestContext(timeProvider: clock.Provider.Object);
        DeviceUpdateTestFacet installation = context.Action("install-package-sample").Facet;
        clock.Tick = _ => context.Server.SetValue(
            installation.Current, Variant.From(new LocalizedText("Idle")), status: StatusCodes.BadTimeout);
        CompanionTaskInput prepared = await context.Provider.PrepareInputAsync(
            context.Server.Context(), context.Target, "observe-update",
            [new("seconds", Variant.From(60u))], CancellationToken.None).ConfigureAwait(false);

        await Assert.ThatAsync(
            () => context.Provider.ExecutePreparedAsync(context.Server.Context(), context.Target,
                "observe-update", prepared, null, CancellationToken.None).AsTask(),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadTimeout))
            .ConfigureAwait(false);

        Assert.That(clock.TimerCount, Is.EqualTo(1));
        Assert.That(context.Server.Calls.IsEmpty, Is.True);
        clock.VerifyDisposedTimers();
    }

    [TestCase("upload-package-sample")]
    [TestCase("install-package-sample")]
    [TestCase("observe-update")]
    public async Task AlreadyCanceledPreparationCannotReadTheDeviceOrPackageAsync(string operationId)
    {
        var context = new DeviceUpdateTestContext();

        await Assert.ThatAsync(
            () => context.Provider.PrepareInputAsync(context.Server.Context(), context.Target,
                operationId, context.Inputs(operationId), new CancellationToken(true)).AsTask(),
            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

        context.Server.Session.Verify(value => value.ReadAsync(
            It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
            It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.That(context.Server.Calls.IsEmpty, Is.True);
        context.VerifyNoPackageRead();
    }

    private static ArrayOf<CompanionValue> Replace(ArrayOf<CompanionValue> fields, string name, string text)
    {
        return fields.ConvertAll(field => field.Name == name ? field with { Value = Variant.From(text) } : field);
    }

    private static Mock<IProgress<CompanionTaskProgress>> Progress(List<CompanionTaskProgress> reports)
    {
        var progress = new Mock<IProgress<CompanionTaskProgress>>(MockBehavior.Strict);
        progress.Setup(value => value.Report(It.IsAny<CompanionTaskProgress>()))
            .Callback((CompanionTaskProgress value) => reports.Add(value));
        return progress;
    }

    private static void AssertInstallArguments(
        CallMethodRequest request, string manufacturer, string revision, ArrayOf<string> expectedPatches)
    {
        Assert.That(request.InputArguments, Has.Count.EqualTo(4));
        Assert.That(request.InputArguments[0].TryGetValue(out string actualManufacturer), Is.True);
        Assert.That(actualManufacturer, Is.EqualTo(manufacturer));
        Assert.That(request.InputArguments[1].TryGetValue(out string actualRevision), Is.True);
        Assert.That(actualRevision, Is.EqualTo(revision));
        Assert.That(request.InputArguments[2].TryGetValue(out ArrayOf<string> patches), Is.True);
        Assert.That(patches.IsNull, Is.False);
        Assert.That(patches, Is.EqualTo(expectedPatches));
        Assert.That(request.InputArguments[3].TryGetValue(out ByteString hash), Is.True);
        Assert.That(hash.Span.ToArray(), Is.EqualTo(Convert.FromHexString(DeviceUpdateTestContext.PackageDigest)));
    }

    private static void AssertState(
        ArrayOf<CompanionValue> values, DeviceUpdateTestFacet facet, string expectedText, NodeId expectedState)
    {
        Assert.That(Field(values, facet.Name + " state").TryGetValue(out LocalizedText text), Is.True);
        Assert.That(text.Text, Is.EqualTo(expectedText));
        Assert.That(Field(values, facet.Name + " state NodeId").TryGetValue(out NodeId state), Is.True);
        Assert.That(state, Is.EqualTo(expectedState));
        Assert.That(Field(values, facet.Name + " state machine").TryGetValue(out NodeId machine), Is.True);
        Assert.That(machine, Is.EqualTo(facet.Machine));
    }

    private static void AssertUpload(
        DeviceUpdateTestContext context, CompanionOperationResult result, string packageId, ByteString bytes)
    {
        Assert.That(context.UploadCallNames,
            Is.EqualTo(s_uploadMethods));
        Assert.That(context.Uploaded, Is.EqualTo(bytes.Span.ToArray()));
        Assert.That(context.CommitSucceeded, Is.True);
        Assert.That(context.Server.Calls[0].InputArguments.ToList().Single().TryGetValue(out string requestedId),
            Is.True);
        Assert.That(requestedId, Is.EqualTo(packageId));
        Assert.That(Field(result.Values, "Package id").TryGetValue(out string returnedId), Is.True);
        Assert.That(returnedId, Is.EqualTo(packageId));
        Assert.That(Field(result.Values, "Package bytes").TryGetValue(out long count), Is.True);
        Assert.That(count, Is.EqualTo(bytes.Length));
        Assert.That(Field(result.Values, "Package SHA-256").TryGetValue(out ByteString digest), Is.True);
        Assert.That(digest.Span.ToArray(), Is.EqualTo(Convert.FromHexString(DeviceUpdateTestContext.PackageDigest)));
        Assert.That(Field(result.Values, "Completion state machine").TryGetValue(out NodeId completion), Is.True);
        Assert.That(completion, Is.EqualTo(context.Completion));
    }

    private static readonly string[] s_operations =
    [
        "refresh", "prepare-sample", "upload-package-sample", "install-package-sample",
        "abort-prepare-sample", "resume-prepare-sample", "resume-installation-sample",
        "confirm-sample", "observe-update"
    ];
    private static readonly string[] s_uploadFields = ["path", "packageId", "sha256"];
    private static readonly string[] s_installFields = ["manufacturer", "revision", "patches", "sha256"];
    private static readonly string[] s_uploadMethods =
        ["GenerateFileForWrite", "Open", "Write", "Close", "CloseAndCommit"];
}

internal sealed class DeviceUpdateTestContext
{
    public DeviceUpdateTestContext(ICompanionPackageReader? reader = null, TimeProvider? timeProvider = null)
    {
        Server.Session.SetupGet(value => value.Connected).Returns(true);
        Server.Session.SetupGet(value => value.SessionId).Returns(new NodeId(101u));
        Server.Session.SetupGet(value => value.Identity).Returns(new Mock<IUserIdentity>(MockBehavior.Strict).Object);
        Server.Session.SetupGet(value => value.Endpoint).Returns(Endpoint);
        NodeId update = Server.Id("software-update");
        Server.AddObject(update, Di.ObjectTypeIds.SoftwareUpdateType, "Update sample");
        Server.AddChild(Server.Resolve(Di.ObjectIds.DeviceSet), update);
        Target = new CompanionTarget("di", update, "Update sample", "Software update");
        DeviceUpdateTestFacet prepare = AddFacet(
            "PrepareForUpdate", Di.ObjectTypeIds.PrepareForUpdateStateMachineType, "Ready");
        DeviceUpdateTestFacet installation = AddFacet(
            "Installation", Di.ObjectTypeIds.InstallationStateMachineType, "Idle");
        DeviceUpdateTestFacet confirmation = AddFacet(
            "Confirmation", Di.ObjectTypeIds.ConfirmationStateMachineType, "Unconfirmed");
        AddAction("install-package-sample", "Install sample software package", installation, "InstallSoftwarePackage",
            Di.MethodIds.InstallationStateMachineType_InstallSoftwarePackage);
        AddAction("abort-prepare-sample", "Abort sample preparation", prepare, "Abort",
            Di.MethodIds.PrepareForUpdateStateMachineType_Abort);
        AddAction("resume-prepare-sample", "Resume sample preparation", prepare, "Resume",
            Di.MethodIds.PrepareForUpdateStateMachineType_Resume);
        AddAction("resume-installation-sample", "Resume sample installation", installation, "Resume",
            Di.MethodIds.InstallationStateMachineType_Resume);
        AddAction("confirm-sample", "Confirm sample update", confirmation, "Confirm",
            Di.MethodIds.ConfirmationStateMachineType_Confirm);
        Loading = Server.AddProperty(update, Di.Namespaces.OpcUaDi, "Loading", Variant.Null);
        Transfer = Server.AddProperty(Loading, Di.Namespaces.OpcUaDi, "FileTransfer", Variant.Null);
        Generate = AddMethod(Transfer, Namespaces.OpcUa, "GenerateFileForWrite");
        Commit = AddMethod(Transfer, Namespaces.OpcUa, "CloseAndCommit");
        FileNode = Server.Id("generated-file");
        Open = Server.AddProperty(FileNode, Namespaces.OpcUa, "Open", Variant.Null);
        Write = Server.AddProperty(FileNode, Namespaces.OpcUa, "Write", Variant.Null);
        Close = Server.AddProperty(FileNode, Namespaces.OpcUa, "Close", Variant.Null);
        Completion = Server.Id("completion-state-machine");
        m_uploadMethods.Add(Generate, "GenerateFileForWrite");
        m_uploadMethods.Add(Commit, "CloseAndCommit");
        m_uploadMethods.Add(Open, "Open");
        m_uploadMethods.Add(Write, "Write");
        m_uploadMethods.Add(Close, "Close");
        Server.CallHandler = HandleUpload;
        Packages.Setup(value => value.ReadAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(Package));
        Provider = new DeviceCompanionProvider(reader ?? Packages.Object, timeProvider);
    }

    public IndustrialCompanionTestSession Server { get; } = new();

    public Mock<ICompanionPackageReader> Packages { get; } = new(MockBehavior.Strict);

    public DeviceCompanionProvider Provider { get; }

    public CompanionTarget Target { get; }

    public EndpointDescription Endpoint { get; } = new()
    {
        EndpointUrl = "opc.tcp://localhost:4840/Sample",
        SecurityMode = MessageSecurityMode.SignAndEncrypt,
        SecurityPolicyUri = SecurityPolicies.Basic256Sha256
    };

    public ByteString Package { get; set; } = ByteString.From([0x61, 0x62, 0x63]);

    public string PackagePath { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "not-opened-package.bin");

    public NodeId Loading { get; }

    public NodeId Transfer { get; }

    public NodeId Generate { get; }

    public NodeId Commit { get; }

    public NodeId FileNode { get; }

    public NodeId Open { get; }

    public NodeId Write { get; }

    public NodeId Close { get; }

    public NodeId Completion { get; }

    public List<byte> Uploaded { get; } = [];

    public bool CommitSucceeded { get; private set; }

    public string? FailingUploadMethod { get; init; }

    public string? InvalidUploadOutput { get; init; }

    public ArrayOf<string> UploadCallNames => Server.Calls.ConvertAll(call => m_uploadMethods[call.MethodId]);

    public ArrayOf<CompanionValue> Inputs(string operationId)
    {
        return operationId switch
        {
            "upload-package-sample" =>
            [
                new("path", Variant.From(PackagePath)),
                new("packageId", Variant.From("package-1")),
                new("sha256", Variant.From(PackageDigest.ToLowerInvariant()))
            ],
            "install-package-sample" =>
            [
                new("manufacturer", Variant.From("urn:manufacturer:requested")),
                new("revision", Variant.From("2.7.9")),
                new("patches", Variant.From(" patch-1 \r\n\n\tpatch-2\r\n ")),
                new("sha256", Variant.From(PackageDigest.ToLowerInvariant()))
            ],
            "observe-update" => [new("seconds", Variant.From(1u))],
            "abort-prepare-sample" or "resume-prepare-sample" or
                "resume-installation-sample" or "confirm-sample" => [],
            _ => throw new ArgumentOutOfRangeException(nameof(operationId))
        };
    }

    public Task<CompanionTaskInput> PrepareAsync(string operationId)
    {
        return Provider.PrepareInputAsync(
            Server.Context(), Target, operationId, Inputs(operationId), CancellationToken.None).AsTask();
    }

    public DeviceUpdateTestAction Action(string operationId)
    {
        return m_actions[operationId];
    }

    public NodeId AddMethod(NodeId parent, string namespaceUri, string name)
    {
        NodeId method = Server.AddProperty(parent, namespaceUri, name, Variant.Null);
        Server.SetValue(method, Variant.From(true), Attributes.Executable);
        Server.SetValue(method, Variant.From(true), Attributes.UserExecutable);
        return method;
    }

    public void HideChild(NodeId parent, string namespaceUri, string name)
    {
        var qualifiedName = new QualifiedName(name, (ushort)Server.NamespaceUris.GetIndex(namespaceUri));
        Server.TranslateHandler = (path, _) =>
            path.StartingNode == parent && path.RelativePath.Elements.Count == 1 &&
                path.RelativePath.Elements[0].TargetName == qualifiedName
                ? new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch }
                : Server.Translate(path);
    }

    public void SetState(DeviceUpdateTestFacet facet, string text, NodeId state)
    {
        Server.SetValue(facet.Current, Variant.From(new LocalizedText(text)));
        Server.SetValue(facet.CurrentId, Variant.From(state));
    }

    public void ReplaceFacet(DeviceUpdateTestFacet facet)
    {
        NodeId replacement = Server.Id(facet.Name + "-replacement");
        Server.AddObject(replacement, Di.ObjectTypeIds.InstallationStateMachineType);
        Server.AddChild(Target.NodeId, replacement, Di.Namespaces.OpcUaDi, facet.Name);
        NodeId current = Server.AddProperty(
            replacement, Namespaces.OpcUa, "CurrentState", Variant.From(new LocalizedText("Idle")));
        Server.AddProperty(current, Namespaces.OpcUa, "Id", Variant.From(facet.InitialState));
        AddMethod(replacement, Di.Namespaces.OpcUaDi, "InstallSoftwarePackage");
    }

    public void VerifyNoPackageRead()
    {
        Packages.Verify(value => value.ReadAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private DeviceUpdateTestFacet AddFacet(string name, ExpandedNodeId type, string state)
    {
        NodeId machine = Server.Id(name);
        Server.AddObject(machine, type);
        Server.AddChild(Target.NodeId, machine, Di.Namespaces.OpcUaDi, name);
        NodeId current = Server.AddProperty(
            machine, Namespaces.OpcUa, "CurrentState", Variant.From(new LocalizedText(state)));
        NodeId stateId = Server.Id(name + "-initial");
        NodeId currentId = Server.AddProperty(current, Namespaces.OpcUa, "Id", Variant.From(stateId));
        return new DeviceUpdateTestFacet(name, machine, current, currentId, stateId);
    }

    private void AddAction(
        string operationId, string displayName, DeviceUpdateTestFacet facet, string methodName, ExpandedNodeId methodId)
    {
        NodeId permission = AddMethod(facet.Machine, Di.Namespaces.OpcUaDi, methodName);
        m_actions.Add(operationId, new DeviceUpdateTestAction(
            displayName, facet, methodName, permission, Server.Resolve(methodId)));
    }

    private CallMethodResult HandleUpload(CallMethodRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!m_uploadMethods.TryGetValue(request.MethodId, out string? name))
        {
            throw new AssertionException("Upload invoked an unrelated device method: " + request.MethodId);
        }
        Assert.That(request.ObjectId, Is.EqualTo(
            name is "GenerateFileForWrite" or "CloseAndCommit" ? Transfer : FileNode));
        if (name == FailingUploadMethod)
        {
            return new CallMethodResult { StatusCode = StatusCodes.BadInvalidState };
        }
        switch (name)
        {
            case "GenerateFileForWrite":
                Assert.That(request.InputArguments, Has.Count.EqualTo(1));
                return InvalidUploadOutput == "generate" ? Good([Variant.From(FileNode)]) :
                    Good([Variant.From(FileNode), Variant.From(71u)]);
            case "Open":
                Assert.That(request.InputArguments.ToList().Single().TryGetValue(out byte mode), Is.True);
                Assert.That(mode, Is.EqualTo(6));
                return Good([Variant.From(92u)]);
            case "Write":
                Assert.That(request.InputArguments, Has.Count.EqualTo(2));
                Assert.That(request.InputArguments[0].TryGetValue(out uint writeHandle), Is.True);
                Assert.That(writeHandle, Is.EqualTo(92u));
                Assert.That(request.InputArguments[1].TryGetValue(out ByteString bytes), Is.True);
                Uploaded.AddRange(bytes.Span.ToArray());
                return Good();
            case "Close":
                Assert.That(request.InputArguments.ToList().Single().TryGetValue(out uint closeHandle), Is.True);
                Assert.That(closeHandle, Is.EqualTo(92u));
                return Good();
            case "CloseAndCommit":
                Assert.That(request.InputArguments.ToList().Single().TryGetValue(out uint commitHandle), Is.True);
                Assert.That(commitHandle, Is.EqualTo(71u));
                CommitSucceeded = true;
                return InvalidUploadOutput == "commit" ? Good([Variant.From("not a NodeId")]) :
                    Good([Variant.From(Completion)]);
            default:
                throw new AssertionException("Unexpected upload method: " + name);
        }
    }

    public const string PackageDigest = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";

    private readonly Dictionary<string, DeviceUpdateTestAction> m_actions = new(StringComparer.Ordinal);
    private readonly Dictionary<NodeId, string> m_uploadMethods = [];
}

internal sealed record DeviceUpdateTestFacet(
    string Name, NodeId Machine, NodeId Current, NodeId CurrentId, NodeId InitialState);

internal sealed record DeviceUpdateTestAction(
    string DisplayName, DeviceUpdateTestFacet Facet, string MethodName, NodeId PermissionNode, NodeId MethodId);

internal sealed class DeviceUpdateTestClock
{
    public DeviceUpdateTestClock(bool autoAdvance = true)
    {
        Provider.SetupGet(value => value.TimestampFrequency).Returns(TimeSpan.TicksPerSecond);
        Provider.Setup(value => value.GetTimestamp()).Returns(() => m_timestamp);
        Provider.Setup(value => value.CreateTimer(
                It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns((TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            {
                Assert.That(dueTime, Is.EqualTo(TimeSpan.FromSeconds(1)));
                Assert.That(period, Is.EqualTo(Timeout.InfiniteTimeSpan));
                var timer = new Mock<ITimer>(MockBehavior.Strict);
                timer.Setup(value => value.Dispose());
                timer.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
                m_timers.Add(timer);
                TimerCreated.TrySetResult();
                if (autoAdvance)
                {
                    m_timestamp += dueTime.Ticks;
                    Tick?.Invoke(m_timers.Count);
                    callback(state);
                }
                return timer.Object;
            });
    }

    public Mock<TimeProvider> Provider { get; } = new(MockBehavior.Strict);

    public TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int TimerCount => m_timers.Count;

    public Action<int>? Tick { get; set; }

    public void VerifyDisposedTimers()
    {
        foreach (Mock<ITimer> timer in m_timers)
        {
            timer.Verify(value => value.Dispose(), Times.Once);
        }
    }

    private readonly List<Mock<ITimer>> m_timers = [];
    private long m_timestamp;
}
