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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Guards the generated OPC 10000-110 model against drift from the OPC
    /// Foundation publication it is built from, and pins the generator
    /// behaviors AMB is the first companion model to rely on.
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    public sealed class AmbModelTests
    {
        private const string NodeSetSha256 =
            "d2ea805b6f799589156c85ffc7799b5d296a68f4d7cace3a5ffb40a0fd1f065c";

        private const string NodeSetGitBlob = "21f9475b646e9c5508171277ffe5b74edbeb0a58";

        /// <summary>
        /// The published id of <c>AssetsByProductInstanceUri/NodeVersion</c>.
        /// </summary>
        private const uint AssetsByProductInstanceUriNodeVersion = 6005;

        /// <summary>
        /// The published id of <c>AssetsByAssetId/NodeVersion</c>.
        /// </summary>
        private const uint AssetsByAssetIdNodeVersion = 6008;

        private static readonly XNamespace s_ua = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";

        private static readonly string[] s_findAliasInputs = ["AliasNameSearchPattern", "ReferenceTypeFilter"];

        private static readonly string[] s_findAliasOutputs = ["AliasNodeList"];

        private static readonly string[] s_stateMachineTables =
            ["StateTable", "TransitionTable", "TransitionMappings"];

        /// <summary>
        /// The browse names OPC 10000-110 §8.2.3, §10.6, §10.7, §13.3.2,
        /// §13.4.2 and §13.5 define without nodes.
        /// </summary>
        private static readonly string[] s_specifiedBrowseNames =
        [
            "Requirements",
            "Capabilities",
            "HierarchicalLocation",
            "OperationalLocation",
            "DigitalLocation",
            "NoAssetIdAssigned"
        ];

        /// <summary>
        /// The rows of the published table whose nodes are generated without
        /// an identifier constant.
        /// </summary>
        private static readonly HashSet<string> s_rowsWithoutConstant =
        [
            // Children the NodeSet adds to an instance on its own.
            "AssetsByProductInstanceUri_NodeVersion",
            "AssetsByAssetId_NodeVersion",

            // The entries of the type dictionaries; no companion model in
            // the repository gets constants for them (IA and PackML alike).
            "TypeDictionary_BinarySchema_NamespaceUri",
            "TypeDictionary_XmlSchema_NamespaceUri",
            "TypeDictionary_BinarySchema_RootCauseDataType",
            "TypeDictionary_XmlSchema_RootCauseDataType",
            "TypeDictionary_BinarySchema_NameNodeIdDataType",
            "TypeDictionary_XmlSchema_NameNodeIdDataType"
        ];

        [Test]
        public void NamespaceVersionAndPublicationDateMatchTheSpecification()
        {
            ModelDependencyAttribute model = typeof(AmbBrowseNames).Assembly
                .GetCustomAttributes<ModelDependencyAttribute>()
                .Single(attribute => attribute.ModelUri == Namespaces.AMB);

            Assert.Multiple(() =>
            {
                Assert.That(Namespaces.AMB, Is.EqualTo("http://opcfoundation.org/UA/AMB/"));
                Assert.That(ModelVersions.Target, Is.EqualTo("1.01.1"));
                Assert.That(model.Version, Is.EqualTo("1.01.1"));
                Assert.That(model.PublicationDate, Is.EqualTo("2024-02-27T00:00:00Z"));
                Assert.That(model.Name, Is.EqualTo("AMB"));
                Assert.That(model.Prefix, Is.EqualTo("Opc.Ua.AMB"));
            });
        }

        [Test]
        public void VendoredNodeSetIsTheUnmodifiedPublication()
        {
            byte[] bytes = File.ReadAllBytes(ModelFile("Opc.Ua.AMB.NodeSet2.xml"));

            Assert.Multiple(() =>
            {
                Assert.That(Sha256Hex(bytes), Is.EqualTo(NodeSetSha256));
                Assert.That(GitBlobId(bytes), Is.EqualTo(NodeSetGitBlob));
            });
        }

        [Test]
        public void NodeSetRequiresTheBaseNamespaceOnly()
        {
            XDocument nodeSet = XDocument.Load(ModelFile("Opc.Ua.AMB.NodeSet2.xml"));
            XElement model = nodeSet.Descendants(s_ua + "Model").Single();
            XElement[] required = [.. model.Elements(s_ua + "RequiredModel")];

            Assert.Multiple(() =>
            {
                Assert.That(model.Attribute("ModelUri")?.Value, Is.EqualTo(Namespaces.AMB));
                Assert.That(model.Attribute("Version")?.Value, Is.EqualTo("1.01.1"));
                Assert.That(model.Attribute("ModelVersion")?.Value, Is.EqualTo("1.1.1"));
                Assert.That(required, Has.Length.EqualTo(1));
                Assert.That(required[0].Attribute("ModelUri")?.Value, Is.EqualTo(Ua.Namespaces.OpcUa));

                // A lower bound, not a pin: the core this repository ships is newer.
                Assert.That(required[0].Attribute("Version")?.Value, Is.EqualTo("1.05.02"));
            });
        }

        [Test]
        public void EveryTypeOfTheSpecificationIsGenerated()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    ObjectTypes.Identifiers,
                    Is.EquivalentTo(Enumerable.Range(1002, 18).Select(id => (uint)id)));
                Assert.That(DataTypes.Identifiers, Is.EquivalentTo(new uint[] { 3002, 3003, 3004 }));
                Assert.That(ReferenceTypes.Identifiers, Is.EquivalentTo(new uint[] { 4002, 4003, 4004 }));
                Assert.That(Objects.Identifiers.Count(), Is.EqualTo(17));
                // 46 variables, eight of which have no constant (see
                // s_rowsWithoutConstant).
                Assert.That(Variables.Identifiers.Count(), Is.EqualTo(46 - s_rowsWithoutConstant.Count));
                Assert.That(Methods.Identifiers.Count(), Is.EqualTo(5));

                Assert.That(ObjectTypes.IRootCauseIndicationType, Is.EqualTo(1002u));
                Assert.That(ObjectTypes.DocumentationLinksType, Is.EqualTo(1011u));
                Assert.That(ObjectTypes.IMaintenanceEventType, Is.EqualTo(1012u));
                Assert.That(ObjectTypes.MaintenanceEventStateMachineType, Is.EqualTo(1013u));
                Assert.That(ObjectTypes.FlashUpdateFailedConditionClassType, Is.EqualTo(1019u));
                Assert.That(DataTypes.RootCauseDataType, Is.EqualTo(3002u));
                Assert.That(DataTypes.NameNodeIdDataType, Is.EqualTo(3003u));
                Assert.That(DataTypes.MaintenanceMethodEnum, Is.EqualTo(3004u));
                Assert.That(ReferenceTypes.Contains, Is.EqualTo(4002u));
                Assert.That(ReferenceTypes.HierarchicalContains, Is.EqualTo(4003u));
                Assert.That(ReferenceTypes.OperationalContains, Is.EqualTo(4004u));
                Assert.That((int)MaintenanceMethodEnum.Local, Is.Zero);
                Assert.That((int)MaintenanceMethodEnum.Remote, Is.EqualTo(1));
            });
        }

        [Test]
        public void DerivedIdentifierTableIsTheDriftGuardOfTheGeneratedModel()
        {
            List<IdentifierRow> rows = ReadIdentifierTable("Opc.Ua.AMB.NodeIds.csv", hasHeader: true);

            Assert.That(rows, Has.Count.EqualTo(92));
            Assert.Multiple(() =>
            {
                foreach (IdentifierRow row in rows)
                {
                    if (s_rowsWithoutConstant.Contains(row.SymbolicName))
                    {
                        continue;
                    }
                    Assert.That(
                        TryGetGeneratedIdentifier(row.NodeClass, row.SymbolicName, out uint generated),
                        Is.True,
                        $"{row.NodeClass} {row.SymbolicName} is generated");
                    Assert.That(generated, Is.EqualTo(row.NodeId), row.SymbolicName);
                }
            });
        }

        [Test]
        public void DerivedIdentifierTableOnlyRenamesThePublishedTable()
        {
            List<IdentifierRow> upstream = ReadIdentifierTable(
                "Opc.Ua.AMB.Upstream.NodeIds.csv",
                hasHeader: false);
            List<IdentifierRow> derived = ReadIdentifierTable("Opc.Ua.AMB.NodeIds.csv", hasHeader: true);

            Assert.That(derived, Has.Count.EqualTo(upstream.Count));
            int renamed = 0;
            Assert.Multiple(() =>
            {
                for (int ii = 0; ii < upstream.Count; ii++)
                {
                    Assert.That(derived[ii].NodeId, Is.EqualTo(upstream[ii].NodeId));
                    Assert.That(derived[ii].NodeClass, Is.EqualTo(upstream[ii].NodeClass));
                    if (derived[ii].SymbolicName == upstream[ii].SymbolicName)
                    {
                        continue;
                    }
                    renamed++;

                    // The rename classes tools/nodesets/README.md documents.
                    Assert.That(
                        ExpectedRenames(upstream[ii].SymbolicName, derived[ii].SymbolicName),
                        Does.Contain(derived[ii].SymbolicName),
                        upstream[ii].SymbolicName);
                }
            });
            Assert.That(renamed, Is.EqualTo(25));
        }

        [TestCase(Objects.Assets, Methods.Assets_FindAlias, Variables.Assets_FindAlias_InputArguments)]
        [TestCase(
            Objects.AssetsByProductInstanceUri,
            Methods.AssetsByProductInstanceUri_FindAlias,
            Variables.AssetsByProductInstanceUri_FindAlias_InputArguments)]
        [TestCase(
            Objects.AssetsByAssetId,
            Methods.AssetsByAssetId_FindAlias,
            Variables.AssetsByAssetId_FindAlias_InputArguments)]
        public void FindAliasOfEachAliasCategoryIsTheTypedFindAliasMethod(
            uint categoryId,
            uint methodId,
            uint inputArgumentsId)
        {
            // AMB is the first companion model whose NodeSet instantiates a
            // base-namespace type (AliasNameCategoryType). The generator emits
            // the method as a plain MethodState; the category's typed
            // CreateOrReplaceFindAlias has to adopt it, or OnCallAsync with the
            // FindAlias signature is unreachable for the server.
            SystemContext context = CreateContext();
            AliasNameCategoryState category = Load<AliasNameCategoryState>(context, categoryId);
            FindAliasMethodState? findAlias = category.FindAlias;

            Assert.That(findAlias, Is.InstanceOf<FindAliasMethodState>());
            Assert.Multiple(() =>
            {
                Assert.That(findAlias!.NodeId, Is.EqualTo(AmbNodeId(context, methodId)));
                Assert.That(findAlias.BrowseName, Is.EqualTo(new QualifiedName("FindAlias")));
                Assert.That(
                    findAlias.MethodDeclarationId,
                    Is.EqualTo(Ua.MethodIds.AliasNameCategoryType_FindAlias));
                Assert.That(findAlias.Parent, Is.SameAs(category));
                Assert.That(findAlias.InputArguments, Is.Not.Null);
                Assert.That(findAlias.InputArguments!.NodeId, Is.EqualTo(AmbNodeId(context, inputArgumentsId)));
                Assert.That(
                    ArgumentNames(findAlias.InputArguments.Value),
                    Is.EqualTo(s_findAliasInputs));
                Assert.That(findAlias.OutputArguments, Is.Not.Null);
                Assert.That(
                    ArgumentNames(findAlias.OutputArguments!.Value),
                    Is.EqualTo(s_findAliasOutputs));
                Assert.That(
                    findAlias.OutputArguments.Value[0].DataType,
                    Is.EqualTo(Ua.DataTypeIds.AliasNameDataType));
            });
        }

        [TestCase(Objects.AssetsByProductInstanceUri, AssetsByProductInstanceUriNodeVersion)]
        [TestCase(Objects.AssetsByAssetId, AssetsByAssetIdNodeVersion)]
        public void EachSubcategoryCarriesANodeVersionProperty(uint categoryId, uint nodeVersionId)
        {
            // NodeVersion is not declared by AliasNameCategoryType and the
            // NodeSet gives it no ParentNodeId, so the generator has to infer
            // the parent from the inverse HasProperty reference.
            SystemContext context = CreateContext();
            AliasNameCategoryState category = Load<AliasNameCategoryState>(context, categoryId);
            BaseInstanceState? nodeVersion = ChildOf(context, category, new QualifiedName("NodeVersion"));

            Assert.That(nodeVersion, Is.InstanceOf<PropertyState>());
            var property = (PropertyState)nodeVersion!;
            Assert.Multiple(() =>
            {
                Assert.That(property.NodeId, Is.EqualTo(AmbNodeId(context, nodeVersionId)));
                Assert.That(property.DataType, Is.EqualTo(Ua.DataTypeIds.String));
                Assert.That(property.ValueRank, Is.EqualTo(ValueRanks.Scalar));
                Assert.That(property.ReferenceTypeId, Is.EqualTo(Ua.ReferenceTypeIds.HasProperty));
                Assert.That(property.TypeDefinitionId, Is.EqualTo(Ua.VariableTypeIds.PropertyType));
                Assert.That(property.Parent, Is.SameAs(category));
            });
        }

        [Test]
        public void TheAssetsRootCategoryHasNoNodeVersion()
        {
            SystemContext context = CreateContext();
            AliasNameCategoryState assets = Load<AliasNameCategoryState>(context, Objects.Assets);

            Assert.That(ChildOf(context, assets, new QualifiedName("NodeVersion")), Is.Null);
        }

        [Test]
        public void NodeVersionHasNoIdentifierConstant()
        {
            // The generator emits identifier constants for
            // the children of a top-level instance only where the instance's
            // type declares them mandatory. NodeVersion is a child the AMB
            // NodeSet adds to the alias categories on its own, so its rows of
            // the published table (6005, 6008) get no constant although the
            // nodes are generated with those NodeIds. If a generator change
            // emits them, this fails and the literals above can go.
            Assert.Multiple(() =>
            {
                foreach (string name in s_rowsWithoutConstant)
                {
                    Assert.That(Variables.TryGetValue(name, out _), Is.False, name);
                }
                Assert.That(
                    Variables.TryGetValue("AssetsByProductInstanceUri_FindAlias_InputArguments", out _),
                    Is.True,
                    "a child its type declares mandatory has its constant");
                Assert.That(
                    Variables.TryGetBrowseName(AssetsByProductInstanceUriNodeVersion, out _),
                    Is.False);
                Assert.That(Variables.TryGetBrowseName(AssetsByAssetIdNodeVersion, out _), Is.False);
            });
        }

        [Test]
        public void MaintenanceStateMachineIdsAreGeneratedButNotItsTables()
        {
            // The generated state machine does not override the tables
            // FiniteStateMachineState drives SetState with, so the server
            // builds them from these ids. When a generator fix emits them,
            // this fails and the hand-built tables can go.
            SystemContext context = CreateContext();
            NodeStateCollection nodes = new NodeStateCollection().AddOpcUaAMB(context);

            Assert.Multiple(() =>
            {
                Assert.That(MaintenanceEventStateMachineTypeIds.StateNumbers.Planned, Is.EqualTo(1u));
                Assert.That(MaintenanceEventStateMachineTypeIds.StateNumbers.Executing, Is.EqualTo(2u));
                Assert.That(MaintenanceEventStateMachineTypeIds.StateNumbers.Finished, Is.EqualTo(3u));
                Assert.That(
                    MaintenanceEventStateMachineTypeIds.TransitionNumbers.FromPlannedToExecuting,
                    Is.EqualTo(1u));
                Assert.That(
                    MaintenanceEventStateMachineTypeIds.TransitionNumbers.FromExecutingToFinished,
                    Is.EqualTo(2u));
                Assert.That(
                    MaintenanceEventStateMachineTypeIds.TransitionNumbers.FromFinishedToPlanned,
                    Is.EqualTo(3u));
                Assert.That(
                    MaintenanceEventStateMachineTypeIds.StateIds.Planned,
                    Is.EqualTo(Objects.MaintenanceEventStateMachineType_Planned));
                Assert.That(
                    MaintenanceEventStateMachineTypeIds.TransitionIds.FromFinishedToPlanned,
                    Is.EqualTo(Objects.MaintenanceEventStateMachineType_FromFinishedToPlanned));

                Assert.That(
                    StateNumberOf(context, nodes, Variables.MaintenanceEventStateMachineType_Planned_StateNumber),
                    Is.EqualTo(MaintenanceEventStateMachineTypeIds.StateNumbers.Planned));
                Assert.That(
                    StateNumberOf(context, nodes, Variables.MaintenanceEventStateMachineType_Executing_StateNumber),
                    Is.EqualTo(MaintenanceEventStateMachineTypeIds.StateNumbers.Executing));
                Assert.That(
                    StateNumberOf(context, nodes, Variables.MaintenanceEventStateMachineType_Finished_StateNumber),
                    Is.EqualTo(MaintenanceEventStateMachineTypeIds.StateNumbers.Finished));

                foreach (string table in s_stateMachineTables)
                {
                    PropertyInfo? property = typeof(MaintenanceEventStateMachineState).GetProperty(
                        table,
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.That(property, Is.Not.Null, table);
                    Assert.That(property!.DeclaringType, Is.EqualTo(typeof(FiniteStateMachineState)), table);
                }
            });
        }

        [Test]
        public void MaintenanceStateOfTheMaintenanceInterfaceIsTheCompanionStateMachine()
        {
            // An interface whose mandatory component is typed by a
            // companion state machine.
            SystemContext context = CreateContext();
            IMaintenanceEventState activity = context.CreateInstanceOfIMaintenanceEventType(
                null!,
                new QualifiedName("Activity", AmbIndex(context)));
            MaintenanceEventStateMachineState? state = activity.MaintenanceState;

            Assert.That(state, Is.InstanceOf<MaintenanceEventStateMachineState>());
            Assert.Multiple(() =>
            {
                Assert.That(
                    state!.BrowseName,
                    Is.EqualTo(new QualifiedName(BrowseNames.MaintenanceState, AmbIndex(context))));
                Assert.That(
                    state.TypeDefinitionId,
                    Is.EqualTo(AmbNodeId(context, ObjectTypes.MaintenanceEventStateMachineType)));
                Assert.That(state.CurrentState, Is.Not.Null);
                Assert.That(state.CurrentState!.Id, Is.Not.Null);
                Assert.That(activity.PlannedDate, Is.Null, "optional, created on demand");
            });

            BaseObjectTypeState type = Load<BaseObjectTypeState>(context, ObjectTypes.IMaintenanceEventType);
            Assert.Multiple(() =>
            {
                Assert.That(type.IsAbstract, Is.True);
                Assert.That(type.SuperTypeId, Is.EqualTo(Ua.ObjectTypeIds.BaseInterfaceType));
            });
        }

        [Test]
        public void RootCauseInterfaceDeclaresItsPotentialRootCauses()
        {
            SystemContext context = CreateContext();
            IRootCauseIndicationState indication = context.CreateInstanceOfIRootCauseIndicationType(
                null!,
                new QualifiedName("Indication", AmbIndex(context)));

            Assert.That(indication.PotentialRootCauses, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(
                    indication.PotentialRootCauses!.DataType,
                    Is.EqualTo(AmbNodeId(context, DataTypes.RootCauseDataType)));
                Assert.That(indication.PotentialRootCauses.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
            });

            BaseObjectTypeState type = Load<BaseObjectTypeState>(context, ObjectTypes.IRootCauseIndicationType);
            Assert.Multiple(() =>
            {
                Assert.That(type.IsAbstract, Is.True);
                Assert.That(type.SuperTypeId, Is.EqualTo(Ua.ObjectTypeIds.BaseInterfaceType));
            });
        }

        [Test]
        public void DocumentationLinksPlaceholderIsNoSingletonAndHoldsSeveralLinks()
        {
            // <Link> is an OptionalPlaceholder with SymbolicName "Link".
            // A singleton property would collide with the links AddLink
            // creates at runtime.
            Assert.That(typeof(DocumentationLinksState).GetProperty("Link"), Is.Null);

            SystemContext context = CreateContext();
            ushort amb = AmbIndex(context);
            DocumentationLinksState links = context.CreateInstanceOfDocumentationLinksType(
                null!,
                new QualifiedName(BrowseNames.DocumentationLinks, amb));
            BaseDataVariableState<string> manual =
                links.AddLink_Placeholder(context, new QualifiedName("Manual", amb));
            BaseDataVariableState<string> datasheet =
                links.AddLink_Placeholder(context, new QualifiedName("Datasheet", amb));

            var children = new List<BaseInstanceState>();
            links.GetChildren(context, children);
            Assert.Multiple(() =>
            {
                Assert.That(children, Does.Contain(manual));
                Assert.That(children, Does.Contain(datasheet));
                Assert.That(manual, Is.Not.SameAs(datasheet));
                Assert.That(manual.DataType, Is.EqualTo(Ua.DataTypeIds.UriString));
                Assert.That(manual.ReferenceTypeId, Is.EqualTo(Ua.ReferenceTypeIds.HasComponent));
                Assert.That(links.AddLink, Is.Null, "AddLink is optional");
                Assert.That(links.RemoveLink, Is.Null, "RemoveLink is optional");
            });

            links.AddAddLink(context).AddRemoveLink(context);
            Assert.Multiple(() =>
            {
                Assert.That(links.AddLink, Is.InstanceOf<AddLinkMethodState>());
                Assert.That(links.RemoveLink, Is.InstanceOf<RemoveLinkMethodState>());
            });

            BaseObjectTypeState type = Load<BaseObjectTypeState>(context, ObjectTypes.DocumentationLinksType);
            BaseInstanceState? defaultName = ChildOf(context, type, new QualifiedName("DefaultInstanceBrowseName"));
            Assert.That(defaultName, Is.InstanceOf<BaseVariableState>());
            Assert.That(
                ((BaseVariableState)defaultName!).WrappedValue.TryGetValue(out QualifiedName name),
                Is.True);
            Assert.That(name, Is.EqualTo(new QualifiedName(BrowseNames.DocumentationLinks, amb)));
        }

        [Test]
        public async Task GeneratedEntryPointsAreOrganizedByTheBaseObjectsAsync()
        {
            // The AMB instances hang below the base
            // namespace's Aliases (i=23470) and Locations (i=31915) objects
            // through inverse Organizes references, which only a hosted
            // server resolves into forward references.
            await using var fixture = new AmbModelServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);

            NodeId assets = fixture.ToNodeId(ObjectIds.Assets);
            NodeId byUri = fixture.ToNodeId(ObjectIds.AssetsByProductInstanceUri);
            NodeId byId = fixture.ToNodeId(ObjectIds.AssetsByAssetId);

            IReadOnlyList<ReferenceDescription> objects = await fixture.BrowseAsync(
                Ua.ObjectIds.ObjectsFolder,
                Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> aliases = await fixture.BrowseAsync(
                Ua.ObjectIds.Aliases,
                Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> categories = await fixture.BrowseAsync(
                assets,
                Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> parentOfAssets = await fixture.BrowseAsync(
                assets,
                Ua.ReferenceTypeIds.Organizes,
                BrowseDirection.Inverse).ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> nodeVersion = await fixture.BrowseAsync(
                byUri,
                Ua.ReferenceTypeIds.HasProperty).ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> locations = await fixture.BrowseAsync(
                Ua.ObjectIds.Locations,
                Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(Targets(fixture, objects), Does.Contain(Ua.ObjectIds.Locations), "0:Locations");
                Assert.That(Targets(fixture, aliases), Does.Contain(assets));
                Assert.That(Targets(fixture, categories), Is.SupersetOf(new[] { byUri, byId }));
                Assert.That(Targets(fixture, parentOfAssets), Does.Contain(Ua.ObjectIds.Aliases));
                Assert.That(
                    nodeVersion.Select(reference => reference.BrowseName),
                    Does.Contain(new QualifiedName("NodeVersion")));
                Assert.That(
                    Targets(fixture, locations),
                    Is.SupersetOf(new[]
                    {
                        fixture.ToNodeId(ObjectIds.HierarchicalLocations),
                        fixture.ToNodeId(ObjectIds.OperationalLocations)
                    }));

                // Once registered, the address space indexes the typed method.
                Assert.That(
                    fixture.Manager.Find(MethodIds.AssetsByAssetId_FindAlias),
                    Is.InstanceOf<FindAliasMethodState>());

                // Once registered, the location references are hierarchical.
                Assert.That(
                    fixture.Server.TypeTree.IsTypeOf(
                        fixture.ToNodeId(ReferenceTypeIds.OperationalContains),
                        Ua.ReferenceTypeIds.HierarchicalReferences),
                    Is.True);
            });
        }

        [Test]
        public void LocationReferenceTypesFormTheContainsHierarchy()
        {
            // An abstract reference type with subtypes, all with inverse names.
            SystemContext context = CreateContext();
            ReferenceTypeState contains = Load<ReferenceTypeState>(context, ReferenceTypes.Contains);
            ReferenceTypeState hierarchical = Load<ReferenceTypeState>(context, ReferenceTypes.HierarchicalContains);
            ReferenceTypeState operational = Load<ReferenceTypeState>(context, ReferenceTypes.OperationalContains);

            Assert.Multiple(() =>
            {
                Assert.That(contains.IsAbstract, Is.True);
                Assert.That(contains.Symmetric, Is.False);
                Assert.That(contains.InverseName.Text, Is.EqualTo("LocatedIn"));
                Assert.That(contains.SuperTypeId, Is.EqualTo(Ua.ReferenceTypeIds.HierarchicalReferences));
                Assert.That(hierarchical.IsAbstract, Is.False);
                Assert.That(hierarchical.InverseName.Text, Is.EqualTo("HierarchicalLocatedIn"));
                Assert.That(hierarchical.SuperTypeId, Is.EqualTo(contains.NodeId));
                Assert.That(operational.IsAbstract, Is.False);
                Assert.That(operational.InverseName.Text, Is.EqualTo("OperationalLocatedIn"));
                Assert.That(operational.SuperTypeId, Is.EqualTo(contains.NodeId));
            });
        }

        [Test]
        public void StructuresRoundTripThroughTheirBinaryEncodings()
        {
            // Two structures whose encoding nodes share the SymbolicName
            // DefaultBinary, distinguished by the data type they encode.
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.Create(telemetry);
            ushort amb = messageContext.NamespaceUris.GetIndexOrAppend(Namespaces.AMB);
            messageContext.Factory.Builder.AddOpcUaAMB().Commit();

            var cause = new RootCauseDataType
            {
                RootCauseId = new NodeId(42u, amb),
                RootCause = new LocalizedText("en", "Fieldbus cable unplugged")
            };
            var part = new NameNodeIdDataType
            {
                Name = new LocalizedText("en", "Bearing"),
                NodeId = new NodeId("Bearing", amb)
            };

            RootCauseDataType decodedCause = RoundTrip<RootCauseDataType>(messageContext, cause);
            NameNodeIdDataType decodedPart = RoundTrip<NameNodeIdDataType>(messageContext, part);

            Assert.Multiple(() =>
            {
                Assert.That(decodedCause.IsEqual(cause), Is.True);
                Assert.That(decodedPart.IsEqual(part), Is.True);
                Assert.That(
                    cause.BinaryEncodingId,
                    Is.EqualTo(ObjectIds.RootCauseDataType_Encoding_DefaultBinary));
                Assert.That(
                    part.BinaryEncodingId,
                    Is.EqualTo(ObjectIds.NameNodeIdDataType_Encoding_DefaultBinary));
                Assert.That(
                    ObjectIds.RootCauseDataType_Encoding_DefaultBinary,
                    Is.Not.EqualTo(ObjectIds.NameNodeIdDataType_Encoding_DefaultBinary));
            });
        }

        [TestCase(Variables.TypeDictionary_BinarySchema)]
        [TestCase(Variables.TypeDictionary_XmlSchema)]
        public void TypeDictionariesCarryTheirSchemas(uint dictionaryId)
        {
            SystemContext context = CreateContext();
            BaseVariableState dictionary = Load<BaseVariableState>(context, dictionaryId);

            Assert.That(dictionary.WrappedValue.TryGetValue(out ByteString schema), Is.True);
            Assert.That(schema.IsEmpty, Is.False);
            Assert.That(
                Encoding.UTF8.GetString(schema.ToArray()),
                Does.Contain("RootCauseDataType").And.Contain("NameNodeIdDataType"));
        }

        [Test]
        public void BrowseNamesWithoutNodesAreNotGenerated()
        {
            // AMB standardizes these names without publishing nodes for
            // them, so the generated BrowseNames cannot carry them. If a
            // future model revision adds nodes, this fails and AmbBrowseNames
            // can delegate to the generated constants.
            string[] generated = [.. typeof(BrowseNames)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral)
                .Select(field => (string)field.GetRawConstantValue()!)];

            string[] handWritten =
            [
                AmbBrowseNames.Requirements,
                AmbBrowseNames.Capabilities,
                AmbBrowseNames.HierarchicalLocation,
                AmbBrowseNames.OperationalLocation,
                AmbBrowseNames.DigitalLocation,
                AmbBrowseNames.NoAssetIdAssigned
            ];

            Assert.Multiple(() =>
            {
                Assert.That(handWritten, Is.EqualTo(s_specifiedBrowseNames));
                Assert.That(generated, Is.Not.Empty);
                foreach (string name in handWritten)
                {
                    Assert.That(generated, Does.Not.Contain(name), name);
                }
            });
        }

        internal static SystemContext CreateContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend(Namespaces.AMB);
            messageContext.Factory.Builder.AddOpcUaAMB().Commit();
            return new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        internal static ushort AmbIndex(ISystemContext context)
        {
            return (ushort)context.NamespaceUris.GetIndex(Namespaces.AMB);
        }

        internal static NodeId AmbNodeId(ISystemContext context, uint identifier)
        {
            return new NodeId(identifier, AmbIndex(context));
        }

        internal static T Load<T>(ISystemContext context, uint identifier) where T : NodeState
        {
            NodeId nodeId = AmbNodeId(context, identifier);
            foreach (NodeState node in new NodeStateCollection().AddOpcUaAMB(context))
            {
                NodeState? found = Find(context, node, nodeId);
                if (found is T typed)
                {
                    return typed;
                }
            }
            Assert.Fail($"AddOpcUaAMB does not emit {nodeId} as {typeof(T).Name}.");
            return null!;
        }

        private static NodeState? Find(ISystemContext context, NodeState node, NodeId nodeId)
        {
            if (node.NodeId == nodeId)
            {
                return node;
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                NodeState? found = Find(context, child, nodeId);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static BaseInstanceState? ChildOf(ISystemContext context, NodeState parent, QualifiedName browseName)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(context, children);
            return children.FirstOrDefault(child => child.BrowseName == browseName);
        }

        private static uint StateNumberOf(ISystemContext context, NodeStateCollection nodes, uint variableId)
        {
            NodeId nodeId = AmbNodeId(context, variableId);
            foreach (NodeState node in nodes)
            {
                if (Find(context, node, nodeId) is BaseVariableState variable &&
                    variable.WrappedValue.TryGetValue(out uint number))
                {
                    return number;
                }
            }
            Assert.Fail($"State number {nodeId} is not emitted.");
            return 0;
        }

        private static string[] ArgumentNames(ArrayOf<Argument> arguments)
        {
            var names = new List<string>();
            foreach (Argument argument in arguments)
            {
                names.Add(argument.Name ?? string.Empty);
            }
            return [.. names];
        }

        private static HashSet<NodeId> Targets(
            AmbModelServerFixture fixture,
            IReadOnlyList<ReferenceDescription> references)
        {
            return [.. references.Select(reference => fixture.ToNodeId(reference.NodeId))];
        }

        private static T RoundTrip<T>(IServiceMessageContext context, T value) where T : IEncodeable
        {
            byte[] encoded;
            using (var stream = new MemoryStream())
            {
                using (var encoder = new BinaryEncoder(stream, context, leaveOpen: true))
                {
                    encoder.WriteExtensionObject("Value", new ExtensionObject(value));
                }
                encoded = stream.ToArray();
            }

            using var input = new MemoryStream(encoded);
            using var decoder = new BinaryDecoder(input, context);
            ExtensionObject decoded = decoder.ReadExtensionObject("Value");
            Assert.That(decoded.TryGetValue(out T? result, context), Is.True, typeof(T).Name);
            return result!;
        }

        private static bool TryGetGeneratedIdentifier(string nodeClass, string symbolicName, out uint identifier)
        {
            switch (nodeClass)
            {
                case "ObjectType":
                    return ObjectTypes.TryGetValue(symbolicName, out identifier);
                case "DataType":
                    return DataTypes.TryGetValue(symbolicName, out identifier);
                case "ReferenceType":
                    return ReferenceTypes.TryGetValue(symbolicName, out identifier);
                case "Object":
                    return Objects.TryGetValue(symbolicName, out identifier);
                case "Variable":
                    return Variables.TryGetValue(symbolicName, out identifier);
                case "Method":
                    return Methods.TryGetValue(symbolicName, out identifier);
                default:
                    identifier = 0;
                    return false;
            }
        }

        private static string[] ExpectedRenames(string published, string derived)
        {
            // The publication qualifies a node with the path of the objects
            // organizing it (Server_Namespaces_, Aliases_Assets_, Locations_),
            // where the generator starts at the node; and it names a
            // placeholder without the _Placeholder suffix.
            var candidates = new List<string> { published + "_Placeholder" };
            if (published.EndsWith("_" + derived, StringComparison.Ordinal))
            {
                candidates.Add(derived);
            }
            return [.. candidates];
        }

        private static List<IdentifierRow> ReadIdentifierTable(string fileName, bool hasHeader)
        {
            var rows = new List<IdentifierRow>();
            string[] lines = File.ReadAllLines(ModelFile(fileName));
            Assert.That(lines[0] == "SymbolicName,NodeId,NodeClass", Is.EqualTo(hasHeader), fileName);
            foreach (string line in lines.Skip(hasHeader ? 1 : 0))
            {
                if (line.Length == 0)
                {
                    continue;
                }
                string[] fields = line.Split(',');
                Assert.That(fields, Has.Length.EqualTo(3), line);
                rows.Add(new IdentifierRow(
                    fields[0],
                    uint.Parse(fields[1], CultureInfo.InvariantCulture),
                    fields[2]));
            }
            return rows;
        }

        internal static string ModelFile(string fileName)
        {
            DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "src", "Opc.Ua.AMB", "Model", fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
                directory = directory.Parent;
            }
            Assert.Fail($"{fileName} was not found above {TestContext.CurrentContext.TestDirectory}.");
            return string.Empty;
        }

        private static string Sha256Hex(byte[] bytes)
        {
#if NET5_0_OR_GREATER
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
#else
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
#endif
        }

        /// <summary>
        /// Computes the git object id of a blob, which the GitHub contents API
        /// reports as the file's <c>sha</c>.
        /// </summary>
        private static string GitBlobId(byte[] bytes)
        {
            byte[] header = Encoding.ASCII.GetBytes(
                "blob " + bytes.Length.ToString(CultureInfo.InvariantCulture) + "\0");
            byte[] blob = new byte[header.Length + bytes.Length];
            Buffer.BlockCopy(header, 0, blob, 0, header.Length);
            Buffer.BlockCopy(bytes, 0, blob, header.Length, bytes.Length);
#pragma warning disable CA5350 // git names its objects by SHA-1; this is an identity check, not security
#if NET5_0_OR_GREATER
            return Convert.ToHexString(SHA1.HashData(blob)).ToLowerInvariant();
#else
            using var sha = SHA1.Create();
            return BitConverter.ToString(sha.ComputeHash(blob)).Replace("-", string.Empty).ToLowerInvariant();
#endif
#pragma warning restore CA5350
        }

        private sealed record IdentifierRow(string SymbolicName, uint NodeId, string NodeClass);
    }
}
