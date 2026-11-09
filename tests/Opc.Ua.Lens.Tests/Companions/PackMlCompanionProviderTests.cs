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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PackML;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using PackObjects = Opc.Ua.PackML.Objects;
using PackTypes = Opc.Ua.PackML.ObjectTypeIds;
using UaTypes = Opc.Ua.ObjectTypeIds;
using UaNames = Opc.Ua.BrowseNames;
using UaRefs = Opc.Ua.ReferenceTypeIds;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class PackMlCompanionProviderTests
    {
        [Test]
        public async Task DiscoveryFindsGenericStateMachinesWithoutTreatingThemAsScalesAsync()
        {
            var test = new PackContext();
            ArrayOf<CompanionTarget> found = await test.Provider.DiscoverAsync(test.Context, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(found, Has.Count.EqualTo(3));
            string[] expectedKinds = ["PackML base", "PackML machine", "PackML execute"];
            Assert.That(found.ToList().Select(target => target.TypeName),
                Is.EquivalentTo(expectedKinds));
            Assert.That(test.Services.Calls.IsEmpty, Is.True);
            Assert.That(test.Services.NamespaceUris.GetIndex(Opc.Ua.Scales.Namespaces.Scales), Is.LessThan(0));
        }

        [Test]
        public async Task InspectionShowsOnlyActiveHierarchyAndOwnedRunCommandsAsync()
        {
            var test = new PackContext();
            CompanionInspection inspection = await test.Provider.InspectAsync(test.Context, test.Target, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(inspection.Values.ToList().Count(value => !value.SourceNodeId.IsNull), Is.EqualTo(6));
            Assert.That(inspection.Operations.ToList().Select(operation => operation.Id), Does.Contain("start"));
            Assert.That(inspection.Summary, Does.Contain("equipment-owned"));
            var denied = new PackMlCompanionProvider();
            CompanionInspection noGrant = await denied.InspectAsync(test.Context, test.Target, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(noGrant.Operations, Has.Count.EqualTo(1));
            Assert.That(noGrant.Operations[0].Id, Is.EqualTo("snapshot"));
            Assert.That(test.Services.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task AbortedBaseDoesNotReadInactiveSubmachinesAsync()
        {
            var test = new PackContext();
            test.Services.SetValue(test.BaseStateId, Variant.From(test.State(PackObjects.PackMLBaseStateMachineType_Aborted)));
            test.Services.ReadHandler = (read, _) =>
            {
                Assert.That(read.NodeId, Is.Not.EqualTo(test.MachineStateId).And.Not.EqualTo(test.ExecuteStateId));
                return test.Services.Read(read);
            };
            CompanionInspection inspection = await test.Provider.InspectAsync(test.Context, test.Target, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(inspection.Values.ToList().Count(value => !value.SourceNodeId.IsNull), Is.EqualTo(2));
            Assert.That(inspection.Operations.ToList().Any(operation => operation.Id == "start"), Is.False);
        }

        [Test]
        public async Task StartUsesCopiedDescriptorsAndDoesNotClaimActingStateCompletionAsync()
        {
            var test = new PackContext();
            var parameter = new PackMLDescriptorDataType
            {
                ID = 7,
                Name = "Target",
                Unit = new EUInformation { NamespaceUri = "urn:units", UnitId = 1 },
                Value = 12.5f
            };
            CompanionTaskInput prepared = await test.Provider.PrepareInputAsync(
                test.Context, test.Target, "start",
                [new("parameters", Variant.FromStructure((ArrayOf<PackMLDescriptorDataType>)[parameter]))],
                CancellationToken.None).ConfigureAwait(false);
            parameter.Value = 1000f;
            parameter.Unit.UnitId = 99;
            test.Services.CallHandler = (call, _) =>
            {
                Assert.That(call.ObjectId, Is.EqualTo(test.Execute));
                Assert.That(call.MethodId, Is.EqualTo(test.StartMethod));
                Assert.That(call.InputArguments[0].TryGetValue(
                    out ArrayOf<PackMLDescriptorDataType> parameters, test.Services.MessageContext), Is.True);
                Assert.That(parameters, Has.Count.EqualTo(1));
                Assert.That(parameters[0].Value, Is.EqualTo(12.5f));
                Assert.That(parameters[0].Unit.UnitId, Is.EqualTo(1));
                test.Services.SetValue(test.ExecuteStateId,
                    Variant.From(test.State(PackObjects.PackMLExecuteStateMachineType_Starting)));
                return new CallMethodResult();
            };
            CompanionOperationResult result = await test.Provider.ExecutePreparedAsync(
                test.Context, test.Target, "start", prepared, null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result.Summary, Does.Contain("accepted").And.Contain("equipment-owned"));
            Assert.That(result.Values[0].Value, Is.EqualTo(Variant.From(StatusCodes.Good)));
            Assert.That(test.Services.Calls, Has.Count.EqualTo(1));
            await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                test.Context, test.Target, "start", prepared, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            test.Transport.Verify(channel => channel.Dispose(), Times.Never);
        }

        [TestCase("state")]
        [TestCase("identity")]
        [TestCase("certificate")]
        [TestCase("namespace")]
        [TestCase("permission")]
        [TestCase("expiry")]
        public async Task StaleCommandEvidenceCannotDispatchAsync(string change)
        {
            var test = new PackContext();
            CompanionTaskInput prepared = await test.PrepareStartAsync().ConfigureAwait(false);
            switch (change)
            {
                case "state":
                    test.Services.SetValue(test.ExecuteStateId,
                        Variant.From(test.State(PackObjects.PackMLExecuteStateMachineType_Complete)));
                    break;
                case "identity":
                    test.Services.Session.SetupGet(session => session.Identity).Returns(new UserIdentity());
                    break;
                case "certificate":
                    test.Endpoint.ServerCertificate = ByteString.From("replacement-peer"u8);
                    break;
                case "namespace":
                    test.Services.NamespaceUris.GetIndexOrAppend("urn:replacement");
                    break;
                case "permission":
                    test.Services.SetValue(test.StartMethod, Variant.From(false), Attributes.UserExecutable);
                    break;
                case "expiry":
                    test.Clock.Now = test.Expiry;
                    break;
                default:
                    throw new AssertionException("Unknown evidence partition.");
            }
            await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                test.Context, test.Target, "start", prepared, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf(change is "state" or "permission"
                    ? typeof(ServiceResultException) : typeof(UnauthorizedAccessException))).ConfigureAwait(false);
            Assert.That(test.Services.Calls.IsEmpty, Is.True);
        }

        [TestCase("uncertain")]
        [TestCase("timeout")]
        [TestCase("invalid-method")]
        public async Task NonGoodCommandOutcomeIsNeverRetriedAsync(string kind)
        {
            var test = new PackContext();
            CompanionTaskInput prepared = await test.PrepareStartAsync().ConfigureAwait(false);
            test.Services.CallHandler = (_, _) => kind switch
            {
                "timeout" => throw new TimeoutException("Unknown server outcome"),
                "uncertain" => new CallMethodResult { StatusCode = StatusCodes.Uncertain },
                _ => new CallMethodResult { StatusCode = StatusCodes.BadMethodInvalid }
            };
            await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                test.Context, test.Target, "start", prepared, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf(kind == "timeout" ? typeof(TimeoutException) : typeof(ServiceResultException)))
                .ConfigureAwait(false);
            Assert.That(test.Services.Calls, Has.Count.EqualTo(1));
        }

        [TestCase(32, true)]
        [TestCase(33, false)]
        public async Task DescriptorCountIsBoundedBeforePreparationAsync(int count, bool accepted)
        {
            var test = new PackContext();
            ArrayOf<PackMLDescriptorDataType> descriptors = Enumerable.Range(0, count)
                .Select(id => new PackMLDescriptorDataType
                {
                    ID = id,
                    Name = "Parameter" + id,
                    Unit = new EUInformation { NamespaceUri = "urn:unit" },
                    Value = 1f
                }).ToArrayOf();
            var input = new CompanionValue("parameters", Variant.FromStructure(descriptors));
            if (accepted)
            {
                CompanionTaskInput result = await test.Provider.PrepareInputAsync(
                    test.Context, test.Target, "start", [input], CancellationToken.None).ConfigureAwait(false);
                Assert.That(result.Review, Does.Contain("32 start parameter"));
            }
            else
            {
                await Assert.ThatAsync(async () => await test.Provider.PrepareInputAsync(
                    test.Context, test.Target, "start", [input], CancellationToken.None).ConfigureAwait(false),
                    Throws.TypeOf<ArgumentException>()).ConfigureAwait(false);
            }
            Assert.That(test.Services.Calls.IsEmpty, Is.True);
        }

        [TestCase("id-quality")]
        [TestCase("label-type")]
        public async Task BadStateEvidenceCannotLookLikeAValidStateAsync(string invalid)
        {
            var test = new PackContext();
            if (invalid == "id-quality")
            {
                test.Services.SetValue(test.BaseStateId,
                    Variant.From(test.State(PackObjects.PackMLBaseStateMachineType_Cleared)), status: StatusCodes.Uncertain);
            }
            else
            {
                test.Services.SetValue(test.BaseValueNode, Variant.From("Wrong wire type"));
            }
            await Assert.ThatAsync(async () => await test.Provider.InspectAsync(
                test.Context, test.Target, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
            Assert.That(test.Services.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task InactiveStateWithGoodNullIdentifierRemainsExplicitAndCannotOfferCommandsAsync()
        {
            var test = new PackContext();
            test.Services.SetValue(test.BaseValueNode, Variant.From(LocalizedText.Null),
                status: StatusCodes.BadStateNotActive);
            test.Services.SetValue(test.BaseStateId, Variant.From(NodeId.Null));
            CompanionInspection inspection = await test.Provider.InspectAsync(
                test.Context, test.Target, CancellationToken.None).ConfigureAwait(false);
            Assert.That(inspection.Operations, Has.Count.EqualTo(1));
            Assert.That(inspection.Values[0].Quality, Is.EqualTo(StatusCodes.BadStateNotActive));
            Assert.That(inspection.Values[1].Value.TryGetValue(out NodeId inactive) && inactive.IsNull, Is.True);
            Assert.That(test.Services.Calls.IsEmpty, Is.True);
        }

        [TestCase("duplicate")]
        [TestCase("nan")]
        [TestCase("infinite")]
        [TestCase("unit")]
        [TestCase("name")]
        public async Task InvalidStartDescriptorsAreRejectedBeforeDiscoveryOrMutationAsync(string kind)
        {
            var test = new PackContext();
            var first = new PackMLDescriptorDataType
            {
                ID = 1,
                Name = "Rate",
                Value = 12,
                Unit = new EUInformation { NamespaceUri = "urn:units" }
            };
            switch (kind)
            {
                case "nan":
                    first.Value = float.NaN;
                    break;
                case "infinite":
                    first.Value = float.PositiveInfinity;
                    break;
                case "unit":
                    first.Unit.NamespaceUri = string.Empty;
                    break;
                case "name":
                    first.Name = new string('x', 257);
                    break;
                case "duplicate":
                    break;
                default:
                    throw new AssertionException("Unknown descriptor partition.");
            }
            ArrayOf<PackMLDescriptorDataType> parameters = kind == "duplicate" ? [first, first] : [first];
            await Assert.ThatAsync(async () => await test.Provider.PrepareInputAsync(
                test.Context, test.Target, "start",
                [new("parameters", Variant.FromStructure(parameters))], CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>()).ConfigureAwait(false);
            Assert.That(test.Services.BrowseCalls.IsEmpty, Is.True);
            Assert.That(test.Services.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task CancellationConsumesAPreparedCommandWithoutSendingItAsync()
        {
            var test = new PackContext();
            CompanionTaskInput input = await test.PrepareStartAsync().ConfigureAwait(false);
            await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                test.Context, test.Target, "start", input, null, new CancellationToken(true)).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                test.Context, test.Target, "start", input, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(test.Services.Calls.IsEmpty, Is.True);
        }

        private sealed class PackContext
        {
            public PackContext()
            {
                Services.NamespaceUris.GetIndexOrAppend(Opc.Ua.PackML.Namespaces.PackML);
                Services.MessageContext.Factory.Builder.AddOpcUaPackML().Commit();
                Endpoint = new EndpointDescription("opc.tcp://127.0.0.1:58124/ScalesServer")
                {
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                    Server = new ApplicationDescription { ApplicationUri = "urn:owned:PackML" },
                    ServerCertificate = ByteString.From("public-peer"u8)
                };
                Services.Session.SetupGet(session => session.Connected).Returns(true);
                Services.Session.SetupGet(session => session.SessionId).Returns(new NodeId(1234u));
                Services.Session.SetupGet(session => session.Identity).Returns(new UserIdentity());
                Services.Session.SetupGet(session => session.Endpoint).Returns(Endpoint);
                Transport.SetupGet(channel => channel.MessageContext).Returns(Services.MessageContext);
                Services.Session.SetupGet(session => session.TransportChannel).Returns(Transport.Object);
                NodeId root = Services.Id("base");
                Target = new CompanionTarget("packml", root, "Generic PackML owner", "PackML base");
                Services.AddObject(root, PackTypes.PackMLBaseStateMachineType);
                Services.AddChild(Opc.Ua.ObjectIds.ObjectsFolder, root);
                (BaseValueNode, BaseStateId) = AddState(root, PackObjects.PackMLBaseStateMachineType_Cleared);
                NodeId machine = Services.Id("machine");
                Services.AddObject(machine, PackTypes.PackMLMachineStateMachineType);
                Services.AddChild(root, machine, Opc.Ua.PackML.Namespaces.PackML, Opc.Ua.PackML.BrowseNames.MachineState);
                (_, MachineStateId) = AddState(machine, PackObjects.PackMLMachineStateMachineType_Running);
                Execute = Services.Id("execute");
                Services.AddObject(Execute, PackTypes.PackMLExecuteStateMachineType);
                Services.AddChild(machine, Execute, Opc.Ua.PackML.Namespaces.PackML, Opc.Ua.PackML.BrowseNames.ExecuteState);
                (_, ExecuteStateId) = AddState(Execute, PackObjects.PackMLExecuteStateMachineType_Idle);
                StartMethod = Services.Id("start");
                Services.AddObject(StartMethod, UaTypes.BaseObjectType);
                Services.AddChild(Execute, StartMethod, Opc.Ua.PackML.Namespaces.PackML, Opc.Ua.PackML.BrowseNames.Start);
                Services.SetValue(StartMethod, Variant.From((int)NodeClass.Method), Attributes.NodeClass);
                Services.SetValue(StartMethod, Variant.From(true), Attributes.Executable);
                Services.SetValue(StartMethod, Variant.From(true), Attributes.UserExecutable);
                Services.AddProperty(StartMethod, Opc.Ua.Namespaces.OpcUa, UaNames.InputArguments,
                    Variant.FromStructure((ArrayOf<Argument>)
                    [
                        new Argument
                        {
                            DataType = ExpandedNodeId.ToNodeId(
                                Opc.Ua.PackML.DataTypeIds.PackMLDescriptorDataType, Services.NamespaceUris),
                            ValueRank = ValueRanks.OneDimension
                        }
                    ]));
                Expiry = Clock.Now.AddMinutes(1);
                var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
                policy.Setup(value => value.AuthorizeAsync(
                    It.IsAny<CompanionContext>(), It.IsAny<CompanionOperationDraft>(), It.IsAny<CancellationToken>()))
                    .Returns(() => new ValueTask<CompanionDeploymentGrant>(
                        new CompanionDeploymentGrant("owned-packml", "1", Expiry)));
                Provider = new PackMlCompanionProvider(policy.Object, Clock);
            }

            public IndustrialCompanionTestSession Services { get; } = new();
            public CompanionContext Context => Services.Context();
            public Mock<ITransportChannel> Transport { get; } = new(MockBehavior.Strict);
            public EndpointDescription Endpoint { get; }
            public CompanionTarget Target { get; }
            public PackMlCompanionProvider Provider { get; }
            public PackClock Clock { get; } = new();
            public DateTimeOffset Expiry { get; }
            public NodeId Execute { get; }
            public NodeId BaseValueNode { get; }
            public NodeId BaseStateId { get; }
            public NodeId MachineStateId { get; }
            public NodeId ExecuteStateId { get; }
            public NodeId StartMethod { get; }

            public NodeId State(uint numeric) =>
                new(numeric, (ushort)Services.NamespaceUris.GetIndex(Opc.Ua.PackML.Namespaces.PackML));

            public ValueTask<CompanionTaskInput> PrepareStartAsync()
            {
                return Provider.PrepareInputAsync(Context, Target, "start",
                    [new("parameters", Variant.FromStructure(ArrayOf<PackMLDescriptorDataType>.Empty))],
                    CancellationToken.None);
            }

            private (NodeId Value, NodeId Id) AddState(NodeId owner, uint state)
            {
                NodeId variable = Services.Id(owner + "-current");
                Services.AddObject(variable, UaTypes.BaseObjectType);
                Services.AddChild(owner, variable, Opc.Ua.Namespaces.OpcUa, UaNames.CurrentState);
                Services.SetValue(variable, Variant.From(new LocalizedText("State")));
                NodeId id = Services.AddProperty(variable, Opc.Ua.Namespaces.OpcUa, UaNames.Id, Variant.From(State(state)));
                return (variable, id);
            }
        }

        private sealed class PackClock : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
            public override DateTimeOffset GetUtcNow() => Now;
        }
    }
}
