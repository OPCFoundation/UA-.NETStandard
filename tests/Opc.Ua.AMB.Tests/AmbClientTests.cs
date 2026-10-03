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
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.AMB.Client;
using Opc.Ua.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Tests the parts of the Asset Management Basics client that need no
    /// server: the event filters, the mapping of event fields to records,
    /// the normalization of values and the hosting extension.
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    public sealed class AmbClientTests
    {
        [Test]
        public void TheConditionFiltersSelectInterfaceFieldsOfEveryEventAndSplitByConditionClass()
        {
            NamespaceTable namespaceUris = CreateNamespaces();
            ushort amb = (ushort)namespaceUris.GetIndex(Namespaces.AMB);
            HashSet<NodeId> classes = AmbConditionFields.KnownMaintenanceClasses(namespaceUris);

            EventFilter health = AmbConditionFields.Filter(namespaceUris, classes, maintenance: false);
            EventFilter maintenance = AmbConditionFields.Filter(namespaceUris, classes, maintenance: true);
            SimpleAttributeOperand conditionId = health.SelectClauses[AmbConditionFields.ConditionId];
            SimpleAttributeOperand rootCauses = health.SelectClauses[AmbConditionFields.PotentialRootCauses];
            SimpleAttributeOperand state = health.SelectClauses[AmbConditionFields.MaintenanceState];
            ContentFilterElement healthRoot = health.WhereClause.Elements[0];
            ContentFilterElement maintenanceRoot = maintenance.WhereClause.Elements[0];

            Assert.Multiple(() =>
            {
                Assert.That(health.SelectClauses.Count, Is.EqualTo(AmbConditionFields.Count));
                Assert.That(conditionId.AttributeId, Is.EqualTo(Attributes.NodeId));
                Assert.That(conditionId.TypeDefinitionId, Is.EqualTo(Ua.ObjectTypeIds.ConditionType));
                Assert.That(conditionId.BrowsePath.Count, Is.Zero);
                Assert.That(rootCauses.TypeDefinitionId, Is.EqualTo(Ua.ObjectTypeIds.BaseEventType));
                Assert.That(rootCauses.BrowsePath[0], Is.EqualTo(new QualifiedName("PotentialRootCauses", amb)));
                Assert.That(state.TypeDefinitionId, Is.EqualTo(Ua.ObjectTypeIds.BaseEventType));
                Assert.That(state.BrowsePath.Count, Is.EqualTo(3));
                Assert.That(state.BrowsePath[2], Is.EqualTo(new QualifiedName("Id")));
                Assert.That(
                    health.SelectClauses[AmbConditionFields.Comment].BrowsePath[0],
                    Is.EqualTo(new QualifiedName("Comment")));
                Assert.That(healthRoot.FilterOperator, Is.EqualTo(FilterOperator.And));
                Assert.That(maintenanceRoot.FilterOperator, Is.EqualTo(FilterOperator.And));
                Assert.That(
                    health.WhereClause.Elements.ToArray()!.Select(element => element.FilterOperator),
                    Does.Contain(FilterOperator.Not),
                    "the health filter leaves the maintenance classes out");
                Assert.That(
                    maintenance.WhereClause.Elements.ToArray()!.Select(element => element.FilterOperator),
                    Does.Not.Contain(FilterOperator.Not));
                Assert.That(
                    classes,
                    Does.Contain(Ua.ObjectTypeIds.MaintenanceConditionClassType)
                        .And.Contain(AmbConditionClass.Inspection.GetTypeId(namespaceUris)));
                Assert.That(classes, Does.Not.Contain(AmbConditionClass.ConnectionFailure.GetTypeId(namespaceUris)));
            });
        }

        [Test]
        public void TheRefreshFilterSelectsTheConditionsOfAnAssetAndTheBracket()
        {
            NamespaceTable namespaceUris = CreateNamespaces();
            var asset = new NodeId("Pump", 3);

            EventFilter filter = AmbConditionFields.RefreshFilter(namespaceUris, asset);
            Variant[] end = Empty();
            end[AmbConditionFields.EventType] = Variant.From(Ua.ObjectTypeIds.RefreshEndEventType);
            Variant[] start = Empty();
            start[AmbConditionFields.EventType] = Variant.From(Ua.ObjectTypeIds.RefreshStartEventType);

            Assert.Multiple(() =>
            {
                Assert.That(filter.SelectClauses.Count, Is.EqualTo(AmbConditionFields.Count));
                Assert.That(filter.WhereClause.Elements[0].FilterOperator, Is.EqualTo(FilterOperator.Or));
                Assert.That(
                    filter.WhereClause.Elements.ToArray()!.Select(element => element.FilterOperator),
                    Does.Contain(FilterOperator.Equals));
                Assert.That(AmbConditionFields.IsRefreshEnd(end), Is.True);
                Assert.That(AmbConditionFields.IsRefreshBracket(start), Is.True);
                Assert.That(AmbConditionFields.IsRefreshEnd(start), Is.False);
            });
        }

        [Test]
        public void TheFiltersNeedTheAmbAndDeviceIntegrationNamespaces()
        {
            var withoutDi = new NamespaceTable();
            withoutDi.GetIndexOrAppend(Namespaces.AMB);

            Assert.Multiple(() =>
            {
                Assert.That(
                    Assert.Throws<ServiceResultException>(() => AmbConditionFields.Build(new NamespaceTable()))!
                        .StatusCode,
                    Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.Throws<ServiceResultException>(() => AmbConditionFields.Build(withoutDi));
            });
        }

        [Test]
        public void TheModelChangeFilterSelectsTheChanges()
        {
            EventFilter filter = AmbEventFilters.ModelChanges();

            Assert.Multiple(() =>
            {
                Assert.That(filter.SelectClauses.Count, Is.EqualTo(2));
                Assert.That(
                    filter.SelectClauses[1].TypeDefinitionId,
                    Is.EqualTo(Ua.ObjectTypeIds.GeneralModelChangeEventType));
                Assert.That(filter.SelectClauses[1].BrowsePath[0], Is.EqualTo(new QualifiedName("Changes")));
                Assert.That(filter.WhereClause.Elements.Count, Is.EqualTo(1));
            });
        }

        [Test]
        public void EventFieldsBecomeAHealthAlarm()
        {
            ServiceMessageContext context = CreateContext();
            NamespaceTable namespaceUris = context.NamespaceUris;
            Variant[] values = Empty();
            var cable = new NodeId("Cable", 3);
            values[AmbConditionFields.ConditionId] = Variant.From(new NodeId(42u, 3));
            values[AmbConditionFields.EventId] = Variant.From(ByteString.From(1, 2, 3));
            values[AmbConditionFields.SourceName] = Variant.From(string.Empty);
            values[AmbConditionFields.ConditionName] = Variant.From("Fieldbus");
            values[AmbConditionFields.Message] = Variant.From(new LocalizedText("down"));
            values[AmbConditionFields.Severity] = Variant.From((ushort)700);
            values[AmbConditionFields.ConditionClassId] = Variant.From(
                AmbConditionClass.ConnectionFailure.GetTypeId(namespaceUris));
            values[AmbConditionFields.Active] = Variant.From(true);
            values[AmbConditionFields.Acked] = Variant.From(false);
            values[AmbConditionFields.Retain] = Variant.From(true);
            values[AmbConditionFields.PotentialRootCauses] = Variant.FromStructure(
                (ArrayOf<RootCauseDataType>)
                [
                    new RootCauseDataType { RootCauseId = cable, RootCause = new LocalizedText("cut") }
                ]);

            values[AmbConditionFields.Comment] = Variant.From(new LocalizedText("seen"));
            bool maintenance = AmbConditionFields.IsMaintenance(
                values,
                AmbConditionFields.KnownMaintenanceClasses(namespaceUris));
            AssetAlarmRecord alarm = AmbConditionFields.ToAlarm(values, context);

            Assert.Multiple(() =>
            {
                Assert.That(maintenance, Is.False);
                Assert.That(alarm.ConditionId, Is.EqualTo(new NodeId(42u, 3)));
                Assert.That(alarm.EventId.Length, Is.EqualTo(3));
                Assert.That(alarm.SourceName, Is.Null, "an empty value counts as not set");
                Assert.That(alarm.ConditionName, Is.EqualTo("Fieldbus"));
                Assert.That(alarm.Severity, Is.EqualTo((ushort)700));
                Assert.That(alarm.FaultCategory, Is.EqualTo(AssetFaultSeverity.MajorRecoverableFault));
                Assert.That(alarm.ConditionClass, Is.SameAs(AmbConditionClass.ConnectionFailure));
                Assert.That(alarm.IsActive, Is.True);
                Assert.That(alarm.IsAcknowledged, Is.False);
                Assert.That(alarm.Retain, Is.True);
                Assert.That(alarm.PotentialRootCauses.Count, Is.EqualTo(1));
                Assert.That(alarm.PotentialRootCauses[0].RootCauseId, Is.EqualTo(cable));
                Assert.That(alarm.Comment.Text, Is.EqualTo("seen"));
            });
        }

        [Test]
        public void EventFieldsBecomeAMaintenanceActivity()
        {
            ServiceMessageContext context = CreateContext();
            NamespaceTable namespaceUris = context.NamespaceUris;
            ushort amb = (ushort)namespaceUris.GetIndex(Namespaces.AMB);
            var planned = new DateTimeUtc(new DateTime(2027, 3, 1, 8, 0, 0, DateTimeKind.Utc));
            Variant[] values = Empty();
            values[AmbConditionFields.MaintenanceState] = Variant.From(
                new NodeId(MaintenanceEventStateMachineTypeIds.StateIds.Executing, amb));
            values[AmbConditionFields.ConditionClassId] = Variant.From(
                AmbConditionClass.Inspection.GetTypeId(namespaceUris));
            values[AmbConditionFields.PlannedDate] = Variant.From(planned);
            values[AmbConditionFields.EstimatedDowntime] = Variant.From(7_200_000d);
            values[AmbConditionFields.MaintenanceSupplier] = Variant.FromStructure(
                new NameNodeIdDataType { Name = new LocalizedText("ACME Service"), NodeId = NodeId.Null });
            values[AmbConditionFields.PartsOfAssetReplaced] = Variant.FromStructure(
                (ArrayOf<NameNodeIdDataType>)[new NameNodeIdDataType { Name = new LocalizedText("Bearing") }]);
            values[AmbConditionFields.MaintenanceMethod] = Variant.From((int)MaintenanceMethodEnum.Remote);
            values[AmbConditionFields.ConfigurationChanged] = Variant.From(true);

            MaintenanceActivityRecord activity = AmbConditionFields.ToMaintenance(values, context);

            Assert.Multiple(() =>
            {
                Assert.That(
                    AmbConditionFields.IsMaintenance(values, AmbConditionFields.KnownMaintenanceClasses(namespaceUris)),
                    Is.True,
                    "§12.1: told apart by its maintenance condition class");
                Assert.That(activity.State, Is.EqualTo(MaintenanceStateKind.Executing));
                Assert.That(activity.ConditionClass, Is.SameAs(AmbConditionClass.Inspection));
                Assert.That(activity.PlannedDate, Is.EqualTo(planned));
                Assert.That(activity.EstimatedDowntime, Is.EqualTo(TimeSpan.FromHours(2)));
                Assert.That(activity.MaintenanceSupplier?.Name.Text, Is.EqualTo("ACME Service"));
                Assert.That(activity.QualificationOfPersonnel, Is.Null);
                Assert.That(activity.PartsOfAssetReplaced.Count, Is.EqualTo(1));
                Assert.That(activity.PartsOfAssetServiced.IsNull, Is.True);
                Assert.That(activity.MaintenanceMethod, Is.EqualTo(MaintenanceMethodEnum.Remote));
                Assert.That(activity.ConfigurationChanged, Is.True);
                Assert.That(activity.ConditionId.IsNull, Is.True);
                Assert.That(activity.EventId.IsEmpty, Is.True);
            });
        }

        [Test]
        public void StateIdsMapToTheStatesOfTheModel()
        {
            NamespaceTable namespaceUris = CreateNamespaces();
            ushort amb = (ushort)namespaceUris.GetIndex(Namespaces.AMB);

            Assert.Multiple(() =>
            {
                Assert.That(
                    AmbConditionFields.StateOf(
                        new NodeId(MaintenanceEventStateMachineTypeIds.StateIds.Planned, amb),
                        namespaceUris),
                    Is.EqualTo(MaintenanceStateKind.Planned));
                Assert.That(
                    AmbConditionFields.StateOf(
                        new NodeId(MaintenanceEventStateMachineTypeIds.StateIds.Finished, amb),
                        namespaceUris),
                    Is.EqualTo(MaintenanceStateKind.Finished));
                Assert.That(AmbConditionFields.StateOf(new NodeId(4711u, amb), namespaceUris), Is.Null);
                Assert.That(
                    AmbConditionFields.StateOf(
                        new NodeId(MaintenanceEventStateMachineTypeIds.StateIds.Planned, 0),
                        namespaceUris),
                    Is.Null,
                    "a state of another namespace");
                Assert.That(AmbConditionFields.StateOf(NodeId.Null, namespaceUris), Is.Null);
                Assert.That(AmbConditionFields.StateOf(new NodeId("Planned", amb), namespaceUris), Is.Null);
            });
        }

        [Test]
        public void EmptyValuesCountAsNotSet()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AmbClient.StringOf(Variant.From(string.Empty)), Is.Null);
                Assert.That(AmbClient.StringOf(Variant.Null), Is.Null);
                Assert.That(AmbClient.StringOf(Variant.From("urn:x")), Is.EqualTo("urn:x"));
                Assert.That(AmbClient.TextOf(Variant.From(new LocalizedText(string.Empty))).IsNullOrEmpty, Is.True);
                Assert.That(AmbClient.TextOf(Variant.From(new LocalizedText("ACME"))).Text, Is.EqualTo("ACME"));
            });
        }

        [Test]
        public void TheClientNeedsASessionWithAMessageContext()
        {
            Mock<ISession> session = CreateSession();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var client = new AmbClient(session.Object, telemetry);
            AmbClient again = session.Object.AssetManagement(telemetry);

            Assert.Multiple(() =>
            {
                Assert.That(client.IsSupported, Is.True);
                Assert.That(client.Session, Is.SameAs(session.Object));
                Assert.That(client.Telemetry, Is.SameAs(telemetry));
                Assert.That(again.IsSupported, Is.True);
                Assert.That(
                    client.GetCategoryId(AssetAliasCategory.ByAssetId),
                    Is.EqualTo(ExpandedNodeId.ToNodeId(ObjectIds.AssetsByAssetId, session.Object.NamespaceUris)));
                Assert.That(
                    session.Object.MessageContext.Factory.ContainsEncodeableType(DataTypeIds.RootCauseDataType),
                    Is.True,
                    "the client registers the AMB data types");
                Assert.That(
                    Assert.Throws<ServiceResultException>(() => client.StreamingOf(null))!.StatusCode,
                    Is.EqualTo(StatusCodes.BadInvalidState),
                    "observing needs a ManagedSession or a streaming subscription");
                Assert.That(
                    Assert.Throws<ServiceResultException>(() => client.NamespaceIndexOf("urn:missing"))!.StatusCode,
                    Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.Throws<ArgumentNullException>(() => _ = new AmbClient(null!, telemetry));
                Assert.Throws<ArgumentNullException>(() => _ = new AmbClient(session.Object, null!));
                Assert.Throws<ArgumentNullException>(() => SessionAmbExtensions.AssetManagement(null!, telemetry));
                Assert.Throws<ArgumentNullException>(() => session.Object.AssetManagement(null!));
            });
        }

        [Test]
        public void AddAssetManagementClientRegistersAndResolvesTheFactory()
        {
            var services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(options =>
            {
                options.ApplicationName = "AmbClientHosting";
                options.ApplicationUri = "urn:localhost:OPCFoundation:AmbClientHosting";
            });

            builder.AddAssetManagementClient();

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(provider.GetRequiredService<AmbClientFactory>(), Is.Not.Null);
                Assert.That(provider.GetRequiredService<Func<CancellationToken, Task<AmbClient>>>(), Is.Not.Null);
                Assert.Throws<ArgumentNullException>(
                    () => OpcUaAmbClientBuilderExtensions.AddAssetManagementClient(null!));
                Assert.Throws<ArgumentNullException>(
                    () => _ = new AmbClientFactory(null!, NUnitTelemetryContext.Create()));
                Assert.Throws<ArgumentNullException>(
                    () => _ = new AmbClientFactory(_ => Task.FromResult<ManagedSession>(null!), null!));
            });
        }

        [Test]
        public void ARecordReportsTheFaultCategoryOfItsSeverity()
        {
            Assert.Multiple(() =>
            {
                Assert.That(new AssetAlarmRecord { Severity = 100 }.FaultCategory, Is.Null);
                Assert.That(
                    new AssetAlarmRecord { Severity = 350 }.FaultCategory,
                    Is.EqualTo(AssetFaultSeverity.MaintenanceNeeded));
                Assert.That(new AssetSnapshot().Identification, Is.Not.Null);
                Assert.That(new AssetSnapshot().Context, Is.Not.Null);
            });
        }

        private static Variant[] Empty()
        {
            var values = new Variant[AmbConditionFields.Count];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = Variant.Null;
            }
            return values;
        }

        private static NamespaceTable CreateNamespaces()
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.GetIndexOrAppend(Opc.Ua.Di.Namespaces.OpcUaDi);
            namespaceUris.GetIndexOrAppend(Namespaces.AMB);
            namespaceUris.GetIndexOrAppend("urn:test:instances");
            return namespaceUris;
        }

        private static ServiceMessageContext CreateContext()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.NamespaceUris = CreateNamespaces();
            context.Factory.Builder.AddOpcUaAMB().Commit();
            return context;
        }

        private static Mock<ISession> CreateSession()
        {
            var session = new Mock<ISession>();
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.NamespaceUris = CreateNamespaces();
            session.SetupGet(s => s.NamespaceUris).Returns(context.NamespaceUris);
            session.SetupGet(s => s.MessageContext).Returns(context);
            return session;
        }
    }
}
