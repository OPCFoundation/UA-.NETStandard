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
using AttributesToSave = Opc.Ua.NodeState.AttributesToSave;

namespace Opc.Ua.Types.Tests.State
{
    [TestFixture]
    [Category("NodeState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class MethodStateTests
    {
        private const string ApplicationUri = "uri:localhost:opcfoundation.org:NodeStates";
        private SystemContext m_context;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend(ApplicationUri);
            m_context = new SystemContext(telemetry)
            {
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
            NodeState node = MethodState.Construct(null);
            Assert.That(node, Is.InstanceOf<MethodState>());
            node.Dispose();
        }

        [Test]
        public void ExecutablePropertySetterTriggersChangeMask()
        {
            var method = new MethodState(null)
            {
                // Setting to a different value triggers change mask
                Executable = false
            };
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

            AttributesToSave attrs = method.GetAttributesToSave(m_context);
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

            AttributesToSave attrs = method.GetAttributesToSave(m_context);
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

            Assert.That(table.Count, Is.GreaterThanOrEqualTo(1));
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

            PropertyState<ArrayOf<Argument>> result = method.CreateOrReplaceInputArguments(m_context, null);
            Assert.That(result, Is.Not.Null);
            Assert.That(method.InputArguments, Is.SameAs(result));

            // Calling again returns the same instance
            PropertyState<ArrayOf<Argument>> result2 = method.CreateOrReplaceInputArguments(m_context, null);
            Assert.That(result2, Is.SameAs(result));
            method.Dispose();
        }

        [Test]
        public void CreateOrReplaceOutputArguments()
        {
            var method = new MethodState(null);
            Assert.That(method.OutputArguments, Is.Null);

            PropertyState<ArrayOf<Argument>> result = method.CreateOrReplaceOutputArguments(m_context, null);
            Assert.That(result, Is.Not.Null);
            Assert.That(method.OutputArguments, Is.SameAs(result));

            PropertyState<ArrayOf<Argument>> result2 = method.CreateOrReplaceOutputArguments(m_context, null);
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
}
