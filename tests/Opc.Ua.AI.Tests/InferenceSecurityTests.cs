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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.AI.Inference;
using Opc.Ua.AI.Server;
using Opc.Ua.Server;
using CoreObjectIds = Opc.Ua.ObjectIds;

namespace Opc.Ua.AI.Tests
{
    /// <summary>
    /// Exercises inference ownership and result integrity without a transport.
    /// </summary>
    [TestFixture]
    [Category("AIModelManagement")]
    public sealed class InferenceSecurityTests
    {
        /// <summary>
        /// The independently authenticated caller attempting to use a transfer.
        /// </summary>
        public enum OtherCaller
        {
            /// <summary>
            /// A different authenticated user.
            /// </summary>
            DifferentUser,

            /// <summary>
            /// The same user on another session.
            /// </summary>
            SameUserNewSession,

            /// <summary>
            /// A different anonymous session.
            /// </summary>
            AnonymousNewSession,

            /// <summary>
            /// A new identity activated on the original session.
            /// </summary>
            ChangedIdentitySameSession
        }

        /// <summary>
        /// Another caller cannot discover, read, alter, execute or abort a transfer.
        /// </summary>
        [TestCase(false, OtherCaller.DifferentUser)]
        [TestCase(true, OtherCaller.DifferentUser)]
        [TestCase(false, OtherCaller.SameUserNewSession)]
        [TestCase(true, OtherCaller.SameUserNewSession)]
        [TestCase(false, OtherCaller.AnonymousNewSession)]
        [TestCase(true, OtherCaller.AnonymousNewSession)]
        [TestCase(false, OtherCaller.ChangedIdentitySameSession)]
        [TestCase(true, OtherCaller.ChangedIdentitySameSession)]
        [Category("SecurityRegression")]
        public async Task SecurityF01TransfersRejectOtherCallers(bool oversizeInvoke, OtherCaller otherCaller)
        {
            var backend = new ControlledInferenceBackend();
            backend.Release();
            using AINodeManager nm = await CreateAsync(backend).ConfigureAwait(false);
            string? ownerName = otherCaller == OtherCaller.AnonymousNewSession ? null : "creator";
            string? otherName = otherCaller switch
            {
                OtherCaller.SameUserNewSession => ownerName,
                OtherCaller.AnonymousNewSession => null,
                _ => "other"
            };
            using var owner = new Caller(nm, 1, Identity(ownerName));
            using var other = new Caller(
                nm,
                otherCaller == OtherCaller.ChangedIdentitySameSession ? 1u : 2u,
                Identity(otherName));
            InferenceTransferState transfer = await BeginTransferAsync(nm, owner, oversizeInvoke)
                .ConfigureAwait(false);
            FileState request = transfer.Request!;
            uint writeHandle = Open(owner, request, 6);
            Assert.That(
                request.Write!.OnCall!(
                    owner.Context, request.Write, request.NodeId, writeHandle, Bytes(RequestPayload)).StatusCode,
                Is.EqualTo(StatusCodes.Good));

            await AssertTreeDeniedAsync(nm, other, transfer).ConfigureAwait(false);
            Assert.That(await IsVisibleAsync(nm, other, transfer.NodeId).ConfigureAwait(false), Is.False);
            Assert.That(await IsVisibleAsync(nm, owner, transfer.NodeId).ConfigureAwait(false), Is.True);

            foreach (FileState file in new[] { request, transfer.Response! })
            {
                foreach (byte mode in new byte[] { 1, 6 })
                {
                    uint deniedHandle = 0;
                    ServiceResult denied = file.Open!.OnCall!(
                        other.Context, file.Open, file.NodeId, mode, ref deniedHandle);
                    Assert.Multiple(() =>
                    {
                        Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                        Assert.That(deniedHandle, Is.Zero);
                    });
                }
            }

            ByteString deniedBytes = ByteString.Empty;
            ulong position = 0;
            Assert.Multiple(() =>
            {
                Assert.That(request.Read!.OnCall!(
                    other.Context, request.Read, request.NodeId, writeHandle, 4096, ref deniedBytes).StatusCode,
                    Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(deniedBytes.IsEmpty, Is.True);
                Assert.That(request.Write!.OnCall!(
                    other.Context, request.Write, request.NodeId, writeHandle, Bytes("replacement")).StatusCode,
                    Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(request.SetPosition!.OnCall!(
                    other.Context, request.SetPosition, request.NodeId, writeHandle, 0).StatusCode,
                    Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(request.GetPosition!.OnCall!(
                    other.Context, request.GetPosition, request.NodeId, writeHandle, ref position).StatusCode,
                    Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(request.Close!.OnCall!(
                    other.Context, request.Close, request.NodeId, writeHandle).StatusCode,
                    Is.EqualTo(StatusCodes.BadUserAccessDenied));
            });

            ExecuteMethodStateResult refused = await transfer.Execute!.OnCallAsync!(
                other.Context, transfer.Execute, transfer.NodeId, CancellationToken.None).ConfigureAwait(false);
            ServiceResult abortRefused = await transfer.Abort!.OnCallMethod2Async!(
                other.Context, transfer.Abort, transfer.NodeId, [], [],
                CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(refused.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(refused.Accepted, Is.False);
                Assert.That(abortRefused.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(backend.InvocationCount, Is.Zero);
                Assert.That(transfer.State!.Value, Is.EqualTo(TransferStateEnum.Building));
                Assert.That(nm.IndexedNode(transfer.NodeId), Is.SameAs(transfer));
                Assert.That(request.GetPosition!.OnCall!(
                    owner.Context, request.GetPosition, request.NodeId, writeHandle, ref position).StatusCode,
                    Is.EqualTo(StatusCodes.Good));
                Assert.That(position, Is.EqualTo((ulong)Encoding.UTF8.GetByteCount(RequestPayload)));
                Assert.That(Read(owner, request), Is.EqualTo(RequestPayload));
            });
            Assert.That(request.Close!.OnCall!(
                owner.Context, request.Close, request.NodeId, writeHandle).StatusCode,
                Is.EqualTo(StatusCodes.Good));

            ExecuteMethodStateResult executed = await transfer.Execute.OnCallAsync!(
                owner.Context, transfer.Execute, transfer.NodeId, CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(executed.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(executed.Accepted, Is.True);
                Assert.That(backend.InvocationCount, Is.EqualTo(1));
                Assert.That(Encoding.UTF8.GetString(backend.Request!.Payload.Span), Is.EqualTo(RequestPayload));
                Assert.That(Read(owner, transfer.Response!), Is.EqualTo(ResponsePayload));
                Assert.That(transfer.ModelUsed!.Value, Is.EqualTo(PrimaryModelId(nm)));
                Assert.That(transfer.State!.Value, Is.EqualTo(TransferStateEnum.Completed));
            });
            await AssertTreeDeniedAsync(nm, other, transfer).ConfigureAwait(false);
            uint responseHandle = 0;
            Assert.That(transfer.Response!.Open!.OnCall!(
                other.Context, transfer.Response.Open, transfer.Response.NodeId, 1, ref responseHandle).StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));

            ServiceResult aborted = await transfer.Abort.OnCallMethod2Async!(
                owner.Context, transfer.Abort, transfer.NodeId, [], [],
                CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(aborted.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(nm.IndexedNode(transfer.NodeId), Is.Null);
            });
        }

        /// <summary>
        /// Execute rechecks Call on both the deployment and the method that created the transfer.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        [Category("SecurityRegression")]
        public async Task SecurityF01OwnerCannotExecuteAfterDeploymentRevocation(
            bool oversizeInvoke,
            bool revokeMethod)
        {
            var backend = new ControlledInferenceBackend();
            backend.Release();
            using AINodeManager nm = await CreateAsync(backend).ConfigureAwait(false);
            using var owner = new Caller(nm, 1, Identity("creator"));
            InferenceTransferState transfer = await BeginTransferAsync(nm, owner, oversizeInvoke)
                .ConfigureAwait(false);
            Upload(owner, transfer.Request!, RequestPayload);
            DeploymentState deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);
            NodeState restricted = revokeMethod
                ? oversizeInvoke ? deployment.Invoke! : deployment.BeginTransfer!
                : deployment;
            ArrayOf<RolePermissionType> original = restricted.RolePermissions;
            restricted.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = CoreObjectIds.WellKnownRole_Anonymous,
                    Permissions = (uint)(PermissionType.Browse | PermissionType.Read)
                }
            ];

            ExecuteMethodStateResult denied = await transfer.Execute!.OnCallAsync!(
                owner.Context, transfer.Execute, transfer.NodeId, CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(denied.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(denied.Accepted, Is.False);
                Assert.That(backend.InvocationCount, Is.Zero);
                Assert.That(transfer.State!.Value, Is.EqualTo(TransferStateEnum.Building));
                Assert.That(Read(owner, transfer.Request!), Is.EqualTo(RequestPayload));
            });

            restricted.RolePermissions = original;
            ExecuteMethodStateResult accepted = await transfer.Execute.OnCallAsync!(
                owner.Context, transfer.Execute, transfer.NodeId, CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(accepted.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(accepted.Accepted, Is.True);
                Assert.That(backend.InvocationCount, Is.EqualTo(1));
                Assert.That(Read(owner, transfer.Response!), Is.EqualTo(ResponsePayload));
            });
        }

        /// <summary>
        /// Execute cannot bypass changed deployment transport restrictions or method executability.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF01ExecutionRechecksDeploymentSecurityAndMethodExecutability(bool executable)
        {
            var backend = new ControlledInferenceBackend();
            backend.Release();
            using AINodeManager nm = await CreateAsync(backend).ConfigureAwait(false);
            using var owner = new Caller(nm, 1, Identity("creator"), securityMode: MessageSecurityMode.Sign);
            InferenceTransferState transfer = await BeginTransferAsync(nm, owner, false).ConfigureAwait(false);
            Upload(owner, transfer.Request!, RequestPayload);
            DeploymentState deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);
            if (executable)
            {
                deployment.BeginTransfer!.UserExecutable = false;
            }
            else
            {
                deployment.AccessRestrictions = AccessRestrictionType.EncryptionRequired;
            }

            ExecuteMethodStateResult denied = await transfer.Execute!.OnCallAsync!(
                owner.Context, transfer.Execute, transfer.NodeId, CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(denied.ServiceResult.StatusCode, Is.EqualTo(executable
                    ? StatusCodes.BadUserAccessDenied
                    : StatusCodes.BadSecurityModeInsufficient));
                Assert.That(denied.Accepted, Is.False);
                Assert.That(backend.InvocationCount, Is.Zero);
                Assert.That(transfer.State!.Value, Is.EqualTo(TransferStateEnum.Building));
                Assert.That(Read(owner, transfer.Request!), Is.EqualTo(RequestPayload));
            });
        }

