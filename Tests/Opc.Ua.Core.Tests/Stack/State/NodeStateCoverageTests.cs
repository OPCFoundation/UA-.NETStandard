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
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.State
{
    [TestFixture]
    [Category("NodeStateCoverage")]
    [Parallelizable]
    public class NodeStateCoverageTests
    {
        [Test]
        public void BaseObjectStateConstructorSetsNodeClass()
        {
            var node = new BaseObjectState(null);
            Assert.That(node.NodeClass, Is.EqualTo(NodeClass.Object));
        }

        [Test]
        public void FolderStateConstructorSetsNodeClass()
        {
            var node = new FolderState(null);
            Assert.That(node.NodeClass, Is.EqualTo(NodeClass.Object));
        }

        [Test]
        public void BaseVariableStateConstructorSetsNodeClass()
        {
            var node = new BaseDataVariableState(null);
            Assert.That(node.NodeClass, Is.EqualTo(NodeClass.Variable));
        }

        [Test]
        public void MethodStateConstructorSetsNodeClass()
        {
            var node = new MethodState(null);
            Assert.That(node.NodeClass, Is.EqualTo(NodeClass.Method));
        }

        [Test]
        public void NodeIdCanBeSetAndRetrieved()
        {
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId(1234, 2)
            };
            Assert.That(node.NodeId, Is.EqualTo(new NodeId(1234, 2)));
        }

        [Test]
        public void BrowseNameCanBeSetAndRetrieved()
        {
            var node = new BaseObjectState(null)
            {
                BrowseName = new QualifiedName("TestObject", 2)
            };
            Assert.That(node.BrowseName.Name, Is.EqualTo("TestObject"));
            Assert.That(node.BrowseName.NamespaceIndex, Is.EqualTo(2));
        }

        [Test]
        public void DisplayNameCanBeSetAndRetrieved()
        {
            var node = new BaseObjectState(null)
            {
                DisplayName = new LocalizedText("en", "Test Display")
            };
            Assert.That(node.DisplayName.Text, Is.EqualTo("Test Display"));
        }

        [Test]
        public void DescriptionCanBeSetAndRetrieved()
        {
            var node = new BaseObjectState(null)
            {
                Description = new LocalizedText("en", "A test node")
            };
            Assert.That(node.Description.Text, Is.EqualTo("A test node"));
        }

        [Test]
        public void AddChildAddsToChildren()
        {
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId(1, 0),
                BrowseName = new QualifiedName("Parent")
            };
            var child = new BaseObjectState(parent)
            {
                NodeId = new NodeId(2, 0),
                BrowseName = new QualifiedName("Child")
            };
            parent.AddChild(child);

            List<BaseInstanceState> children = new List<BaseInstanceState>();
            parent.GetChildren(null, children);
            Assert.That(children, Has.Count.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void AddReferenceCreatesReference()
        {
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId(100, 0),
                BrowseName = new QualifiedName("Source")
            };
            node.AddReference(
                ReferenceTypeIds.Organizes,
                false,
                new NodeId(200, 0));

            // Verify references exist
            List<IReference> references = new List<IReference>();
            node.GetReferences(null, references);
            Assert.That(references, Has.Count.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void AddMultipleReferences()
        {
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId(100, 0),
                BrowseName = new QualifiedName("Source")
            };
            node.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(200, 0));
            node.AddReference(ReferenceTypeIds.HasComponent, false, new NodeId(201, 0));

            List<IReference> references = new List<IReference>();
            node.GetReferences(null, references);
            Assert.That(references, Has.Count.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void MethodStateExecutableDefault()
        {
            var method = new MethodState(null);
            Assert.That(method.Executable, Is.True);
        }

        [Test]
        public void MethodStateUserExecutableDefault()
        {
            var method = new MethodState(null);
            Assert.That(method.UserExecutable, Is.True);
        }

        [Test]
        public void MethodStateExecutableCanBeSet()
        {
            var method = new MethodState(null)
            {
                Executable = false
            };
            Assert.That(method.Executable, Is.False);
        }

        [Test]
        public void MethodStateUserExecutableCanBeSet()
        {
            var method = new MethodState(null)
            {
                UserExecutable = false
            };
            Assert.That(method.UserExecutable, Is.False);
        }

        [Test]
        public void MethodStateConstructCreatesNewInstance()
        {
            var parent = new BaseObjectState(null);
            var method = MethodState.Construct(parent);
            Assert.That(method, Is.Not.Null);
            Assert.That(method.NodeClass, Is.EqualTo(NodeClass.Method));
        }

        [Test]
        public void MethodStateDeepEqualsIdentical()
        {
            var m1 = new MethodState(null)
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Method1"),
                Executable = true,
                UserExecutable = true
            };
            var m2 = new MethodState(null)
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Method1"),
                Executable = true,
                UserExecutable = true
            };
            Assert.That(m1.DeepEquals(m2), Is.True);
        }

        [Test]
        public void MethodStateDeepEqualsDifferent()
        {
            var m1 = new MethodState(null)
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Method1"),
                Executable = true
            };
            var m2 = new MethodState(null)
            {
                NodeId = new NodeId(2),
                BrowseName = new QualifiedName("Method2"),
                Executable = false
            };
            Assert.That(m1.DeepEquals(m2), Is.False);
        }

        [Test]
        public void MethodStateInputArgumentsDefault()
        {
            var method = new MethodState(null);
            Assert.That(method.InputArguments, Is.Null);
        }

        [Test]
        public void MethodStateDeepGetHashCodeReturnsValue()
        {
            var method = new MethodState(null)
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Test")
            };
            int hash = method.DeepGetHashCode();
            Assert.That(hash, Is.Not.EqualTo(0));
        }

        [Test]
        public void BaseDataVariableStateValueCanBeSet()
        {
            var variable = new BaseDataVariableState(null)
            {
                Value = 42
            };
            Assert.That(variable.Value, Is.EqualTo(42));
        }

        [Test]
        public void BaseDataVariableStateDataTypeCanBeSet()
        {
            var variable = new BaseDataVariableState(null)
            {
                DataType = DataTypeIds.Int32
            };
            Assert.That(variable.DataType, Is.EqualTo(DataTypeIds.Int32));
        }

        [Test]
        public void BaseDataVariableStateValueRankCanBeSet()
        {
            var variable = new BaseDataVariableState(null)
            {
                ValueRank = ValueRanks.OneDimension
            };
            Assert.That(variable.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        [Test]
        public void BaseDataVariableStateAccessLevelCanBeSet()
        {
            var variable = new BaseDataVariableState(null)
            {
                AccessLevel = AccessLevels.CurrentRead
            };
            Assert.That(variable.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
        }

        [Test]
        public void BaseDataVariableStateUserAccessLevelCanBeSet()
        {
            var variable = new BaseDataVariableState(null)
            {
                UserAccessLevel = AccessLevels.CurrentReadOrWrite
            };
            Assert.That(
                variable.UserAccessLevel,
                Is.EqualTo(AccessLevels.CurrentReadOrWrite));
        }

        [Test]
        public void BaseDataVariableStateHistorizingCanBeSet()
        {
            var variable = new BaseDataVariableState(null)
            {
                Historizing = true
            };
            Assert.That(variable.Historizing, Is.True);
        }

        [Test]
        public void BaseDataVariableStateMinimumSamplingInterval()
        {
            var variable = new BaseDataVariableState(null)
            {
                MinimumSamplingInterval = 500.0
            };
            Assert.That(variable.MinimumSamplingInterval, Is.EqualTo(500.0));
        }

        [Test]
        public void FolderStateAddChildVariable()
        {
            var folder = new FolderState(null)
            {
                NodeId = new NodeId(1, 0),
                BrowseName = new QualifiedName("MyFolder")
            };
            var variable = new BaseDataVariableState(folder)
            {
                NodeId = new NodeId(2, 0),
                BrowseName = new QualifiedName("MyVar"),
                Value = "hello"
            };
            folder.AddChild(variable);

            var children = new List<BaseInstanceState>();
            folder.GetChildren(null, children);
            Assert.That(children, Has.Count.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void NodeStateInitializedDefault()
        {
            var node = new BaseObjectState(null);
            Assert.That(node.Initialized, Is.False);
        }

        [Test]
        public void MethodStateMethodDeclarationIdCanBeSet()
        {
            var method = new MethodState(null)
            {
                MethodDeclarationId = new NodeId(999)
            };
            Assert.That(method.MethodDeclarationId, Is.EqualTo(new NodeId(999)));
        }

        [Test]
        public void MethodStateOnCallMethodHandlerCanBeAssigned()
        {
            var method = new MethodState(null);
            method.OnCallMethod = (context, methodState, inputArgs, outputArgs) =>
            {
                return ServiceResult.Good;
            };
            Assert.That(method.OnCallMethod, Is.Not.Null);
        }

        [Test]
        public void MethodStateOnCallMethod2HandlerCanBeAssigned()
        {
            var method = new MethodState(null);
            method.OnCallMethod2 = (context, methodToCall, objectId, inputArgs, outputArgs) =>
            {
                return ServiceResult.Good;
            };
            Assert.That(method.OnCallMethod2, Is.Not.Null);
        }

        [Test]
        public void BaseDataVariableStateCopyPolicyDefault()
        {
            var variable = new BaseDataVariableState(null);
            Assert.That(
                variable.CopyPolicy,
                Is.EqualTo(VariableCopyPolicy.CopyOnRead));
        }

        [Test]
        public void BaseDataVariableStateCopyPolicyCanBeSet()
        {
            var variable = new BaseDataVariableState(null)
            {
                CopyPolicy = VariableCopyPolicy.Never
            };
            Assert.That(variable.CopyPolicy, Is.EqualTo(VariableCopyPolicy.Never));
        }
    }
}
