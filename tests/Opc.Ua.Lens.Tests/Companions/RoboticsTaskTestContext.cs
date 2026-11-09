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
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using R = Opc.Ua.Robotics;
using RI = Opc.Ua.RobotIntent;

namespace UaLens.Tests.Companions;

/// <summary>
/// Service-level test double with explicit method signatures and no live robot or owned transport.
/// </summary>
internal sealed class RoboticsTaskTestContext
{
    public RoboticsTaskTestContext()
    {
        Services.NamespaceUris.GetIndexOrAppend(R.Namespaces.Robotics);
        Services.NamespaceUris.GetIndexOrAppend(RI.Namespaces.RobotIntent);
        Services.Session.SetupGet(session => session.Factory).Returns(Services.MessageContext.Factory);
        Services.Session.SetupGet(session => session.Connected).Returns(true);
        Services.Session.SetupGet(session => session.SessionId).Returns(SessionId);
        Services.Session.SetupGet(session => session.Identity)
            .Returns(new UserIdentity(new UserNameIdentityToken { UserName = "owned-operator" }));
        Endpoint = new EndpointDescription
        {
            EndpointUrl = "opc.tcp://localhost:4840/OwnedRobot",
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            Server = new ApplicationDescription { ApplicationUri = "urn:owned:robot" },
            ServerCertificate = ByteString.From("non-secret-test-peer"u8)
        };
        Services.Session.SetupGet(session => session.Endpoint).Returns(Endpoint);
        Channel.SetupGet(channel => channel.MessageContext).Returns(Services.MessageContext);
        Services.Session.SetupGet(session => session.TransportChannel).Returns(Channel.Object);
        Grant = new CompanionDeploymentGrant("owned-robot-intent-test", "revision-1", Clock.Now.AddMinutes(1));
        Policy.As<IRoboticsSimulationPolicy>().Setup(policy => policy.AuthorizeRoboticsAsync(
            It.IsAny<CompanionContext>(), It.IsAny<CompanionOperationDraft>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(Grant));
        Policy.Setup(policy => policy.AuthorizeAsync(It.IsAny<CompanionContext>(),
            It.IsAny<CompanionOperationDraft>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(Grant));
        NodeId root = Object(
            "RobotIntent", Opc.Ua.ObjectTypeIds.BaseObjectType, Opc.Ua.ObjectIds.Server, "RobotIntent");
        NodeId controllers = Object("Controllers", Opc.Ua.ObjectTypeIds.FolderType, root, "Controllers");
        Controller = Object("controller", RI.ObjectTypeIds.IntentControllerType, controllers, "OwnedController");
        Target = new CompanionTarget("robot-intent", Controller, "Owned intent simulation", "DraftIntentController");
        ControlOwner = Property(Controller, "ControlOwner", Variant.From(SessionId));
        Ready = Property(Controller, "Ready", Variant.From(true));
        Mode = Property(Controller, "OperationalMode", Variant.From((int)OperationalModeEnum.AutomaticExternal));
        Property(Controller, "ActiveIntent", Variant.From(NodeId.Null));
        Property(Controller, "ActiveMission", Variant.From(NodeId.Null));
        Property(Controller, "MaxQueueDepth", Variant.From(16u));
        NodeId capabilities = Object("Capabilities", Opc.Ua.ObjectTypeIds.BaseObjectType, Controller, "Capabilities");
        Supported = Property(capabilities, "SupportedIntents", Variant.FromStructure((ArrayOf<IntentCapabilityDataType>)
        [
            new()
            {
                IntentType = Services.Resolve(RI.DataTypeIds.WaitIntentDataType),
                CancelSupported = true,
                SupportedBufferModes = [BufferModeEnum.Aborting, BufferModeEnum.Buffered],
                SupportedBlockingModes = [BlockingModeEnum.None]
            }
        ]));
        MissionsSupported = Property(capabilities, "MissionsSupported", Variant.From(true));
        IntentsFolder = Object("Intents", Opc.Ua.ObjectTypeIds.FolderType, Controller, "Intents");
        MissionsFolder = Object("Missions", Opc.Ua.ObjectTypeIds.FolderType, Controller, "Missions");
        NodeId channels = Object("RealTimeChannels", Opc.Ua.ObjectTypeIds.FolderType, Controller, "RealTimeChannels");
        LeaseChannel = Object("telemetry", RI.ObjectTypeIds.RealTimeChannelType, channels, "Telemetry");
        Property(LeaseChannel, "ChannelId", Variant.From("joint-telemetry"));
        LeaseHolder = Property(LeaseChannel, "LeaseHolder", Variant.From(NodeId.Null));
        LeaseExpiry = Property(LeaseChannel, "LeaseExpiry", Variant.From(default(DateTimeUtc)));
        AddMethod(Controller, RI.Namespaces.RobotIntent, "RequestControl", [],
            [Opc.Ua.DataTypeIds.Boolean, Opc.Ua.DataTypeIds.NodeId]);
        AddMethod(Controller, RI.Namespaces.RobotIntent, "ReleaseControl", [], []);
        ArrayOf<ExpandedNodeId> submission =
            [Opc.Ua.DataTypeIds.Boolean, Opc.Ua.DataTypeIds.String, Opc.Ua.DataTypeIds.NodeId,
                RI.DataTypeIds.IntentFailureEnum, Opc.Ua.DataTypeIds.LocalizedText];
        Submit = AddMethod(
            Controller, RI.Namespaces.RobotIntent, "SubmitIntent", [RI.DataTypeIds.IntentDataType], submission);
        AddMethod(Controller, RI.Namespaces.RobotIntent, "SubmitMission", [RI.DataTypeIds.MissionDataType], submission);
        AddMethod(Controller, RI.Namespaces.RobotIntent, "CancelIntent",
            [Opc.Ua.DataTypeIds.String, RI.DataTypeIds.StopModeEnum], [Opc.Ua.DataTypeIds.Boolean]);
        AddMethod(Controller, RI.Namespaces.RobotIntent, "CancelMission",
            [Opc.Ua.DataTypeIds.String, RI.DataTypeIds.StopModeEnum], [Opc.Ua.DataTypeIds.Boolean]);
        AddMethod(Controller, RI.Namespaces.RobotIntent, "OpenRealTimeChannel",
            [Opc.Ua.DataTypeIds.String, Opc.Ua.DataTypeIds.Duration],
            [Opc.Ua.DataTypeIds.Boolean, Opc.Ua.DataTypeIds.String, Opc.Ua.DataTypeIds.String,
                Opc.Ua.DataTypeIds.UtcTime, Opc.Ua.DataTypeIds.LocalizedText]);
        AddMethod(Controller, RI.Namespaces.RobotIntent, "CloseRealTimeChannel",
            [Opc.Ua.DataTypeIds.String], [Opc.Ua.DataTypeIds.Boolean]);
        Reader.Setup(reader => reader.ReadOperationSnapshotAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
            .Returns((NodeId id, CancellationToken _) => ValueTask.FromResult(Operations[id]));
        Reader.Setup(reader => reader.ReadMissionSnapshotAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
            .Returns((NodeId id, CancellationToken _) => ValueTask.FromResult(Missions[id]));
        Provider = new RobotIntentCompanionProvider(Policy.Object, Clock, (_, _) => Reader.Object);
    }

    public IndustrialCompanionTestSession Services { get; } = new();
    public Mock<ITransportChannel> Channel { get; } = new(MockBehavior.Strict);
    public Mock<ICompanionDeploymentPolicy> Policy { get; } = new(MockBehavior.Strict);
    public Mock<IRobotIntentTransport> Reader { get; } = new(MockBehavior.Strict);
    public RoboticsTestClock Clock { get; } = new();
    public CompanionDeploymentGrant Grant { get; set; }
    public EndpointDescription Endpoint { get; }
    public CompanionContext Context => Services.Context();
    public RobotIntentCompanionProvider Provider { get; }
    public CompanionTarget Target { get; }
    public NodeId SessionId { get; } = new(125u);
    public NodeId Controller { get; }
    public NodeId ControlOwner { get; }
    public NodeId Ready { get; }
    public NodeId Mode { get; }
    public NodeId Supported { get; }
    public NodeId MissionsSupported { get; }
    public NodeId IntentsFolder { get; }
    public NodeId MissionsFolder { get; }
    public NodeId Submit { get; }
    public NodeId LeaseChannel { get; }
    public NodeId LeaseHolder { get; }
    public NodeId LeaseExpiry { get; }
    public Dictionary<NodeId, IntentOperationSnapshot> Operations { get; } = [];
    public Dictionary<NodeId, MissionSnapshot> Missions { get; } = [];
    public Dictionary<NodeId, NodeId> ExecutionStates { get; } = [];

    public NodeId AddMethod(
        NodeId owner, string ns, string name, ArrayOf<ExpandedNodeId> inputs, ArrayOf<ExpandedNodeId> outputs)
    {
        NodeId method = Services.Id(owner + "-" + name);
        Services.AddObject(method, Opc.Ua.ObjectTypeIds.BaseObjectType);
        Services.AddChild(owner, method, ns, name);
        Services.SetValue(method, Variant.From((int)NodeClass.Method), Attributes.NodeClass);
        Services.SetValue(method, Variant.From(true), Attributes.Executable);
        Services.SetValue(method, Variant.From(true), Attributes.UserExecutable);
        Services.AddProperty(method, Opc.Ua.Namespaces.OpcUa, Opc.Ua.BrowseNames.InputArguments,
            Variant.FromStructure(inputs.ConvertAll(type => new Argument
            {
                DataType = Services.Resolve(type),
                ValueRank = ValueRanks.Scalar
            })));
        Services.AddProperty(method, Opc.Ua.Namespaces.OpcUa, Opc.Ua.BrowseNames.OutputArguments,
            Variant.FromStructure(outputs.ConvertAll(type => new Argument
            {
                DataType = Services.Resolve(type),
                ValueRank = ValueRanks.Scalar
            })));
        return method;
    }

    public NodeId AddWait(WaitIntentDataType? payload = null)
    {
        NodeId operation = Object(
            "wait-operation", RI.ObjectTypeIds.IntentOperationType, IntentsFolder, "wait-operation");
        payload ??= RobotIntentBuilder.Wait(1000).WithIntentId("owned-wait")
            .WithBufferMode(BufferModeEnum.Buffered).WithBlockingMode(BlockingModeEnum.None).Build();
        Property(operation, "Intent", Variant.FromStructure(payload));
        ExecutionStates[operation] = Property(
            operation, "ExecutionState", Variant.From((int)ExecutionStateEnum.Executing));
        Operations[operation] = new IntentOperationSnapshot
        {
            Operation = operation,
            IntentId = payload.IntentId ?? throw new AssertionException("The wait has no correlation id."),
            ExecutionState = ExecutionStateEnum.Executing
        };
        return operation;
    }

    public NodeId AddWaitMission()
    {
        NodeId operation = Object(
            "mission-operation", RI.ObjectTypeIds.MissionType, MissionsFolder, "mission-operation");
        MissionDataType mission = RobotIntentBuilder.Mission("owned-mission")
            .ReleasedStep("1", RobotIntentBuilder.Wait(1000).WithIntentId("owned-mission:1")
                .WithBufferMode(BufferModeEnum.Buffered).WithBlockingMode(BlockingModeEnum.None).Build()).Build();
        Missions[operation] = new MissionSnapshot
        {
            MissionNode = operation,
            MissionId = mission.MissionId ?? throw new AssertionException("The mission has no correlation id."),
            Mission = mission,
            ExecutionState = ExecutionStateEnum.Executing
        };
        ExecutionStates[operation] = Property(
            operation, "ExecutionState", Variant.From((int)ExecutionStateEnum.Executing));
        return operation;
    }

    public ValueTask<CompanionTaskInput> PrepareWaitAsync()
    {
        return Provider.PrepareInputAsync(Context, Target, "submit-wait", WaitInputs(), CancellationToken.None);
    }

    public Task<CompanionOperationResult> ExecuteAsync(
        CompanionTaskInput prepared, string operation = "submit-wait", CancellationToken token = default)
    {
        return Provider.ExecutePreparedAsync(Context, Target, operation, prepared, null, token).AsTask();
    }

    public void VerifyBorrowed()
    {
        Services.Session.Verify(session => session.Dispose(), Times.Never);
        Channel.Verify(channel => channel.Dispose(), Times.Never);
        Reader.Verify(reader => reader.RequestControlAsync(It.IsAny<CancellationToken>()), Times.Never);
        Reader.Verify(reader => reader.ReleaseControlAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    public static ArrayOf<CompanionValue> WaitInputs(double duration = 1000)
    {
        return [new("id", Variant.From("owned-wait")), new("duration", Variant.From(duration))];
    }

    public static Variant Value(CompanionOperationResult result, string name)
    {
        return result.Values.ToList().Single(value => value.Name == name).Value;
    }

    private NodeId Object(string id, ExpandedNodeId type, NodeId parent, string name)
    {
        NodeId node = Services.Id(id);
        Services.AddObject(node, type);
        Services.AddChild(parent, node, RI.Namespaces.RobotIntent, name);
        return node;
    }

    private NodeId Property(NodeId parent, string name, Variant value)
    {
        return Services.AddProperty(parent, RI.Namespaces.RobotIntent, name, value);
    }
}

/// <summary>
/// Deterministic authorization and broker-expiry clock.
/// </summary>
internal sealed class RoboticsTestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 11, 10, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Runs a deterministic UI callback at the operation's progress boundary.
/// </summary>
internal sealed class ImmediateRoboticsProgress(Action report) : IProgress<CompanionTaskProgress>
{
    /// <inheritdoc/>
    public void Report(CompanionTaskProgress value)
    {
        report();
    }
}
