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

using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for <see cref="RelativePathFormatter"/> and its inner <see cref="RelativePathFormatter.Element"/> class.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class RelativePathFormatterTests
    {
        #region RelativePathFormatter Constructor Tests

        [Test]
        public void DefaultConstructorCreatesEmptyElements()
        {
            var formatter = new RelativePathFormatter();

            Assert.That(formatter.Elements, Is.Not.Null);
            Assert.That(formatter.Elements, Is.Empty);
        }

        [Test]
        public void ConstructorWithNullRelativePathCreatesEmptyElements()
        {
            var typeTable = new Mock<ITypeTable>();

            var formatter = new RelativePathFormatter(null, typeTable.Object);

            Assert.That(formatter.Elements, Is.Not.Null);
            Assert.That(formatter.Elements, Is.Empty);
        }

        [Test]
        public void ConstructorWithRelativePathPopulatesElements()
        {
            var typeTable = new Mock<ITypeTable>();
            var relativePath = new RelativePath();
            relativePath.Elements.Add(new RelativePathElement {
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IsInverse = false,
                IncludeSubtypes = true,
                TargetName = new QualifiedName("Node1")
            });

            var formatter = new RelativePathFormatter(relativePath, typeTable.Object);

            Assert.That(formatter.Elements, Has.Count.EqualTo(1));
            Assert.That(formatter.Elements[0].TargetName.Name, Is.EqualTo("Node1"));
        }

        [Test]
        public void ConstructorWithMultipleElementsPopulatesAll()
        {
            var typeTable = new Mock<ITypeTable>();
            var relativePath = new RelativePath();
            relativePath.Elements.Add(new RelativePathElement {
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IsInverse = false,
                IncludeSubtypes = true,
                TargetName = new QualifiedName("Node1")
            });
            relativePath.Elements.Add(new RelativePathElement {
                ReferenceTypeId = ReferenceTypeIds.Aggregates,
                IsInverse = false,
                IncludeSubtypes = true,
                TargetName = new QualifiedName("Node2")
            });

            var formatter = new RelativePathFormatter(relativePath, typeTable.Object);

            Assert.That(formatter.Elements, Has.Count.EqualTo(2));
            Assert.That(formatter.Elements[0].TargetName.Name, Is.EqualTo("Node1"));
            Assert.That(formatter.Elements[1].TargetName.Name, Is.EqualTo("Node2"));
        }

        #endregion

        #region Element Constructor Tests

        [Test]
        public void ElementConstructorWithNullElementThrowsArgumentNull()
        {
            var typeTable = new Mock<ITypeTable>();

            Assert.That(
                () => new RelativePathFormatter.Element(null, typeTable.Object),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("element"));
        }

        [Test]
        public void ElementConstructorWithNullTypeTreeThrowsArgumentNull()
        {
            var element = new RelativePathElement {
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IsInverse = false,
                IncludeSubtypes = true,
                TargetName = new QualifiedName("Test")
            };

            Assert.That(
                () => new RelativePathFormatter.Element(element, null),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("typeTree"));
        }

        [Test]
        public void ElementConstructorWithHierarchicalReferencesSetsAnyHierarchical()
        {
            var typeTable = new Mock<ITypeTable>();
            var element = new RelativePathElement {
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IsInverse = false,
                IncludeSubtypes = true,
                TargetName = new QualifiedName("TestNode")
            };

            var result = new RelativePathFormatter.Element(element, typeTable.Object);

            Assert.That(result.ElementType, Is.EqualTo(RelativePathFormatter.ElementType.AnyHierarchical));
            Assert.That(result.TargetName.Name, Is.EqualTo("TestNode"));
            Assert.That(result.IncludeSubtypes, Is.True);
        }

        [Test]
        public void ElementConstructorWithAggregatesSetsAnyComponent()
        {
            var typeTable = new Mock<ITypeTable>();
            var element = new RelativePathElement {
                ReferenceTypeId = ReferenceTypeIds.Aggregates,
                IsInverse = false,
                IncludeSubtypes = true,
                TargetName = new QualifiedName("TestNode")
            };

            var result = new RelativePathFormatter.Element(element, typeTable.Object);

            Assert.That(result.ElementType, Is.EqualTo(RelativePathFormatter.ElementType.AnyComponent));
        }

        [Test]
        public void ElementConstructorWithOtherForwardReferenceSetsForwardReference()
        {
            var typeTable = new Mock<ITypeTable>();
            var customRefId = new NodeId(999);
            typeTable.Setup(t => t.FindReferenceTypeName(customRefId))
                .Returns(new QualifiedName("CustomRef"));

            var element = new RelativePathElement {
                ReferenceTypeId = customRefId,
                IsInverse = false,
                IncludeSubtypes = true,
                TargetName = new QualifiedName("TestNode")
            };

            var result = new RelativePathFormatter.Element(element, typeTable.Object);

            Assert.That(result.ElementType, Is.EqualTo(RelativePathFormatter.ElementType.ForwardReference));
            Assert.That(result.ReferenceTypeName.Name, Is.EqualTo("CustomRef"));
        }

        [Test]
        public void ElementConstructorWithInverseReferenceSetsInverseReference()
        {
            var typeTable = new Mock<ITypeTable>();
            var customRefId = new NodeId(999);
            typeTable.Setup(t => t.FindReferenceTypeName(customRefId))
                .Returns(new QualifiedName("CustomRef"));

            var element = new RelativePathElement {
                ReferenceTypeId = customRefId,
                IsInverse = true,
                IncludeSubtypes = true,
                TargetName = new QualifiedName("TestNode")
            };

            var result = new RelativePathFormatter.Element(element, typeTable.Object);

            Assert.That(result.ElementType, Is.EqualTo(RelativePathFormatter.ElementType.InverseReference));
            Assert.That(result.ReferenceTypeName.Name, Is.EqualTo("CustomRef"));
        }

        [Test]
        public void ElementConstructorForwardWithoutSubtypesSetsForwardReference()
        {
            var typeTable = new Mock<ITypeTable>();
            var customRefId = new NodeId(999);
            typeTable.Setup(t => t.FindReferenceTypeName(customRefId))
                .Returns(new QualifiedName("CustomRef"));

            var element = new RelativePathElement {
                ReferenceTypeId = customRefId,
                IsInverse = false,
                IncludeSubtypes = false,
                TargetName = new QualifiedName("TestNode")
            };

            var result = new RelativePathFormatter.Element(element, typeTable.Object);

            Assert.That(result.ElementType, Is.EqualTo(RelativePathFormatter.ElementType.ForwardReference));
            Assert.That(result.IncludeSubtypes, Is.False);
            Assert.That(result.ReferenceTypeName.Name, Is.EqualTo("CustomRef"));
        }

        [Test]
        public void ElementDefaultConstructorSetsDefaults()
        {
            var element = new RelativePathFormatter.Element();

            Assert.That(element.ElementType, Is.EqualTo(RelativePathFormatter.ElementType.AnyHierarchical));
            Assert.That(element.IncludeSubtypes, Is.True);
            Assert.That(element.ReferenceTypeName.IsNull, Is.True);
            Assert.That(element.TargetName.IsNull, Is.True);
        }

        #endregion

        #region Parse Tests

        [Test]
        public void ParseNullStringReturnsEmptyFormatter()
        {
            var result = RelativePathFormatter.Parse(null);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Elements, Is.Empty);
        }

        [Test]
        public void ParseEmptyStringReturnsEmptyFormatter()
        {
            var result = RelativePathFormatter.Parse(string.Empty);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Elements, Is.Empty);
        }

        [Test]
        public void ParseSimpleHierarchicalPath()
        {
            var result = RelativePathFormatter.Parse("/NodeA");

            Assert.That(result.Elements, Has.Count.EqualTo(1));
            Assert.That(result.Elements[0].ElementType, Is.EqualTo(RelativePathFormatter.ElementType.AnyHierarchical));
            Assert.That(result.Elements[0].TargetName.Name, Is.EqualTo("NodeA"));
        }

        [Test]
        public void ParseSimpleComponentPath()
        {
            var result = RelativePathFormatter.Parse(".NodeA");

            Assert.That(result.Elements, Has.Count.EqualTo(1));
            Assert.That(result.Elements[0].ElementType, Is.EqualTo(RelativePathFormatter.ElementType.AnyComponent));
            Assert.That(result.Elements[0].TargetName.Name, Is.EqualTo("NodeA"));
        }

        [Test]
        public void ParseMultipleHierarchicalSegments()
        {
            var result = RelativePathFormatter.Parse("/NodeA/NodeB/NodeC");

            Assert.That(result.Elements, Has.Count.EqualTo(3));
            Assert.That(result.Elements[0].TargetName.Name, Is.EqualTo("NodeA"));
            Assert.That(result.Elements[1].TargetName.Name, Is.EqualTo("NodeB"));
            Assert.That(result.Elements[2].TargetName.Name, Is.EqualTo("NodeC"));
        }

        [Test]
        public void ParseMixedHierarchicalAndComponentPath()
        {
            var result = RelativePathFormatter.Parse("/NodeA.NodeB/NodeC");

            Assert.That(result.Elements, Has.Count.EqualTo(3));
            Assert.That(result.Elements[0].ElementType, Is.EqualTo(RelativePathFormatter.ElementType.AnyHierarchical));
            Assert.That(result.Elements[0].TargetName.Name, Is.EqualTo("NodeA"));
            Assert.That(result.Elements[1].ElementType, Is.EqualTo(RelativePathFormatter.ElementType.AnyComponent));
            Assert.That(result.Elements[1].TargetName.Name, Is.EqualTo("NodeB"));
            Assert.That(result.Elements[2].ElementType, Is.EqualTo(RelativePathFormatter.ElementType.AnyHierarchical));
            Assert.That(result.Elements[2].TargetName.Name, Is.EqualTo("NodeC"));
        }

        [Test]
        public void ParseForwardReferenceWithBrowseName()
        {
            var result = RelativePathFormatter.Parse("<MyRef>NodeA");

            Assert.That(result.Elements, Has.Count.EqualTo(1));
            Assert.That(result.Elements[0].ElementType, Is.EqualTo(RelativePathFormatter.ElementType.ForwardReference));
            Assert.That(result.Elements[0].ReferenceTypeName.Name, Is.EqualTo("MyRef"));
            Assert.That(result.Elements[0].TargetName.Name, Is.EqualTo("NodeA"));
            Assert.That(result.Elements[0].IncludeSubtypes, Is.True);
        }

        [Test]
        public void ParseInverseReferenceWithExclamation()
        {
            var result = RelativePathFormatter.Parse("<!MyRef>NodeA");

            Assert.That(result.Elements, Has.Count.EqualTo(1));
            Assert.That(result.Elements[0].ElementType, Is.EqualTo(RelativePathFormatter.ElementType.InverseReference));
            Assert.That(result.Elements[0].ReferenceTypeName.Name, Is.EqualTo("MyRef"));
            Assert.That(result.Elements[0].TargetName.Name, Is.EqualTo("NodeA"));
        }

        [Test]
        public void ParseReferenceWithNoSubtypes()
        {
            var result = RelativePathFormatter.Parse("<#MyRef>NodeA");

            Assert.That(result.Elements, Has.Count.EqualTo(1));
            Assert.That(result.Elements[0].IncludeSubtypes, Is.False);
            Assert.That(result.Elements[0].ElementType, Is.EqualTo(RelativePathFormatter.ElementType.ForwardReference));
            Assert.That(result.Elements[0].ReferenceTypeName.Name, Is.EqualTo("MyRef"));
        }

        [Test]
        public void ParseInverseReferenceWithNoSubtypes()
        {
            var result = RelativePathFormatter.Parse("<#!MyRef>NodeA");

            Assert.That(result.Elements, Has.Count.EqualTo(1));
            Assert.That(result.Elements[0].IncludeSubtypes, Is.False);
            Assert.That(result.Elements[0].ElementType, Is.EqualTo(RelativePathFormatter.ElementType.InverseReference));
            Assert.That(result.Elements[0].ReferenceTypeName.Name, Is.EqualTo("MyRef"));
        }

        [Test]
        public void ParseNameWithNamespaceIndex()
        {
            var result = RelativePathFormatter.Parse("/2:NodeA");

            Assert.That(result.Elements, Has.Count.EqualTo(1));
            Assert.That(result.Elements[0].TargetName.NamespaceIndex, Is.EqualTo(2));
            Assert.That(result.Elements[0].TargetName.Name, Is.EqualTo("NodeA"));
        }

        [Test]
        public void ParseReferenceNameWithNamespaceIndex()
        {
            var result = RelativePathFormatter.Parse("<3:MyRef>NodeA");

            Assert.That(result.Elements, Has.Count.EqualTo(1));
            Assert.That(result.Elements[0].ReferenceTypeName.NamespaceIndex, Is.EqualTo(3));
            Assert.That(result.Elements[0].ReferenceTypeName.Name, Is.EqualTo("MyRef"));
        }

        [Test]
        public void ParseEscapedSpecialCharactersInName()
        {
            // '&/' is escape for '/' in name
            var result = RelativePathFormatter.Parse("/Node&/A");

            Assert.That(result.Elements, Has.Count.EqualTo(1));
            Assert.That(result.Elements[0].TargetName.Name, Is.EqualTo("Node/A"));
        }

        [TestCase("/Node&.A", "Node.A")]
        [TestCase("/Node&<A", "Node<A")]
        [TestCase("/Node&>A", "Node>A")]
        [TestCase("/Node&:A", "Node:A")]
        [TestCase("/Node&!A", "Node!A")]
        [TestCase("/Node&#A", "Node#A")]
        [TestCase("/Node&&A", "Node&A")]
        public void ParseVariousEscapeSequencesInTargetName(string input, string expectedName)
        {
            var result = RelativePathFormatter.Parse(input);

            Assert.That(result.Elements, Has.Count.EqualTo(1));
            Assert.That(result.Elements[0].TargetName.Name, Is.EqualTo(expectedName));
        }

        [Test]
        public void ParseEscapeSequenceInReferenceName()
        {
            var result = RelativePathFormatter.Parse("<My&/Ref>NodeA");

            Assert.That(result.Elements, Has.Count.EqualTo(1));
            Assert.That(result.Elements[0].ReferenceTypeName.Name, Is.EqualTo("My/Ref"));
        }

        [Test]
        public void ParseWithoutLeadingSeparatorDefaultsToHierarchical()
        {
            // A path that starts with a regular character (no /, ., <) defaults to AnyHierarchical
            var result = RelativePathFormatter.Parse("NodeA/NodeB");

            Assert.That(result.Elements, Has.Count.EqualTo(2));
            Assert.That(result.Elements[0].ElementType, Is.EqualTo(RelativePathFormatter.ElementType.AnyHierarchical));
            Assert.That(result.Elements[0].TargetName.Name, Is.EqualTo("NodeA"));
            Assert.That(result.Elements[1].ElementType, Is.EqualTo(RelativePathFormatter.ElementType.AnyHierarchical));
            Assert.That(result.Elements[1].TargetName.Name, Is.EqualTo("NodeB"));
        }

        [Test]
        public void ParseInvalidEscapeSequenceThrowsException()
        {
            Assert.That(
                () => RelativePathFormatter.Parse("/Node&XA"),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ParseEscapeAtEndOfStringThrowsException()
        {
            Assert.That(
                () => RelativePathFormatter.Parse("/Node&"),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ParseMissingClosingBracketThrowsException()
        {
            Assert.That(
                () => RelativePathFormatter.Parse("<MyRef"),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ParseEmptyReferenceNameThrowsException()
        {
            Assert.That(
                () => RelativePathFormatter.Parse("<>NodeA"),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ParseInvalidCharacterInTargetNameThrowsException()
        {
            // '!' is not valid in a target name without escape
            Assert.That(
                () => RelativePathFormatter.Parse("/Node!A"),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ParseComplexPath()
        {
            // "/2:Objects/" produces a hierarchical element with target "Objects",
            // then "/" starts a new hierarchical element with no target (next char is '<'),
            // then "<HasComponent>" is a forward reference with target "3:Pump",
            // then ".Status" is a component element.
            var result = RelativePathFormatter.Parse("/2:Objects/<HasComponent>3:Pump.Status");

            Assert.That(result.Elements, Has.Count.EqualTo(4));
            Assert.That(result.Elements[0].ElementType, Is.EqualTo(RelativePathFormatter.ElementType.AnyHierarchical));
            Assert.That(result.Elements[0].TargetName.NamespaceIndex, Is.EqualTo(2));
            Assert.That(result.Elements[0].TargetName.Name, Is.EqualTo("Objects"));
            Assert.That(result.Elements[1].ElementType, Is.EqualTo(RelativePathFormatter.ElementType.AnyHierarchical));
            Assert.That(result.Elements[1].TargetName.IsNull, Is.True);
            Assert.That(result.Elements[2].ElementType, Is.EqualTo(RelativePathFormatter.ElementType.ForwardReference));
            Assert.That(result.Elements[2].ReferenceTypeName.Name, Is.EqualTo("HasComponent"));
            Assert.That(result.Elements[2].TargetName.NamespaceIndex, Is.EqualTo(3));
            Assert.That(result.Elements[2].TargetName.Name, Is.EqualTo("Pump"));
            Assert.That(result.Elements[3].ElementType, Is.EqualTo(RelativePathFormatter.ElementType.AnyComponent));
            Assert.That(result.Elements[3].TargetName.Name, Is.EqualTo("Status"));
        }

        [Test]
        public void ParseWithNamespaceTablesTranslatesIndexes()
        {
            var currentTable = new NamespaceTable();
            currentTable.Append("urn:test:ns1");  // index 1

            var targetTable = new NamespaceTable();
            targetTable.Append("urn:other:ns");   // index 1
            targetTable.Append("urn:test:ns1");   // index 2

            var result = RelativePathFormatter.Parse("/1:NodeA", currentTable, targetTable);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Elements, Has.Count.EqualTo(1));
            Assert.That(result.Elements[0].TargetName.Name, Is.EqualTo("NodeA"));
            Assert.That(result.Elements[0].TargetName.NamespaceIndex, Is.EqualTo(2));
        }

        [Test]
        public void ParseHierarchicalPathWithNoTargetName()
        {
            // A bare "/" with no target name followed by another element
            var result = RelativePathFormatter.Parse("//NodeA");

            Assert.That(result.Elements, Has.Count.EqualTo(2));
            Assert.That(result.Elements[0].ElementType, Is.EqualTo(RelativePathFormatter.ElementType.AnyHierarchical));
            Assert.That(result.Elements[0].TargetName.IsNull, Is.True);
            Assert.That(result.Elements[1].TargetName.Name, Is.EqualTo("NodeA"));
        }

        #endregion

        #region ToString Tests

        [Test]
        public void ToStringEmptyFormatterReturnsEmptyString()
        {
            var formatter = new RelativePathFormatter();

            Assert.That(formatter.ToString(), Is.EqualTo(string.Empty));
        }

        [Test]
        public void ToStringHierarchicalElement()
        {
            var result = RelativePathFormatter.Parse("/NodeA");

            Assert.That(result.ToString(), Is.EqualTo("/NodeA"));
        }

        [Test]
        public void ToStringComponentElement()
        {
            var result = RelativePathFormatter.Parse(".NodeA");

            Assert.That(result.ToString(), Is.EqualTo(".NodeA"));
        }

        [Test]
        public void ToStringForwardReference()
        {
            var result = RelativePathFormatter.Parse("<MyRef>NodeA");

            Assert.That(result.ToString(), Is.EqualTo("<MyRef>NodeA"));
        }

        [Test]
        public void ToStringInverseReference()
        {
            var result = RelativePathFormatter.Parse("<!MyRef>NodeA");

            Assert.That(result.ToString(), Is.EqualTo("<!MyRef>NodeA"));
        }

        [Test]
        public void ToStringReferenceWithNoSubtypes()
        {
            var result = RelativePathFormatter.Parse("<#MyRef>NodeA");

            Assert.That(result.ToString(), Is.EqualTo("<#MyRef>NodeA"));
        }

        [Test]
        public void ToStringInverseReferenceWithNoSubtypes()
        {
            var result = RelativePathFormatter.Parse("<#!MyRef>NodeA");

            Assert.That(result.ToString(), Is.EqualTo("<#!MyRef>NodeA"));
        }

        [Test]
        public void ToStringWithNamespaceIndexOnTarget()
        {
            var result = RelativePathFormatter.Parse("/2:NodeA");

            Assert.That(result.ToString(), Is.EqualTo("/2:NodeA"));
        }

        [Test]
        public void ToStringWithNamespaceIndexOnReference()
        {
            var result = RelativePathFormatter.Parse("<3:MyRef>NodeA");

            Assert.That(result.ToString(), Is.EqualTo("<3:MyRef>NodeA"));
        }

        [Test]
        public void ToStringMultipleElements()
        {
            var result = RelativePathFormatter.Parse("/NodeA.NodeB/NodeC");

            Assert.That(result.ToString(), Is.EqualTo("/NodeA.NodeB/NodeC"));
        }

        [Test]
        public void ToStringWithInvalidFormatThrowsFormatException()
        {
            var formatter = new RelativePathFormatter();

            Assert.That(
                () => formatter.ToString("invalid", null),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void ToStringRoundTripsEscapedCharacters()
        {
            // Build an element with special chars in the target name
            var formatter = new RelativePathFormatter();
            formatter.Elements.Add(new RelativePathFormatter.Element {
                ElementType = RelativePathFormatter.ElementType.AnyHierarchical,
                TargetName = new QualifiedName("Node/A")
            });

            var str = formatter.ToString();

            Assert.That(str, Is.EqualTo("/Node&/A"));

            // Parse it back and confirm
            var parsed = RelativePathFormatter.Parse(str);
            Assert.That(parsed.Elements[0].TargetName.Name, Is.EqualTo("Node/A"));
        }

        [TestCase("Node.A", "/Node&.A")]
        [TestCase("Node<A", "/Node&<A")]
        [TestCase("Node>A", "/Node&>A")]
        [TestCase("Node:A", "/Node&:A")]
        [TestCase("Node!A", "/Node&!A")]
        [TestCase("Node&A", "/Node&&A")]
        public void ToStringEncodesSpecialCharactersInTargetName(string targetName, string expected)
        {
            var formatter = new RelativePathFormatter();
            formatter.Elements.Add(new RelativePathFormatter.Element {
                ElementType = RelativePathFormatter.ElementType.AnyHierarchical,
                TargetName = new QualifiedName(targetName)
            });

            Assert.That(formatter.ToString(), Is.EqualTo(expected));
        }

        [Test]
        public void ToStringEncodesSpecialCharactersInReferenceName()
        {
            var formatter = new RelativePathFormatter();
            formatter.Elements.Add(new RelativePathFormatter.Element {
                ElementType = RelativePathFormatter.ElementType.ForwardReference,
                ReferenceTypeName = new QualifiedName("My/Ref"),
                TargetName = new QualifiedName("NodeA")
            });

            Assert.That(formatter.ToString(), Is.EqualTo("<My&/Ref>NodeA"));
        }

        [Test]
        public void ToStringForwardReferenceWithNullReferenceNameOmitsBrackets()
        {
            var formatter = new RelativePathFormatter();
            formatter.Elements.Add(new RelativePathFormatter.Element {
                ElementType = RelativePathFormatter.ElementType.ForwardReference,
                ReferenceTypeName = default,
                TargetName = new QualifiedName("NodeA")
            });

            // ForwardReference with null ref name should not output angle brackets
            Assert.That(formatter.ToString(), Is.EqualTo("NodeA"));
        }

        #endregion

        #region Element ToString Tests

        [Test]
        public void ElementToStringWithInvalidFormatThrowsFormatException()
        {
            var element = new RelativePathFormatter.Element();

            Assert.That(
                () => element.ToString("bad", null),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void ElementToStringDefaultsToParameterlessOverload()
        {
            var element = new RelativePathFormatter.Element {
                ElementType = RelativePathFormatter.ElementType.AnyHierarchical,
                TargetName = new QualifiedName("Test")
            };

            Assert.That(element.ToString(), Is.EqualTo("/Test"));
            Assert.That(element.ToString(null, null), Is.EqualTo("/Test"));
        }

        [Test]
        public void ElementToStringWithNullTargetNameOmitsTarget()
        {
            var element = new RelativePathFormatter.Element {
                ElementType = RelativePathFormatter.ElementType.AnyHierarchical,
                TargetName = default
            };

            Assert.That(element.ToString(), Is.EqualTo("/"));
        }

        [Test]
        public void ElementToStringReferenceWithNamespaceOnBothNames()
        {
            var element = new RelativePathFormatter.Element {
                ElementType = RelativePathFormatter.ElementType.ForwardReference,
                ReferenceTypeName = new QualifiedName("MyRef", 5),
                IncludeSubtypes = true,
                TargetName = new QualifiedName("Target", 3)
            };

            Assert.That(element.ToString(), Is.EqualTo("<5:MyRef>3:Target"));
        }

        #endregion

        #region IsEmpty Tests

        [Test]
        public void IsEmptyWithNullReturnsTrue()
        {
            Assert.That(RelativePathFormatter.IsEmpty(null), Is.True);
        }

        [Test]
        public void IsEmptyWithEmptyElementsReturnsTrue()
        {
            var formatter = new RelativePathFormatter();

            Assert.That(RelativePathFormatter.IsEmpty(formatter), Is.True);
        }

        [Test]
        public void IsEmptyWithElementsReturnsFalse()
        {
            var formatter = RelativePathFormatter.Parse("/NodeA");

            Assert.That(RelativePathFormatter.IsEmpty(formatter), Is.False);
        }

        #endregion

        #region UpdateNamespaceTable Tests

        [Test]
        public void UpdateNamespaceTableAddsMissingNamespaces()
        {
            var currentTable = new NamespaceTable();
            currentTable.Append("urn:local");      // index 1
            currentTable.Append("urn:test:ns2");   // index 2

            var targetTable = new NamespaceTable();

            // Parse a path with namespace index 2
            var formatter = RelativePathFormatter.Parse("/2:NodeA");

            formatter.UpdateNamespaceTable(currentTable, targetTable);

            // targetTable should have had a placeholder appended at index 1,
            // then urn:test:ns2 appended
            Assert.That(targetTable.Count, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void UpdateNamespaceTableHandlesEmptyElements()
        {
            var currentTable = new NamespaceTable();
            currentTable.Append("urn:local");  // index 1 — need at least 2 entries

            var targetTable = new NamespaceTable();
            targetTable.Append("urn:local");

            var formatter = new RelativePathFormatter();

            // Should not throw with empty elements list
            formatter.UpdateNamespaceTable(currentTable, targetTable);

            Assert.That(formatter.Elements, Is.Empty);
        }

        [Test]
        public void UpdateNamespaceTableWithReferenceTypeName()
        {
            var currentTable = new NamespaceTable();
            currentTable.Append("urn:local");      // index 1
            currentTable.Append("urn:ref:ns");     // index 2

            var targetTable = new NamespaceTable();

            // Build a formatter with a reference type name in namespace 2
            var formatter = new RelativePathFormatter();
            formatter.Elements.Add(new RelativePathFormatter.Element {
                ElementType = RelativePathFormatter.ElementType.ForwardReference,
                ReferenceTypeName = new QualifiedName("CustomRef", 2),
                TargetName = new QualifiedName("NodeA")
            });

            formatter.UpdateNamespaceTable(currentTable, targetTable);

            Assert.That(targetTable.Count, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void UpdateNamespaceTableWithTargetNameInNamespace()
        {
            var currentTable = new NamespaceTable();
            currentTable.Append("urn:local");      // index 1
            currentTable.Append("urn:target:ns");  // index 2

            var targetTable = new NamespaceTable();

            var formatter = new RelativePathFormatter();
            formatter.Elements.Add(new RelativePathFormatter.Element {
                ElementType = RelativePathFormatter.ElementType.AnyHierarchical,
                TargetName = new QualifiedName("NodeA", 2)
            });

            formatter.UpdateNamespaceTable(currentTable, targetTable);

            Assert.That(targetTable.Count, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void UpdateNamespaceTableSkipsNamespaceIndex0And1()
        {
            var currentTable = new NamespaceTable();
            currentTable.Append("urn:local");  // index 1

            var targetTable = new NamespaceTable();
            targetTable.Append("urn:other");   // index 1

            var formatter = new RelativePathFormatter();
            formatter.Elements.Add(new RelativePathFormatter.Element {
                ElementType = RelativePathFormatter.ElementType.AnyHierarchical,
                TargetName = new QualifiedName("NodeA", 0)
            });

            int initialCount = targetTable.Count;
            formatter.UpdateNamespaceTable(currentTable, targetTable);

            // Namespace index 0 and 1 are not added; target already exists with ns:0
            Assert.That(targetTable.Count, Is.EqualTo(initialCount));
        }

        #endregion

        #region TranslateNamespaceIndexes Tests

        [Test]
        public void TranslateNamespaceIndexesUpdatesTargetName()
        {
            var currentTable = new NamespaceTable();
            currentTable.Append("urn:test:ns1");   // index 1

            var targetTable = new NamespaceTable();
            targetTable.Append("urn:test:ns1");    // index 1

            var formatter = RelativePathFormatter.Parse("/1:NodeA");

            formatter.TranslateNamespaceIndexes(currentTable, targetTable);

            Assert.That(formatter.Elements[0].TargetName.Name, Is.EqualTo("NodeA"));
            Assert.That(formatter.Elements[0].TargetName.NamespaceIndex, Is.EqualTo(1));
        }

        [Test]
        public void TranslateNamespaceIndexesRemapsTargetName()
        {
            var currentTable = new NamespaceTable();
            currentTable.Append("urn:test:ns1");   // index 1

            var targetTable = new NamespaceTable();
            targetTable.Append("urn:other:ns");    // index 1
            targetTable.Append("urn:test:ns1");    // index 2

            var formatter = RelativePathFormatter.Parse("/1:NodeA");

            formatter.TranslateNamespaceIndexes(currentTable, targetTable);

            Assert.That(formatter.Elements[0].TargetName.NamespaceIndex, Is.EqualTo(2));
        }

        [Test]
        public void TranslateNamespaceIndexesRemapsReferenceTypeName()
        {
            var currentTable = new NamespaceTable();
            currentTable.Append("urn:ref:ns");     // index 1

            var targetTable = new NamespaceTable();
            targetTable.Append("urn:other:ns");    // index 1
            targetTable.Append("urn:ref:ns");      // index 2

            var formatter = new RelativePathFormatter();
            formatter.Elements.Add(new RelativePathFormatter.Element {
                ElementType = RelativePathFormatter.ElementType.ForwardReference,
                ReferenceTypeName = new QualifiedName("CustomRef", 1),
                TargetName = new QualifiedName("NodeA")
            });

            formatter.TranslateNamespaceIndexes(currentTable, targetTable);

            Assert.That(formatter.Elements[0].ReferenceTypeName.NamespaceIndex, Is.EqualTo(2));
            Assert.That(formatter.Elements[0].ReferenceTypeName.Name, Is.EqualTo("CustomRef"));
        }

        [Test]
        public void TranslateNamespaceIndexesThrowsForUnmappedTargetName()
        {
            var currentTable = new NamespaceTable();
            currentTable.Append("urn:test:ns1");  // index 1

            var targetTable = new NamespaceTable();
            // target table does NOT contain urn:test:ns1

            var formatter = RelativePathFormatter.Parse("/1:NodeA");

            Assert.That(
                () => formatter.TranslateNamespaceIndexes(currentTable, targetTable),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void TranslateNamespaceIndexesThrowsForUnmappedReferenceTypeName()
        {
            var currentTable = new NamespaceTable();
            currentTable.Append("urn:ref:ns");  // index 1

            var targetTable = new NamespaceTable();
            // target table does NOT contain urn:ref:ns

            var formatter = new RelativePathFormatter();
            formatter.Elements.Add(new RelativePathFormatter.Element {
                ElementType = RelativePathFormatter.ElementType.ForwardReference,
                ReferenceTypeName = new QualifiedName("CustomRef", 1),
                TargetName = new QualifiedName("NodeA")
            });

            Assert.That(
                () => formatter.TranslateNamespaceIndexes(currentTable, targetTable),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void TranslateNamespaceIndexesSkipsZeroNamespaceIndex()
        {
            var currentTable = new NamespaceTable();
            currentTable.Append("urn:test:ns1");  // index 1

            var targetTable = new NamespaceTable();
            targetTable.Append("urn:test:ns1");   // index 1

            var formatter = RelativePathFormatter.Parse("/NodeA");

            // TargetName has namespace index 0, should be skipped
            formatter.TranslateNamespaceIndexes(currentTable, targetTable);

            Assert.That(formatter.Elements[0].TargetName.NamespaceIndex, Is.EqualTo(0));
        }

        [Test]
        public void TranslateNamespaceIndexesHandlesEmptyElements()
        {
            var currentTable = new NamespaceTable();
            var targetTable = new NamespaceTable();

            var formatter = new RelativePathFormatter();

            // Should not throw
            formatter.TranslateNamespaceIndexes(currentTable, targetTable);
        }

        #endregion

        #region Round-Trip Tests

        [TestCase("/NodeA")]
        [TestCase(".NodeA")]
        [TestCase("/NodeA/NodeB")]
        [TestCase("/NodeA.NodeB")]
        [TestCase("<MyRef>NodeA")]
        [TestCase("<!MyRef>NodeA")]
        [TestCase("<#MyRef>NodeA")]
        [TestCase("<#!MyRef>NodeA")]
        [TestCase("/2:NodeA")]
        [TestCase("<3:MyRef>2:NodeA")]
        [TestCase("/2:Objects/<HasComponent>3:Pump.Status")]
        public void ParseThenToStringRoundTrips(string path)
        {
            var result = RelativePathFormatter.Parse(path);

            Assert.That(result.ToString(), Is.EqualTo(path));
        }

        [Test]
        public void RoundTripWithEscapedCharacters()
        {
            var original = "/Node&/A";
            var parsed = RelativePathFormatter.Parse(original);

            Assert.That(parsed.Elements[0].TargetName.Name, Is.EqualTo("Node/A"));
            Assert.That(parsed.ToString(), Is.EqualTo(original));
        }

        #endregion

        #region Edge Case Tests

        [Test]
        public void ParseSingleSlashYieldsOneElementWithNoTarget()
        {
            // A single "/" followed by another "/" means the first element has no target name
            var result = RelativePathFormatter.Parse("/");

            Assert.That(result.Elements, Has.Count.EqualTo(1));
            Assert.That(result.Elements[0].ElementType, Is.EqualTo(RelativePathFormatter.ElementType.AnyHierarchical));
            Assert.That(result.Elements[0].TargetName.IsNull, Is.True);
        }

        [Test]
        public void ParseSingleDotYieldsOneElementWithNoTarget()
        {
            var result = RelativePathFormatter.Parse(".");

            Assert.That(result.Elements, Has.Count.EqualTo(1));
            Assert.That(result.Elements[0].ElementType, Is.EqualTo(RelativePathFormatter.ElementType.AnyComponent));
            Assert.That(result.Elements[0].TargetName.IsNull, Is.True);
        }

        [Test]
        public void ParseReferenceWithTargetInDifferentNamespace()
        {
            var result = RelativePathFormatter.Parse("<5:Ref>10:Target");

            Assert.That(result.Elements[0].ReferenceTypeName.NamespaceIndex, Is.EqualTo(5));
            Assert.That(result.Elements[0].ReferenceTypeName.Name, Is.EqualTo("Ref"));
            Assert.That(result.Elements[0].TargetName.NamespaceIndex, Is.EqualTo(10));
            Assert.That(result.Elements[0].TargetName.Name, Is.EqualTo("Target"));
        }

        [Test]
        public void ElementPropertiesAreSettable()
        {
            var element = new RelativePathFormatter.Element();

            element.ElementType = RelativePathFormatter.ElementType.InverseReference;
            element.ReferenceTypeName = new QualifiedName("TestRef", 2);
            element.IncludeSubtypes = false;
            element.TargetName = new QualifiedName("TestTarget", 3);

            Assert.That(element.ElementType, Is.EqualTo(RelativePathFormatter.ElementType.InverseReference));
            Assert.That(element.ReferenceTypeName.Name, Is.EqualTo("TestRef"));
            Assert.That(element.ReferenceTypeName.NamespaceIndex, Is.EqualTo(2));
            Assert.That(element.IncludeSubtypes, Is.False);
            Assert.That(element.TargetName.Name, Is.EqualTo("TestTarget"));
            Assert.That(element.TargetName.NamespaceIndex, Is.EqualTo(3));
        }

        [Test]
        public void FormatProviderPassedThroughInToString()
        {
            var formatter = RelativePathFormatter.Parse("/NodeA");

            // Passing null formatProvider should work normally
            string result = formatter.ToString(null, null);

            Assert.That(result, Is.EqualTo("/NodeA"));
        }

        [Test]
        public void ParseNameStartingWithDigitsButNotNamespace()
        {
            // "123abc" - digits followed by non-colon char means digits are part of name
            var result = RelativePathFormatter.Parse("/123abc");

            Assert.That(result.Elements, Has.Count.EqualTo(1));
            Assert.That(result.Elements[0].TargetName.Name, Is.EqualTo("123abc"));
            Assert.That(result.Elements[0].TargetName.NamespaceIndex, Is.EqualTo(0));
        }

        [Test]
        public void ParseNameWithNamespaceIndexFollowedByReference()
        {
            var result = RelativePathFormatter.Parse("/1:A<Ref>B");

            Assert.That(result.Elements, Has.Count.EqualTo(2));
            Assert.That(result.Elements[0].TargetName.NamespaceIndex, Is.EqualTo(1));
            Assert.That(result.Elements[0].TargetName.Name, Is.EqualTo("A"));
            Assert.That(result.Elements[1].ReferenceTypeName.Name, Is.EqualTo("Ref"));
            Assert.That(result.Elements[1].TargetName.Name, Is.EqualTo("B"));
        }

        [Test]
        public void ConstructorFromRelativePathWithHierarchicalRef()
        {
            var typeTable = new Mock<ITypeTable>();
            var relativePath = new RelativePath(new QualifiedName("TestNode"));

            var formatter = new RelativePathFormatter(relativePath, typeTable.Object);

            Assert.That(formatter.Elements, Has.Count.EqualTo(1));
            Assert.That(formatter.Elements[0].ElementType, Is.EqualTo(RelativePathFormatter.ElementType.AnyHierarchical));
            Assert.That(formatter.Elements[0].TargetName.Name, Is.EqualTo("TestNode"));
        }

        [Test]
        public void UpdateNamespaceTableEnsuresPlaceholderForLocalNamespace()
        {
            var currentTable = new NamespaceTable();
            currentTable.Append("urn:test:ns1");   // index 1
            currentTable.Append("urn:test:ns2");   // index 2

            // Target table has only 1 entry (index 0 = OPC UA namespace)
            var targetTable = new NamespaceTable();

            var formatter = RelativePathFormatter.Parse("/2:NodeA");

            formatter.UpdateNamespaceTable(currentTable, targetTable);

            // Target table should have a placeholder at index 1 (the "---" entry)
            Assert.That(targetTable.Count, Is.GreaterThan(1));
        }

        [Test]
        public void UpdateNamespaceTableWithAlreadyMappedNamespace()
        {
            var currentTable = new NamespaceTable();
            currentTable.Append("urn:local");      // index 1
            currentTable.Append("urn:test:ns2");   // index 2

            var targetTable = new NamespaceTable();
            targetTable.Append("urn:local");       // index 1
            targetTable.Append("urn:test:ns2");    // index 2

            var formatter = RelativePathFormatter.Parse("/2:NodeA");

            // Already mapped namespace should not cause issues
            formatter.UpdateNamespaceTable(currentTable, targetTable);
        }

        [Test]
        public void ParseWithNullNamespaceTablesOnly()
        {
            var currentTable = new NamespaceTable();
            var targetTable = new NamespaceTable();

            // Parse with namespace tables but path has no namespace refs (ns:0)
            var result = RelativePathFormatter.Parse("/NodeA", currentTable, targetTable);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Elements[0].TargetName.Name, Is.EqualTo("NodeA"));
        }

        #endregion
    }
}
