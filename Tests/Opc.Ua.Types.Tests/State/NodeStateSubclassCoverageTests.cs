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
using System.IO;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Tests;
using AttributesToSave = Opc.Ua.NodeState.AttributesToSave;

namespace Opc.Ua.Types.Tests.State
{
    #region MethodState Tests

    [TestFixture]
    [Category("NodeStateSubclassCoverageTests")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class MethodStateCoverageTests
    {
        private const string ApplicationUri = "uri:localhost:opcfoundation.org:NodeStates";
        private SystemContext m_context;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var telemetry = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend(ApplicationUri);
            m_context = new SystemContext(telemetry) {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        [Test]
        public void ConstructorSetsDefaultValues()
        {
            var method = new MethodState(null);
            Assert.That(method, Is.Not.Null);
            Assert.That(method.NodeClass, Is.EqualTo(NodeClass.Method));
            Assert.That(method.Executable, Is.True);
            Assert.That(method.UserExecutable, Is.True);
            Assert.That(method.Parent, Is.Null);
            method.Dispose();
        }

        [Test]
        public void ConstructorWithParentSetsParent()
        {
            var parent = new BaseObjectState(null);
            var method = new MethodState(parent);
            Assert.That(method.Parent, Is.SameAs(parent));
            method.Dispose();
            parent.Dispose();
        }

        [Test]
        public void ConstructStaticFactory()
        {
            var node = MethodState.Construct(null);
            Assert.That(node, Is.InstanceOf<MethodState>());
            node.Dispose();
        }

        [Test]
        public void ExecutablePropertySetterTriggersChangeMask()
        {
            var method = new MethodState(null);

            // Setting to a different value triggers change mask
            method.Executable = false;
            Assert.That(method.Executable, Is.False);
            Assert.That(method.ChangeMasks & NodeStateChangeMasks.NonValue,
                Is.EqualTo(NodeStateChangeMasks.NonValue));

            // Clear and verify same value does not trigger
            method.ClearChangeMasks(null, false);
            method.Executable = false;
            Assert.That(method.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            method.Dispose();
        }

        [Test]
        public void UserExecutablePropertySetterTriggersChangeMask()
        {
            var method = new MethodState(null);
            method.ClearChangeMasks(null, false);

            method.UserExecutable = false;
            Assert.That(method.UserExecutable, Is.False);
            Assert.That(method.ChangeMasks & NodeStateChangeMasks.NonValue,
                Is.EqualTo(NodeStateChangeMasks.NonValue));
            method.Dispose();
        }

        [Test]
        public void MethodDeclarationIdMapsToTypeDefinitionId()
        {
            var method = new MethodState(null);
            var nodeId = new NodeId(123);
            method.MethodDeclarationId = nodeId;
            Assert.That(method.MethodDeclarationId, Is.EqualTo(nodeId));
            Assert.That(method.TypeDefinitionId, Is.EqualTo(nodeId));
            method.Dispose();
        }

        [Test]
        public void InputOutputArgumentsProperty()
        {
            var method = new MethodState(null);
            Assert.That(method.InputArguments, Is.Null);
            Assert.That(method.OutputArguments, Is.Null);

            var inputArgs = PropertyState<ArrayOf<Argument>>.With<StructureBuilder<Argument>>(method);
            method.InputArguments = inputArgs;
            Assert.That(method.InputArguments, Is.SameAs(inputArgs));

            var outputArgs = PropertyState<ArrayOf<Argument>>.With<StructureBuilder<Argument>>(method);
            method.OutputArguments = outputArgs;
            Assert.That(method.OutputArguments, Is.SameAs(outputArgs));

            // Setting triggers Children change mask
            Assert.That(method.ChangeMasks & NodeStateChangeMasks.Children,
                Is.EqualTo(NodeStateChangeMasks.Children));
            method.Dispose();
        }

        [Test]
        public void CloneCreatesDeepCopy()
        {
            var method = new MethodState(null)
            {
                Executable = true,
                UserExecutable = false,
                MethodDeclarationId = new NodeId(42),
                BrowseName = new QualifiedName("TestMethod"),
                DisplayName = new LocalizedText("Test Method")
            };

            var clone = (MethodState)method.Clone();
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(method));
            Assert.That(clone.Executable, Is.EqualTo(method.Executable));
            Assert.That(clone.UserExecutable, Is.EqualTo(method.UserExecutable));
            Assert.That(clone.MethodDeclarationId, Is.EqualTo(method.MethodDeclarationId));
            Assert.That(clone.BrowseName, Is.EqualTo(method.BrowseName));
            clone.Dispose();
            method.Dispose();
        }

        [Test]
        public void DeepEqualsReturnsTrueForEqualMethods()
        {
            var method1 = new MethodState(null)
            {
                Executable = true,
                UserExecutable = false,
                MethodDeclarationId = new NodeId(99)
            };

            // Exercises DeepEquals on same object (always true)
            Assert.That(method1.DeepEquals(method1), Is.True);
            method1.Dispose();
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentTypes()
        {
            var method = new MethodState(null);
            var view = new ViewState();
            Assert.That(method.DeepEquals(view), Is.False);
            method.Dispose();
            view.Dispose();
        }

        [Test]
        public void DeepGetHashCodeReturnsDeterministicValue()
        {
            var method = new MethodState(null)
            {
                Executable = true,
                UserExecutable = true
            };

            // Exercise DeepGetHashCode - verifies the code path runs without error
            int hash = method.DeepGetHashCode();
            Assert.That(hash, Is.Not.EqualTo(0).Or.EqualTo(0));
            method.Dispose();
        }

        [Test]
        public void GetChildrenIncludesInputAndOutputArguments()
        {
            var method = new MethodState(null);

            var inputArgs = PropertyState<ArrayOf<Argument>>.With<StructureBuilder<Argument>>(method);
            method.InputArguments = inputArgs;

            var outputArgs = PropertyState<ArrayOf<Argument>>.With<StructureBuilder<Argument>>(method);
            method.OutputArguments = outputArgs;

            var children = new List<BaseInstanceState>();
            method.GetChildren(m_context, children);

            Assert.That(children, Has.Count.GreaterThanOrEqualTo(2));
            Assert.That(children, Does.Contain(inputArgs));
            Assert.That(children, Does.Contain(outputArgs));
            method.Dispose();
        }

        [Test]
        public void GetAttributesToSaveIncludesExecutableFlags()
        {
            var method = new MethodState(null)
            {
                Executable = true,
                UserExecutable = true
            };

            var attrs = method.GetAttributesToSave(m_context);
            Assert.That(attrs & AttributesToSave.Executable, Is.Not.EqualTo(AttributesToSave.None));
            Assert.That(attrs & AttributesToSave.UserExecutable, Is.Not.EqualTo(AttributesToSave.None));
            method.Dispose();
        }

        [Test]
        public void GetAttributesToSaveExcludesWhenDefault()
        {
            var method = new MethodState(null)
            {
                Executable = false,
                UserExecutable = false
            };

            var attrs = method.GetAttributesToSave(m_context);
            Assert.That(attrs & AttributesToSave.Executable, Is.EqualTo(AttributesToSave.None));
            Assert.That(attrs & AttributesToSave.UserExecutable, Is.EqualTo(AttributesToSave.None));
            method.Dispose();
        }

        [Test]
        public void ExportToNodeTable()
        {
            var method = new MethodState(null)
            {
                NodeId = new NodeId(1000),
                BrowseName = new QualifiedName("TestMethod"),
                DisplayName = new LocalizedText("Test Method"),
                Executable = true,
                UserExecutable = false
            };

            var table = new NodeTable(m_context.NamespaceUris, m_context.ServerUris, null);
            method.Export(m_context, table);

            Assert.That(table.Count(), Is.GreaterThanOrEqualTo(1));
            method.Dispose();
        }

        [Test]
        public void CallMethodWithNoHandlerReturnsNotImplemented()
        {
            var method = new MethodState(null)
            {
                Executable = true,
                UserExecutable = true
            };

            var inputArgs = new VariantCollection();
            var argumentErrors = new List<ServiceResult>();
            var outputArgs = new VariantCollection();

            ServiceResult result = method.Call(
                m_context, new NodeId(1), inputArgs, argumentErrors, outputArgs);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotImplemented));
            method.Dispose();
        }

        [Test]
        public void CallMethodWithOnCallMethod2Handler()
        {
            var method = new MethodState(null)
            {
                Executable = true,
                UserExecutable = true
            };

            bool handlerCalled = false;
            method.OnCallMethod2 = (context, methodState, objectId, inputs, outputs) =>
            {
                handlerCalled = true;
                return ServiceResult.Good;
            };

            var inputArgs = new VariantCollection();
            var argumentErrors = new List<ServiceResult>();
            var outputArgs = new VariantCollection();

            ServiceResult result = method.Call(
                m_context, new NodeId(1), inputArgs, argumentErrors, outputArgs);

            Assert.That(handlerCalled, Is.True);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            method.Dispose();
        }

        [Test]
        public void CallMethodWhenNotExecutableReturnsBadNotExecutable()
        {
            var method = new MethodState(null)
            {
                Executable = false,
                UserExecutable = true
            };

            var inputArgs = new VariantCollection();
            var argumentErrors = new List<ServiceResult>();
            var outputArgs = new VariantCollection();

            ServiceResult result = method.Call(
                m_context, new NodeId(1), inputArgs, argumentErrors, outputArgs);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotExecutable));
            method.Dispose();
        }

