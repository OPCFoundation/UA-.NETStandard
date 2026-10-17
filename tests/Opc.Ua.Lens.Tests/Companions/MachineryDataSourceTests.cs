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
using Opc.Ua.Client;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Machinery;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class MachineryDataSourceTests
    {
        [Test]
        public async Task OverviewDoesNotLoadOtherMachineryPartsAsync()
        {
            var test = new MachineryContext();
            test.Services.TranslateHandler = (path, _) =>
            {
                if (path.StartingNode == test.Machine.NodeId)
                {
                    Assert.That(path.RelativePath.Elements.ToList().Select(element => element.TargetName.Name),
                        Does.Not.Contain("JobManagement").And.Not.Contain("ResultManagement")
                            .And.Not.Contain("Consumption"));
                }
                return test.Services.Translate(path);
            };
            ArrayOf<MachineryMachine> machines = await test.Source.DiscoverAsync(
                test.Context, CancellationToken.None).ConfigureAwait(false);
            Assert.That(machines, Has.Count.EqualTo(1));
            Assert.That(machines[0].NodeId, Is.EqualTo(test.Machine.NodeId));
            MachineryPage overview = await test.Source.ReadAsync(
                test.Context, machines[0], MachinerySection.Overview, 0, 32, CancellationToken.None)
                .ConfigureAwait(false);
            CompanionValue manufacturer = overview.Overview.ToList().Single(value => value.Name == "Manufacturer");
            Assert.That(manufacturer.Value, Is.EqualTo(Variant.From(new LocalizedText("Acme"))));
            Assert.That(manufacturer.SourceNodeId, Is.EqualTo(test.Manufacturer));
            Assert.That(manufacturer.Quality, Is.EqualTo(StatusCodes.Good));
            Assert.That(overview.Overview.Count, Is.LessThanOrEqualTo(16));
            Assert.That(overview.Items.IsEmpty, Is.True);
            Assert.That(overview.HasMore, Is.False);
            Assert.That(test.Services.Calls.IsEmpty, Is.True);
            Assert.That(test.Context.Session.Connected, Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task DirectManagementObjectsWorkWithoutInventingAMachineTypeAsync(bool jobs)
        {
            var test = new MachineryContext();
            NodeId machines = test.Services.Resolve(Opc.Ua.Machinery.ObjectIds.Machines);
            test.Services.SetReferences(
                machines, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences, []);
            string model = jobs ? Opc.Ua.Machinery.Jobs.Namespaces.MachineryJobs
                : Opc.Ua.Machinery.Result.Namespaces.MachineryResult;
            NodeId management = test.Services.Id("direct-management");
            test.Services.AddObject(management, jobs ? Opc.Ua.Machinery.Jobs.ObjectTypeIds.JobManagementType
                : Opc.Ua.Machinery.Result.ObjectTypeIds.ResultManagementType);
            test.Services.AddChild(ObjectIds.ObjectsFolder, management, model,
                jobs ? "JobManagement" : "ResultManagement");
            NodeId content = test.Services.Id("direct-content");
            test.Services.AddObject(content, jobs
                ? Opc.Ua.ISA95.JobControl.V2.ObjectTypeIds.ISA95JobOrderReceiverObjectType
                : ObjectTypeIds.FolderType);
            test.Services.AddChild(management, content, model, jobs ? "JobOrderControl" : "Results");
            if (jobs)
            {
                test.Services.AddProperty(content, Opc.Ua.ISA95.JobControl.V2.Namespaces.ISA95JobControlV2,
                    "JobOrderList", Variant.FromStructure(
                        ArrayOf<Opc.Ua.ISA95.JobControl.V2.ISA95JobOrderAndStateDataType>.Empty));
            }
            ArrayOf<MachineryMachine> sources = await test.Source.DiscoverAsync(
                test.Context, CancellationToken.None).ConfigureAwait(false);
            Assert.That(sources, Has.Count.EqualTo(1));
            Assert.That(sources[0].Kind, Is.EqualTo("Management owner"));
            Assert.That(sources[0].NodeId, Is.EqualTo(ObjectIds.ObjectsFolder));
            MachineryPage overview = await test.Source.ReadAsync(
                test.Context, sources[0], MachinerySection.Overview, 0, 32, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(overview.Status, Does.Contain("not a physical machine"));
            MachineryPage page = await test.Source.ReadAsync(
                test.Context, sources[0], jobs ? MachinerySection.Jobs : MachinerySection.Results,
                0, 32, CancellationToken.None).ConfigureAwait(false);
            Assert.That(page.Items.IsEmpty, Is.True);
            Assert.That(page.HasMore, Is.False);
            MachineryPage unrelated = await test.Source.ReadAsync(
                test.Context, sources[0], MachinerySection.Energy, 0, 32, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(unrelated.Status, Does.StartWith("Not exposed"));
            Assert.That(test.Services.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task RemovedMachineMembershipCannotReadAnotherObjectsValuesAsync()
        {
            var test = new MachineryContext();
            test.Services.SetReferences(test.Machine.NodeId, BrowseDirection.Inverse,
                ReferenceTypeIds.HierarchicalReferences, []);
            await Assert.ThatAsync(() => test.Source.ReadAsync(
                test.Context, test.Machine, MachinerySection.Overview, 0, 32, CancellationToken.None),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadInvalidState)).ConfigureAwait(false);
        }

        [Test]
        public async Task DeniedIdentityDoesNotBecomeOptionalAbsenceAsync()
        {
            var test = new MachineryContext();
            test.Services.SetValue(test.Manufacturer, Variant.From(new LocalizedText("Acme")),
                status: StatusCodes.BadUserAccessDenied);
            await Assert.ThatAsync(() => test.Source.ReadAsync(
                test.Context, test.Machine, MachinerySection.Overview, 0, 32, CancellationToken.None),
                Throws.InstanceOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadUserAccessDenied)).ConfigureAwait(false);
        }

        [Test]
        public async Task PreCanceledReadDoesNotContactTheSessionAsync()
        {
            var test = new MachineryContext();
            test.Services.ReadHandler = (_, _) => throw new AssertionException("A canceled read must not start.");
            await Assert.ThatAsync(() => test.Source.ReadAsync(
                test.Context, test.Machine, MachinerySection.Overview, 0, 32, new CancellationToken(true)),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(test.Services.BrowseCalls.IsEmpty, Is.True);
        }

        [TestCase(0, 4, true)]
        [TestCase(1, 4, true)]
        [TestCase(8, 1, false)]
        public async Task ResultPagesDoNotReadUnselectedPayloadsAsync(int pageIndex, int expectedCount, bool hasMore)
        {
            var test = new MachineryContext();
            NodeId folder = AddResultFolder(test);
            test.Services.SetReferences(folder, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences,
            [
                .. Enumerable.Range(0, 33).Select(index => new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId(test.Services.Id("result-" + index)),
                    NodeClass = NodeClass.Variable,
                    BrowseName = new QualifiedName("Result " + index),
                    DisplayName = new LocalizedText("Result " + index),
                    TypeDefinition = Opc.Ua.Machinery.Result.VariableTypeIds.ResultType
                })
            ]);
            int payloadReads = 0;
            test.Services.ReadHandler = (read, _) =>
            {
                if (read.AttributeId == Attributes.Value)
                {
                    payloadReads++;
                    throw new AssertionException("Listing result identities must not read their payloads.");
                }
                return test.Services.Read(read);
            };
            MachineryPage page = await test.Source.ReadAsync(
                test.Context, test.Machine, MachinerySection.Results, pageIndex, 4, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(page.Items, Has.Count.EqualTo(expectedCount));
            Assert.That(page.HasMore, Is.EqualTo(hasMore));
            Assert.That(page.Items[0].NodeId, Is.EqualTo(test.Services.Id("result-" + (pageIndex * 4))));
            Assert.That(payloadReads, Is.Zero);
            Assert.That(page.Items.ToList().All(item => item.Values.IsEmpty && item.ItemId is null), Is.True);
        }

        [Test]
        public async Task SelectedResultReadKeepsExactIdentityAndSourceEvidenceAsync()
        {
            var test = new MachineryContext();
            NodeId folder = AddResultFolder(test);
            NodeId result = test.Services.Id("result");
            test.Services.SetReferences(folder, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences,
            [
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId(result), NodeClass = NodeClass.Variable,
                    BrowseName = new QualifiedName("Result 1"),
                    TypeDefinition = Opc.Ua.Machinery.Result.VariableTypeIds.ResultType
                }
            ]);
            test.Services.SetValue(result, Variant.FromStructure(new Opc.Ua.Machinery.Result.ResultDataType
            {
                ResultMetaData = new Opc.Ua.Machinery.Result.ResultMetaDataType { ResultId = "exact-result" }
            }));
            MachineryPage page = await test.Source.ReadAsync(
                test.Context, test.Machine, MachinerySection.Results, 0, 32, CancellationToken.None)
                .ConfigureAwait(false);
            ArrayOf<CompanionValue> inspected = await test.Source.InspectAsync(
                test.Context, test.Machine, MachinerySection.Results, page.Items[0], CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(inspected.ToList().Single(value => value.Name == "Result ID").Value,
                Is.EqualTo(Variant.From("exact-result")));
            CompanionValue value = inspected.ToList().Single(field => field.Name == "Published result");
            Assert.That(value.SourceNodeId, Is.EqualTo(result));
            Assert.That(value.Quality, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task JobPagesRetainOnlyTheSelectedOrderInEachDisplayRowAsync()
        {
            var test = new MachineryContext();
            NodeId management = test.Services.Id("jobs");
            NodeId control = test.Services.Id("order-control");
            test.Services.AddObject(management, Opc.Ua.Machinery.Jobs.ObjectTypeIds.JobManagementType);
            test.Services.AddObject(control, Opc.Ua.ISA95.JobControl.V2.ObjectTypeIds.ISA95JobOrderReceiverObjectType);
            test.Services.AddChild(test.Machine.NodeId, management,
                Opc.Ua.Machinery.Jobs.Namespaces.MachineryJobs, "JobManagement");
            test.Services.AddChild(management, control,
                Opc.Ua.Machinery.Jobs.Namespaces.MachineryJobs, "JobOrderControl");
            ArrayOf<Opc.Ua.ISA95.JobControl.V2.ISA95JobOrderAndStateDataType> orders =
            [
                .. Enumerable.Range(0, 256).Select(index =>
                    new Opc.Ua.ISA95.JobControl.V2.ISA95JobOrderAndStateDataType
                    {
                        JobOrder = new Opc.Ua.ISA95.JobControl.V2.ISA95JobOrderDataType
                        {
                            JobOrderID = "job-" + index
                        },
                        State = [new Opc.Ua.ISA95.JobControl.V2.ISA95StateDataType { StateNumber = 1 }]
                    })
            ];
            NodeId list = test.Services.AddProperty(control,
                Opc.Ua.ISA95.JobControl.V2.Namespaces.ISA95JobControlV2, "JobOrderList",
                Variant.FromStructure(orders));
            MachineryPage page = await test.Source.ReadAsync(
                test.Context, test.Machine, MachinerySection.Jobs, 1, 32, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(page.Items, Has.Count.EqualTo(32));
            Assert.That(page.HasMore, Is.True);
            Assert.That(page.Items[0].ItemId, Is.EqualTo("job-32"));
            Assert.That(page.Items[^1].ItemId, Is.EqualTo("job-63"));
            foreach (MachineryItem item in page.Items)
            {
                Assert.That(item.Values, Has.Count.EqualTo(1));
                Assert.That(item.Values[0].SourceNodeId, Is.EqualTo(list));
                Assert.That(item.Values[0].Value.TryGetValue<Opc.Ua.ISA95.JobControl.V2.ISA95JobOrderAndStateDataType>(
                    out Opc.Ua.ISA95.JobControl.V2.ISA95JobOrderAndStateDataType? selected,
                    test.Services.MessageContext), Is.True);
                Assert.That(selected!.JobOrder.JobOrderID, Is.EqualTo(item.ItemId));
            }
        }

        [TestCase(-1, 32)]
        [TestCase(1024, 32)]
        [TestCase(0, 0)]
        [TestCase(0, 33)]
        public async Task InvalidPagingDoesNotStartAReadAsync(int pageIndex, int pageSize)
        {
            var test = new MachineryContext();
            test.Services.ReadHandler = (_, _) => throw new AssertionException("Invalid paging must not read.");
            await Assert.ThatAsync(() => test.Source.ReadAsync(
                test.Context, test.Machine, MachinerySection.Results, pageIndex, pageSize, CancellationToken.None),
                Throws.InstanceOf<ArgumentOutOfRangeException>()).ConfigureAwait(false);
        }

        private static NodeId AddResultFolder(MachineryContext test)
        {
            NodeId management = test.Services.Id("result-management");
            NodeId folder = test.Services.Id("results");
            test.Services.AddObject(management, Opc.Ua.Machinery.Result.ObjectTypeIds.ResultManagementType);
            test.Services.AddObject(folder, ObjectTypeIds.FolderType);
            test.Services.AddChild(test.Machine.NodeId, management, Opc.Ua.Machinery.Result.Namespaces.MachineryResult,
                "ResultManagement");
            test.Services.AddChild(management, folder, Opc.Ua.Machinery.Result.Namespaces.MachineryResult, "Results");
            return folder;
        }

        private sealed class MachineryContext
        {
            public MachineryContext()
            {
                Services.NamespaceUris.GetIndexOrAppend(Opc.Ua.Machinery.Namespaces.Machinery);
                Services.NamespaceUris.GetIndexOrAppend(Opc.Ua.Machinery.Jobs.Namespaces.MachineryJobs);
                Services.NamespaceUris.GetIndexOrAppend(Opc.Ua.Machinery.Result.Namespaces.MachineryResult);
                Services.Session.SetupGet(session => session.Factory).Returns(Services.MessageContext.Factory);
                Services.Session.SetupGet(session => session.Connected).Returns(true);
                Services.Session.SetupGet(session => session.SessionId).Returns(new NodeId(1234u));
                Services.Session.SetupGet(session => session.Identity).Returns(new UserIdentity());
                Services.Session.SetupGet(session => session.Endpoint)
                    .Returns(new EndpointDescription("opc.tcp://127.0.0.1:58126/Machinery"));
                var cache = new Mock<INodeCache>(MockBehavior.Strict);
                cache.Setup(value => value.IsTypeOfAsync(
                    It.IsAny<ExpandedNodeId>(), It.IsAny<ExpandedNodeId>(), It.IsAny<CancellationToken>()))
                    .Returns((ExpandedNodeId actual, ExpandedNodeId expected, CancellationToken _) =>
                        new ValueTask<bool>(Services.Resolve(actual) == Services.Resolve(expected)));
                Services.Session.SetupGet(session => session.NodeCache).Returns(cache.Object);
                NodeId root = Services.Resolve(Opc.Ua.Machinery.ObjectIds.Machines);
                Services.AddObject(root, ObjectTypeIds.FolderType, "Machines");
                NodeId machine = Services.Id("press");
                Services.AddObject(machine, ObjectTypeIds.BaseObjectType, "HydraPress");
                Services.AddChild(root, machine);
                NodeId identification = Services.Id("identification");
                Services.AddObject(identification, ObjectTypeIds.BaseObjectType);
                Services.AddChild(machine, identification, Opc.Ua.Di.Namespaces.OpcUaDi, "Identification");
                Manufacturer = Services.AddProperty(
                    identification, Opc.Ua.Di.Namespaces.OpcUaDi, "Manufacturer",
                    Variant.From(new LocalizedText("Acme")));
                Services.AddProperty(
                    identification, Opc.Ua.Di.Namespaces.OpcUaDi, "SerialNumber", Variant.From("PRESS-1"));
                Services.AddProperty(identification, Opc.Ua.Di.Namespaces.OpcUaDi,
                    "ProductInstanceUri", Variant.From("urn:acme:press:1"));
                Machine = new MachineryMachine(machine, "HydraPress", "Machine");
            }

            public IndustrialCompanionTestSession Services { get; } = new();

            public CompanionContext Context => Services.Context();

            public MachineryDataSource Source { get; } = new();

            public MachineryMachine Machine { get; }

            public NodeId Manufacturer { get; }
        }
    }
}
