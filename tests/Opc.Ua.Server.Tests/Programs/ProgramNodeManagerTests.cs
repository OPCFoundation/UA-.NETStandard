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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Programs;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.Programs;
using Opc.Ua.Server.Tests.NodeManager;
using Opc.Ua.Tests;
using ProgramTypeIds = Opc.Ua.Programs.ObjectTypeIds;

namespace Opc.Ua.Server.Tests.Programs
{
    /// <summary>
    /// Tests checked Program admission, type policy, provider ownership and managed address-space removal.
    /// </summary>
    [TestFixture]
    [Category("Programs")]
    [Parallelizable(ParallelScope.All)]
    public sealed class ProgramNodeManagerTests
    {
        [Test]
        public void FactoryRejectsDuplicateConcreteTypes()
        {
            var provider = new ControlledProvider();
            ProgramDefinition definition = CreateDefinition(provider);
            Assert.That(
                () => new ProgramNodeManagerFactory([definition, definition]),
                Throws.ArgumentException);
        }

        [Test]
        public void FactoryRejectsUnregisteredAddNodesTypes()
        {
            Assert.That(
                () => new ProgramNodeManagerFactory(
                    default, addNodesTypes: [ProgramTypeIds.ManagedProgramType]),
                Throws.ArgumentException);
        }

        [Test]
        public void FluentRegistrationUsesOneHostWithoutCreatingWorkloads()
        {
            var builder = new Mock<IOpcUaServerBuilder>();
            builder.SetupGet(value => value.Services).Returns(new ServiceCollection());
            var provider = new ControlledProvider();
            ProgramDefinition definition = CreateDefinition(provider);
            Assert.That(builder.Object.AddPrograms([definition]), Is.SameAs(builder.Object));
            Assert.That(provider.CreateCount, Is.Zero);
            Assert.That(() => builder.Object.AddPrograms([definition]), Throws.InvalidOperationException);
        }

