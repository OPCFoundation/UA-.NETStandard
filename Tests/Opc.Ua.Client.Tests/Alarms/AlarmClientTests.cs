/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * MIT License - see /Docs/License.md
 * ======================================================================*/

#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Alarms;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.Alarms
{
    /// <summary>
    /// Tests that <see cref="AlarmClient"/> dispatches each public
    /// operation to the right source-generated <c>*TypeClient</c> proxy,
    /// passing the supplied <c>conditionId</c> as the request
    /// <c>ObjectId</c> and the correct <c>MethodId</c>. The
    /// <c>comment.IsNullOrEmpty</c> branch dispatch (Xxx vs Xxx2) is
    /// covered for every alarm-condition operation that takes an
    /// optional comment.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("Alarms")]
    [Parallelizable]
    public sealed class AlarmClientTests
    {
        private Mock<ISessionClient> m_sessionMock = null!;
        private ITelemetryContext m_telemetry = null!;
        private AlarmClient m_client = null!;
        private NodeId m_conditionId;

        [SetUp]
        public void SetUp()
        {
            m_sessionMock = new Mock<ISessionClient>(MockBehavior.Loose);
            m_sessionMock.SetupGet(s => s.MessageContext)
                .Returns(ServiceMessageContext.Create(NUnitTelemetryContext.Create()));
            m_telemetry = NUnitTelemetryContext.Create();
            m_client = new AlarmClient(m_sessionMock.Object, m_telemetry);
            m_conditionId = new NodeId(42u, 3);
        }

        [Test]
        public void ConstructorWithNullSessionThrowsArgumentNullException()
        {
            Assert.That(
                () => new AlarmClient(null!, m_telemetry),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorWithNullTelemetryThrowsArgumentNullException()
        {
            Assert.That(
                () => new AlarmClient(m_sessionMock.Object, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task AcknowledgeAsyncForwardsToAcknowledgeableConditionTypeClient()
        {
            CallMethodRequest captured = await CaptureCallAsync(
                _ => m_client.AcknowledgeAsync(
                    m_conditionId,
                    new ByteString(new byte[] { 1, 2, 3 }),
                    new LocalizedText("en", "ack"))).ConfigureAwait(false);

            Assert.That(captured.ObjectId, Is.EqualTo(m_conditionId));
            Assert.That(captured.MethodId,
                Is.EqualTo(MethodIds.AcknowledgeableConditionType_Acknowledge));
            Assert.That(captured.InputArguments.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task ConfirmAsyncForwardsToAcknowledgeableConditionTypeClient()
        {
            CallMethodRequest captured = await CaptureCallAsync(
                _ => m_client.ConfirmAsync(
                    m_conditionId,
                    new ByteString(new byte[] { 5 }),
                    new LocalizedText("en", "ok"))).ConfigureAwait(false);

            Assert.That(captured.ObjectId, Is.EqualTo(m_conditionId));
            Assert.That(captured.MethodId,
                Is.EqualTo(MethodIds.AcknowledgeableConditionType_Confirm));
            Assert.That(captured.InputArguments.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task AddCommentAsyncForwardsToConditionTypeClient()
        {
            CallMethodRequest captured = await CaptureCallAsync(
                _ => m_client.AddCommentAsync(
                    m_conditionId,
                    new ByteString(new byte[] { 9 }),
                    new LocalizedText("en", "note"))).ConfigureAwait(false);

            Assert.That(captured.ObjectId, Is.EqualTo(m_conditionId));
            Assert.That(captured.MethodId,
                Is.EqualTo(MethodIds.ConditionType_AddComment));
        }

        [Test]
        public async Task EnableAsyncForwardsToConditionTypeClient()
        {
            CallMethodRequest captured = await CaptureCallAsync(
                _ => m_client.EnableAsync(m_conditionId)).ConfigureAwait(false);

            Assert.That(captured.ObjectId, Is.EqualTo(m_conditionId));
            Assert.That(captured.MethodId,
                Is.EqualTo(MethodIds.ConditionType_Enable));
            Assert.That(captured.InputArguments.Count, Is.Zero);
        }

        [Test]
        public async Task DisableAsyncForwardsToConditionTypeClient()
        {
            CallMethodRequest captured = await CaptureCallAsync(
                _ => m_client.DisableAsync(m_conditionId)).ConfigureAwait(false);

            Assert.That(captured.ObjectId, Is.EqualTo(m_conditionId));
            Assert.That(captured.MethodId,
                Is.EqualTo(MethodIds.ConditionType_Disable));
        }

        [Test]
        public async Task RespondAsyncForwardsToDialogConditionTypeClient()
        {
            CallMethodRequest captured = await CaptureCallAsync(
                _ => m_client.RespondAsync(m_conditionId, 2)).ConfigureAwait(false);

            Assert.That(captured.ObjectId, Is.EqualTo(m_conditionId));
            Assert.That(captured.MethodId,
                Is.EqualTo(MethodIds.DialogConditionType_Respond));
            Assert.That(captured.InputArguments.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task SilenceAsyncForwardsToAlarmConditionTypeClient()
        {
            CallMethodRequest captured = await CaptureCallAsync(
                _ => m_client.SilenceAsync(m_conditionId)).ConfigureAwait(false);

            Assert.That(captured.ObjectId, Is.EqualTo(m_conditionId));
            Assert.That(captured.MethodId,
                Is.EqualTo(MethodIds.AlarmConditionType_Silence));
        }

        [Test]
        public async Task Respond2AsyncForwardsToDialogConditionTypeClient()
        {
            CallMethodRequest captured = await CaptureCallAsync(
                _ => m_client.Respond2Async(
                    m_conditionId, 1, new LocalizedText("en", "details")))
                .ConfigureAwait(false);

            Assert.That(captured.ObjectId, Is.EqualTo(m_conditionId));
            Assert.That(captured.MethodId,
                Is.EqualTo(MethodIds.DialogConditionType_Respond2));
            Assert.That(captured.InputArguments.Count, Is.EqualTo(2));
        }

        [TestCase(null)]
        [TestCase("")]
        public async Task SuppressAsyncWithEmptyCommentDispatchesToSuppress(string? text)
        {
            LocalizedText comment = text == null
                ? default
                : new LocalizedText(text);

            CallMethodRequest captured = await CaptureCallAsync(
                _ => m_client.SuppressAsync(m_conditionId, comment)).ConfigureAwait(false);

            Assert.That(captured.MethodId,
                Is.EqualTo(MethodIds.AlarmConditionType_Suppress));
            Assert.That(captured.InputArguments.Count, Is.Zero);
        }

        [Test]
        public async Task SuppressAsyncWithNonEmptyCommentDispatchesToSuppress2()
        {
            CallMethodRequest captured = await CaptureCallAsync(
                _ => m_client.SuppressAsync(
                    m_conditionId,
                    new LocalizedText("en", "reason"))).ConfigureAwait(false);

            Assert.That(captured.MethodId,
                Is.EqualTo(MethodIds.AlarmConditionType_Suppress2));
            Assert.That(captured.InputArguments.Count, Is.EqualTo(1));
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("note", true)]
        public async Task UnsuppressAsyncCommentBranchSelection(string? text, bool expectV2)
        {
            LocalizedText comment = text == null ? default : new LocalizedText(text);
            NodeId expected = expectV2
                ? MethodIds.AlarmConditionType_Unsuppress2
                : MethodIds.AlarmConditionType_Unsuppress;

            CallMethodRequest captured = await CaptureCallAsync(
                _ => m_client.UnsuppressAsync(m_conditionId, comment)).ConfigureAwait(false);

            Assert.That(captured.MethodId, Is.EqualTo(expected));
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("note", true)]
        public async Task RemoveFromServiceAsyncCommentBranchSelection(string? text, bool expectV2)
        {
            LocalizedText comment = text == null ? default : new LocalizedText(text);
            NodeId expected = expectV2
                ? MethodIds.AlarmConditionType_RemoveFromService2
                : MethodIds.AlarmConditionType_RemoveFromService;

            CallMethodRequest captured = await CaptureCallAsync(
                _ => m_client.RemoveFromServiceAsync(m_conditionId, comment))
                .ConfigureAwait(false);

            Assert.That(captured.MethodId, Is.EqualTo(expected));
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("note", true)]
        public async Task PlaceInServiceAsyncCommentBranchSelection(string? text, bool expectV2)
        {
            LocalizedText comment = text == null ? default : new LocalizedText(text);
            NodeId expected = expectV2
                ? MethodIds.AlarmConditionType_PlaceInService2
                : MethodIds.AlarmConditionType_PlaceInService;

            CallMethodRequest captured = await CaptureCallAsync(
                _ => m_client.PlaceInServiceAsync(m_conditionId, comment))
                .ConfigureAwait(false);

            Assert.That(captured.MethodId, Is.EqualTo(expected));
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("note", true)]
        public async Task ResetAsyncCommentBranchSelection(string? text, bool expectV2)
        {
            LocalizedText comment = text == null ? default : new LocalizedText(text);
            NodeId expected = expectV2
                ? MethodIds.AlarmConditionType_Reset2
                : MethodIds.AlarmConditionType_Reset;

            CallMethodRequest captured = await CaptureCallAsync(
                _ => m_client.ResetAsync(m_conditionId, comment))
                .ConfigureAwait(false);

            Assert.That(captured.MethodId, Is.EqualTo(expected));
        }

        [Test]
        public async Task ConditionRefreshAsyncUsesConditionTypeAsObjectId()
        {
            CallMethodRequest captured = await CaptureCallAsync(
                _ => m_client.ConditionRefreshAsync(11u)).ConfigureAwait(false);

            Assert.That(captured.ObjectId, Is.EqualTo((NodeId)ObjectTypeIds.ConditionType));
            Assert.That(captured.MethodId,
                Is.EqualTo(MethodIds.ConditionType_ConditionRefresh));
            Assert.That(captured.InputArguments.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task TimedShelveAsyncForwardsToShelvedStateMachineTypeClient()
        {
            CallMethodRequest captured = await CaptureCallAsync(
                _ => m_client.TimedShelveAsync(m_conditionId, 1000.0)).ConfigureAwait(false);

            Assert.That(captured.ObjectId, Is.EqualTo(m_conditionId));
            Assert.That(captured.MethodId,
                Is.EqualTo(MethodIds.ShelvedStateMachineType_TimedShelve));
        }

        [Test]
        public async Task OneShotShelveAsyncForwardsToShelvedStateMachineTypeClient()
        {
            CallMethodRequest captured = await CaptureCallAsync(
                _ => m_client.OneShotShelveAsync(m_conditionId)).ConfigureAwait(false);

            Assert.That(captured.ObjectId, Is.EqualTo(m_conditionId));
            Assert.That(captured.MethodId,
                Is.EqualTo(MethodIds.ShelvedStateMachineType_OneShotShelve));
        }

        [Test]
        public async Task UnshelveAsyncForwardsToShelvedStateMachineTypeClient()
        {
            CallMethodRequest captured = await CaptureCallAsync(
                _ => m_client.UnshelveAsync(m_conditionId)).ConfigureAwait(false);

            Assert.That(captured.ObjectId, Is.EqualTo(m_conditionId));
            Assert.That(captured.MethodId,
                Is.EqualTo(MethodIds.ShelvedStateMachineType_Unshelve));
        }

        /// <summary>
        /// Wires up the loose mock for <see cref="ISessionClient.CallAsync"/>
        /// to capture the (single) <see cref="CallMethodRequest"/> and
        /// return a Good-status response, then runs the supplied
        /// invocation and returns the captured request for assertions.
        /// </summary>
        private async Task<CallMethodRequest> CaptureCallAsync(
            System.Func<CallMethodRequest, ValueTask> action)
        {
            ArrayOf<CallMethodRequest> captured = default;
            m_sessionMock.Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>(
                    (_, r, _) => captured = r)
                .Returns(new ValueTask<CallResponse>(new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = ArrayOf.Wrapped(
                    [
                        new CallMethodResult
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments = default
                        }
                    ]),
                    DiagnosticInfos = []
                }));

            await action(default!).ConfigureAwait(false);

            Assert.That(captured.Count, Is.EqualTo(1));
            return captured[0];
        }
    }
}