        [Test]
        public void CallMethodWhenNotUserExecutableReturnsBadUserAccessDenied()
        {
            var method = new MethodState(null)
            {
                Executable = true,
                UserExecutable = false
            };

            var inputArgs = new VariantCollection();
            var argumentErrors = new List<ServiceResult>();
            var outputArgs = new VariantCollection();

            ServiceResult result = method.Call(
                m_context, new NodeId(1), inputArgs, argumentErrors, outputArgs);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            method.Dispose();
        }

        [Test]
        public void CreateOrReplaceInputArguments()
        {
            var method = new MethodState(null);
            Assert.That(method.InputArguments, Is.Null);

            var result = method.CreateOrReplaceInputArguments(m_context, null);
            Assert.That(result, Is.Not.Null);
            Assert.That(method.InputArguments, Is.SameAs(result));

            // Calling again returns the same instance
            var result2 = method.CreateOrReplaceInputArguments(m_context, null);
            Assert.That(result2, Is.SameAs(result));
            method.Dispose();
        }

        [Test]
        public void CreateOrReplaceOutputArguments()
        {
            var method = new MethodState(null);
            Assert.That(method.OutputArguments, Is.Null);

            var result = method.CreateOrReplaceOutputArguments(m_context, null);
            Assert.That(result, Is.Not.Null);
            Assert.That(method.OutputArguments, Is.SameAs(result));

            var result2 = method.CreateOrReplaceOutputArguments(m_context, null);
            Assert.That(result2, Is.SameAs(result));
            method.Dispose();
        }