        /// <summary>
        /// Authenticated jobs survive an owner reconnect, but anonymous jobs never cross sessions.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF01JobOwnerSurvivesOnlyAuthorizedReconnect(bool anonymous)
        {
            var backend = new ControlledInferenceBackend();
            using AINodeManager nm = await CreateAsync(backend).ConfigureAwait(false);
            using var owner = new Caller(nm, 1, Identity(anonymous ? null : "creator"));
            using var other = new Caller(nm, 2, Identity(anonymous ? null : "other"));
            using var reconnect = new Caller(nm, 3, Identity(anonymous ? null : "creator"));
            using var changedIdentity = new Caller(nm, 1, Identity("other"));
            (InferenceJobState job, Task completed) = await StartJobAsync(nm, owner, backend).ConfigureAwait(false);
            try
            {
                Assert.That((await ReadAsync(nm, owner, job.RequestPayload!).ConfigureAwait(false)).WrappedValue,
                    Is.EqualTo(Variant.From(Bytes(RequestPayload))));
                await AssertTreeDeniedAsync(nm, other, job).ConfigureAwait(false);
                await AssertTreeDeniedAsync(nm, changedIdentity, job).ConfigureAwait(false);
                Assert.That(await IsVisibleAsync(nm, other, job.NodeId).ConfigureAwait(false), Is.False);
                Assert.That(
                    await IsVisibleAsync(nm, reconnect, job.NodeId).ConfigureAwait(false), Is.EqualTo(!anonymous));
                DataValue reconnectRead = await ReadAsync(nm, reconnect, job.RequestPayload!).ConfigureAwait(false);
                Assert.That(reconnectRead.StatusCode, Is.EqualTo(
                    anonymous ? StatusCodes.BadUserAccessDenied : StatusCodes.Good));

                backend.Release();
                await completed.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                DataValue answer = await ReadAsync(nm, owner, job.ResponsePayload!).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(answer.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(answer.WrappedValue, Is.EqualTo(Variant.From(Bytes(ResponsePayload))));
                    Assert.That(job.ModelUsed!.Value, Is.EqualTo(PrimaryModelId(nm)));
                    Assert.That(
                        job.CurrentState!.Id!.Value, Is.EqualTo(CoreObjectIds.ProgramStateMachineType_Halted));
                    Assert.That(backend.InvocationCount, Is.EqualTo(1));
                    Assert.That(Encoding.UTF8.GetString(backend.Request!.Payload.Span), Is.EqualTo(RequestPayload));
                });
                await AssertTreeDeniedAsync(nm, other, job).ConfigureAwait(false);
                DataValue reconnectedAnswer = await ReadAsync(nm, reconnect, job.ResponsePayload!)
                    .ConfigureAwait(false);
                Assert.That(reconnectedAnswer.StatusCode, Is.EqualTo(
                    anonymous ? StatusCodes.BadUserAccessDenied : StatusCodes.Good));
                if (!anonymous)
                {
                    Assert.That(reconnectedAnswer.WrappedValue, Is.EqualTo(Variant.From(Bytes(ResponsePayload))));
                }
            }
            finally
            {
                backend.Release();
                await completed.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A notification captured for the owner is never forwarded to another subscriber.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF01JobNotificationsUseEachSubscribersOwner()
        {
            var backend = new ControlledInferenceBackend();
            using AINodeManager nm = await CreateAsync(backend).ConfigureAwait(false);
            using var owner = new Caller(nm, 1, Identity("creator"));
            using var other = new Caller(nm, 2, Identity("other"));
            (InferenceJobState job, Task completed) = await StartJobAsync(nm, owner, backend).ConfigureAwait(false);
            backend.Release();
            await completed.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Mock<IDataChangeMonitoredItem2> ownerItem = MonitoredItem(1, owner);
            Mock<IDataChangeMonitoredItem2> otherItem = MonitoredItem(2, other);
            await using var monitored = new MonitoredNode2(nm, nm.SystemContext.Server, job.ResponsePayload!);
            monitored.Add(ownerItem.Object);
            monitored.Add(otherItem.Object);

            await monitored.OnMonitoredNodeChangedAsync(
                owner.Context, job.ResponsePayload!, NodeStateChangeMasks.Value).ConfigureAwait(false);
            await monitored.DrainAsync().ConfigureAwait(false);
            ArrayOf<DataValue> ownerValues = QueuedValues(ownerItem);
            ArrayOf<DataValue> otherValues = QueuedValues(otherItem);
            Assert.That(ownerValues.Count, Is.EqualTo(1));
            Assert.That(otherValues.Count, Is.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(ownerValues[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(ownerValues[0].WrappedValue, Is.EqualTo(Variant.From(Bytes(ResponsePayload))));
                Assert.That(otherValues[0].StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(otherValues[0].WrappedValue.IsNull, Is.True);
                Assert.That(backend.InvocationCount, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// Issued-token ownership distinguishes issuer/subject boundaries, not display names or concatenations.
        /// </summary>
        [TestCase("issuer-a", "subject", "issuer-b", "subject")]
        [TestCase("ab", "c", "a", "bc")]
        [Category("SecurityRegression")]
        public async Task SecurityF01IssuedJobOwnersRemainInTheirIdentityDomain(
            string issuer,
            string subject,
            string otherIssuer,
            string otherSubject)
        {
            var backend = new ControlledInferenceBackend();
            using AINodeManager nm = await CreateAsync(backend).ConfigureAwait(false);
            using var owner = new Caller(nm, 1, IssuedIdentity(issuer, subject));
            using var other = new Caller(nm, 2, IssuedIdentity(otherIssuer, otherSubject));
            using var reconnect = new Caller(nm, 3, IssuedIdentity(issuer, subject));
            using var sameDisplayName = new Caller(nm, 4, Identity(subject));
            (InferenceJobState job, Task completed) = await StartJobAsync(nm, owner, backend).ConfigureAwait(false);
            try
            {
                await AssertTreeDeniedAsync(nm, other, job).ConfigureAwait(false);
                await AssertTreeDeniedAsync(nm, sameDisplayName, job).ConfigureAwait(false);
                backend.Release();
                await completed.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                DataValue answer = await ReadAsync(nm, reconnect, job.ResponsePayload!).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(answer.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(answer.WrappedValue, Is.EqualTo(Variant.From(Bytes(ResponsePayload))));
                    Assert.That(backend.InvocationCount, Is.EqualTo(1));
                });
                await AssertTreeDeniedAsync(nm, other, job).ConfigureAwait(false);
            }
            finally
            {
                backend.Release();
                await completed.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Even an owner allowed to write by role policy cannot overwrite server-produced job results.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF23OwnerCannotWriteServerResults()
        {
            var backend = new ControlledInferenceBackend();
            using AINodeManager nm = await CreateAsync(backend).ConfigureAwait(false);
            using var owner = new Caller(nm, 1, Identity("creator"));
            (InferenceJobState job, Task completed) = await StartJobAsync(nm, owner, backend).ConfigureAwait(false);
            backend.Release();
            await completed.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            (BaseVariableState Node, Variant Forged, Variant Expected)[] attempts =
            [
                (job.ResponsePayload!, Variant.From(Bytes("forged")), Variant.From(Bytes(ResponsePayload))),
                (job.ResponseContentType!, Variant.From("forged/type"), Variant.From("application/json")),
                (job.ModelUsed!, Variant.From(nm.PrimaryDeploymentId), Variant.From(PrimaryModelId(nm))),
                (job.FinishReason!, Variant.From(FinishReasonEnum.Error), Variant.From(FinishReasonEnum.Stop)),
                (job.Usage!, Variant.FromStructure(new UsageDataType { TotalUnits = 999 }),
                    Variant.FromStructure(new UsageDataType
                    {
                        UnitKind = "tokens", InputUnits = 1, OutputUnits = 2, TotalUnits = 3
                    })),
                (job.Progress!, Variant.From(1d), Variant.From(100d)),
                (job.RequestPayload!, Variant.From(Bytes("forged")), Variant.From(Bytes(RequestPayload))),
                (job.CurrentState!, Variant.From(new LocalizedText("Running")),
                    Variant.From(new LocalizedText("Halted"))),
                (job.CurrentState!.Id!, Variant.From(CoreObjectIds.ProgramStateMachineType_Running),
                    Variant.From(CoreObjectIds.ProgramStateMachineType_Halted))
            ];
            foreach ((BaseVariableState node, Variant forged, Variant expected) in attempts)
            {
                ServiceResult rolePermission = await nm.ValidateRolePermissionsAsync(
                    owner.Operation, node.NodeId, PermissionType.Write).ConfigureAwait(false);
                Assert.That(rolePermission.StatusCode, Is.EqualTo(StatusCodes.Good));
                ServiceResult written = await WriteAsync(nm, owner, node, forged).ConfigureAwait(false);
                DataValue retained = await ReadAsync(nm, owner, node).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(written.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable), node.BrowseName.ToString());
                    Assert.That(retained.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(retained.WrappedValue, Is.EqualTo(expected));
                });
            }
            Assert.That(backend.InvocationCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Dynamically materialized output properties retain the declaration's access policy.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF23OptionalChildrenRetainDeclarationRestrictions()
        {
            var backend = new ControlledInferenceBackend();
            backend.Release();
            using AINodeManager nm = await CreateAsync(backend).ConfigureAwait(false);
            using var owner = new Caller(nm, 1, Identity("creator"));
            using var withoutRole = new Caller(nm, 1, Identity("creator"), grantAuthenticatedRole: false);
            var typeId = new NodeId(ObjectTypes.InferenceTransferType, nm.NamespaceIndex);
            BaseObjectTypeState type = nm.FindPredefinedNode<BaseObjectTypeState>(typeId);
            var declaration = (BaseVariableState)type.FindChild(
                nm.SystemContext, new QualifiedName(BrowseNames.ResponseContentType, nm.NamespaceIndex))!;
            declaration.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = CoreObjectIds.WellKnownRole_AuthenticatedUser,
                    Permissions = (uint)(PermissionType.Browse | PermissionType.Read)
                }
            ];
            declaration.UserRolePermissions = declaration.RolePermissions;
            declaration.AccessRestrictions =
                AccessRestrictionType.EncryptionRequired | AccessRestrictionType.SessionRequired;

            InferenceTransferState transfer = await BeginTransferAsync(nm, owner, false).ConfigureAwait(false);
            PropertyState<string> result = transfer.ResponseContentType!;
            ServiceResult allowed = await nm.ValidateRolePermissionsAsync(
                owner.Operation, result.NodeId, PermissionType.Read).ConfigureAwait(false);
            ServiceResult denied = await nm.ValidateRolePermissionsAsync(
                withoutRole.Operation, result.NodeId, PermissionType.Read).ConfigureAwait(false);
            ServiceResult overwrite = await WriteAsync(nm, owner, result, Variant.From("forged/type"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(allowed.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(overwrite.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
                Assert.That(result.RolePermissions, Is.EqualTo(declaration.RolePermissions));
                Assert.That(result.UserRolePermissions, Is.EqualTo(declaration.UserRolePermissions));
                Assert.That(result.AccessRestrictions, Is.EqualTo(declaration.AccessRestrictions));
                Assert.That(result.AccessLevel, Is.EqualTo(declaration.AccessLevel));
                Assert.That(result.UserAccessLevel, Is.EqualTo(declaration.UserAccessLevel));
                Assert.That(result.NodeId, Is.Not.EqualTo(declaration.NodeId));
            });
            Upload(owner, transfer.Request!, RequestPayload);
            ExecuteMethodStateResult executed = await transfer.Execute!.OnCallAsync!(
                owner.Context, transfer.Execute, transfer.NodeId, CancellationToken.None).ConfigureAwait(false);
            DataValue responseType = await ReadAsync(nm, owner, result).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(executed.Accepted, Is.True);
                Assert.That(responseType.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(responseType.WrappedValue, Is.EqualTo(Variant.From("application/json")));
                Assert.That(Read(owner, transfer.Response!), Is.EqualTo(ResponsePayload));
            });
        }

        private static async Task<AINodeManager> CreateAsync(ControlledInferenceBackend backend)
        {
            AINodeManager nm = await AIServerTestHarness.CreateAsync(
                new InferenceBackends(backend),
                new AIOptions { EnableFallback = false, AsyncInferenceDelay = TimeSpan.Zero },
                new InferenceBackendOptions { MaxInlinePayloadSize = InlineLimit }).ConfigureAwait(false);
            try
            {
                var configuration = new Mock<IConfigurationNodeManager>();
                configuration
                    .Setup(m => m.GetNamespaceMetadataStateAsync(
                        It.IsAny<ushort>(), It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<NamespaceMetadataState?>((NamespaceMetadataState?)null));
                var master = Mock.Get(nm.SystemContext.Server.NodeManager);
                master.SetupGet(m => m.ConfigurationNodeManager).Returns(configuration.Object);
                Mock.Get(nm.SystemContext.Server)
                    .SetupGet(s => s.ConfigurationNodeManager).Returns(configuration.Object);
                master
                    .Setup(m => m.GetManagerHandleAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .Returns((NodeId nodeId, CancellationToken ct) => ResolveHandleAsync(nm, nodeId, ct));
                return nm;
            }
            catch
            {
                await nm.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private static async ValueTask<(object? Handle, IAsyncNodeManager? Manager)> ResolveHandleAsync(
            AINodeManager nm,
            NodeId nodeId,
            CancellationToken ct)
        {
            object? handle = await nm.GetManagerHandleAsync(nodeId, ct).ConfigureAwait(false);
            return (handle, handle is null ? null : nm);
        }

        private static UserIdentity Identity(string? name)
        {
            return name is null ? new UserIdentity() : new UserIdentity(name, []);
        }

        private static JwtUserIdentity IssuedIdentity(string issuer, string subject)
        {
            return new JwtUserIdentity(
                new IssuedIdentityTokenHandler(new IssuedIdentityToken { PolicyId = Profiles.JwtUserToken }),
                new Dictionary<string, object?> { ["iss"] = issuer, ["sub"] = subject },
                [], [], issuer, subject);
        }

        private static async Task<InferenceTransferState> BeginTransferAsync(
            AINodeManager nm,
            Caller caller,
            bool oversizeInvoke)
        {
            DeploymentState deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);
            NodeId transferId;
            if (oversizeInvoke)
            {
                InvokeMethodStateResult result = await deployment.Invoke!.OnCallAsync!(
                    caller.Context, deployment.Invoke, deployment.NodeId, ByteString.From(new byte[InlineLimit + 1]),
                    string.Empty, "application/json", [], 5000,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(result.TransferRequired, Is.True);
                transferId = result.Transfer;
            }
            else
            {
                BeginTransferMethodStateResult result = await deployment.BeginTransfer!.OnCallAsync!(
                    caller.Context, deployment.BeginTransfer, deployment.NodeId, "application/json", 1024,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(result.Accepted, Is.True);
                transferId = result.Transfer;
            }
            return nm.FindPredefinedNode<InferenceTransferState>(transferId);
        }

        private static async Task<(InferenceJobState Job, Task Completed)> StartJobAsync(
            AINodeManager nm,
            Caller caller,
            ControlledInferenceBackend backend)
        {
            DeploymentState deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);
            InvokeAsyncMethodStateResult result = await deployment.InvokeAsync!.OnCallAsync!(
                caller.Context, deployment.InvokeAsync, deployment.NodeId, Bytes(RequestPayload), string.Empty,
                "application/json", [], CancellationToken.None).ConfigureAwait(false);
            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            InferenceJobState job = nm.FindPredefinedNode<InferenceJobState>(result.Job);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            job.StateChanged += (_, _, _) =>
            {
                if (job.CurrentState!.Id!.Value == CoreObjectIds.ProgramStateMachineType_Halted)
                {
                    completed.TrySetResult();
                }
            };
            // Root notifications follow descendant notifications, so this also drains
            // the completion's child changes before another monitored node is attached.
            job.UpdateChangeMasks(NodeStateChangeMasks.NonValue);
            try
            {
                await backend.Entered.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                return (job, completed.Task);
            }
            catch
            {
                backend.Release();
                throw;
            }
        }

        private static async Task AssertTreeDeniedAsync(AINodeManager nm, Caller caller, NodeState node)
        {
            ServiceResult denied = await nm.ValidateRolePermissionsAsync(
                caller.Operation, node.NodeId, PermissionType.Browse).ConfigureAwait(false);
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied), node.BrowseName.ToString());
            if (node is BaseVariableState variable)
            {
                DataValue read = await ReadAsync(nm, caller, variable).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(read.StatusCode,
                        Is.EqualTo(StatusCodes.BadUserAccessDenied).Or.EqualTo(StatusCodes.BadNotReadable),
                        node.BrowseName.ToString());
                    Assert.That(read.WrappedValue.IsNull, Is.True);
                });
            }
            if (node is MethodState method && method.Parent is { } parent)
            {
                ServiceResult called = await CallAsync(nm, caller, parent.NodeId, method.NodeId).ConfigureAwait(false);
                Assert.That(called.StatusCode,
                    Is.EqualTo(StatusCodes.BadUserAccessDenied), method.BrowseName.ToString());
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(nm.SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                await AssertTreeDeniedAsync(nm, caller, child).ConfigureAwait(false);
            }
        }

        private static async Task<bool> IsVisibleAsync(AINodeManager nm, Caller caller, NodeId target)
        {
            AiRootState root = nm.FindPredefinedNode<AiRootState>(nm.RootId);
            using var continuation = new ContinuationPoint
            {
                Manager = nm,
                NodeToBrowse = await nm.GetManagerHandleAsync(root.Jobs!.NodeId).ConfigureAwait(false),
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = NodeId.Null,
                IncludeSubtypes = true,
                ResultMask = BrowseResultMask.All
            };
            var references = new List<ReferenceDescription>();
            await nm.BrowseAsync(caller.Operation, continuation, references).ConfigureAwait(false);
            return references.Exists(reference => reference.NodeId == new ExpandedNodeId(target));
        }

        private static async Task<DataValue> ReadAsync(AINodeManager nm, Caller caller, BaseVariableState node)
        {
            var values = new List<DataValue> { new() };
            var errors = new List<ServiceResult> { ServiceResult.Good };
            await nm.ReadAsync(
                caller.Operation, 0, [new ReadValueId { NodeId = node.NodeId, AttributeId = Attributes.Value }],
                values, errors).ConfigureAwait(false);
            return values[0];
        }

        private static async Task<ServiceResult> WriteAsync(
            AINodeManager nm,
            Caller caller,
            BaseVariableState node,
            Variant value)
        {
            var errors = new List<ServiceResult> { ServiceResult.Good };
            await nm.WriteAsync(
                caller.Operation,
                [new WriteValue { NodeId = node.NodeId, AttributeId = Attributes.Value, Value = new DataValue(value) }],
                errors).ConfigureAwait(false);
            return errors[0];
        }

        private static async Task<ServiceResult> CallAsync(
            AINodeManager nm,
            Caller caller,
            NodeId objectId,
            NodeId methodId)
        {
            var results = new List<CallMethodResult> { new() };
            var errors = new List<ServiceResult> { ServiceResult.Good };
            await nm.CallAsync(
                caller.Operation,
                [new CallMethodRequest { ObjectId = objectId, MethodId = methodId, InputArguments = [] }],
                results,
                errors).ConfigureAwait(false);
            return errors[0];
        }

        private static Mock<IDataChangeMonitoredItem2> MonitoredItem(uint id, Caller caller)
        {
            var item = new Mock<IDataChangeMonitoredItem2>();
            item.SetupGet(value => value.Id).Returns(id);
            item.SetupGet(value => value.AttributeId).Returns(Attributes.Value);
            item.SetupGet(value => value.IndexRange).Returns(NumericRange.Null);
            item.SetupGet(value => value.DataEncoding).Returns(QualifiedName.Null);
            item.SetupGet(value => value.Session).Returns(caller.Session);
            item.SetupGet(value => value.EffectiveIdentity).Returns(caller.Session.EffectiveIdentity);
            return item;
        }

        private static ArrayOf<DataValue> QueuedValues(Mock<IDataChangeMonitoredItem2> item)
        {
            var values = new List<DataValue>();
            foreach (IInvocation invocation in item.Invocations)
            {
                if (invocation.Method.Name == nameof(IDataChangeMonitoredItem2.QueueValue) &&
                    invocation.Arguments[0] is DataValue value)
                {
                    values.Add(value);
                }
            }
            return values.ToArrayOf();
        }

        private static uint Open(Caller caller, FileState file, byte mode)
        {
            uint handle = 0;
            Assert.That(file.Open!.OnCall!(
                caller.Context, file.Open, file.NodeId, mode, ref handle).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            return handle;
        }

        private static void Upload(Caller caller, FileState file, string payload)
        {
            uint handle = Open(caller, file, 6);
            try
            {
                Assert.That(file.Write!.OnCall!(
                    caller.Context, file.Write, file.NodeId, handle, Bytes(payload)).StatusCode,
                    Is.EqualTo(StatusCodes.Good));
            }
            finally
            {
                file.Close!.OnCall!(caller.Context, file.Close, file.NodeId, handle);
            }
        }

        private static string Read(Caller caller, FileState file)
        {
            uint handle = Open(caller, file, 1);
            try
            {
                ByteString data = ByteString.Empty;
                Assert.That(file.Read!.OnCall!(
                    caller.Context, file.Read, file.NodeId, handle, 4096, ref data).StatusCode,
                    Is.EqualTo(StatusCodes.Good));
                return Encoding.UTF8.GetString(data.Span);
            }
            finally
            {
                file.Close!.OnCall!(caller.Context, file.Close, file.NodeId, handle);
            }
        }

        private static ByteString Bytes(string text)
        {
            return ByteString.From(Encoding.UTF8.GetBytes(text));
        }

        private static NodeId PrimaryModelId(AINodeManager nm)
        {
            AiRootState root = nm.FindPredefinedNode<AiRootState>(nm.RootId);
            var children = new List<BaseInstanceState>();
            root.Models!.GetChildren(nm.SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is ModelState model)
                {
                    return model.NodeId;
                }
            }
            throw new AssertionException("The primary model is missing.");
        }

        /// <summary>
        /// A separate server session and the context dispatched on its behalf.
        /// </summary>
        private sealed class Caller : IDisposable
        {
            /// <summary>
            /// Creates a session and an independently attributed operation.
            /// </summary>
            public Caller(
                AINodeManager nm,
                uint sessionId,
                IUserIdentity identity,
                bool grantAuthenticatedRole = true,
                MessageSecurityMode securityMode = MessageSecurityMode.SignAndEncrypt)
            {
                var session = new Mock<ISession>();
                IUserIdentity effective = grantAuthenticatedRole && identity.TokenType != UserTokenType.Anonymous
                    ? new RoleBasedIdentity(identity, [Role.AuthenticatedUser], nm.SystemContext.NamespaceUris)
                    : identity;
                session.SetupGet(s => s.Id).Returns(new NodeId(sessionId, nm.NamespaceIndex));
                session.SetupGet(s => s.Identity).Returns(identity);
                session.SetupGet(s => s.IdentityToken).Returns(identity.TokenHandler);
                session.SetupGet(s => s.EffectiveIdentity).Returns(effective);
                session.SetupGet(s => s.PreferredLocales).Returns([]);
                Session = session.Object;
                var channel = new SecureChannelContext(
                    "in-memory-channel",
                    new EndpointDescription
                    {
                        SecurityMode = securityMode,
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                        TransportProfileUri = Profiles.UaTcpTransport
                    },
                    RequestEncoding.Binary);
                Operation = new OperationContext(
                    new RequestHeader(), channel, RequestType.Call, RequestLifetime.None, Session);
                Context = new ServerSystemContext(nm.SystemContext.Server, Operation);
            }

            /// <summary>
            /// The distinct server-side session.
            /// </summary>
            public ISession Session { get; }

            /// <summary>
            /// The operation dispatched for this caller.
            /// </summary>
            public OperationContext Operation { get; }

            /// <summary>
            /// The system context supplied to real node callbacks.
            /// </summary>
            public ServerSystemContext Context { get; }

            /// <inheritdoc/>
            public void Dispose()
            {
                Operation.Dispose();
                Session.Dispose();
            }
        }

        /// <summary>
        /// Records real inference dispatch and explicitly gates background-job completion.
        /// </summary>
        private sealed class ControlledInferenceBackend : IInferenceBackend
        {
            /// <inheritdoc/>
            public InferenceSite Site => InferenceSite.OnServer;

            /// <summary>
            /// The number of actual inference dispatches.
            /// </summary>
            public int InvocationCount => Volatile.Read(ref m_invocationCount);

            /// <summary>
            /// The request that reached the backend.
            /// </summary>
            public InferenceRequest? Request { get; private set; }

            /// <summary>
            /// Signals actual backend entry before the completion gate.
            /// </summary>
            public Task Entered => m_entered.Task;

            /// <summary>
            /// Allows an inference to complete deterministically.
            /// </summary>
            public void Release()
            {
                m_release.TrySetResult();
            }

            /// <inheritdoc/>
            public ValueTask<IReadOnlyList<BackendModel>> ListModelsAsync(
                string? filter,
                uint maxResults,
                CancellationToken ct)
            {
                return ValueTask.FromResult<IReadOnlyList<BackendModel>>([]);
            }

            /// <inheritdoc/>
            public async ValueTask<InferenceResult> InvokeAsync(InferenceRequest request, CancellationToken ct)
            {
                Request = request;
                Interlocked.Increment(ref m_invocationCount);
                m_entered.TrySetResult();
                await m_release.Task.WaitAsync(ct).ConfigureAwait(false);
                return new InferenceResult
                {
                    Ok = true,
                    Payload = Encoding.UTF8.GetBytes(ResponsePayload),
                    ContentType = "application/json",
                    Finish = InferenceFinish.Stop,
                    UsageUnit = "tokens",
                    InputUnits = 1,
                    OutputUnits = 2,
                    TotalUnits = 3
                };
            }

            /// <inheritdoc/>
            public ValueTask<BackendProbe> ProbeAsync(CancellationToken ct)
            {
                return ValueTask.FromResult(new BackendProbe { Reachable = true });
            }

            private readonly TaskCompletionSource m_entered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly TaskCompletionSource m_release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private int m_invocationCount;
        }

        private const int InlineLimit = 64;
        private const string RequestPayload = /*lang=json,strict*/ "{\"prompt\":\"creator-only\"}";
        private const string ResponsePayload = /*lang=json,strict*/ "{\"answer\":42}";
    }
}
