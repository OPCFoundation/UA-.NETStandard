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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.AI.Client;
using Opc.Ua.Client;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using static UaLens.Tests.Companions.IndustrialCompanionTestSession;
using Ai = Opc.Ua.AI;

namespace UaLens.Tests.Companions
{

    /// <summary>
    /// Typed clients dispatch to finite in-memory service responses. No endpoint is opened.
    /// </summary>
    internal sealed class AIWorkflowFixture
    {
        public AIWorkflowFixture(IAITaskEgressPolicy? policy = null, int maxFields = 256)
        {
            Server.NamespaceUris.GetIndexOrAppend(Ai.Namespaces.AI);
            Server.Session.SetupGet(value => value.Connected).Returns(() => Connected);
            Server.Session.SetupGet(value => value.SessionId).Returns(() => SessionId);
            Server.Session.SetupGet(value => value.Endpoint).Returns(Endpoint);
            Server.Session.SetupGet(value => value.Factory).Returns(Server.MessageContext.Factory);
            Server.Session.SetupGet(value => value.TypeTree).Returns(new TypeTable(Server.NamespaceUris));
            Server.Session.SetupGet(value => value.OperationLimits).Returns(new OperationLimits());
            Server.Session.SetupGet(value => value.ServerCapabilities).Returns(new ServerCapabilities());
            Server.Session.SetupGet(value => value.ContinuationPointPolicy).Returns(ContinuationPointPolicy.Default);
            var cache = new Mock<INodeCache>(MockBehavior.Strict);
            cache.Setup(value => value.IsTypeOfAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId child, NodeId parent, CancellationToken _) => ValueTask.FromResult(child == parent));
            Server.Session.SetupGet(value => value.NodeCache).Returns(cache.Object);
            Server.Session.Setup(value => value.BrowseAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ViewDescription? _, uint _, ArrayOf<BrowseDescription> requests,
                    CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(new BrowseResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = requests.ConvertAll(Server.Browse)
                    });
                });
            Context = Server.Context(maxFields: maxFields);
            var clock = new Mock<TimeProvider>(MockBehavior.Strict);
            clock.Setup(value => value.GetUtcNow()).Returns(new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero));
            Provider = new AICompanionProvider(policy, clock.Object);
            Model = Target("model", Ai.ObjectTypeIds.ModelType, "Model");
            Candidate = Target("candidate", Ai.ObjectTypeIds.ModelType, "Model");
            Dataset = Target("dataset", Ai.ObjectTypeIds.DatasetType, "Dataset");
            Deployment = Target("deployment", Ai.ObjectTypeIds.DeploymentType, "Deployment");
            Job = Target("inference", Ai.ObjectTypeIds.InferenceJobType, "Inference job");
            Learning = Target("learning", Ai.ObjectTypeIds.LearningJobType, "Learning job");
            Transfer = Target("transfer", Ai.ObjectTypeIds.InferenceTransferType, "Transfer");
            Evaluation = Target("evaluation", Ai.ObjectTypeIds.EvaluationRunType, "Evaluation");
            RequestFile = Target("request-file", ObjectTypeIds.FileType, "File");
            ResponseFile = Target("response-file", ObjectTypeIds.FileType, "File");
            ReturnedJob = Job.NodeId;
            ReturnedModel = Model.NodeId;
            Set(Deployment, Ai.BrowseNames.DeploymentId, Variant.From("deployment-1"));
            SetDestination("http://localhost:8080/v1", false, Ai.InferenceLocationEnum.OnServer);
            Set(Deployment, Ai.BrowseNames.State, Variant.From((int)Ai.DeploymentStateEnum.Ready));
            Set(Deployment, Ai.BrowseNames.DataJurisdiction, Variant.From("local-fixture"));
            Set(Deployment, Ai.BrowseNames.MaxInlinePayloadSize, Variant.From(16384u));
            ModelReference(Model.NodeId);
            Set(Model, Ai.BrowseNames.ModelId, Variant.From("model-1"));
            Set(Model, Ai.BrowseNames.Name, Variant.From("Fixture model"));
            Set(Candidate, Ai.BrowseNames.ModelId, Variant.From("model-2"));
            Set(Dataset, Ai.BrowseNames.DatasetId, Variant.From("dataset-1"));
            Set(Dataset, Ai.BrowseNames.Name, Variant.From("Fixture dataset"));
            Set(Dataset, Ai.BrowseNames.SourceKind, Variant.From((int)Ai.DatasetSourceEnum.Synthetic));
            Set(Dataset, Ai.BrowseNames.ContentType, Variant.From("application/json"));
            Set(Dataset, Ai.BrowseNames.SizeBytes, Variant.From(0ul));
            Set(Dataset, Ai.BrowseNames.SampleCount, Variant.From(0u));
            Set(Job, Ai.BrowseNames.JobId, Variant.From("job-1"));
            Set(Job, Ai.BrowseNames.Deployment, Variant.From(Deployment.NodeId));
            Set(Job, Ai.BrowseNames.ResponsePayload, Variant.From(Response));
            Set(Job, Ai.BrowseNames.ResponseContentType, Variant.From("application/json"));
            Set(Job, Ai.BrowseNames.ModelUsed, Variant.From(Model.NodeId));
            Set(Job, Ai.BrowseNames.FinishReason, Variant.From((int)Ai.FinishReasonEnum.Stop));
            Set(Job, Ai.BrowseNames.LastError, Variant.From(LocalizedText.Null));
            SetProgramState(Job, ObjectIds.ProgramStateMachineType_Running);
            Set(Learning, Ai.BrowseNames.JobId, Variant.From("learning-1"));
            Set(Learning, Ai.BrowseNames.State, Variant.From((int)Ai.LearningJobStateEnum.Collecting));
            Set(Learning, Ai.BrowseNames.BaseModel, Variant.From(Model.NodeId));
            Set(Learning, Ai.BrowseNames.Dataset, Variant.From(Dataset.NodeId));
            Set(Learning, Ai.BrowseNames.CandidateModel, Variant.From(Candidate.NodeId));
            Set(Learning, Ai.BrowseNames.TargetDeployment, Variant.From(Deployment.NodeId));
            SetProgramState(Learning, ObjectIds.ProgramStateMachineType_Running);
            Set(Transfer, Ai.BrowseNames.TransferId, Variant.From("transfer-1"));
            Set(Transfer, Ai.BrowseNames.State, Variant.From((int)Ai.TransferStateEnum.Completed));
            Set(Transfer, Ai.BrowseNames.ResponseContentType, Variant.From("application/json"));
            Set(Transfer, Ai.BrowseNames.ModelUsed, Variant.From(Model.NodeId));
            Set(Transfer, Ai.BrowseNames.ExpiresAt, Variant.From(new DateTimeUtc(2099, 1, 1)));
            Server.AddChild(Transfer.NodeId, RequestFile.NodeId, Ai.Namespaces.AI, Ai.BrowseNames.Request);
            Server.AddChild(Transfer.NodeId, ResponseFile.NodeId, Ai.Namespaces.AI, Ai.BrowseNames.Response);
            SetResponse(Response);
            Set(Evaluation, Ai.BrowseNames.RunId, Variant.From("evaluation-1"));
            Set(Evaluation, Ai.BrowseNames.EvaluatedModel, Variant.From(Model.NodeId));
            Set(Evaluation, Ai.BrowseNames.Passed, Variant.From(true));
            Set(Evaluation, Ai.BrowseNames.Metrics, Variant.FromStructure<Ai.EvaluationMetricDataType>(
            [
                new()
                {
                    Name = "accuracy", Value = 0.95, Threshold = 0.9, Comparison = ">=", Passed = true
                }
            ]));
            AddMethod(Deployment, "GetCapabilities", Ai.MethodIds.DeploymentType_GetCapabilities);
            AddMethod(Deployment, "Invoke", Ai.MethodIds.DeploymentType_Invoke);
            AddMethod(Deployment, "InvokeAsync", Ai.MethodIds.DeploymentType_InvokeAsync);
            AddMethod(Deployment, "BeginTransfer", Ai.MethodIds.DeploymentType_BeginTransfer);
            AddMethod(Transfer, "Execute", Ai.MethodIds.InferenceTransferType_Execute);
            AddMethod(Transfer, "Abort", Ai.MethodIds.InferenceTransferType_Abort);
            AddMethod(Job, "Halt", MethodIds.ProgramStateMachineType_Halt, Namespaces.OpcUa);
            AddMethod(Learning, "Halt", MethodIds.ProgramStateMachineType_Halt, Namespaces.OpcUa);
            AddMethod(Learning, "StartCollection", Ai.MethodIds.LearningJobType_StartCollection);
            AddMethod(Learning, "StopCollection", Ai.MethodIds.LearningJobType_StopCollection);
            AddMethod(Learning, "TriggerTraining", Ai.MethodIds.LearningJobType_TriggerTraining);
            AddMethod(Learning, "PromoteModel", Ai.MethodIds.LearningJobType_PromoteModel);
            AddFileMethods(RequestFile);
            AddFileMethods(ResponseFile);
            AddDiscovery();
            Server.CallHandler = HandleCall;
        }

        public IndustrialCompanionTestSession Server { get; } = new();

        public CompanionContext Context { get; }

        public AICompanionProvider Provider { get; }

        public CompanionTarget Model { get; }

        public CompanionTarget Candidate { get; }

        public CompanionTarget Dataset { get; }

        public CompanionTarget Deployment { get; }

        public CompanionTarget Job { get; }

        public CompanionTarget Learning { get; }

        public CompanionTarget Transfer { get; }

        public CompanionTarget RequestFile { get; }

        public CompanionTarget ResponseFile { get; }

        public CompanionTarget Evaluation { get; }

        public EndpointDescription Endpoint { get; } = new()
        {
            EndpointUrl = "opc.tcp://localhost:4840/AI",
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
        };

        public bool Connected { get; set; } = true;

        public NodeId SessionId { get; set; } = new(101u);

        public ByteString Payload { get; set; } = ByteString.From(
            "{\"messages\":[{\"role\":\"user\",\"content\":\"private-prompt-marker\"}]}"u8);

        public ByteString Response { get; private set; } = ByteString.From("{\"answer\":\"fixture\"}"u8);

        public ArrayOf<Ai.CapabilityDataType> Capabilities { get; set; } =
        [
            new() { Name = "reachable", Supported = true },
            new() { Name = "inline-payload", Supported = true },
            new() { Name = "async-inference", Supported = true },
            new() { Name = "chunked-transfer", Supported = true },
            new() { Name = "chat", Supported = true }
        ];

        public NodeId ReturnedJob { get; set; }

        public NodeId ReturnedModel { get; set; }

        public Ai.FinishReasonEnum FinishReason { get; init; } = Ai.FinishReasonEnum.Stop;

        public bool TransferRequired { get; init; }

        public bool BeginAccepted { get; init; } = true;

        public bool TrainingAccepted { get; init; } = true;

        public int ResponseChunkLimit { get; init; } = 4096;

        public Action? AfterCapabilities { get; set; }

        public string? FailingMethod { get; set; }

        public StatusCode FailureStatus { get; set; } = StatusCodes.BadInvalidState;

        public bool FailAbort { get; init; }

        public List<byte> Uploaded { get; } = [];

        public ArrayOf<string> Mutations => Server.Calls
            .Filter(call => m_methodNames[call.MethodId] != "GetCapabilities")
            .ConvertAll(call => m_methodNames[call.MethodId]);

        public Task<CompanionTaskInput> PrepareAsync(
            string operation, CompanionTarget? target = null, ArrayOf<CompanionValue> inputs = default)
        {
            return Provider.PrepareInputAsync(Context, target ?? Deployment, operation,
                inputs.IsNull ? RequestInputs(operation) : inputs, CancellationToken.None).AsTask();
        }

        public Task<CompanionOperationResult> ExecuteAsync(
            string operation, CompanionTaskInput input, CompanionTarget? target = null)
        {
            return Provider.ExecutePreparedAsync(
                Context, target ?? Deployment, operation, input, null, CancellationToken.None).AsTask();
        }

        public ArrayOf<CompanionValue> RequestInputs(string operation)
        {
            ArrayOf<CompanionValue> common =
            [
                new("model", Variant.From(Model.NodeId)),
                new("capability", Variant.From("chat")),
                new("payload", Variant.From(Payload)),
                new("contentType", Variant.From("application/json")),
                new("parameters", Variant.FromStructure(ArrayOf<Opc.Ua.KeyValuePair>.Empty))
            ];
            return operation == "invoke-request" ? [.. common, new("timeout", Variant.From(1250d))] : common;
        }

        public ArrayOf<CompanionValue> LearningInputs(string operation)
        {
            ArrayOf<CompanionValue> common =
            [
                new("model", Variant.From(Model.NodeId)),
                new("dataset", Variant.From(Dataset.NodeId))
            ];
            return operation == "promote-model"
                ? [.. common, new("deployment", Variant.From(Deployment.NodeId))] : common;
        }

        public ArrayOf<CompanionValue> TransferInputs(ulong maximum)
        {
            return
            [
                new("maximumBytes", Variant.From(maximum)),
                new("sha256", Variant.From(ByteString.From(SHA256.HashData(Response.Span))))
            ];
        }

        public CallMethodRequest Call(string name)
        {
            return Server.Calls.ToList().Single(call => m_methodNames[call.MethodId] == name);
        }

        public void SetDestination(string endpoint, bool egress, Ai.InferenceLocationEnum location)
        {
            Set(Deployment, Ai.BrowseNames.EndpointUri, Variant.From(endpoint));
            Set(Deployment, Ai.BrowseNames.EgressPermitted, Variant.From(egress));
            Set(Deployment, Ai.BrowseNames.InferenceLocation, Variant.From((int)location));
        }

        public void ModelReference(NodeId model)
        {
            SetReference(Ai.ReferenceTypeIds.UsesModel, model);
        }

        public void FallbackReference(NodeId deployment)
        {
            SetReference(Ai.ReferenceTypeIds.FallsBackTo, deployment);
        }

        public NodeId Property(CompanionTarget target, string name)
        {
            return m_properties[(target.NodeId, name)];
        }

        public void Set(CompanionTarget target, string name, Variant value)
        {
            string namespaceUri = target.TypeName == "File" ? Namespaces.OpcUa : Ai.Namespaces.AI;
            if (m_properties.TryGetValue((target.NodeId, name), out NodeId node))
            {
                Server.SetValue(node, value);
            }
            else
            {
                m_properties.Add((target.NodeId, name), Server.AddProperty(target.NodeId, namespaceUri, name, value));
            }
        }

        public void SetResponse(ByteString bytes)
        {
            Response = bytes;
            Set(ResponseFile, BrowseNames.Size, Variant.From((ulong)bytes.Length));
            m_responsePosition = 0;
        }

        public void Deny(CompanionTarget target, string name, string? namespaceUri = null)
        {
            NodeId method = m_permissions[(target.NodeId, namespaceUri ?? Ai.Namespaces.AI, name)];
            Server.SetValue(method, Variant.From(false), Attributes.UserExecutable);
        }

        public NodeId Permission(CompanionTarget target, string name)
        {
            return m_permissions[(target.NodeId, Ai.Namespaces.AI, name)];
        }

        public void Hide(CompanionTarget target, string name, string? namespaceUri = null)
        {
            var qualified = new QualifiedName(name,
                (ushort)Server.NamespaceUris.GetIndex(namespaceUri ?? Ai.Namespaces.AI));
            Server.TranslateHandler = (path, _) =>
                path.StartingNode == target.NodeId &&
                path.RelativePath.Elements.Count == 1 &&
                path.RelativePath.Elements[0].TargetName == qualified
                    ? new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch } : Server.Translate(path);
        }

        public void SetProgramState(CompanionTarget target, NodeId state)
        {
            string name = state == ObjectIds.ProgramStateMachineType_Halted ? "Halted" :
                state == ObjectIds.ProgramStateMachineType_Ready ? "Ready" : "Running";
            if (!m_states.TryGetValue(target.NodeId, out AIWorkflowStateNodes? nodes))
            {
                NodeId current = Server.AddProperty(
                    target.NodeId, Namespaces.OpcUa, BrowseNames.CurrentState, Variant.From(new LocalizedText(name)));
                NodeId id = Server.AddProperty(current, Namespaces.OpcUa, BrowseNames.Id, Variant.From(state));
                nodes = new AIWorkflowStateNodes(current, id);
                m_states.Add(target.NodeId, nodes);
            }
            Server.SetValue(nodes.Current, Variant.From(new LocalizedText(name)));
            Server.SetValue(nodes.Id, Variant.From(state));
        }

        public void ObserveStates(ArrayOf<NodeId> states)
        {
            int reads = 0;
            AIWorkflowStateNodes nodes = m_states[Job.NodeId];
            Server.ReadHandler = (read, _) =>
            {
                if (read.NodeId == nodes.Current)
                {
                    SetProgramState(Job, states[Math.Min(reads / 2, states.Count - 1)]);
                    reads++;
                }
                return Server.Read(read);
            };
        }

        public void ReplaceResponseFile()
        {
            CompanionTarget replacement = Target("replacement-file", ObjectTypeIds.FileType, "File");
            Set(replacement, BrowseNames.Size, Variant.From((ulong)Response.Length));
            AddFileMethods(replacement);
            Server.AddChild(Transfer.NodeId, replacement.NodeId, Ai.Namespaces.AI, Ai.BrowseNames.Response);
        }

        public void VerifyNoMutation()
        {
            Assert.That(Mutations.IsEmpty, Is.True);
            Server.Session.Verify(value => value.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        public void VerifyBorrowedSession()
        {
            Server.Session.Verify(value => value.CloseAsync(
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            Server.Session.Verify(value => value.Dispose(), Times.Never);
        }

        private CompanionTarget Target(string id, ExpandedNodeId type, string kind)
        {
            NodeId node = Server.Id(id);
            Server.AddObject(node, type, id);
            return new CompanionTarget("ai", node, id, kind);
        }

        private void SetReference(ExpandedNodeId reference, NodeId target)
        {
            Server.SetReferences(Deployment.NodeId, BrowseDirection.Forward, Server.Resolve(reference),
            [
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId(target),
                    NodeClass = NodeClass.Object,
                    IsForward = true,
                    ReferenceTypeId = Server.Resolve(reference)
                }
            ]);
        }

        private void AddMethod(
            CompanionTarget target, string name, ExpandedNodeId declaration, string? namespaceUri = null)
        {
            string uri = namespaceUri ?? Ai.Namespaces.AI;
            NodeId method = Server.AddProperty(target.NodeId, uri, name, Variant.Null);
            Server.SetValue(method, Variant.From(true), Attributes.Executable);
            Server.SetValue(method, Variant.From(true), Attributes.UserExecutable);
            m_permissions.Add((target.NodeId, uri, name), method);
            m_methodNames[method] = name;
            m_methodNames[Server.Resolve(declaration)] = name;
        }

        private void AddFileMethods(CompanionTarget file)
        {
            AddMethod(file, BrowseNames.Open, MethodIds.FileType_Open, Namespaces.OpcUa);
            AddMethod(file, BrowseNames.Read, MethodIds.FileType_Read, Namespaces.OpcUa);
            AddMethod(file, BrowseNames.Write, MethodIds.FileType_Write, Namespaces.OpcUa);
            AddMethod(file, BrowseNames.Close, MethodIds.FileType_Close, Namespaces.OpcUa);
        }

        private void AddDiscovery()
        {
            NodeId root = new AIClient(Server.Session.Object, Server.Telemetry).AIRootId;
            AddFolder(root, Ai.BrowseNames.Models, [Model, Candidate]);
            AddFolder(root, Ai.BrowseNames.Datasets, [Dataset]);
            AddFolder(root, Ai.BrowseNames.Deployments, [Deployment]);
            AddFolder(root, Ai.BrowseNames.LearningJobs, [Learning]);
            AddFolder(root, Ai.BrowseNames.Jobs, [Job, Transfer]);
            AddFolder(root, Ai.BrowseNames.Evaluations, [Evaluation]);
        }

        private void AddFolder(NodeId root, string name, ArrayOf<CompanionTarget> targets)
        {
            NodeId folder = Server.Id(name + "-folder");
            Server.AddObject(folder, ObjectTypeIds.FolderType);
            Server.AddChild(root, folder, Ai.Namespaces.AI, name);
            foreach (CompanionTarget target in targets)
            {
                Server.AddChild(folder, target.NodeId);
            }
        }

        private CallMethodResult HandleCall(CallMethodRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!m_methodNames.TryGetValue(request.MethodId, out string? method))
            {
                throw new AssertionException("Unexpected AI method: " + request.MethodId);
            }
            if (method == FailingMethod || (method == "Abort" && FailAbort))
            {
                return new CallMethodResult { StatusCode = FailureStatus };
            }
            switch (method)
            {
                case "GetCapabilities":
                    Assert.That(request.ObjectId, Is.EqualTo(Deployment.NodeId));
                    AfterCapabilities?.Invoke();
                    return Good([Variant.FromStructure(Capabilities)]);
                case "Invoke":
                    Assert.That(request.ObjectId, Is.EqualTo(Deployment.NodeId));
                    return Good(
                    [
                        Variant.From(TransferRequired ? ByteString.Empty : Response),
                        Variant.From("application/json"),
                        Variant.From(TransferRequired ? NodeId.Null : ReturnedModel),
                        Variant.FromStructure(new Ai.UsageDataType
                        {
                            InputUnits = 11, OutputUnits = 7, TotalUnits = 18, UnitKind = "tokens"
                        }),
                        Variant.From((int)(TransferRequired ? Ai.FinishReasonEnum.Length : FinishReason)),
                        Variant.FromStructure(ArrayOf<Ai.SafetyAssessmentDataType>.Empty),
                        Variant.From(0d),
                        Variant.From(TransferRequired),
                        Variant.From(TransferRequired ? Transfer.NodeId : NodeId.Null)
                    ]);
                case "InvokeAsync":
                    Assert.That(request.ObjectId, Is.EqualTo(Deployment.NodeId));
                    return Good([Variant.From(ReturnedJob)]);
                case "BeginTransfer":
                    Assert.That(request.ObjectId, Is.EqualTo(Deployment.NodeId));
                    Set(Transfer, Ai.BrowseNames.State, Variant.From((int)Ai.TransferStateEnum.Building));
                    return Good(
                        [Variant.From(BeginAccepted ? Transfer.NodeId : NodeId.Null), Variant.From(BeginAccepted)]);
                case "Open":
                    Assert.That(request.InputArguments[0].TryGetValue(out byte mode), Is.True);
                    Assert.That(request.ObjectId == RequestFile.NodeId || request.ObjectId == ResponseFile.NodeId,
                        Is.True);
                    Assert.That(mode, Is.EqualTo(request.ObjectId == RequestFile.NodeId ? 6 : 1));
                    return Good([Variant.From(request.ObjectId == RequestFile.NodeId ? 37u : 42u)]);
                case "Write":
                    Assert.That(request.ObjectId, Is.EqualTo(RequestFile.NodeId));
                    Assert.That(request.InputArguments[0].TryGetValue(out uint writeHandle), Is.True);
                    Assert.That(writeHandle, Is.EqualTo(37u));
                    Assert.That(request.InputArguments[1].TryGetValue(out ByteString data), Is.True);
                    Assert.That(data.Length, Is.InRange(1, 4096));
                    Uploaded.AddRange(data.Span.ToArray());
                    return Good();
                case "Read":
                    Assert.That(request.ObjectId, Is.EqualTo(ResponseFile.NodeId));
                    Assert.That(request.InputArguments[0].TryGetValue(out uint readHandle), Is.True);
                    Assert.That(readHandle, Is.EqualTo(42u));
                    Assert.That(request.InputArguments[1].TryGetValue(out int maximum), Is.True);
                    Assert.That(maximum, Is.EqualTo(4096));
                    int count = Math.Min(Math.Min(maximum, ResponseChunkLimit), Response.Length - m_responsePosition);
                    var chunk = ByteString.From(Response.Span.Slice(m_responsePosition, count));
                    m_responsePosition += count;
                    return Good([Variant.From(chunk)]);
                case "Close":
                    Assert.That(token.CanBeCanceled, Is.True, "Owned handle cleanup must have its own deadline.");
                    Assert.That(request.ObjectId == RequestFile.NodeId || request.ObjectId == ResponseFile.NodeId,
                        Is.True);
                    Assert.That(request.InputArguments[0].TryGetValue(out uint closeHandle), Is.True);
                    Assert.That(closeHandle, Is.EqualTo(request.ObjectId == RequestFile.NodeId ? 37u : 42u));
                    return Good();
                case "Execute":
                    Assert.That(request.ObjectId, Is.EqualTo(Transfer.NodeId));
                    return Good([Variant.From(true)]);
                case "Abort":
                    Assert.That(request.ObjectId, Is.EqualTo(Transfer.NodeId));
                    Assert.That(request.InputArguments.IsEmpty, Is.True);
                    return Good();
                case "Halt":
                    Assert.That(request.ObjectId == Job.NodeId || request.ObjectId == Learning.NodeId, Is.True);
                    Assert.That(request.InputArguments.IsEmpty, Is.True);
                    return Good();
                case "StartCollection":
                case "StopCollection":
                    Assert.That(request.ObjectId, Is.EqualTo(Learning.NodeId));
                    return Good();
                case "TriggerTraining":
                    Assert.That(request.ObjectId, Is.EqualTo(Learning.NodeId));
                    return Good([Variant.From(TrainingAccepted)]);
                case "PromoteModel":
                    Assert.That(request.ObjectId, Is.EqualTo(Learning.NodeId));
                    return Good([Variant.From(Candidate.NodeId)]);
                default:
                    throw new AssertionException("Unspecified typed AI method: " + method);
            }
        }

        private readonly Dictionary<(NodeId Parent, string Name), NodeId> m_properties = [];
        private readonly Dictionary<(NodeId Parent, string Namespace, string Name), NodeId> m_permissions = [];
        private readonly Dictionary<NodeId, string> m_methodNames = [];
        private readonly Dictionary<NodeId, AIWorkflowStateNodes> m_states = [];
        private int m_responsePosition;
    }
}