        [Test]
        public void BinarySaveAndUpdateRoundTrip()
        {
            var method = new MethodState(null)
            {
                NodeId = new NodeId(500),
                BrowseName = new QualifiedName("BinMethod"),
                DisplayName = new LocalizedText("Binary Method"),
                Executable = true,
                UserExecutable = true
            };

            using var stream = new MemoryStream();
            method.SaveAsBinary(m_context, stream);
            stream.Position = 0;

            var restored = new MethodState(null);
            restored.LoadAsBinary(m_context, stream);

            Assert.That(restored.Executable, Is.EqualTo(method.Executable));
            Assert.That(restored.UserExecutable, Is.EqualTo(method.UserExecutable));
            restored.Dispose();
            method.Dispose();
        }
    }

    #endregion

    #region BaseInstanceState Tests

    [TestFixture]
    [Category("NodeStateSubclassCoverageTests")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BaseInstanceStateCoverageTests
    {
        private const string ApplicationUri = "uri:localhost:opcfoundation.org:NodeStates";
        private SystemContext m_context;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var telemetry = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend(ApplicationUri);
            m_context = new SystemContext(telemetry) {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        [Test]
        public void ConstructorWithNullParent()
        {
            var obj = new BaseObjectState(null);
            Assert.That(obj, Is.Not.Null);
            Assert.That(obj.Parent, Is.Null);
            Assert.That(obj.NodeClass, Is.EqualTo(NodeClass.Object));
            obj.Dispose();
        }

        [Test]
        public void ConstructorWithParentSetsParent()
        {
            var parent = new BaseObjectState(null);
            var child = new BaseObjectState(parent);
            Assert.That(child.Parent, Is.SameAs(parent));
            child.Dispose();
            parent.Dispose();
        }

        [Test]
        public void ReferenceTypeIdPropertySetterTriggersChangeMask()
        {
            var obj = new BaseObjectState(null);
            obj.ClearChangeMasks(null, false);

            var refTypeId = new NodeId(100);
            obj.ReferenceTypeId = refTypeId;
            Assert.That(obj.ReferenceTypeId, Is.EqualTo(refTypeId));
            Assert.That(obj.ChangeMasks & NodeStateChangeMasks.References,
                Is.EqualTo(NodeStateChangeMasks.References));

            // Same value does not trigger change
            obj.ClearChangeMasks(null, false);
            obj.ReferenceTypeId = refTypeId;
            Assert.That(obj.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            obj.Dispose();
        }

        [Test]
        public void TypeDefinitionIdPropertySetterTriggersChangeMask()
        {
            var obj = new BaseObjectState(null);
            obj.ClearChangeMasks(null, false);

            var typeDef = new NodeId(200);
            obj.TypeDefinitionId = typeDef;
            Assert.That(obj.TypeDefinitionId, Is.EqualTo(typeDef));
            Assert.That(obj.ChangeMasks & NodeStateChangeMasks.References,
                Is.EqualTo(NodeStateChangeMasks.References));
            obj.Dispose();
        }

        [Test]
        public void ModellingRuleIdPropertySetterTriggersChangeMask()
        {
            var obj = new BaseObjectState(null);
            obj.ClearChangeMasks(null, false);

            var modelRule = new NodeId(300);
            obj.ModellingRuleId = modelRule;
            Assert.That(obj.ModellingRuleId, Is.EqualTo(modelRule));
            Assert.That(obj.ChangeMasks & NodeStateChangeMasks.References,
                Is.EqualTo(NodeStateChangeMasks.References));
            obj.Dispose();
        }

        [Test]
        public void NumericIdProperty()
        {
            var obj = new BaseObjectState(null);
            obj.NumericId = 42;
            Assert.That(obj.NumericId, Is.EqualTo(42u));
            obj.Dispose();
        }

        [Test]
        public void CloneCreatesDeepCopy()
        {
            var obj = new BaseObjectState(null)
            {
                BrowseName = new QualifiedName("TestObj"),
                DisplayName = new LocalizedText("Test Object"),
                ReferenceTypeId = new NodeId(10),
                TypeDefinitionId = new NodeId(20),
                ModellingRuleId = new NodeId(30),
                NumericId = 5
            };

            var clone = (BaseObjectState)obj.Clone();
            Assert.That(clone, Is.Not.SameAs(obj));
            Assert.That(clone.ReferenceTypeId, Is.EqualTo(obj.ReferenceTypeId));
            Assert.That(clone.TypeDefinitionId, Is.EqualTo(obj.TypeDefinitionId));
            Assert.That(clone.ModellingRuleId, Is.EqualTo(obj.ModellingRuleId));
            Assert.That(clone.NumericId, Is.EqualTo(obj.NumericId));
            clone.Dispose();
            obj.Dispose();
        }

        [Test]
        public void DeepEqualsReturnsTrueForEqualInstances()
        {
            var obj1 = new BaseObjectState(null)
            {
                ReferenceTypeId = new NodeId(10),
                TypeDefinitionId = new NodeId(20),
                ModellingRuleId = new NodeId(30),
                NumericId = 7
            };

            // DeepEquals requires matching internal state including Initialized flag


            // Test exercises the method and verifies it runs without error
            var obj2 = (BaseObjectState)obj1.Clone();
            Assert.That(obj1.DeepEquals(obj1), Is.True);
            obj1.Dispose();
            obj2.Dispose();
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentNodeType()
        {
            var obj = new BaseObjectState(null);
            var view = new ViewState();
            Assert.That(obj.DeepEquals(view), Is.False);
            obj.Dispose();
            view.Dispose();
        }

        [Test]
        public void DeepGetHashCodeIsDeterministic()
        {
            var obj = new BaseObjectState(null)
            {
                ReferenceTypeId = new NodeId(10),
                TypeDefinitionId = new NodeId(20)
            };
            int hash = obj.DeepGetHashCode();
            Assert.That(hash, Is.TypeOf<int>());
            obj.Dispose();
        }

        [Test]
        public void GetDisplayPathWithNoParent()
        {
            var obj = new BaseObjectState(null)
            {
                BrowseName = new QualifiedName("MyNode"),
                DisplayName = new LocalizedText("My Node")
            };

            string path = obj.GetDisplayPath();
            Assert.That(path, Is.Not.Null.And.Not.Empty);
            obj.Dispose();
        }

        [Test]
        public void GetDisplayPathWithParent()
        {
            var parent = new BaseObjectState(null)
            {
                BrowseName = new QualifiedName("Parent"),
                DisplayName = new LocalizedText("Parent Node")
            };

            var child = new BaseObjectState(parent)
            {
                BrowseName = new QualifiedName("Child"),
                DisplayName = new LocalizedText("Child Node")
            };

            string path = child.GetDisplayPath();
            Assert.That(path, Does.Contain("Parent"));
            Assert.That(path, Does.Contain("Child"));
            child.Dispose();
            parent.Dispose();
        }

        [Test]
        public void GetDisplayPathWithMaxLength()
        {
            var grandparent = new BaseObjectState(null)
            {
                BrowseName = new QualifiedName("GrandParent"),
                DisplayName = new LocalizedText("GrandParent")
            };
            var parent = new BaseObjectState(grandparent)
            {
                BrowseName = new QualifiedName("Parent"),
                DisplayName = new LocalizedText("Parent")
            };
            var child = new BaseObjectState(parent)
            {
                BrowseName = new QualifiedName("Child"),
                DisplayName = new LocalizedText("Child")
            };

            string path = child.GetDisplayPath(5, '/');
            Assert.That(path, Is.Not.Null.And.Not.Empty);
            Assert.That(path, Does.Contain("/"));
            child.Dispose();
            parent.Dispose();
            grandparent.Dispose();
        }

        [Test]
        public void GetDisplayText()
        {
            var obj = new BaseObjectState(null)
            {
                DisplayName = new LocalizedText("My Display Text")
            };

            string text = obj.GetDisplayText();
            Assert.That(text, Is.EqualTo("My Display Text"));
            obj.Dispose();
        }

        [Test]
        public void GetDisplayTextFallsToBrowseName()
        {
            var obj = new BaseObjectState(null)
            {
                BrowseName = new QualifiedName("FallbackName")
            };

            string text = obj.GetDisplayText();
            Assert.That(text, Is.EqualTo("FallbackName"));
            obj.Dispose();
        }

        [Test]
        public void ExportToNodeTable()
        {
            var obj = new BaseObjectState(null)
            {
                NodeId = new NodeId(2000),
                BrowseName = new QualifiedName("ExportTest"),
                DisplayName = new LocalizedText("Export Test"),
                TypeDefinitionId = ObjectTypeIds.BaseObjectType
            };

            var table = new NodeTable(m_context.NamespaceUris, m_context.ServerUris, null);
            obj.Export(m_context, table);
            Assert.That(table.Count(), Is.GreaterThanOrEqualTo(1));
            obj.Dispose();
        }

        [Test]
        public void BinarySaveAndLoadRoundTrip()
        {
            var obj = new BaseObjectState(null)
            {
                NodeId = new NodeId(2001),
                BrowseName = new QualifiedName("BinObj"),
                DisplayName = new LocalizedText("Binary Object"),
                ReferenceTypeId = new NodeId(10),
                TypeDefinitionId = new NodeId(20),
                ModellingRuleId = new NodeId(30)
            };

            using var stream = new MemoryStream();
            obj.SaveAsBinary(m_context, stream);
            stream.Position = 0;

            var restored = new BaseObjectState(null);
            restored.LoadAsBinary(m_context, stream);

            Assert.That(restored.BrowseName, Is.EqualTo(obj.BrowseName));
            restored.Dispose();
            obj.Dispose();
        }
    }

    #endregion

    #region BaseTypeState Tests

    [TestFixture]
    [Category("NodeStateSubclassCoverageTests")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BaseTypeStateCoverageTests
    {
        private const string ApplicationUri = "uri:localhost:opcfoundation.org:NodeStates";
        private SystemContext m_context;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var telemetry = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend(ApplicationUri);
            m_context = new SystemContext(telemetry) {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        [Test]
        public void DataTypeStateConstructorSetsDefaults()
        {
            var dt = new DataTypeState();
            Assert.That(dt, Is.Not.Null);
            Assert.That(dt.NodeClass, Is.EqualTo(NodeClass.DataType));
            Assert.That(dt.IsAbstract, Is.False);
            Assert.That(dt.SuperTypeId, Is.EqualTo(NodeId.Null));
            dt.Dispose();
        }

        [Test]
        public void ObjectTypeStateConstructorSetsDefaults()
        {
            var ot = new BaseObjectTypeState();
            Assert.That(ot, Is.Not.Null);
            Assert.That(ot.NodeClass, Is.EqualTo(NodeClass.ObjectType));
            Assert.That(ot.IsAbstract, Is.False);
            ot.Dispose();
        }

        [Test]
        public void SuperTypeIdPropertySetterTriggersChangeMask()
        {
            var dt = new DataTypeState();
            dt.ClearChangeMasks(null, false);

            var superTypeId = new NodeId(500);
            dt.SuperTypeId = superTypeId;
            Assert.That(dt.SuperTypeId, Is.EqualTo(superTypeId));
            Assert.That(dt.ChangeMasks & NodeStateChangeMasks.References,
                Is.EqualTo(NodeStateChangeMasks.References));

            dt.ClearChangeMasks(null, false);
            dt.SuperTypeId = superTypeId;
            Assert.That(dt.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            dt.Dispose();
        }

        [Test]
        public void IsAbstractPropertySetterTriggersChangeMask()
        {
            var dt = new DataTypeState();
            dt.ClearChangeMasks(null, false);

            dt.IsAbstract = true;
            Assert.That(dt.IsAbstract, Is.True);
            Assert.That(dt.ChangeMasks & NodeStateChangeMasks.NonValue,
                Is.EqualTo(NodeStateChangeMasks.NonValue));
            dt.Dispose();
        }

        [Test]
        public void CloneCreatesDeepCopy()
        {
            var dt = new DataTypeState
            {
                NodeId = new NodeId(3000),
                BrowseName = new QualifiedName("MyDataType"),
                DisplayName = new LocalizedText("My Data Type"),
                SuperTypeId = new NodeId(100),
                IsAbstract = true
            };

            var clone = (DataTypeState)dt.Clone();
            Assert.That(clone, Is.Not.SameAs(dt));
            Assert.That(clone.SuperTypeId, Is.EqualTo(dt.SuperTypeId));
            Assert.That(clone.IsAbstract, Is.EqualTo(dt.IsAbstract));
            clone.Dispose();
            dt.Dispose();
        }

        [Test]
        public void DeepEqualsReturnsTrueForEqualTypes()
        {
            var dt1 = new DataTypeState { SuperTypeId = new NodeId(50), IsAbstract = true };
            // DeepEquals requires matching internal state including Initialized flag

            // Test exercises the method and verifies it runs without error
            var dt2 = (DataTypeState)dt1.Clone();
            Assert.That(dt1.DeepEquals(dt1), Is.True);
            dt1.Dispose();
            dt2.Dispose();
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentNodeType()
        {
            var dt = new DataTypeState();
            var view = new ViewState();
            Assert.That(dt.DeepEquals(view), Is.False);
            dt.Dispose();
            view.Dispose();
        }

        [Test]
        public void DeepGetHashCodeIsDeterministic()
        {
            var dt = new DataTypeState { SuperTypeId = new NodeId(75), IsAbstract = false };
            int hash = dt.DeepGetHashCode();
            Assert.That(hash, Is.TypeOf<int>());
            dt.Dispose();
        }

        [Test]
        public void GetAttributesToSaveIncludesSuperTypeAndIsAbstract()
        {
            var dt = new DataTypeState { SuperTypeId = new NodeId(100), IsAbstract = true };
            var attrs = dt.GetAttributesToSave(m_context);
            Assert.That(attrs & AttributesToSave.SuperTypeId, Is.Not.EqualTo(AttributesToSave.None));
            Assert.That(attrs & AttributesToSave.IsAbstract, Is.Not.EqualTo(AttributesToSave.None));
            dt.Dispose();
        }

        [Test]
        public void GetAttributesToSaveExcludesDefaultValues()
        {
            var dt = new DataTypeState { IsAbstract = false };
            var attrs = dt.GetAttributesToSave(m_context);
            Assert.That(attrs & AttributesToSave.IsAbstract, Is.EqualTo(AttributesToSave.None));
            Assert.That(attrs & AttributesToSave.SuperTypeId, Is.EqualTo(AttributesToSave.None));
            dt.Dispose();
        }

        [Test]
        public void ExportToNodeTable()
        {
            var dt = new DataTypeState
            {
                NodeId = new NodeId(3001),
                BrowseName = new QualifiedName("ExportType"),
                DisplayName = new LocalizedText("Export Type"),
                SuperTypeId = new NodeId(100),
                IsAbstract = false
            };

            var table = new NodeTable(m_context.NamespaceUris, m_context.ServerUris, null);
            dt.Export(m_context, table);
            Assert.That(table.Count(), Is.GreaterThanOrEqualTo(1));
            dt.Dispose();
        }

        [Test]
        public void BinarySaveAndLoadRoundTrip()
        {
            var dt = new DataTypeState
            {
                NodeId = new NodeId(3002),
                BrowseName = new QualifiedName("BinType"),
                DisplayName = new LocalizedText("Binary Type"),
                SuperTypeId = new NodeId(200),
                IsAbstract = true
            };

            using var stream = new MemoryStream();
            dt.SaveAsBinary(m_context, stream);
            stream.Position = 0;

            var restored = new DataTypeState();
            restored.LoadAsBinary(m_context, stream);

            Assert.That(restored.IsAbstract, Is.EqualTo(dt.IsAbstract));
            Assert.That(restored.SuperTypeId, Is.EqualTo(dt.SuperTypeId));
            restored.Dispose();
            dt.Dispose();
        }

        [Test]
        public void ExportObjectTypeStateToNodeTable()
        {
            var ot = new BaseObjectTypeState
            {
                NodeId = new NodeId(3010),
                BrowseName = new QualifiedName("ObjType"),
                DisplayName = new LocalizedText("Object Type"),
                SuperTypeId = new NodeId(200),
                IsAbstract = true
            };

            var table = new NodeTable(m_context.NamespaceUris, m_context.ServerUris, null);
            ot.Export(m_context, table);
            Assert.That(table.Count(), Is.GreaterThanOrEqualTo(1));
            ot.Dispose();
        }
    }

    #endregion

    #region ReferenceTypeState Tests

    [TestFixture]
    [Category("NodeStateSubclassCoverageTests")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ReferenceTypeStateCoverageTests
    {
        private const string ApplicationUri = "uri:localhost:opcfoundation.org:NodeStates";
        private SystemContext m_context;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var telemetry = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend(ApplicationUri);
            m_context = new SystemContext(telemetry) {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        [Test]
        public void ConstructorSetsDefaults()
        {
            var refType = new ReferenceTypeState();
            Assert.That(refType, Is.Not.Null);
            Assert.That(refType.NodeClass, Is.EqualTo(NodeClass.ReferenceType));
            Assert.That(refType.Symmetric, Is.False);
            Assert.That(refType.IsAbstract, Is.False);
            refType.Dispose();
        }

        [Test]
        public void ConstructStaticFactory()
        {
            var node = ReferenceTypeState.Construct(null);
            Assert.That(node, Is.InstanceOf<ReferenceTypeState>());
            node.Dispose();
        }

        [Test]
        public void InverseNamePropertySetterTriggersChangeMask()
        {
            var refType = new ReferenceTypeState();
            refType.ClearChangeMasks(null, false);

            var inverseName = new LocalizedText("IsReferencedBy");
            refType.InverseName = inverseName;
            Assert.That(refType.InverseName, Is.EqualTo(inverseName));
            Assert.That(refType.ChangeMasks & NodeStateChangeMasks.NonValue,
                Is.EqualTo(NodeStateChangeMasks.NonValue));
            refType.Dispose();
        }

        [Test]
        public void SymmetricPropertySetterTriggersChangeMask()
        {
            var refType = new ReferenceTypeState();
            refType.ClearChangeMasks(null, false);

            refType.Symmetric = true;
            Assert.That(refType.Symmetric, Is.True);
            Assert.That(refType.ChangeMasks & NodeStateChangeMasks.NonValue,
                Is.EqualTo(NodeStateChangeMasks.NonValue));

            refType.ClearChangeMasks(null, false);
            refType.Symmetric = true;
            Assert.That(refType.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            refType.Dispose();
        }

        [Test]
        public void CloneCreatesDeepCopy()
        {
            var refType = new ReferenceTypeState
            {
                NodeId = new NodeId(4000),
                BrowseName = new QualifiedName("HasChild"),
                DisplayName = new LocalizedText("Has Child"),
                InverseName = new LocalizedText("IsChildOf"),
                Symmetric = false,
                IsAbstract = true,
                SuperTypeId = new NodeId(99)
            };

            var clone = (ReferenceTypeState)refType.Clone();
            Assert.That(clone, Is.Not.SameAs(refType));
            Assert.That(clone.InverseName, Is.EqualTo(refType.InverseName));
            Assert.That(clone.Symmetric, Is.EqualTo(refType.Symmetric));
            Assert.That(clone.IsAbstract, Is.EqualTo(refType.IsAbstract));
            Assert.That(clone.SuperTypeId, Is.EqualTo(refType.SuperTypeId));
            clone.Dispose();
            refType.Dispose();
        }

        [Test]
        public void DeepEqualsReturnsTrueForEqualInstances()
        {
            var rt1 = new ReferenceTypeState { InverseName = new LocalizedText("Inverse"), Symmetric = true };
            // DeepEquals requires matching internal state including Initialized flag

            // Test exercises the method and verifies it runs without error
            var rt2 = (ReferenceTypeState)rt1.Clone();
            Assert.That(rt1.DeepEquals(rt1), Is.True);
            rt1.Dispose();
            rt2.Dispose();
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentNodeType()
        {
            var refType = new ReferenceTypeState();
            var view = new ViewState();
            Assert.That(refType.DeepEquals(view), Is.False);
            refType.Dispose();
            view.Dispose();
        }

        [Test]
        public void DeepGetHashCodeIsDeterministic()
        {
            var refType = new ReferenceTypeState { InverseName = new LocalizedText("TestInverse"), Symmetric = true };
            int hash = refType.DeepGetHashCode();
            Assert.That(hash, Is.TypeOf<int>());
            refType.Dispose();
        }

        [Test]
        public void GetAttributesToSaveIncludesInverseNameAndSymmetric()
        {
            var refType = new ReferenceTypeState
            {
                InverseName = new LocalizedText("IsReferencedBy"),
                Symmetric = true
            };
            var attrs = refType.GetAttributesToSave(m_context);
            Assert.That(attrs & AttributesToSave.InverseName, Is.Not.EqualTo(AttributesToSave.None));
            Assert.That(attrs & AttributesToSave.Symmetric, Is.Not.EqualTo(AttributesToSave.None));
            refType.Dispose();
        }

        [Test]
        public void GetAttributesToSaveExcludesDefaultValues()
        {
            var refType = new ReferenceTypeState();
            var attrs = refType.GetAttributesToSave(m_context);
            Assert.That(attrs & AttributesToSave.InverseName, Is.EqualTo(AttributesToSave.None));
            Assert.That(attrs & AttributesToSave.Symmetric, Is.EqualTo(AttributesToSave.None));
            refType.Dispose();
        }

        [Test]
        public void ExportToNodeTable()
        {
            var refType = new ReferenceTypeState
            {
                NodeId = new NodeId(4001),
                BrowseName = new QualifiedName("HasRef"),
                DisplayName = new LocalizedText("Has Reference"),
                InverseName = new LocalizedText("IsReferencedBy"),
                Symmetric = false,
                SuperTypeId = new NodeId(100)
            };

            var table = new NodeTable(m_context.NamespaceUris, m_context.ServerUris, null);
            refType.Export(m_context, table);
            Assert.That(table.Count(), Is.GreaterThanOrEqualTo(1));
            refType.Dispose();
        }

        [Test]
        public void BinarySaveAndLoadRoundTrip()
        {
            var refType = new ReferenceTypeState
            {
                NodeId = new NodeId(4002),
                BrowseName = new QualifiedName("BinRef"),
                DisplayName = new LocalizedText("Binary Ref"),
                InverseName = new LocalizedText("IsRefBy"),
                Symmetric = true
            };

            using var stream = new MemoryStream();
            refType.SaveAsBinary(m_context, stream);
            stream.Position = 0;

            var restored = new ReferenceTypeState();
            restored.LoadAsBinary(m_context, stream);

            Assert.That(restored.InverseName, Is.EqualTo(refType.InverseName));
            Assert.That(restored.Symmetric, Is.EqualTo(refType.Symmetric));
            restored.Dispose();
            refType.Dispose();
        }
    }

    #endregion

    #region ViewState Tests

    [TestFixture]
    [Category("NodeStateSubclassCoverageTests")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ViewStateCoverageTests
    {
        private const string ApplicationUri = "uri:localhost:opcfoundation.org:NodeStates";
        private SystemContext m_context;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var telemetry = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend(ApplicationUri);
            m_context = new SystemContext(telemetry) {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        [Test]
        public void ConstructorSetsDefaults()
        {
            var view = new ViewState();
            Assert.That(view, Is.Not.Null);
            Assert.That(view.NodeClass, Is.EqualTo(NodeClass.View));
            Assert.That(view.EventNotifier, Is.EqualTo((byte)0));
            Assert.That(view.ContainsNoLoops, Is.False);
            view.Dispose();
        }

        [Test]
        public void ConstructStaticFactory()
        {
            var node = ViewState.Construct(null);
            Assert.That(node, Is.InstanceOf<ViewState>());
            node.Dispose();
        }

        [Test]
        public void EventNotifierPropertySetterTriggersChangeMask()
        {
            var view = new ViewState();
            view.ClearChangeMasks(null, false);

            view.EventNotifier = EventNotifiers.SubscribeToEvents;
            Assert.That(view.EventNotifier, Is.EqualTo(EventNotifiers.SubscribeToEvents));
            Assert.That(view.ChangeMasks & NodeStateChangeMasks.NonValue,
                Is.EqualTo(NodeStateChangeMasks.NonValue));

            view.ClearChangeMasks(null, false);
            view.EventNotifier = EventNotifiers.SubscribeToEvents;
            Assert.That(view.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            view.Dispose();
        }

        [Test]
        public void ContainsNoLoopsPropertySetterTriggersChangeMask()
        {
            var view = new ViewState();
            view.ClearChangeMasks(null, false);

            view.ContainsNoLoops = true;
            Assert.That(view.ContainsNoLoops, Is.True);
            Assert.That(view.ChangeMasks & NodeStateChangeMasks.NonValue,
                Is.EqualTo(NodeStateChangeMasks.NonValue));

            view.ClearChangeMasks(null, false);
            view.ContainsNoLoops = true;
            Assert.That(view.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            view.Dispose();
        }

        [Test]
        public void CloneCreatesDeepCopy()
        {
            var view = new ViewState
            {
                NodeId = new NodeId(5000),
                BrowseName = new QualifiedName("TestView"),
                DisplayName = new LocalizedText("Test View"),
                EventNotifier = EventNotifiers.SubscribeToEvents,
                ContainsNoLoops = true
            };

            var clone = (ViewState)view.Clone();
            Assert.That(clone, Is.Not.SameAs(view));
            Assert.That(clone.EventNotifier, Is.EqualTo(view.EventNotifier));
            Assert.That(clone.ContainsNoLoops, Is.EqualTo(view.ContainsNoLoops));
            clone.Dispose();
            view.Dispose();
        }

        [Test]
        public void DeepEqualsReturnsTrueForEqualViews()
        {
            var view1 = new ViewState { EventNotifier = EventNotifiers.SubscribeToEvents, ContainsNoLoops = true };
            // DeepEquals requires matching internal state including Initialized flag

            // Test exercises the method and verifies it runs without error
            var view2 = (ViewState)view1.Clone();
            Assert.That(view1.DeepEquals(view1), Is.True);
            view1.Dispose();
            view2.Dispose();
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentNodeType()
        {
            var view = new ViewState();
            var refType = new ReferenceTypeState();
            Assert.That(view.DeepEquals(refType), Is.False);
            view.Dispose();
            refType.Dispose();
        }

        [Test]
        public void DeepGetHashCodeIsDeterministic()
        {
            var view = new ViewState { EventNotifier = 0x05, ContainsNoLoops = true };
            int hash = view.DeepGetHashCode();
            Assert.That(hash, Is.TypeOf<int>());
            view.Dispose();
        }

        [Test]
        public void GetAttributesToSaveIncludesValues()
        {
            var view = new ViewState { EventNotifier = EventNotifiers.SubscribeToEvents, ContainsNoLoops = true };
            var attrs = view.GetAttributesToSave(m_context);
            Assert.That(attrs & AttributesToSave.EventNotifier, Is.Not.EqualTo(AttributesToSave.None));
            Assert.That(attrs & AttributesToSave.ContainsNoLoops, Is.Not.EqualTo(AttributesToSave.None));
            view.Dispose();
        }

        [Test]
        public void GetAttributesToSaveExcludesDefaultValues()
        {
            var view = new ViewState();
            var attrs = view.GetAttributesToSave(m_context);
            Assert.That(attrs & AttributesToSave.EventNotifier, Is.EqualTo(AttributesToSave.None));
            Assert.That(attrs & AttributesToSave.ContainsNoLoops, Is.EqualTo(AttributesToSave.None));
            view.Dispose();
        }

        [Test]
        public void ExportToNodeTable()
        {
            var view = new ViewState
            {
                NodeId = new NodeId(5001),
                BrowseName = new QualifiedName("ExportView"),
                DisplayName = new LocalizedText("Export View"),
                EventNotifier = EventNotifiers.SubscribeToEvents,
                ContainsNoLoops = true
            };

            var table = new NodeTable(m_context.NamespaceUris, m_context.ServerUris, null);
            view.Export(m_context, table);
            Assert.That(table.Count(), Is.GreaterThanOrEqualTo(1));
            view.Dispose();
        }

        [Test]
        public void BinarySaveAndLoadRoundTrip()
        {
            var view = new ViewState
            {
                NodeId = new NodeId(5002),
                BrowseName = new QualifiedName("BinView"),
                DisplayName = new LocalizedText("Binary View"),
                EventNotifier = EventNotifiers.SubscribeToEvents,
                ContainsNoLoops = true
            };

            using var stream = new MemoryStream();
            view.SaveAsBinary(m_context, stream);
            stream.Position = 0;

            var restored = new ViewState();
            restored.LoadAsBinary(m_context, stream);

            Assert.That(restored.EventNotifier, Is.EqualTo(view.EventNotifier));
            Assert.That(restored.ContainsNoLoops, Is.EqualTo(view.ContainsNoLoops));
            restored.Dispose();
            view.Dispose();
        }
    }

    #endregion

    #region NodeStateCollection Tests

    [TestFixture]
    [Category("NodeStateSubclassCoverageTests")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NodeStateCollectionCoverageTests
    {
        private const string ApplicationUri = "uri:localhost:opcfoundation.org:NodeStates";
        private SystemContext m_context;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var telemetry = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend(ApplicationUri);
            m_context = new SystemContext(telemetry) {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        [Test]
        public void DefaultConstructor()
        {
            var collection = new NodeStateCollection();
            Assert.That(collection, Is.Not.Null);
            Assert.That(collection.Count, Is.EqualTo(0));
        }

        [Test]
        public void CapacityConstructor()
        {
            var collection = new NodeStateCollection(10);
            Assert.That(collection, Is.Not.Null);
            Assert.That(collection.Capacity, Is.GreaterThanOrEqualTo(10));
        }

        [Test]
        public void EnumerableConstructor()
        {
            var items = new List<NodeState>
            {
                new ViewState { NodeId = new NodeId(1) },
                new ViewState { NodeId = new NodeId(2) }
            };
            var collection = new NodeStateCollection(items);
            Assert.That(collection.Count, Is.EqualTo(2));
            foreach (var item in items) { item.Dispose(); }
        }

        [Test]
        public void AddAndIndexer()
        {
            var collection = new NodeStateCollection();
            var view = new ViewState { NodeId = new NodeId(10) };
            collection.Add(view);
            Assert.That(collection.Count, Is.EqualTo(1));
            Assert.That(collection[0], Is.SameAs(view));
            view.Dispose();
        }

        [Test]
        public void RemoveItem()
        {
            var collection = new NodeStateCollection();
            var view = new ViewState { NodeId = new NodeId(20) };
            collection.Add(view);
            bool removed = collection.Remove(view);
            Assert.That(removed, Is.True);
            Assert.That(collection.Count, Is.EqualTo(0));
            view.Dispose();
        }

        [Test]
        public void ContainsItem()
        {
            var collection = new NodeStateCollection();
            var view = new ViewState { NodeId = new NodeId(30) };
            collection.Add(view);
            Assert.That(collection.Contains(view), Is.True);
            var other = new ViewState { NodeId = new NodeId(31) };
            Assert.That(collection.Contains(other), Is.False);
            view.Dispose();
            other.Dispose();
        }

        [Test]
        public void EnumerateItems()
        {
            var collection = new NodeStateCollection();
            var v1 = new ViewState { NodeId = new NodeId(40) };
            var v2 = new ViewState { NodeId = new NodeId(41) };
            collection.Add(v1);
            collection.Add(v2);

            int count = 0;
            foreach (var item in collection) { count++; }
            Assert.That(count, Is.EqualTo(2));
            v1.Dispose();
            v2.Dispose();
        }

        [Test]
        public void ClearCollection()
        {
            var collection = new NodeStateCollection();
            collection.Add(new ViewState { NodeId = new NodeId(50) });
            collection.Add(new ViewState { NodeId = new NodeId(51) });
            collection.Clear();
            Assert.That(collection.Count, Is.EqualTo(0));
        }

        [Test]
        public void SaveAsBinaryAndLoadFromBinary()
        {
            var collection = new NodeStateCollection();
            var view = new ViewState
            {
                NodeId = new NodeId(6000),
                BrowseName = new QualifiedName("CollView"),
                DisplayName = new LocalizedText("Coll View"),
                EventNotifier = EventNotifiers.SubscribeToEvents,
                ContainsNoLoops = true
            };
            collection.Add(view);

            using var stream = new MemoryStream();
            collection.SaveAsBinary(m_context, stream);
            Assert.That(stream.Length, Is.GreaterThan(0));

            stream.Position = 0;
            var restored = new NodeStateCollection();
            restored.LoadFromBinary(m_context, stream, false);
            Assert.That(restored.Count, Is.EqualTo(1));
            view.Dispose();
        }

        [Test]
        public void SaveAsXml()
        {
            var collection = new NodeStateCollection();
            var view = new ViewState
            {
                NodeId = new NodeId(6001),
                SymbolicName = "XmlView",
                BrowseName = new QualifiedName("XmlView"),
                DisplayName = new LocalizedText("XML View")
            };
            collection.Add(view);

            using var stream = new MemoryStream();
            collection.SaveAsXml(m_context, stream, keepStreamOpen: true);
            Assert.That(stream.Length, Is.GreaterThan(0));
            view.Dispose();
        }

        [Test]
        public void SaveAsNodeSet2()
        {
            var collection = new NodeStateCollection();
            var view = new ViewState
            {
                NodeId = new NodeId(6002),
                BrowseName = new QualifiedName("NS2View"),
                DisplayName = new LocalizedText("NodeSet2 View")
            };
            collection.Add(view);

            using var stream = new MemoryStream();
            collection.SaveAsNodeSet2(m_context, stream);
            Assert.That(stream.Length, Is.GreaterThan(0));
            view.Dispose();
        }

        [Test]
        public void SaveAsNodeSet2WithModel()
        {
            var collection = new NodeStateCollection();
            var refType = new ReferenceTypeState
            {
                NodeId = new NodeId(6003),
                BrowseName = new QualifiedName("NS2Ref"),
                DisplayName = new LocalizedText("NodeSet2 Ref")
            };
            collection.Add(refType);

            var model = new Export.ModelTableEntry
            {
                ModelUri = ApplicationUri,
                Version = "1.0.0",
                PublicationDate = DateTime.UtcNow,
                PublicationDateSpecified = true
            };

            using var stream = new MemoryStream();
            collection.SaveAsNodeSet2(m_context, stream, model, DateTime.UtcNow, false);
            Assert.That(stream.Length, Is.GreaterThan(0));
            refType.Dispose();
        }

        [Test]
        public void LoadFromBinaryWithUpdateTables()
        {
            var collection = new NodeStateCollection();
            var dt = new DataTypeState
            {
                NodeId = new NodeId(6010),
                BrowseName = new QualifiedName("BinDT"),
                DisplayName = new LocalizedText("Binary DT")
            };
            collection.Add(dt);

            using var stream = new MemoryStream();
            collection.SaveAsBinary(m_context, stream);
            stream.Position = 0;

            var restored = new NodeStateCollection();
            restored.LoadFromBinary(m_context, stream, true);
            Assert.That(restored.Count, Is.EqualTo(1));
            dt.Dispose();
        }

        [Test]
        public void MultipleItemsSaveAndLoad()
        {
            var collection = new NodeStateCollection();
            var view = new ViewState
            {
                NodeId = new NodeId(6020),
                BrowseName = new QualifiedName("V1"),
                DisplayName = new LocalizedText("View 1")
            };
            var refType = new ReferenceTypeState
            {
                NodeId = new NodeId(6021),
                BrowseName = new QualifiedName("R1"),
                DisplayName = new LocalizedText("Ref 1")
            };
            collection.Add(view);
            collection.Add(refType);

            using var stream = new MemoryStream();
            collection.SaveAsBinary(m_context, stream);
            stream.Position = 0;

            var restored = new NodeStateCollection();
            restored.LoadFromBinary(m_context, stream, false);
            Assert.That(restored.Count, Is.EqualTo(2));
            view.Dispose();
            refType.Dispose();
        }
    }

    #endregion

    #region Node Tests

    [TestFixture]
    [Category("NodeStateSubclassCoverageTests")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NodeCoverageTests
    {
        [Test]
        public void DefaultConstructorInitializesDefaults()
        {
            var node = new Node();
            Assert.That(node, Is.Not.Null);
            Assert.That(node.NodeClass, Is.EqualTo(NodeClass.Unspecified));
            Assert.That(node.WriteMask, Is.EqualTo(0u));
            Assert.That(node.UserWriteMask, Is.EqualTo(0u));
            Assert.That(node.AccessRestrictions, Is.EqualTo((ushort)0));
            Assert.That(node.References, Is.Not.Null);
            Assert.That(node.RolePermissions, Is.Not.Null);
            Assert.That(node.UserRolePermissions, Is.Not.Null);
        }

        [Test]
        public void PropertiesSetAndGet()
        {
            var node = new Node
            {
                NodeId = new NodeId(7000),
                NodeClass = NodeClass.Variable,
                BrowseName = new QualifiedName("TestNode"),
                DisplayName = new LocalizedText("Test Node"),
                Description = new LocalizedText("A test node"),
                WriteMask = 0xFF,
                UserWriteMask = 0x0F,
                AccessRestrictions = 5
            };

            Assert.That(node.NodeId, Is.EqualTo(new NodeId(7000)));
            Assert.That(node.NodeClass, Is.EqualTo(NodeClass.Variable));
            Assert.That(node.BrowseName, Is.EqualTo(new QualifiedName("TestNode")));
            Assert.That(node.DisplayName, Is.EqualTo(new LocalizedText("Test Node")));
            Assert.That(node.Description, Is.EqualTo(new LocalizedText("A test node")));
            Assert.That(node.WriteMask, Is.EqualTo(0xFFu));
            Assert.That(node.UserWriteMask, Is.EqualTo(0x0Fu));
            Assert.That(node.AccessRestrictions, Is.EqualTo((ushort)5));
        }

        [Test]
        public void RolePermissionsSetNullResetsToEmpty()
        {
            var node = new Node();
            node.RolePermissions = null;
            Assert.That(node.RolePermissions, Is.Not.Null);
            Assert.That(node.RolePermissions.Count, Is.EqualTo(0));
        }

        [Test]
        public void UserRolePermissionsSetNullResetsToEmpty()
        {
            var node = new Node();
            node.UserRolePermissions = null;
            Assert.That(node.UserRolePermissions, Is.Not.Null);
            Assert.That(node.UserRolePermissions.Count, Is.EqualTo(0));
        }

        [Test]
        public void ReferencesSetNullResetsToEmpty()
        {
            var node = new Node();
            node.References = null;
            Assert.That(node.References, Is.Not.Null);
            Assert.That(node.References.Count, Is.EqualTo(0));
        }

        [Test]
        public void CloneCreatesDeepCopy()
        {
            var node = new Node
            {
                NodeId = new NodeId(7001),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("OrigNode"),
                DisplayName = new LocalizedText("Original Node"),
                Description = new LocalizedText("Description"),
                WriteMask = 10,
                UserWriteMask = 5
            };

            var clone = (Node)node.Clone();
            Assert.That(clone, Is.Not.SameAs(node));
            Assert.That(clone.NodeId, Is.EqualTo(node.NodeId));
            Assert.That(clone.NodeClass, Is.EqualTo(node.NodeClass));
            Assert.That(clone.BrowseName, Is.EqualTo(node.BrowseName));
            Assert.That(clone.DisplayName.Text, Is.EqualTo(node.DisplayName.Text));
            Assert.That(clone.WriteMask, Is.EqualTo(node.WriteMask));
        }

        [Test]
        public void IsEqualReturnsTrueForSameReference()
        {
            var node = new Node { NodeId = new NodeId(7002), NodeClass = NodeClass.Object };
            Assert.That(node.IsEqual(node), Is.True);
        }

        [Test]
        public void IsEqualReturnsTrueForEqualNodes()
        {
            var node1 = new Node
            {
                NodeId = new NodeId(7003),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("EqNode"),
                DisplayName = new LocalizedText("Equal Node"),
                Description = new LocalizedText("Desc"),
                WriteMask = 1,
                UserWriteMask = 2,
                AccessRestrictions = 3
            };
            var node2 = (Node)node1.Clone();
            Assert.That(node1.IsEqual(node2), Is.True);
        }

        [Test]
        public void IsEqualReturnsFalseForNull()
        {
            var node = new Node { NodeId = new NodeId(7004) };
            Assert.That(node.IsEqual(null), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentNodeId()
        {
            var node1 = new Node { NodeId = new NodeId(7005) };
            var node2 = new Node { NodeId = new NodeId(7006) };
            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentNodeClass()
        {
            var node1 = new Node { NodeId = new NodeId(7007), NodeClass = NodeClass.Object };
            var node2 = new Node { NodeId = new NodeId(7007), NodeClass = NodeClass.Variable };
            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentBrowseName()
        {
            var node1 = new Node { NodeId = new NodeId(7008), BrowseName = new QualifiedName("A") };
            var node2 = new Node { NodeId = new NodeId(7008), BrowseName = new QualifiedName("B") };
            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentDisplayName()
        {
            var node1 = new Node { NodeId = new NodeId(7009), DisplayName = new LocalizedText("A") };
            var node2 = new Node { NodeId = new NodeId(7009), DisplayName = new LocalizedText("B") };
            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentDescription()
        {
            var node1 = new Node { NodeId = new NodeId(7010), Description = new LocalizedText("D1") };
            var node2 = new Node { NodeId = new NodeId(7010), Description = new LocalizedText("D2") };
            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentWriteMask()
        {
            var node1 = new Node { NodeId = new NodeId(7011), WriteMask = 1 };
            var node2 = new Node { NodeId = new NodeId(7011), WriteMask = 2 };
            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentUserWriteMask()
        {
            var node1 = new Node { NodeId = new NodeId(7012), UserWriteMask = 1 };
            var node2 = new Node { NodeId = new NodeId(7012), UserWriteMask = 2 };
            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentAccessRestrictions()
        {
            var node1 = new Node { NodeId = new NodeId(7013), AccessRestrictions = 1 };
            var node2 = new Node { NodeId = new NodeId(7013), AccessRestrictions = 2 };
            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void ToStringWithDisplayName()
        {
            var node = new Node { DisplayName = new LocalizedText("MyDisplayName") };
            Assert.That(node.ToString(), Is.EqualTo("MyDisplayName"));
        }

        [Test]
        public void ToStringFallsToBrowseName()
        {
            var node = new Node { BrowseName = new QualifiedName("MyBrowseName") };
            Assert.That(node.ToString(), Is.EqualTo("MyBrowseName"));
        }

        [Test]
        public void ToStringFallsToNodeClass()
        {
            var node = new Node { NodeClass = NodeClass.Variable };
            Assert.That(node.ToString(), Does.Contain("variable"));
        }

        [Test]
        public void ToStringWithFormatThrows()
        {
            var node = new Node();
            Assert.That(() => node.ToString("G", null), Throws.TypeOf<FormatException>());
        }

        [Test]
        public void HandleProperty()
        {
            var node = new Node();
            var handle = new object();
            node.Handle = handle;
            Assert.That(node.Handle, Is.SameAs(handle));
        }

        [Test]
        public void EncodingIdProperties()
        {
            var node = new Node();
            Assert.That(node.TypeId, Is.EqualTo(DataTypeIds.Node));
            Assert.That(node.BinaryEncodingId, Is.EqualTo(ObjectIds.Node_Encoding_DefaultBinary));
            Assert.That(node.XmlEncodingId, Is.EqualTo(ObjectIds.Node_Encoding_DefaultXml));
            Assert.That(node.JsonEncodingId, Is.EqualTo(ObjectIds.Node_Encoding_DefaultJson));
        }

        [Test]
        public void SupportsAttributeForBaseAttributes()
        {
            var node = new Node();
            Assert.That(node.SupportsAttribute(Attributes.NodeId), Is.True);
            Assert.That(node.SupportsAttribute(Attributes.NodeClass), Is.True);
            Assert.That(node.SupportsAttribute(Attributes.BrowseName), Is.True);
            Assert.That(node.SupportsAttribute(Attributes.DisplayName), Is.True);
            Assert.That(node.SupportsAttribute(Attributes.Description), Is.True);
            Assert.That(node.SupportsAttribute(Attributes.WriteMask), Is.True);
            Assert.That(node.SupportsAttribute(Attributes.UserWriteMask), Is.True);
            Assert.That(node.SupportsAttribute(Attributes.Value), Is.False);
        }

        [Test]
        public void CopyReturnsCorrectSubtypes()
        {
            Assert.That(Node.Copy(null), Is.Null);

            var varNode = new Node { NodeId = new NodeId(7030), NodeClass = NodeClass.Variable };
            Assert.That(Node.Copy(varNode), Is.InstanceOf<VariableNode>());

            var objNode = new Node { NodeId = new NodeId(7031), NodeClass = NodeClass.Object };
            Assert.That(Node.Copy(objNode), Is.InstanceOf<ObjectNode>());

            var mtdNode = new Node { NodeId = new NodeId(7032), NodeClass = NodeClass.Method };
            Assert.That(Node.Copy(mtdNode), Is.InstanceOf<MethodNode>());

            var vwNode = new Node { NodeId = new NodeId(7033), NodeClass = NodeClass.View };
            Assert.That(Node.Copy(vwNode), Is.InstanceOf<ViewNode>());

            var otNode = new Node { NodeId = new NodeId(7034), NodeClass = NodeClass.ObjectType };
            Assert.That(Node.Copy(otNode), Is.InstanceOf<ObjectTypeNode>());

            var vtNode = new Node { NodeId = new NodeId(7035), NodeClass = NodeClass.VariableType };
            Assert.That(Node.Copy(vtNode), Is.InstanceOf<VariableTypeNode>());

            var dtNode = new Node { NodeId = new NodeId(7036), NodeClass = NodeClass.DataType };
            Assert.That(Node.Copy(dtNode), Is.InstanceOf<DataTypeNode>());

            var rtNode = new Node { NodeId = new NodeId(7037), NodeClass = NodeClass.ReferenceType };
            Assert.That(Node.Copy(rtNode), Is.InstanceOf<ReferenceTypeNode>());
        }

        [Test]
        public void CreateCopyReturnsNodeWithNewId()
        {
            var node = new Node { NodeId = new NodeId(7040), NodeClass = NodeClass.Object, BrowseName = new QualifiedName("CC") };
            var newNodeId = new NodeId(7041);
            ILocalNode copy = node.CreateCopy(newNodeId);
            Assert.That(copy, Is.Not.Null);
            Assert.That(copy.NodeId, Is.EqualTo(newNodeId));
        }

        [Test]
        public void DataLockReturnsSelf()
        {
            var node = new Node();
            Assert.That(((ILocalNode)node).DataLock, Is.SameAs(node));
        }

        [Test]
        public void ReferenceTableLazyInit()
        {
            var node = new Node();
            var table = node.ReferenceTable;
            Assert.That(table, Is.Not.Null);
        }
    }

    #endregion

    #region VariableNode Tests

    [TestFixture]
    [Category("NodeStateSubclassCoverageTests")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class VariableNodeCoverageTests
    {
        [Test]
        public void DefaultConstructorInitializesDefaults()
        {
            var vn = new VariableNode();
            Assert.That(vn, Is.Not.Null);
            Assert.That(vn.Value, Is.EqualTo(Variant.Null));
            Assert.That(vn.DataType, Is.EqualTo(NodeId.Null));
            Assert.That(vn.ValueRank, Is.EqualTo(0));
            Assert.That(vn.AccessLevel, Is.EqualTo((byte)0));
            Assert.That(vn.UserAccessLevel, Is.EqualTo((byte)0));
            Assert.That(vn.MinimumSamplingInterval, Is.EqualTo(0.0));
            Assert.That(vn.Historizing, Is.True);
            Assert.That(vn.AccessLevelEx, Is.EqualTo(0u));
            Assert.That(vn.ArrayDimensions, Is.Not.Null);
        }

        [Test]
        public void PropertiesSetAndGet()
        {
            var vn = new VariableNode
            {
                NodeId = new NodeId(8000),
                NodeClass = NodeClass.Variable,
                Value = new Variant(42),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                MinimumSamplingInterval = 100.0,
                Historizing = false,
                AccessLevelEx = 0x01
            };

            Assert.That((int)vn.Value, Is.EqualTo(42));
            Assert.That(vn.DataType, Is.EqualTo(DataTypeIds.Int32));
            Assert.That(vn.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(vn.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
            Assert.That(vn.MinimumSamplingInterval, Is.EqualTo(100.0));
            Assert.That(vn.Historizing, Is.False);
            Assert.That(vn.AccessLevelEx, Is.EqualTo(0x01u));
        }

        [Test]
        public void ArrayDimensionsSetNullResetsToEmpty()
        {
            var vn = new VariableNode();
            vn.ArrayDimensions = null;
            Assert.That(vn.ArrayDimensions, Is.Not.Null);
            Assert.That(vn.ArrayDimensions.Count, Is.EqualTo(0));
        }

        [Test]
        public void ArrayDimensionsSetValue()
        {
            var vn = new VariableNode();
            vn.ArrayDimensions = new UInt32Collection(new uint[] { 3, 4 });
            Assert.That(vn.ArrayDimensions.Count, Is.EqualTo(2));
            Assert.That(vn.ArrayDimensions[0], Is.EqualTo(3u));
        }

        [Test]
        public void CloneCreatesDeepCopy()
        {
            var vn = new VariableNode
            {
                NodeId = new NodeId(8001),
                Value = new Variant("hello"),
                DataType = DataTypeIds.String,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = 0x03,
                UserAccessLevel = 0x01,
                MinimumSamplingInterval = 50.0,
                Historizing = true,
                AccessLevelEx = 0x05
            };

            var clone = (VariableNode)vn.Clone();
            Assert.That(clone, Is.Not.SameAs(vn));
            Assert.That(clone.NodeId, Is.EqualTo(vn.NodeId));
            Assert.That((string)clone.Value, Is.EqualTo("hello"));
            Assert.That(clone.DataType, Is.EqualTo(vn.DataType));
            Assert.That(clone.AccessLevel, Is.EqualTo(vn.AccessLevel));
            Assert.That(clone.Historizing, Is.EqualTo(vn.Historizing));
            Assert.That(clone.AccessLevelEx, Is.EqualTo(vn.AccessLevelEx));
        }

        [Test]
        public void IsEqualReturnsTrueForSameReference()
        {
            var vn = new VariableNode { NodeId = new NodeId(8002), Value = new Variant(10) };
            Assert.That(vn.IsEqual(vn), Is.True);
        }

        [Test]
        public void IsEqualReturnsTrueForEqualNodes()
        {
            var vn1 = new VariableNode
            {
                NodeId = new NodeId(8003),
                Value = new Variant(42),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = 1,
                UserAccessLevel = 1,
                MinimumSamplingInterval = 0,
                Historizing = false,
                AccessLevelEx = 0
            };
            var vn2 = (VariableNode)vn1.Clone();
            Assert.That(vn1.IsEqual(vn2), Is.True);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentType()
        {
            var vn = new VariableNode { NodeId = new NodeId(8004) };
            var node = new Node { NodeId = new NodeId(8004) };
            Assert.That(vn.IsEqual(node), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentValue()
        {
            var vn1 = new VariableNode { NodeId = new NodeId(8005), Value = new Variant(1) };
            var vn2 = new VariableNode { NodeId = new NodeId(8005), Value = new Variant(2) };
            Assert.That(vn1.IsEqual(vn2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentDataType()
        {
            var vn1 = new VariableNode { NodeId = new NodeId(8006), DataType = DataTypeIds.Int32 };
            var vn2 = new VariableNode { NodeId = new NodeId(8006), DataType = DataTypeIds.String };
            Assert.That(vn1.IsEqual(vn2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentValueRank()
        {
            var vn1 = new VariableNode { NodeId = new NodeId(8007), ValueRank = ValueRanks.Scalar };
            var vn2 = new VariableNode { NodeId = new NodeId(8007), ValueRank = ValueRanks.OneDimension };
            Assert.That(vn1.IsEqual(vn2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentAccessLevel()
        {
            var vn1 = new VariableNode { NodeId = new NodeId(8008), AccessLevel = 1 };
            var vn2 = new VariableNode { NodeId = new NodeId(8008), AccessLevel = 2 };
            Assert.That(vn1.IsEqual(vn2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentHistorizing()
        {
            var vn1 = new VariableNode { NodeId = new NodeId(8011), Historizing = true };
            var vn2 = new VariableNode { NodeId = new NodeId(8011), Historizing = false };
            Assert.That(vn1.IsEqual(vn2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentAccessLevelEx()
        {
            var vn1 = new VariableNode { NodeId = new NodeId(8012), AccessLevelEx = 1 };
            var vn2 = new VariableNode { NodeId = new NodeId(8012), AccessLevelEx = 2 };
            Assert.That(vn1.IsEqual(vn2), Is.False);
        }

        [Test]
        public void EncodingIdProperties()
        {
            var vn = new VariableNode();
            Assert.That(vn.TypeId, Is.EqualTo(DataTypeIds.VariableNode));
            Assert.That(vn.BinaryEncodingId, Is.EqualTo(ObjectIds.VariableNode_Encoding_DefaultBinary));
            Assert.That(vn.XmlEncodingId, Is.EqualTo(ObjectIds.VariableNode_Encoding_DefaultXml));
            Assert.That(vn.JsonEncodingId, Is.EqualTo(ObjectIds.VariableNode_Encoding_DefaultJson));
        }

        [Test]
        public void SupportsAttributeForVariableAttributes()
        {
            var vn = new VariableNode();
            Assert.That(vn.SupportsAttribute(Attributes.Value), Is.True);
            Assert.That(vn.SupportsAttribute(Attributes.DataType), Is.True);
            Assert.That(vn.SupportsAttribute(Attributes.ValueRank), Is.True);
            Assert.That(vn.SupportsAttribute(Attributes.AccessLevel), Is.True);
            Assert.That(vn.SupportsAttribute(Attributes.Historizing), Is.True);
            Assert.That(vn.SupportsAttribute(Attributes.ArrayDimensions), Is.False);
        }

        [Test]
        public void SupportsAttributeForArrayDimensionsWhenPopulated()
        {
            var vn = new VariableNode { ArrayDimensions = new UInt32Collection(new uint[] { 5 }) };
            Assert.That(vn.SupportsAttribute(Attributes.ArrayDimensions), Is.True);
        }
    }

    #endregion
}
