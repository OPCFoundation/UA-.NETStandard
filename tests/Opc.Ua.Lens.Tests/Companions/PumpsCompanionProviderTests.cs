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
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Pumps.Client;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using PumpsBrowseNames = Opc.Ua.Pumps.BrowseNames;
using PumpsNamespaces = Opc.Ua.Pumps.Namespaces;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class PumpsCompanionProviderTests
    {
        [Test]
        public async Task DiscoveryDeduplicatesDeviceSetAndMachinesWithoutCallingMethodsAsync()
        {
            (IndustrialCompanionTestSession session, CompanionTarget target) = CreatePump();
            var client = new PumpsClient(session.Session.Object, session.Telemetry, 16);
            session.AddChild(client.Topology.DeviceSetId, target.NodeId);
            session.AddChild(client.MachinesFolderId, target.NodeId);
            var provider = new PumpsCompanionProvider();

            ArrayOf<CompanionTarget> found = await provider.DiscoverAsync(session.Context(), CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0].NodeId, Is.EqualTo(target.NodeId));
            Assert.That(found[0].ProviderId, Is.EqualTo("pumps"));
            Assert.That(found[0].TypeName, Is.EqualTo("Pump"));
            Assert.That(session.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task OverviewReadsOnlyIdentificationAndOffersNoMutationAsync()
        {
            (IndustrialCompanionTestSession session, CompanionTarget target) = CreatePump();
            NodeId identification = AddGroup(session, target.NodeId,
                Opc.Ua.Di.Namespaces.OpcUaDi, "Identification");
            NodeId serial = AddVariable(session, identification, "SerialNumber", Variant.From("SN-123"));
            var provider = new PumpsCompanionProvider();

            CompanionInspection result = await provider.InspectAsync(
                session.Context(), target, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Values, Has.Count.EqualTo(1));
            Assert.That(result.Values[0].SourceNodeId, Is.EqualTo(serial));
            Assert.That(result.Values[0].Value, Is.EqualTo(Variant.From("SN-123")));
            CompanionOperation[] operations = [.. result.Operations];
            BrowseDescription[] browses = [.. session.BrowseCalls];
            Assert.That(operations, Has.Length.EqualTo(12));
            Assert.That(browses, Has.Length.EqualTo(2));
            Assert.That(operations.All(
                operation => operation.Safety == CompanionOperationSafety.ReadOnly),
                Is.True);
            Assert.That(operations.Select(operation => operation.Id),
                Does.Contain("measurements").And.Contain("documentation"));
            Assert.That(browses.All(call =>
                call.NodeId == target.NodeId || call.NodeId == identification), Is.True);
            Assert.That(session.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task MeasurementsRetainBadQualityAndSourceRatherThanInventingGoodZeroAsync()
        {
            (IndustrialCompanionTestSession session, CompanionTarget target) = CreatePump();
            NodeId operational = AddGroup(session, target.NodeId,
                Opc.Ua.Di.Namespaces.OpcUaDi, "Operational");
            NodeId group = AddGroup(session, operational, PumpsNamespaces.Pumps, PumpsBrowseNames.Measurements);
            NodeId value = AddVariable(session, group, "MassFlow", Variant.From(0.0));
            session.SetValue(value, Variant.From(0.0), status: StatusCodes.BadOutOfService);
            var provider = new PumpsCompanionProvider();

            CompanionOperationResult result = await provider.ExecuteAsync(
                session.Context(), target, "measurements", null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Values, Has.Count.EqualTo(1));
            Assert.That(result.Values[0].Quality, Is.EqualTo(StatusCodes.BadOutOfService));
            Assert.That(result.Values[0].SourceNodeId, Is.EqualTo(value));
            Assert.That(result.Values[0].Text, Does.Contain("BadOutOfService"));
            Assert.That(result.Values[0].EngineeringMetadata, Does.Contain("units not exposed"));
            Assert.That(session.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task PortsIncludeNestedEngineeringGroupsRatherThanOnlyIdentityAttributesAsync()
        {
            (IndustrialCompanionTestSession session, CompanionTarget target) = CreatePump();
            NodeId ports = AddGroup(session, target.NodeId, PumpsNamespaces.Pumps, PumpsBrowseNames.Ports);
            NodeId port = AddGroup(session, ports, PumpsNamespaces.Pumps, "Inlet");
            NodeId design = session.Id("port-design");
            session.AddObject(design, Opc.Ua.Pumps.ObjectTypeIds.DesignType, PumpsBrowseNames.Design);
            session.AddChild(port, design, PumpsNamespaces.Pumps, PumpsBrowseNames.Design);
            NodeId flow = AddVariable(session, design, "DesignFlow", Variant.From(25.0));

            CompanionOperationResult result = await new PumpsCompanionProvider().ExecuteAsync(
                session.Context(), target, "ports", null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Values, Has.Count.EqualTo(1));
            Assert.That(result.Values[0].SourceNodeId, Is.EqualTo(flow));
            Assert.That(result.Values[0].Value, Is.EqualTo(Variant.From(25.0)));
            Assert.That(result.Values[0].Name, Does.Contain("Design / DesignFlow"));
            Assert.That(session.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task MissingOptionalSectionIsExplicitAndDoesNotReadAnotherGroupAsync()
        {
            (IndustrialCompanionTestSession session, CompanionTarget target) = CreatePump();
            CompanionOperationResult result = await new PumpsCompanionProvider().ExecuteAsync(
                session.Context(), target, "supervision", null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result.Summary, Does.Contain("not exposed"));
            Assert.That(result.Values[0].Value, Is.EqualTo(Variant.From("Not exposed")));
            Assert.That(session.Calls.IsEmpty, Is.True);
        }

        [TestCaseSource(nameof(s_nonGoodStatuses))]
        public async Task DeniedOrUncertainSectionCannotBePresentedAsAbsentAsync(StatusCode status)
        {
            (IndustrialCompanionTestSession session, CompanionTarget target) = CreatePump();
            session.TranslateHandler = (_, _) => new BrowsePathResult { StatusCode = status };
            await Assert.ThatAsync(
                async () => await new PumpsCompanionProvider().ExecuteAsync(
                    session.Context(), target, "supervision", null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(status))
                .ConfigureAwait(false);
        }

        [TestCase("start")]
        [TestCase("stop")]
        [TestCase("set-speed")]
        public async Task InventedPumpCommandsAreRejectedWithoutServiceCallsAsync(string operation)
        {
            (IndustrialCompanionTestSession session, CompanionTarget target) = CreatePump();
            await Assert.ThatAsync(
                async () => await new PumpsCompanionProvider().ExecuteAsync(
                    session.Context(), target, operation, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadNotSupported)).ConfigureAwait(false);
            Assert.That(session.BrowseCalls.IsEmpty, Is.True);
            Assert.That(session.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task CanceledReadCannotStartAnInspectionAsync()
        {
            (IndustrialCompanionTestSession session, CompanionTarget target) = CreatePump();
            await Assert.ThatAsync(
                async () => await new PumpsCompanionProvider().InspectAsync(
                    session.Context(), target, new CancellationToken(true)).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(session.BrowseCalls.IsEmpty, Is.True);
        }

        private static (IndustrialCompanionTestSession Session, CompanionTarget Target) CreatePump()
        {
            var session = new IndustrialCompanionTestSession();
            session.NamespaceUris.GetIndexOrAppend(PumpsNamespaces.Pumps);
            session.NamespaceUris.GetIndexOrAppend(Opc.Ua.Machinery.Namespaces.Machinery);
            NodeId id = session.Id("pump");
            session.AddObject(id, Opc.Ua.Pumps.ObjectTypeIds.PumpType, "Cooling pump");
            return (session, new CompanionTarget("pumps", id, "Cooling pump", "Pump"));
        }

        private static NodeId AddGroup(
            IndustrialCompanionTestSession session, NodeId parent, string namespaceUri, string name)
        {
            NodeId id = session.Id(parent + "-" + name);
            session.AddObject(id, ObjectTypeIds.BaseObjectType, name);
            session.AddChild(parent, id, namespaceUri, name);
            return id;
        }

        private static NodeId AddVariable(
            IndustrialCompanionTestSession session, NodeId group, string name, Variant value)
        {
            NodeId node = session.AddProperty(group, PumpsNamespaces.Pumps, name, value);
            session.SetReferences(group, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences,
            [
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId(node),
                    BrowseName = new QualifiedName(name, (ushort)session.NamespaceUris.GetIndex(PumpsNamespaces.Pumps)),
                    NodeClass = NodeClass.Variable
                }
            ]);
            return node;
        }

        private static readonly StatusCode[] s_nonGoodStatuses =
            [StatusCodes.BadUserAccessDenied, StatusCodes.Uncertain];
    }
}
