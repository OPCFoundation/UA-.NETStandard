// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Tests the server/client-independent AMB facts the model package adds
    /// to the generated code.
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    public sealed class AmbContractsTests
    {
        [Test]
        public void ConditionClassTableListsEveryGeneratedConditionClassOnce()
        {
            var listed = new List<uint>();
            foreach (AmbConditionClass conditionClass in AmbConditionClass.All)
            {
                listed.Add(conditionClass.ObjectTypeId);
            }
            uint[] generated = [.. ObjectTypes.BrowseNames
                .Where(name => name.EndsWith("ConditionClassType", StringComparison.Ordinal))
                .Select(ObjectTypes.GetIdentifier)];

            Assert.Multiple(() =>
            {
                Assert.That(AmbConditionClass.All.Count, Is.EqualTo(14));
                Assert.That(listed, Is.EquivalentTo(generated));
                foreach (AmbConditionClass conditionClass in AmbConditionClass.All)
                {
                    Assert.That(
                        ObjectTypes.GetBrowseName(conditionClass.ObjectTypeId),
                        Is.EqualTo(conditionClass.Name));
                    Assert.That(conditionClass.ToString(), Is.EqualTo(conditionClass.Name));
                }
            });
        }

        [Test]
        public void ConditionClassBaseMatchesTheTypeHierarchyOfTheModel()
        {
            SystemContext context = AmbModelTests.CreateContext();

            Assert.Multiple(() =>
            {
                foreach (AmbConditionClass conditionClass in AmbConditionClass.All)
                {
                    BaseObjectTypeState type = AmbModelTests.Load<BaseObjectTypeState>(
                        context,
                        conditionClass.ObjectTypeId);
                    Assert.That(type.IsAbstract, Is.True, conditionClass.Name);
                    Assert.That(
                        StandardBaseOf(context, type),
                        Is.EqualTo(conditionClass.BaseClassTypeId),
                        conditionClass.Name);
                }

                Assert.That(
                    AmbConditionClass.OutOfMemory.BaseClass,
                    Is.EqualTo(AmbConditionClassBase.System));
                Assert.That(
                    AmbConditionClass.CalibrationDue.BaseClass,
                    Is.EqualTo(AmbConditionClassBase.Maintenance));
            });
        }

        [Test]
        public void ConditionClassResolvesAgainstANamespaceTable()
        {
            var namespaceUris = new NamespaceTable();
            Assert.That(AmbConditionClass.Repair.GetTypeId(namespaceUris), Is.EqualTo(NodeId.Null));

            ushort amb = namespaceUris.GetIndexOrAppend(Namespaces.AMB);
            NodeId repair = AmbConditionClass.Repair.GetTypeId(namespaceUris);

            Assert.Multiple(() =>
            {
                Assert.That(repair, Is.EqualTo(new NodeId(ObjectTypes.RepairConditionClassType, amb)));
                Assert.That(
                    AmbConditionClass.Repair.ExpandedTypeId,
                    Is.EqualTo(ObjectTypeIds.RepairConditionClassType));
                Assert.That(AmbConditionClass.Find(repair, namespaceUris), Is.SameAs(AmbConditionClass.Repair));
                Assert.That(
                    AmbConditionClass.Find(ObjectTypes.InspectionConditionClassType),
                    Is.SameAs(AmbConditionClass.Inspection));

                // Not an AMB condition class: an AMB type of another kind, a
                // standard class, a null id and an id of another namespace.
                Assert.That(AmbConditionClass.Find(ObjectTypes.DocumentationLinksType), Is.Null);
                Assert.That(
                    AmbConditionClass.Find(Ua.ObjectTypeIds.SystemConditionClassType, namespaceUris),
                    Is.Null);
                Assert.That(AmbConditionClass.Find(NodeId.Null, namespaceUris), Is.Null);
                Assert.That(
                    AmbConditionClass.Find(new NodeId("Repair", amb), namespaceUris),
                    Is.Null);
            });

            Assert.Throws<ArgumentNullException>(() => AmbConditionClass.Repair.GetTypeId(null!));
            Assert.Throws<ArgumentNullException>(() => AmbConditionClass.Find(repair, null!));
        }

        [Test]
        public void MaintenanceStateKindCarriesTheStateNumbersOfTheModel()
        {
            Assert.Multiple(() =>
            {
                Assert.That((uint)MaintenanceStateKind.Planned, Is.EqualTo(1u));
                Assert.That((uint)MaintenanceStateKind.Executing, Is.EqualTo(2u));
                Assert.That((uint)MaintenanceStateKind.Finished, Is.EqualTo(3u));
            });
        }

        [Test]
        public void SeverityBandsCoverTwoHundredOneToOneThousandWithoutGaps()
        {
            AssetFaultSeverity[] ordered =
            [
                AssetFaultSeverity.LimitedResourceCapacityNearLimit,
                AssetFaultSeverity.MaintenanceNeeded,
                AssetFaultSeverity.MinorRecoverableFault,
                AssetFaultSeverity.MajorRecoverableFault,
                AssetFaultSeverity.CriticalFault
            ];

            Assert.Multiple(() =>
            {
                Assert.That(AssetFaultSeverities.MinimumOf(ordered[0]), Is.EqualTo(201));
                Assert.That(AssetFaultSeverities.MaximumOf(ordered[^1]), Is.EqualTo(1000));
                for (int ii = 1; ii < ordered.Length; ii++)
                {
                    Assert.That(
                        AssetFaultSeverities.MinimumOf(ordered[ii]),
                        Is.EqualTo(AssetFaultSeverities.MaximumOf(ordered[ii - 1]) + 1),
                        ordered[ii].ToString());
                }
                foreach (AssetFaultSeverity category in ordered)
                {
                    ushort minimum = AssetFaultSeverities.MinimumOf(category);
                    ushort maximum = AssetFaultSeverities.MaximumOf(category);
                    Assert.That(AssetFaultSeverities.Classify(minimum), Is.EqualTo(category));
                    Assert.That(AssetFaultSeverities.Classify(maximum), Is.EqualTo(category));
                    Assert.That(AssetFaultSeverities.IsWithin(category, minimum), Is.True);
                    Assert.That(AssetFaultSeverities.IsWithin(category, maximum), Is.True);
                    Assert.That(AssetFaultSeverities.IsWithin(category, (ushort)(maximum + 1)), Is.False);
                    Assert.That(AssetFaultSeverities.IsInactive(minimum), Is.False);
                }
            });
        }

        [TestCase((ushort)0, false)]
        [TestCase((ushort)1, true)]
        [TestCase((ushort)200, true)]
        [TestCase((ushort)201, false)]
        [TestCase((ushort)1001, false)]
        public void InactiveSeveritiesAreOneToTwoHundred(ushort severity, bool inactive)
        {
            Assert.That(AssetFaultSeverities.IsInactive(severity), Is.EqualTo(inactive));
            if (severity is < 201 or > 1000)
            {
                Assert.That(AssetFaultSeverities.Classify(severity), Is.Null);
            }
        }

        [Test]
        public void UndefinedSeverityCategoriesAreRejected()
        {
            const AssetFaultSeverity undefined = (AssetFaultSeverity)42;

            Assert.Throws<ArgumentOutOfRangeException>(() => AssetFaultSeverities.MinimumOf(undefined));
            Assert.Throws<ArgumentOutOfRangeException>(() => AssetFaultSeverities.MaximumOf(undefined));
        }

        [Test]
        public void AmbBrowseNamesAreQualifiedWithTheAmbNamespace()
        {
            var namespaceUris = new NamespaceTable();
            Assert.That(
                AmbBrowseNames.Qualify(AmbBrowseNames.Requirements, namespaceUris),
                Is.EqualTo(QualifiedName.Null));

            ushort amb = namespaceUris.GetIndexOrAppend(Namespaces.AMB);
            Assert.That(
                AmbBrowseNames.Qualify(AmbBrowseNames.NoAssetIdAssigned, namespaceUris),
                Is.EqualTo(new QualifiedName("NoAssetIdAssigned", amb)));

            Assert.Throws<ArgumentNullException>(() => AmbBrowseNames.Qualify(null!, namespaceUris));
            Assert.Throws<ArgumentNullException>(
                () => AmbBrowseNames.Qualify(AmbBrowseNames.Capabilities, null!));
        }

        private static NodeId StandardBaseOf(ISystemContext context, BaseObjectTypeState type)
        {
            var visited = new HashSet<NodeId>();
            NodeId current = type.SuperTypeId;
            ushort amb = AmbModelTests.AmbIndex(context);
            while (current.NamespaceIndex == amb && visited.Add(current))
            {
                current.TryGetValue(out uint identifier);
                current = AmbModelTests.Load<BaseObjectTypeState>(context, identifier).SuperTypeId;
            }
            return current;
        }
    }
}
