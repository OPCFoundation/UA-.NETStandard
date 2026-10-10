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
