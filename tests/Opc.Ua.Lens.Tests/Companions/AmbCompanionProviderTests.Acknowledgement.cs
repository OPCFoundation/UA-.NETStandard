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
using Opc.Ua.AMB;
using UaLens.Plugins.Companions;
using UaNames = Opc.Ua.BrowseNames;
using UaTypes = Opc.Ua.ObjectTypeIds;

namespace UaLens.Tests.Companions
{
    public sealed partial class AmbCompanionProviderTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task AcknowledgementUsesTheExactEventAndInstanceMethodOnceAsync(bool maintenance)
        {
            var test = new AssetTaskContext();
            AckFixture condition = AddCondition(test, maintenance);
            string operation = maintenance ? "acknowledge-maintenance" : "acknowledge-health";
            CompanionTaskInput prepared = await test.Provider.PrepareInputAsync(
                test.Context, test.Target, operation, AckInput(condition), CancellationToken.None).ConfigureAwait(false);
            Assert.That(test.Session.Calls.IsEmpty, Is.True);
            test.Session.CallHandler = (call, _) =>
            {
                Assert.That(call.ObjectId, Is.EqualTo(condition.Id));
                Assert.That(call.MethodId, Is.EqualTo(condition.Method));
                Assert.That(call.InputArguments, Has.Count.EqualTo(2));
                Assert.That(call.InputArguments[0], Is.EqualTo(Variant.From(condition.EventId)));
                Assert.That(call.InputArguments[1], Is.EqualTo(Variant.From(new LocalizedText("Reviewed event"))));
                return new CallMethodResult { StatusCode = StatusCodes.Good };
            };
            await test.Provider.ExecutePreparedAsync(
                test.Context, test.Target, operation, prepared, null, CancellationToken.None).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                test.Context, test.Target, operation, prepared, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(test.Session.Calls, Has.Count.EqualTo(1));
            condition.Transport.Verify(channel => channel.Dispose(), Times.Never);
            test.Session.Session.Verify(session => session.Dispose(), Times.Never);
        }

