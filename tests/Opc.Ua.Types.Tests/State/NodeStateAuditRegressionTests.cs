/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Regression tests for the NodeState / Nodes defects reported by the
    /// read-only bug audit of Opc.Ua.Types.
    /// </summary>
    [TestFixture]
    [Category("NodeState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NodeStateAuditRegressionTests
    {
        private static readonly bool[] s_booleans = [true, false];
        private static readonly uint[] s_masks = [1u, 2u];

        private static SystemContext CreateContext()
        {
            var namespaceUris = new NamespaceTable();
            return new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = namespaceUris,
                ServerUris = new StringTable(),
                TypeTable = new TypeTable(namespaceUris)
            };
        }

        [Test]
        public void VariableTypeCreatedFromSourceKeepsItsValue()
        {
            // Initialize copied its own (still empty) value instead of the
            // source's, so the value was lost.
            SystemContext context = CreateContext();
            var source = new BaseDataVariableTypeState
            {
                Value = new Variant(42),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };
            var target = new BaseDataVariableTypeState();

            target.Create(context, source);

            Assert.That(target.Value.GetInt32(), Is.EqualTo(42));
        }

        [Test]
        public void VariableCreatedFromSourceKeepsItsStatusCode()
        {
            // m_valueTouched was copied but m_statusCode was not, so the copy
            // stayed BadWaitingForInitialData forever.
            SystemContext context = CreateContext();
            var source = new BaseDataVariableState(null)
            {
                Value = new Variant(1),
                StatusCode = StatusCodes.Good
            };
            var target = new BaseDataVariableState(null);

            target.Create(context, source);

            Assert.That(target.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
        }

        [Test]
        public void BinaryRoundTripDoesNotRenameTheParentAfterItsLastChild()
        {
            // UpdateChild assigned the property instead of the local, so the
            // parent ended up named after its last child.
            SystemContext context = CreateContext();
            IServiceMessageContext messageContext = context.AsMessageContext();

            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId(1000u),
                BrowseName = QualifiedName.From("Parent"),
                SymbolicName = "Parent"
            };
            var child = new PropertyState(parent)
            {
                NodeId = new NodeId(1001u),
                BrowseName = QualifiedName.From("Child"),
                SymbolicName = "Child",
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                TypeDefinitionId = VariableTypeIds.PropertyType,
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };
            parent.AddChild(child);

            using var stream = new MemoryStream();
            parent.SaveAsBinary(context, stream);
            stream.Position = 0;

            var reloaded = new BaseObjectState(null);
            reloaded.LoadAsBinary(context, stream);

            Assert.That(reloaded.SymbolicName, Is.EqualTo("Parent"));
        }

        [Test]
        public void MethodCloneDoesNotShareItsArgumentChildren()
        {
            // CopyTo assigned the same child instance to the clone.
            SystemContext context = CreateContext();
            var method = new MethodState(null)
            {
                NodeId = new NodeId(2000u),
                BrowseName = QualifiedName.From("DoIt")
            };
            method.CreateOrReplaceInputArguments(context, null);

            var clone = (MethodState)method.Clone();

            Assert.Multiple(() =>
            {
                Assert.That(clone.InputArguments, Is.Not.Null);
                Assert.That(clone.InputArguments, Is.Not.SameAs(method.InputArguments));
            });
        }

        [Test]
        public void CloneCopiesUserWriteMask()
        {
            var source = new BaseObjectState(null)
            {
                WriteMask = AttributeWriteMask.DisplayName,
                UserWriteMask = AttributeWriteMask.Description
            };

            var copy = (BaseObjectState)source.Clone();

            Assert.That(copy.UserWriteMask, Is.EqualTo(AttributeWriteMask.Description));
        }

        [Test]
        public void MethodStateXmlSaveWritesUserExecutable()
        {
            // The UserExecutable element was written from m_executable. The
            // element is only written when UserExecutable is true, so the two
            // fields have to disagree for the defect to be observable: with
            // Executable false the old code wrote "false" into an element it
            // had just decided to write because the value is true.
            SystemContext context = CreateContext();

            string userExecutableOnly = SaveMethodStateToXml(
                context,
                executable: false,
                userExecutable: true);
            string executableOnly = SaveMethodStateToXml(
                context,
                executable: true,
                userExecutable: false);

            Assert.Multiple(() =>
            {
                Assert.That(userExecutableOnly, Does.Contain("UserExecutable"));
                Assert.That(userExecutableOnly, Does.Contain(">true<"));
                Assert.That(userExecutableOnly, Does.Not.Contain(">false<"));

                // UserExecutable is false here, so nothing is written for it.
                Assert.That(executableOnly, Does.Not.Contain("UserExecutable"));
            });
        }

        private static string SaveMethodStateToXml(
            SystemContext context,
            bool executable,
            bool userExecutable)
        {
            var method = new MethodState(null)
            {
                NodeId = new NodeId(2100u),
                BrowseName = QualifiedName.From("DoIt"),
                Executable = executable,
                UserExecutable = userExecutable
            };

            using var encoder = new XmlEncoder(context.AsMessageContext());
            encoder.PushNamespace(Namespaces.OpcUaXsd);
            method.Save(context, encoder);
            encoder.PopNamespace();
            return encoder.CloseAndReturnText();
        }

        [Test]
        public void WriteToBrowseNameRaisesTheChangeMask()
        {
            // The write handlers assigned the backing fields directly, so
            // monitored items on those attributes were never notified.
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId(2200u),
                BrowseName = QualifiedName.From("Name"),
                WriteMask = AttributeWriteMask.BrowseName |
                    AttributeWriteMask.DisplayName |
                    AttributeWriteMask.Description |
                    AttributeWriteMask.UserWriteMask
            };
            node.ClearChangeMasks(context, false);

            ServiceResult result = node.WriteAttribute(
                context,
                Attributes.BrowseName,
                default,
                new DataValue(new Variant(QualifiedName.From("Renamed"))));

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(node.BrowseName.Name, Is.EqualTo("Renamed"));
                Assert.That(
                    node.ChangeMasks & NodeStateChangeMasks.NonValue,
                    Is.EqualTo(NodeStateChangeMasks.NonValue));
            });
        }

        [Test]
        public void WriteToDisplayNameRaisesTheChangeMask()
        {
            SystemContext context = CreateContext();
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId(2300u),
                WriteMask = AttributeWriteMask.DisplayName
            };
            node.ClearChangeMasks(context, false);

            ServiceResult result = node.WriteAttribute(
                context,
                Attributes.DisplayName,
                default,
                new DataValue(new Variant(LocalizedText.From("Shown"))));

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(
                    node.ChangeMasks & NodeStateChangeMasks.NonValue,
                    Is.EqualTo(NodeStateChangeMasks.NonValue));
            });
        }

        [Test]
        public void WriteChildAttributeDescendsIntoChildren()
        {
            // The path check was inverted, so the write always landed on the
            // node itself and never followed the component path.
            SystemContext context = CreateContext();
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId(2400u),
                BrowseName = QualifiedName.From("Parent")
            };
            var child = new BaseDataVariableState(parent)
            {
                NodeId = new NodeId(2401u),
                BrowseName = QualifiedName.From("Child"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite
            };
            parent.AddChild(child);

            ServiceResult result = parent.WriteChildAttribute(
                context,
                new[] { QualifiedName.From("Child") }.ToArrayOf(),
                0,
                Attributes.Value,
                new DataValue(new Variant(11)));

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(child.Value.GetInt32(), Is.EqualTo(11));
            });
        }

        [Test]
        public void ReplacedChildIsAdopted()
        {
            // The in-place replacement left Parent pointing at the previous
            // owner and did not raise the Children change mask.
            SystemContext context = CreateContext();
            var firstParent = new BaseObjectState(null)
            {
                NodeId = new NodeId(2500u),
                BrowseName = QualifiedName.From("First")
            };
            var secondParent = new BaseObjectState(null)
            {
                NodeId = new NodeId(2501u),
                BrowseName = QualifiedName.From("Second")
            };

            var existing = new BaseObjectState(secondParent)
            {
                NodeId = new NodeId(2502u),
                BrowseName = QualifiedName.From("Child")
            };
            secondParent.AddChild(existing);

            var replacement = new BaseObjectState(firstParent)
            {
                NodeId = new NodeId(2503u),
                BrowseName = QualifiedName.From("Child")
            };

            secondParent.ClearChangeMasks(context, false);
            secondParent.ReplaceChild(context, replacement);

            Assert.Multiple(() =>
            {
                Assert.That(replacement.Parent, Is.SameAs(secondParent));
                Assert.That(
                    secondParent.ChangeMasks & NodeStateChangeMasks.Children,
                    Is.EqualTo(NodeStateChangeMasks.Children));
            });
        }

        [Test]
        public void EqualReferenceNodesHashAlike()
        {
            // The hash mixed in object identity, so two value-equal references
            // hashed differently.
            var first = new ReferenceNode
            {
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IsInverse = false,
                TargetId = new ExpandedNodeId(5u)
            };
            var second = new ReferenceNode
            {
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IsInverse = false,
                TargetId = new ExpandedNodeId(5u)
            };

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.EqualTo(second));
                Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
            });
        }

        [Test]
        public void MinimumSamplingIntervalIsWrittenAsADuration()
        {
            // The gate rejected the Double value as BadTypeMismatch, and the
            // handler behind it cast the Double variant to int.
            var node = new VariableNode
            {
                NodeId = new NodeId(2600u),
                BrowseName = QualifiedName.From("Value"),
                WriteMask = (uint)AttributeWriteMask.MinimumSamplingInterval
            };

            ServiceResult result = node.Write(
                Attributes.MinimumSamplingInterval,
                new DataValue(new Variant(250.0)));

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(node.MinimumSamplingInterval, Is.EqualTo(250.0));
            });
        }

        [Test]
        public void AnArrayValueIsRejectedInsteadOfThrowingFromTheAttributeCast()
        {
            // The gate compared built-in types only, which does not separate a
            // Boolean from an array of Boolean, so an array reached the handler
            // behind it and its unconditional cast threw InvalidCastException
            // out of a method that reports failure as a status code.
            var node = new VariableNode
            {
                NodeId = new NodeId(2601u),
                BrowseName = QualifiedName.From("Value")
            };

            ServiceResult booleans = node.Write(
                Attributes.Historizing,
                new DataValue(new Variant(s_booleans.ToArrayOf())));
            ServiceResult masks = node.Write(
                Attributes.WriteMask,
                new DataValue(new Variant(s_masks.ToArrayOf())));
            ServiceResult scalar = node.Write(
                Attributes.Historizing,
                new DataValue(new Variant(true)));

            Assert.Multiple(() =>
            {
                Assert.That(booleans.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTypeMismatch));
                Assert.That(masks.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTypeMismatch));

                // the matching scalar still writes.
                Assert.That(ServiceResult.IsGood(scalar), Is.True);
                Assert.That(node.Historizing, Is.True);
            });
        }

        [Test]
        public void RolePermissionAttributesSurviveAReadWriteRoundTrip()
        {
            // RolePermissionType is not a built-in type, so the gate derived
            // BuiltInType.Null for the attribute and rejected every value,
            // leaving the handler behind it permanently unreachable.
            var node = new VariableNode
            {
                NodeId = new NodeId(2602u),
                BrowseName = QualifiedName.From("Value"),
                RolePermissions =
                [
                    new RolePermissionType { RoleId = new NodeId(15644u), Permissions = 1 }
                ]
            };

            var read = new DataValue();
            ServiceResult readResult = node.Read(null, Attributes.RolePermissions, ref read);
            ServiceResult result = node.Write(Attributes.RolePermissions, read);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(readResult), Is.True);
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(node.RolePermissions, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public void NodeTableRemoveDropsTheReverseReference()
        {
            // The reverse reference was removed with the wrong direction, so it
            // stayed behind and dangled.
            var namespaceUris = new NamespaceTable();
            var serverUris = new StringTable();
            var table = new NodeTable(namespaceUris, serverUris, new TypeTable(namespaceUris));

            var source = new ObjectNode
            {
                NodeId = new NodeId(3000u),
                BrowseName = QualifiedName.From("Source")
            };
            var target = new ObjectNode
            {
                NodeId = new NodeId(3001u),
                BrowseName = QualifiedName.From("Target")
            };

            table.Attach(target);
            source.ReferenceTable.Add(
                ReferenceTypeIds.Organizes,
                false,
                new ExpandedNodeId(3001u));
            table.Attach(source);

            Assert.That(
                target.ReferenceTable.Exists(
                    ReferenceTypeIds.Organizes,
                    true,
                    new ExpandedNodeId(3000u),
                    false,
                    null),
                Is.True,
                "the reverse reference should have been added");

            table.Remove(new ExpandedNodeId(3000u));

            Assert.That(
                target.ReferenceTable.Exists(
                    ReferenceTypeIds.Organizes,
                    true,
                    new ExpandedNodeId(3000u),
                    false,
                    null),
                Is.False,
                "the reverse reference should have been removed");
        }

        [Test]
        public void NodeTableAttachIndexesTheUnindexedReferenceList()
        {
            // Attach enumerated the (still empty) ReferenceTable instead of the
            // Node's References array, so nothing was indexed.
            var namespaceUris = new NamespaceTable();
            var serverUris = new StringTable();
            var table = new NodeTable(namespaceUris, serverUris, new TypeTable(namespaceUris));

            var node = new ObjectNode
            {
                NodeId = new NodeId(3100u),
                BrowseName = QualifiedName.From("Imported"),
                References = new[]
                {
                    new ReferenceNode
                    {
                        ReferenceTypeId = ReferenceTypeIds.Organizes,
                        IsInverse = false,
                        TargetId = new ExpandedNodeId(3101u)
                    }
                }.ToArrayOf()
            };

            table.Attach(node);

            Assert.Multiple(() =>
            {
                Assert.That(node.ReferenceTable, Has.Count.EqualTo(1));
                Assert.That(node.References.Count, Is.Zero);
            });
        }

        [Test]
        public void ServerUriTableIsLoadedFromTheDocument()
        {
            // LoadFromXml loaded the ServerUris into the shared context table
            // and then built the decoding mapping from an empty local one, so
            // the document's server indexes were never translated.
            SystemContext source = CreateContext();
            source.ServerUris.Append("urn:source:server");

            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId(3200u),
                BrowseName = QualifiedName.From("Node"),
                SymbolicName = "Node"
            };
            node.AddReference(
                ReferenceTypeIds.Organizes,
                false,
                new ExpandedNodeId(new NodeId(3201u), null, 1));

            var collection = new NodeStateCollection { node };

            using var stream = new MemoryStream();
            collection.SaveAsXml(source, stream, true);
            stream.Position = 0;

            // The loading context knows the same server URI at a different index.
            SystemContext target = CreateContext();
            target.ServerUris.Append("urn:other:server");
            target.ServerUris.Append("urn:source:server");

            var reloaded = new NodeStateCollection();
            reloaded.LoadFromXml(target, stream, true);

            Assert.That(reloaded, Is.Not.Empty);

            var references = new List<IReference>();
            reloaded[0].GetReferences(target, references);

            IReference organizes = references.Find(
                r => r.ReferenceTypeId == ReferenceTypeIds.Organizes && !r.IsInverse);

            Assert.That(organizes, Is.Not.Null);
            Assert.That(
                organizes.TargetId.ServerIndex,
                Is.EqualTo(target.ServerUris.GetIndex("urn:source:server")),
                "the document's server index must be mapped into the target table");
        }
    }
}