        [Test]
        public async Task RegistrationDoesNotCreateOrStartAWorkload()
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);
            Assert.That(harness.Provider.CreateCount, Is.Zero);
            Assert.That(harness.Manager.GetInstanceCount(ProgramTypeIds.ManagedProgramType), Is.Zero);
            Assert.That(harness.Manager.Find(harness.Manager.RootId), Is.TypeOf<FolderState>());
            Assert.That(harness.Manager.NamespaceIndexes, Does.Not.Contain((ushort)0));
            NodeId typeId = ExpandedNodeId.ToNodeId(
                ProgramTypeIds.ManagedProgramType, harness.Manager.SystemContext.NamespaceUris);
            NodeState type = harness.Manager.Find(typeId)!;
            BaseVariableState count = (BaseVariableState)type.FindChildWithQualifiedName(
                harness.Manager.SystemContext, new QualifiedName(BrowseNames.InstanceCount))!;
            Assert.That(count.WrappedValue.TryGetValue(out uint value), Is.True);
            Assert.That(value, Is.Zero);
        }

        [Test]
        public async Task ServerCreationCountsEvenWhenClientsCannotCreate()
        {
            await using Harness harness = await Harness.CreateAsync(creatable: false).ConfigureAwait(false);
            ProgramRuntime runtime = await harness.CreateAsync("First").ConfigureAwait(false);
            Assert.That(runtime.CurrentState, Is.EqualTo(ProgramState.Ready));
            Assert.That(harness.Provider.CreateCount, Is.EqualTo(1));
            Assert.That(harness.Provider.Executions[0].ControlCount, Is.Zero);
            Assert.That(harness.Manager.GetInstanceCount(ProgramTypeIds.ManagedProgramType), Is.EqualTo(1));
            Assert.That(
                async () => await harness.CreateAsync("Second").ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(harness.Provider.CreateCount, Is.EqualTo(1));
        }

        [Test]
        public async Task FailedProviderInitializationReleasesCapacity()
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);
            harness.Provider.FailNext = true;
            Assert.That(
                async () => await harness.CreateAsync("Failure").ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(harness.Manager.GetInstanceCount(ProgramTypeIds.ManagedProgramType), Is.Zero);
            ProgramRuntime runtime = await harness.CreateAsync("Success").ConfigureAwait(false);
            Assert.That(harness.Manager.Find(runtime.Node.NodeId), Is.SameAs(runtime.Node));
            Assert.That(harness.Manager.GetInstanceCount(ProgramTypeIds.ManagedProgramType), Is.EqualTo(1));
        }

        [Test]
        public async Task ConcurrentAdmissionsDoNotExceedCapacity()
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);
            harness.Provider.AdmissionStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Provider.ReleaseAdmission = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task<ProgramRuntime> first = harness.CreateAsync("First").AsTask();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await harness.Provider.AdmissionStarted.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            Task<ProgramRuntime> second = harness.CreateAsync("Second").AsTask();
            harness.Provider.ReleaseAdmission.TrySetResult(true);
            await first.ConfigureAwait(false);
            Assert.That(() => second,
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(harness.Provider.CreateCount, Is.EqualTo(1));
            Assert.That(harness.Manager.GetInstanceCount(ProgramTypeIds.ManagedProgramType), Is.EqualTo(1));
        }

        [Test]
        public async Task DisposeCancelsProviderAdmissionBeforeWaitingForOperationsToDrain()
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);
            harness.Provider.AdmissionStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Provider.ReleaseAdmission = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task<ProgramRuntime> creation = harness.CreateAsync("Pending").AsTask();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await harness.Provider.AdmissionStarted.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            await harness.Manager.DisposeAsync().AsTask().WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(() => creation, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(harness.Provider.Executions, Is.Empty);
        }

        [Test]
        public async Task AdoptedInvocationUsesTheSameCapacityAndReceivesFinalChildIds()
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);
            var node = new ManagedProgramState(null);
            node.Create(harness.Manager.SystemContext, NodeId.Null,
                harness.Name("Adopted"), default, true);
            ProgramRuntime runtime = await harness.Manager.AdoptInvocationAsync(
                ProgramTypeIds.ManagedProgramType, node).ConfigureAwait(false);
            Assert.That(runtime.Node, Is.SameAs(node));
            Assert.That(harness.Manager.GetInstanceCount(ProgramTypeIds.ManagedProgramType), Is.EqualTo(1));
            var children = new List<BaseInstanceState>();
            node.GetChildren(harness.Manager.SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                Assert.That(child.NodeId.NamespaceIndex, Is.EqualTo(harness.Manager.NamespaceIndex));
                Assert.That(harness.Manager.Find(child.NodeId), Is.SameAs(child));
            }
        }

        [Test]
        public async Task ReadoptingAnIndexedInvocationDoesNotRemoveOrDisposeIt()
        {
            await using Harness harness = await Harness.CreateAsync(maxInstances: 2).ConfigureAwait(false);
            ProgramRuntime runtime = await harness.CreateAsync("Invocation").ConfigureAwait(false);
            Assert.That(
                async () => await harness.Manager.AdoptInvocationAsync(
                    ProgramTypeIds.ManagedProgramType, runtime.Node).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(harness.Manager.Find(runtime.Node.NodeId), Is.SameAs(runtime.Node));
            Assert.That(harness.Manager.TryGetRuntime(runtime.Node.NodeId, out ProgramRuntime? registered), Is.True);
            Assert.That(registered, Is.SameAs(runtime));
            Assert.That(harness.Provider.Executions[0].DisposeCount, Is.Zero);
            Assert.That(harness.Manager.GetInstanceCount(ProgramTypeIds.ManagedProgramType), Is.EqualTo(1));
        }

        [Test]
        public async Task FactoryCannotReuseAnIndexedInvocationForADifferentBrowseName()
        {
            ProgramStateMachineState? singleton = null;
            await using Harness harness = await Harness.CreateAsync(
                maxInstances: 2, nodeFactory: parent => singleton ??= new ManagedProgramState(parent))
                .ConfigureAwait(false);
            ProgramRuntime runtime = await harness.CreateAsync("First").ConfigureAwait(false);
            Assert.That(async () => await harness.CreateAsync("Second").ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(runtime.Node.BrowseName, Is.EqualTo(harness.Name("First")));
            Assert.That(harness.Manager.Find(runtime.Node.NodeId), Is.SameAs(runtime.Node));
            Assert.That(harness.Provider.CreateCount, Is.EqualTo(1));
            Assert.That(harness.Provider.Executions[0].DisposeCount, Is.Zero);
        }

        [Test]
        public async Task UnknownTypeCountIsAnErrorRatherThanASuccessShapedZero()
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);
            Assert.That(
                () => harness.Manager.GetInstanceCount(new ExpandedNodeId(1u, "urn:unregistered-program-type")),
                Throws.ArgumentException);
        }

        [Test]
        public async Task HostedServersHaveIndependentInvocationCapacity()
        {
            await using Harness first = await Harness.CreateAsync().ConfigureAwait(false);
            await using Harness second = await Harness.CreateAsync().ConfigureAwait(false);
            await first.CreateAsync("First").ConfigureAwait(false);
            await second.CreateAsync("Second").ConfigureAwait(false);
            Assert.That(first.Manager.GetInstanceCount(ProgramTypeIds.ManagedProgramType), Is.EqualTo(1));
            Assert.That(second.Manager.GetInstanceCount(ProgramTypeIds.ManagedProgramType), Is.EqualTo(1));
        }

        [TestCase(ProgramState.Ready, false)]
        [TestCase(ProgramState.Halted, true)]
        public async Task ClientDeletionRequiresHaltedAndDisposesExactlyOnce(ProgramState initialState, bool succeeds)
        {
            StatusCode expected = succeeds ? StatusCodes.Good : StatusCodes.BadInvalidState;
            await using Harness harness = await Harness.CreateAsync(initialState: initialState).ConfigureAwait(false);
            ProgramRuntime runtime = await harness.CreateAsync("Invocation").ConfigureAwait(false);
            ServiceResult result = await harness.Manager.DeleteNodeAsync(
                harness.OperationContext,
                new DeleteNodesItem { NodeId = runtime.Node.NodeId, DeleteTargetReferences = true })
                .ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(expected));
            if (expected == StatusCodes.Good)
            {
                Assert.That(harness.Manager.Find(runtime.Node.NodeId), Is.Null);
                Assert.That(harness.Manager.GetInstanceCount(ProgramTypeIds.ManagedProgramType), Is.Zero);
                Assert.That(harness.Provider.Executions[0].DisposeCount, Is.EqualTo(1));
                result = await harness.Manager.DeleteNodeAsync(harness.OperationContext,
                    new DeleteNodesItem { NodeId = runtime.Node.NodeId }).ConfigureAwait(false);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(harness.Provider.Executions[0].DisposeCount, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task ClientCannotDeleteNonDeletableInvocationOrBypassThroughChildrenAndFolder()
        {
            await using Harness harness = await Harness.CreateAsync(
                initialState: ProgramState.Halted, deletable: false).ConfigureAwait(false);
            ProgramRuntime runtime = await harness.CreateAsync("Invocation").ConfigureAwait(false);
            NodeId[] protectedNodes =
                [runtime.Node.NodeId, runtime.Node.CurrentState!.NodeId, harness.Manager.RootId];
            foreach (NodeId id in protectedNodes)
            {
                ServiceResult result = await harness.Manager.DeleteNodeAsync(
                    harness.OperationContext, new DeleteNodesItem { NodeId = id }).ConfigureAwait(false);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
            Assert.That(harness.Manager.GetInstanceCount(ProgramTypeIds.ManagedProgramType), Is.EqualTo(1));
        }

        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        public async Task AddNodesRequiresExplicitTypeOptInAndCreatable(bool addNodes, bool creatable, bool succeeds)
        {
            StatusCode expected = succeeds ? StatusCodes.Good : StatusCodes.BadUserAccessDenied;
            await using Harness harness = await Harness.CreateAsync(
                addNodes: addNodes, creatable: creatable).ConfigureAwait(false);
            (ServiceResult result, NodeId nodeId) = await harness.Manager.AddNodeAsync(
                harness.OperationContext, harness.AddItem("Created")).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(expected));
            Assert.That(nodeId.IsNull, Is.EqualTo(expected != StatusCodes.Good));
            Assert.That(harness.Provider.CreateCount, Is.EqualTo(expected == StatusCodes.Good ? 1 : 0));
        }

        [Test]
        public async Task AddNodesRejectsForeignParentAndBrowseNamespaceBeforeProviderInitialization()
        {
            await using Harness harness = await Harness.CreateAsync(addNodes: true).ConfigureAwait(false);
            AddNodesItem item = harness.AddItem("Created");
            item.ParentNodeId = ObjectIds.ObjectsFolder;
            (ServiceResult result, _) = await harness.Manager.AddNodeAsync(harness.OperationContext, item)
                .ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadParentNodeIdInvalid));
            item.ParentNodeId = harness.Manager.RootId;
            item.BrowseName = new QualifiedName("Created");
            (result, _) = await harness.Manager.AddNodeAsync(harness.OperationContext, item).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameInvalid));
            Assert.That(harness.Provider.CreateCount, Is.Zero);
        }

        [Test]
        public async Task ClientReferenceMutationIsDenied()
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);
            ServiceResult add = await harness.Manager.AddReferenceAsync(
                harness.OperationContext, new AddReferencesItem()).ConfigureAwait(false);
            ServiceResult delete = await harness.Manager.DeleteReferenceAsync(
                harness.OperationContext, new DeleteReferencesItem()).ConfigureAwait(false);
            Assert.That(add.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(delete.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task TypeCreateMethodUsesItsDeclaredSignatureAndChecksTheParent()
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);
            NodeId methodId = ExpandedNodeId.ToNodeId(
                Opc.Ua.Programs.MethodIds.ManagedProgramType_Create, harness.Manager.SystemContext.NamespaceUris);
            var method = (MethodState)harness.Manager.Find(methodId)!;
            NodeId typeId = ExpandedNodeId.ToNodeId(
                ProgramTypeIds.ManagedProgramType, harness.Manager.SystemContext.NamespaceUris);
            ServerSystemContext context = harness.Manager.SystemContext.Copy(harness.OperationContext);
            var outputs = new List<Variant>();
            var argumentErrors = new List<ServiceResult>();
            ServiceResult rejected = await method.CallAsync(
                context, typeId,
                [new Variant(ObjectIds.ObjectsFolder), new Variant(harness.Name("Rejected"))],
                argumentErrors, outputs).ConfigureAwait(false);
            Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadParentNodeIdInvalid));
            Assert.That(harness.Provider.CreateCount, Is.Zero);
            outputs.Clear();
            argumentErrors.Clear();
            ServiceResult accepted = await method.CallAsync(
                context, typeId,
                [new Variant(harness.Manager.RootId), new Variant(harness.Name("Accepted"))],
                argumentErrors, outputs).ConfigureAwait(false);
            Assert.That(accepted.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(outputs, Has.Count.EqualTo(1));
            Assert.That(outputs[0].TryGetValue(out NodeId invocationId), Is.True);
            Assert.That(harness.Manager.TryGetRuntime(invocationId, out _), Is.True);
        }

        [Test]
        public async Task CreationChecksParentPermissionsBeforeCreatingProviderResources()
        {
            await using Harness harness = await Harness.CreateAsync(addNodes: true).ConfigureAwait(false);
            NodeState root = harness.Manager.Find(harness.Manager.RootId)!;
            root.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_Anonymous,
                    Permissions = (uint)PermissionType.Browse
                }
            ];
            var identity = new Mock<IUserIdentity>();
            identity.SetupGet(value => value.GrantedRoleIds).Returns([ObjectIds.WellKnownRole_Anonymous]);
            var clientContext = new OperationContext(
                new RequestHeader(), null, RequestType.AddNodes, RequestLifetime.None, identity.Object);
            (ServiceResult result, _) = await harness.Manager.AddNodeAsync(
                clientContext, harness.AddItem("Denied")).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(harness.Provider.CreateCount, Is.Zero);
        }

        [Test]
        public async Task ParentPermissionCannotOverrideNamespaceCreationRestrictions()
        {
            await using Harness harness = await Harness.CreateAsync(addNodes: true).ConfigureAwait(false);
            NodeState root = harness.Manager.Find(harness.Manager.RootId)!;
            root.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_Anonymous,
                    Permissions = (uint)(PermissionType.AddReference | PermissionType.AddNode)
                }
            ];
            var metadata = new NamespaceMetadataState(null);
            metadata.DefaultRolePermissions =
                PropertyState<ArrayOf<RolePermissionType>>.With<StructureBuilder<RolePermissionType>>(
                    metadata,
                    [
                        new RolePermissionType
                        {
                            RoleId = ObjectIds.WellKnownRole_Anonymous,
                            Permissions = (uint)PermissionType.Browse
                        }
                    ]);
            Mock.Get(harness.Manager.SystemContext.Server.NodeManager.ConfigurationNodeManager!)
                .Setup(value => value.GetNamespaceMetadataStateAsync(
                    harness.Manager.NamespaceIndex, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NamespaceMetadataState?>(metadata));
            var identity = new Mock<IUserIdentity>();
            identity.SetupGet(value => value.GrantedRoleIds).Returns([ObjectIds.WellKnownRole_Anonymous]);
            var context = new OperationContext(
                new RequestHeader(), null, RequestType.AddNodes, RequestLifetime.None, identity.Object);
            (ServiceResult result, _) = await harness.Manager.AddNodeAsync(context, harness.AddItem("Denied"))
                .ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(harness.Provider.CreateCount, Is.Zero);
        }

        [Test]
        public async Task InvocationEventsPropagateToTheOwningFolderOnce()
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);
            ProgramRuntime runtime = await harness.CreateAsync("Invocation").ConfigureAwait(false);
            int eventCount = 0;
            NodeState root = harness.Manager.Find(harness.Manager.RootId)!;
            root.OnReportEvent = (_, _, _) => Interlocked.Increment(ref eventCount);
            MethodInvocationResult result = await runtime.ControlAsync(
                harness.Manager.SystemContext, ProgramControlMethod.Start).ConfigureAwait(false);
            Assert.That(result.OperationResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(eventCount, Is.EqualTo(1));
        }

        [Test]
        public async Task AutoDeletionWaitsForExecutionTerminationNotInitialHaltedState()
        {
            await using Harness harness = await Harness.CreateAsync(
                initialState: ProgramState.Halted, autoDelete: true).ConfigureAwait(false);
            ProgramRuntime runtime = await harness.CreateAsync("Invocation").ConfigureAwait(false);
            Assert.That(harness.Manager.Find(runtime.Node.NodeId), Is.SameAs(runtime.Node));
            await runtime.ControlAsync(harness.Manager.SystemContext, ProgramControlMethod.Reset)
                .ConfigureAwait(false);
            await runtime.ControlAsync(harness.Manager.SystemContext, ProgramControlMethod.Start)
                .ConfigureAwait(false);
            MethodInvocationResult halt = await runtime.ControlAsync(
                harness.Manager.SystemContext, ProgramControlMethod.Halt).ConfigureAwait(false);
            Assert.That(halt.OperationResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await harness.Provider.Executions[0].Disposed.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(harness.Manager.Find(runtime.Node.NodeId), Is.Null);
            Assert.That(harness.Manager.GetInstanceCount(ProgramTypeIds.ManagedProgramType), Is.Zero);
        }

        [Test]
        public async Task ShutdownRemovesInvocationsAndAwaitsOwnedExecution()
        {
            await using Harness harness = await Harness.CreateAsync(deletable: false).ConfigureAwait(false);
            ProgramRuntime runtime = await harness.CreateAsync("Invocation").ConfigureAwait(false);
            await harness.Manager.DeleteAddressSpaceAsync().ConfigureAwait(false);
            Assert.That(harness.Manager.Find(runtime.Node.NodeId), Is.Null);
            Assert.That(harness.Provider.Executions[0].DisposeCount, Is.EqualTo(1));
            Assert.That(harness.Manager.GetInstanceCount(ProgramTypeIds.ManagedProgramType), Is.Zero);
            Assert.That(harness.Provider.LastTimeProvider, Is.SameAs(harness.Clock));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ProgramAuditsDistinguishClientAndInternalTransitionsWithoutSuppressingRepeatedCauses(
            bool auditing)
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);
            IServerInternal server = harness.Manager.SystemContext.Server;
            Mock<IServerInternal> mock = Mock.Get(server);
            mock.SetupGet(value => value.Auditing).Returns(auditing);
            var audits = new List<AuditEventState>();
            mock.Setup(value => value.ReportAuditEvent(It.IsAny<ISystemContext>(), It.IsAny<AuditEventState>()))
                .Callback<ISystemContext, AuditEventState>((_, value) => audits.Add(value));
            ProgramRuntime runtime = await harness.CreateAsync("Audited").ConfigureAwait(false);
            var identity = new Mock<IUserIdentity>();
            identity.SetupGet(value => value.DisplayName).Returns("Program operator");
            var caller = new ServerSystemContext(server)
            {
                SessionId = new NodeId("Creator", harness.Manager.NamespaceIndex),
                UserIdentity = identity.Object,
                AuditEntryId = "ProgramCall"
            };
            await runtime.ControlAsync(caller, ProgramControlMethod.Start).ConfigureAwait(false);
            await runtime.ApplyUpdateAsync(new ProgramExecutionUpdate(runtime.RunId, ProgramState.Ready))
                .ConfigureAwait(false);
            await runtime.ControlAsync(caller, ProgramControlMethod.Start).ConfigureAwait(false);
            MethodInvocationResult rejected = await runtime.ControlAsync(caller, ProgramControlMethod.Start)
                .ConfigureAwait(false);
            Assert.That(rejected.OperationResult.StatusCode, Is.EqualTo(StatusCodes.BadNotExecutable));
            AuditProgramTransitionEventState[] transitions =
                audits.OfType<AuditProgramTransitionEventState>().ToArray();
            Assert.That(transitions, Has.Length.EqualTo(auditing ? 3 : 0));
            if (auditing)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(transitions[0].Status!.Value, Is.True);
                    Assert.That(transitions[0].SourceName!.Value, Is.EqualTo("Method/Start"));
                    Assert.That(transitions[0].ClientUserId!.Value, Is.EqualTo("Program operator"));
                    Assert.That(transitions[0].TransitionNumber!.Value, Is.EqualTo(2));
                    Assert.That(transitions[1].Status!.Value, Is.False);
                    Assert.That(transitions[1].SourceName!.Value, Is.EqualTo("Internal"));
                    Assert.That(transitions[1].ClientUserId!.Value, Is.Null.Or.Empty);
                    Assert.That(transitions[1].ClientAuditEntryId!.Value, Is.Null.Or.Empty);
                    Assert.That(transitions[1].TransitionNumber!.Value, Is.EqualTo(4));
                    Assert.That(transitions[2].TransitionNumber!.Value, Is.EqualTo(2));
                    Assert.That(transitions[2].ActionTimeStamp!.Value,
                        Is.EqualTo(runtime.Node.LastTransition!.TransitionTime!.Value));
                });
            }
        }

        private static ProgramDefinition CreateDefinition(
            ControlledProvider provider,
            bool creatable = true,
            bool deletable = true,
            ProgramState initialState = ProgramState.Ready,
            bool autoDelete = false,
            uint maxInstances = 1,
            Func<NodeState?, ProgramStateMachineState>? nodeFactory = null)
        {
            return new ProgramDefinition(
                ProgramTypeIds.ManagedProgramType,
                nodeFactory ?? (static parent => new ManagedProgramState(parent)),
                provider,
                [ProgramControlMethod.Start, ProgramControlMethod.Halt, ProgramControlMethod.Reset],
                initialState,
                maxInstances,
                3)
            {
                Creatable = creatable,
                Deletable = deletable,
                AutoDelete = autoDelete,
                DiagnosticsEnabled = false
            };
        }

        private sealed class Harness(
            ProgramNodeManager manager,
            ControlledProvider provider,
            MonitoredItemQueueFactory queues,
            FakeTimeProvider clock) : IAsyncDisposable
        {
            public ProgramNodeManager Manager { get; } = manager;

            public ControlledProvider Provider { get; } = provider;

            public FakeTimeProvider Clock { get; } = clock;

            public OperationContext OperationContext { get; } = CreateOperationContext();

            public static async Task<Harness> CreateAsync(
                bool creatable = true,
                bool deletable = true,
                ProgramState initialState = ProgramState.Ready,
                bool autoDelete = false,
                bool addNodes = false,
                uint maxInstances = 1,
                Func<NodeState?, ProgramStateMachineState>? nodeFactory = null)
            {
                var clock = new FakeTimeProvider();
                Mock<IServerInternal> server = DeterministicServerMock.Create(
                    out MonitoredItemQueueFactory queues, clock);
                server.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
                var tree = (TypeTable)server.Object.TypeTree;
                tree.AddSubtype(ObjectTypeIds.BaseObjectType, NodeId.Null);
                tree.AddSubtype(ObjectTypeIds.StateMachineType, ObjectTypeIds.BaseObjectType);
                tree.AddSubtype(ObjectTypeIds.FiniteStateMachineType, ObjectTypeIds.StateMachineType);
                tree.AddSubtype(ObjectTypeIds.ProgramStateMachineType, ObjectTypeIds.FiniteStateMachineType);
                tree.AddSubtype(ObjectTypeIds.BaseEventType, ObjectTypeIds.BaseObjectType);
                tree.AddSubtype(ObjectTypeIds.TransitionEventType, ObjectTypeIds.BaseEventType);
                tree.AddSubtype(ObjectTypeIds.ProgramTransitionEventType, ObjectTypeIds.TransitionEventType);
                tree.AddSubtype(ObjectTypeIds.AuditEventType, ObjectTypeIds.BaseEventType);
                tree.AddSubtype(ObjectTypeIds.AuditUpdateMethodEventType, ObjectTypeIds.AuditEventType);
                tree.AddSubtype(ObjectTypeIds.AuditUpdateStateEventType, ObjectTypeIds.AuditUpdateMethodEventType);
                tree.AddSubtype(
                    ObjectTypeIds.AuditProgramTransitionEventType, ObjectTypeIds.AuditUpdateStateEventType);
                var provider = new ControlledProvider();
                ProgramDefinition definition = CreateDefinition(
                    provider, creatable, deletable, initialState, autoDelete, maxInstances, nodeFactory);
                var manager = new ProgramNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    [definition],
                    addNodesTypes: addNodes ? [ProgramTypeIds.ManagedProgramType] : default);
                Mock.Get(server.Object.NodeManager).SetupGet(value => value.AsyncNodeManagers).Returns([manager]);
                var harness = new Harness(manager, provider, queues, clock);
                try
                {
                    await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                        .ConfigureAwait(false);
                    return harness;
                }
                catch
                {
                    await harness.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            public QualifiedName Name(string name)
            {
                return new QualifiedName(name, Manager.NamespaceIndex);
            }

            public ValueTask<ProgramRuntime> CreateAsync(string name)
            {
                return Manager.CreateInvocationAsync(ProgramTypeIds.ManagedProgramType, Name(name));
            }

            public AddNodesItem AddItem(string name)
            {
                return new AddNodesItem
                {
                    ParentNodeId = Manager.RootId,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    BrowseName = Name(name),
                    NodeClass = NodeClass.Object,
                    TypeDefinition = ProgramTypeIds.ManagedProgramType
                };
            }

            public async ValueTask DisposeAsync()
            {
                await Manager.DisposeAsync().ConfigureAwait(false);
                queues.Dispose();
            }

            private static OperationContext CreateOperationContext()
            {
                var session = new Mock<ISession>();
                session.SetupGet(value => value.EffectiveIdentity).Returns(new Mock<IUserIdentity>().Object);
                session.SetupGet(value => value.PreferredLocales).Returns([]);
                return new OperationContext(new RequestHeader(), null!, RequestType.AddNodes,
                    RequestLifetime.None, session.Object);
            }
        }

        private sealed class ControlledProvider : IProgramProvider
        {
            public bool FailNext { get; set; }

            public TaskCompletionSource<bool>? AdmissionStarted { get; set; }

            public TaskCompletionSource<bool>? ReleaseAdmission { get; set; }

            public int CreateCount { get; private set; }

            public TimeProvider? LastTimeProvider { get; private set; }

            public List<ControlledExecution> Executions { get; } = [];

            public async ValueTask<IProgramExecution> CreateAsync(
                ProgramInvocationContext context,
                CancellationToken cancellationToken = default)
            {
                CreateCount++;
                LastTimeProvider = context.TimeProvider;
                AdmissionStarted?.TrySetResult(true);
                if (ReleaseAdmission is { } release)
                {
                    await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                if (FailNext)
                {
                    FailNext = false;
                    throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                }
                var execution = new ControlledExecution();
                Executions.Add(execution);
                return execution;
            }
        }

        private sealed class ControlledExecution : IProgramExecution
        {
            public int ControlCount { get; private set; }

            public int DisposeCount { get; private set; }

            public TaskCompletionSource<bool> Disposed { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public ValueTask<ProgramControlResult> ControlAsync(
                ProgramControlContext context,
                CancellationToken cancellationToken = default)
            {
                ControlCount++;
                return new ValueTask<ProgramControlResult>(new ProgramControlResult(ServiceResult.Good, default));
            }

            public async IAsyncEnumerable<ProgramExecutionUpdate> ObserveAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await foreach (ProgramExecutionUpdate update in
                    m_updates.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    yield return update;
                }
            }

            public ValueTask DisposeAsync()
            {
                DisposeCount++;
                m_updates.Writer.TryComplete();
                Disposed.TrySetResult(true);
                return default;
            }

            private readonly Channel<ProgramExecutionUpdate> m_updates =
                Channel.CreateUnbounded<ProgramExecutionUpdate>();
        }
    }
}