        [TestCase("event")]
        [TestCase("source")]
        [TestCase("branch")]
        [TestCase("acked")]
        [TestCase("retained")]
        [TestCase("permission")]
        [TestCase("signature")]
        public async Task ChangingConditionEvidencePreventsAcknowledgingAnotherEventAsync(string change)
        {
            var test = new AssetTaskContext();
            AckFixture condition = AddCondition(test, false);
            CompanionTaskInput prepared = await test.Provider.PrepareInputAsync(
                test.Context, test.Target, "acknowledge-health", AckInput(condition), CancellationToken.None)
                .ConfigureAwait(false);
            switch (change)
            {
                case "event":
                    test.Session.SetValue(condition.EventNode, Variant.From(ByteString.From("next-event"u8)));
                    break;
                case "source":
                    test.Session.SetValue(condition.SourceNode, Variant.From(test.Session.Id("other-asset")));
                    break;
                case "branch":
                    test.Session.SetValue(condition.BranchNode, Variant.From(test.Session.Id("branch")));
                    break;
                case "acked":
                    test.Session.SetValue(condition.AckedNode, Variant.From(true));
                    break;
                case "retained":
                    test.Session.SetValue(condition.RetainNode, Variant.From(false));
                    break;
                case "permission":
                    test.Session.SetValue(condition.Method, Variant.From(false), Attributes.UserExecutable);
                    break;
                case "signature":
                    test.Session.SetValue(condition.InputArguments, Variant.FromStructure((ArrayOf<Argument>)
                    [
                        new Argument { DataType = Opc.Ua.DataTypeIds.String, ValueRank = ValueRanks.Scalar }
                    ]));
                    break;
                default:
                    throw new AssertionException("Unknown condition partition.");
            }
            await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                test.Context, test.Target, "acknowledge-health", prepared, null, CancellationToken.None)
                .ConfigureAwait(false), Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
            Assert.That(test.Session.Calls.IsEmpty, Is.True);
        }

        [TestCase("uncertain")]
        [TestCase("timeout")]
        [TestCase("extra-output")]
        public async Task AcknowledgementCannotReplayAnUnknownResultAsync(string result)
        {
            var test = new AssetTaskContext();
            AckFixture condition = AddCondition(test, false);
            CompanionTaskInput prepared = await test.Provider.PrepareInputAsync(
                test.Context, test.Target, "acknowledge-health", AckInput(condition), CancellationToken.None)
                .ConfigureAwait(false);
            test.Session.CallHandler = (_, _) => result switch
            {
                "uncertain" => new CallMethodResult { StatusCode = StatusCodes.Uncertain },
                "extra-output" => new CallMethodResult { OutputArguments = [Variant.From(true)] },
                _ => throw new TimeoutException("The acknowledgement may have been applied.")
            };
            await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                test.Context, test.Target, "acknowledge-health", prepared, null, CancellationToken.None)
                .ConfigureAwait(false),
                Throws.TypeOf(result == "timeout" ? typeof(TimeoutException) : typeof(ServiceResultException)))
                .ConfigureAwait(false);
            await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                test.Context, test.Target, "acknowledge-health", prepared, null, CancellationToken.None)
                .ConfigureAwait(false), Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(test.Session.Calls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task MissingAcknowledgementStateCannotBecomeFalseAndAuthorizeACallAsync()
        {
            var test = new AssetTaskContext();
            AckFixture condition = AddCondition(test, false);
            test.Session.SetValue(condition.AckedNode, Variant.Null);
            await Assert.ThatAsync(async () => await test.Provider.PrepareInputAsync(
                test.Context, test.Target, "acknowledge-health", AckInput(condition), CancellationToken.None)
                .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadNoData))
                .ConfigureAwait(false);
            Assert.That(test.Session.Calls.IsEmpty, Is.True);
        }

        private static ArrayOf<CompanionValue> AckInput(AckFixture fixture)
        {
            return
            [
                new("conditionId", Variant.From(fixture.Id)),
                new("eventId", Variant.From(fixture.EventId)),
                new("comment", Variant.From("Reviewed event"))
            ];
        }

        private static AckFixture AddCondition(AssetTaskContext test, bool maintenance)
        {
            IndustrialCompanionTestSession session = test.Session;
            NodeId folder = session.Id("alarms");
            session.AddObject(folder, UaTypes.FolderType);
            session.AddChild(test.Target.NodeId, folder, Opc.Ua.Di.Namespaces.OpcUaDi, Opc.Ua.Di.BrowseNames.DeviceHealthAlarms);
            NodeId condition = session.Id("condition");
            session.AddObject(condition, UaTypes.AcknowledgeableConditionType);
            session.AddChild(folder, condition);
            ByteString eventId = ByteString.From("current-event"u8);
            NodeId eventNode = session.AddProperty(condition, Opc.Ua.Namespaces.OpcUa, UaNames.EventId, Variant.From(eventId));
            NodeId source = session.AddProperty(
                condition, Opc.Ua.Namespaces.OpcUa, UaNames.SourceNode, Variant.From(test.Target.NodeId));
            NodeId retain = session.AddProperty(condition, Opc.Ua.Namespaces.OpcUa, UaNames.Retain, Variant.From(true));
            NodeId branch = session.AddProperty(condition, Opc.Ua.Namespaces.OpcUa, UaNames.BranchId, Variant.From(NodeId.Null));
            session.AddProperty(condition, Opc.Ua.Namespaces.OpcUa, UaNames.ConditionName, Variant.From("Alarm"));
            NodeId ackState = session.Id("acked-state");
            session.AddObject(ackState, UaTypes.BaseObjectType);
            session.AddChild(condition, ackState, Opc.Ua.Namespaces.OpcUa, UaNames.AckedState);
            NodeId acked = session.AddProperty(ackState, Opc.Ua.Namespaces.OpcUa, UaNames.Id, Variant.From(false));
            if (maintenance)
            {
                session.AddProperty(condition, Opc.Ua.Namespaces.OpcUa, UaNames.ConditionClassId,
                    Variant.From(UaTypes.MaintenanceConditionClassType));
                NodeId state = session.Id("maintenance-state");
                session.AddObject(state, UaTypes.BaseObjectType);
                session.AddChild(condition, state, Opc.Ua.AMB.Namespaces.AMB, Opc.Ua.AMB.BrowseNames.MaintenanceState);
                NodeId current = session.Id("maintenance-current");
                session.AddObject(current, UaTypes.BaseObjectType);
                session.AddChild(state, current, Opc.Ua.Namespaces.OpcUa, UaNames.CurrentState);
                session.AddProperty(current, Opc.Ua.Namespaces.OpcUa, UaNames.Id, Variant.From(
                    new NodeId(MaintenanceEventStateMachineTypeIds.StateIds.Finished,
                        (ushort)session.NamespaceUris.GetIndex(Opc.Ua.AMB.Namespaces.AMB))));
            }
            NodeId method = session.Id("acknowledge");
            session.AddObject(method, UaTypes.BaseObjectType);
            session.AddChild(condition, method, Opc.Ua.Namespaces.OpcUa, UaNames.Acknowledge);
            session.SetValue(method, Variant.From((int)NodeClass.Method), Attributes.NodeClass);
            session.SetValue(method, Variant.From(true), Attributes.Executable);
            session.SetValue(method, Variant.From(true), Attributes.UserExecutable);
            NodeId arguments = session.AddProperty(method, Opc.Ua.Namespaces.OpcUa, UaNames.InputArguments,
                Variant.FromStructure((ArrayOf<Argument>)
                [
                    new Argument { DataType = Opc.Ua.DataTypeIds.ByteString, ValueRank = ValueRanks.Scalar },
                    new Argument { DataType = Opc.Ua.DataTypeIds.LocalizedText, ValueRank = ValueRanks.Scalar }
                ]));
            var transport = new Mock<ITransportChannel>(MockBehavior.Strict);
            transport.SetupGet(channel => channel.MessageContext).Returns(session.MessageContext);
            session.Session.SetupGet(value => value.TransportChannel).Returns(transport.Object);
            return new AckFixture(condition, method, eventId, eventNode, source, retain, branch, acked, arguments, transport);
        }

        private sealed record AckFixture(
            NodeId Id, NodeId Method, ByteString EventId, NodeId EventNode, NodeId SourceNode,
            NodeId RetainNode, NodeId BranchNode, NodeId AckedNode, NodeId InputArguments, Mock<ITransportChannel> Transport);
    }
}
