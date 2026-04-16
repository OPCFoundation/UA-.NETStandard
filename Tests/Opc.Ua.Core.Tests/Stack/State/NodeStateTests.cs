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
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.State
{
    /// <summary>
    /// Tests for the NodeState classes.
    /// </summary>
    [TestFixture]
    [Category("NodeStateTypes")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class StateTypesTests
    {
        public const string ApplicationUri = "uri:localhost:opcfoundation.org:NodeStates";
        public const string OpcUa = "http://opcfoundation.org/UA/";
        public IServiceMessageContext Context;
        public ITelemetryContext Telemetry;

        [DatapointSource]
        public Type[] TypeArray = [.. typeof(BaseObjectState).Assembly.GetExportedTypes()
            .Where(IsNodeStateType)];

        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            Telemetry = NUnitTelemetryContext.Create();
            Context = ServiceMessageContext.Create(Telemetry);
            NamespaceTable nameSpaceUris = Context.NamespaceUris;
            // namespace index 1 must be the ApplicationUri
            nameSpaceUris.GetIndexOrAppend(ApplicationUri);
            nameSpaceUris.GetIndexOrAppend(Namespaces.OpcUaGds);
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            (Context as IDisposable)?.Dispose();
        }

        /// <summary>
        /// Verify activation of a NodeState type.
        /// </summary>
        [Theory]
        public void ActivateNodeStateType(Type systemType)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var testObject = CreateDefaultNodeStateType(systemType) as NodeState;
            Assert.That(testObject, Is.Not.Null);
            var context = new SystemContext(telemetry) { NamespaceUris = Context.NamespaceUris };
            Assert.That(context.NamespaceUris.GetIndexOrAppend(OpcUa), Is.Zero);
            testObject.Create(context, new NodeId(1000), QualifiedName.From("Name"), LocalizedText.From("DisplayName"), true);
            testObject.Dispose();
        }

        /// <summary>
        /// Instantiate NodeState types across Opc.Ua assemblies and fail on placeholder children.
        /// </summary>
        [Test]
        public void NodeStateTypesAcrossOpcUaAssemblies_ShouldNotInstantiatePlaceholderChildren()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = new SystemContext(telemetry) { NamespaceUris = Context.NamespaceUris };
            var placeholders = new List<string>();
            uint nodeId = 200000;
            foreach (Type systemType in GetOpcUaNodeStateTypes().OrderBy(type => type.FullName))
            {
                if (CreateDefaultNodeStateType(systemType) is not NodeState testObject)
                {
                    continue;
                }

                try
                {
                    testObject.Create(context, new NodeId(nodeId++), QualifiedName.From("Name"), LocalizedText.From("DisplayName"), true);
                    CollectInstantiatedPlaceholders(
                        context,
                        testObject,
                        systemType.Assembly.GetName().Name,
                        systemType.FullName,
                        placeholders);
                }
                finally
                {
                    testObject.Dispose();
                }
            }

            Assert.That(
                placeholders,
                Is.Empty,
                "Instantiated placeholder children were found:" + Environment.NewLine + string.Join(Environment.NewLine, placeholders));
        }

        /// <summary>
        /// Create an instance of a NodeState type with default values.
        /// </summary>
        /// <param name="systemType">The type to create</param>
        private static object CreateDefaultNodeStateType(Type systemType)
        {
            System.Reflection.TypeInfo systemTypeInfo = systemType.GetTypeInfo();
            object instance;
            try
            {
                if (typeof(BaseObjectState).GetTypeInfo().IsAssignableFrom(systemTypeInfo) ||
                    typeof(BaseVariableState).GetTypeInfo().IsAssignableFrom(systemTypeInfo) ||
                    typeof(MethodState).GetTypeInfo().IsAssignableFrom(systemTypeInfo))
                {
                    instance = Activator.CreateInstance(systemType, (NodeState)null);
                }
                else if (systemType.IsAbstract)
                {
                    instance = null;
                }
                else
                {
                    ConstructorInfo defaultConstructor = systemType.GetConstructor([]);
                    if (defaultConstructor == null || !defaultConstructor.IsPublic)
                    {
                        instance = null;
                    }
                    else
                    {
                        instance = Activator.CreateInstance(systemType);
                    }
                }
            }
            catch
            {
                return null;
            }
            return instance;
        }

        /// <summary>
        /// Return true if system Type is IEncodeable.
        /// </summary>
        private static bool IsNodeStateType(Type systemType)
        {
            if (systemType == null)
            {
                return false;
            }

            System.Reflection.TypeInfo systemTypeInfo = systemType.GetTypeInfo();
            if (systemTypeInfo.IsAbstract ||
                systemTypeInfo.IsGenericType ||
                systemTypeInfo.IsGenericTypeDefinition ||
                !typeof(NodeState).GetTypeInfo().IsAssignableFrom(systemTypeInfo))
            {
                return false;
            }

            return CreateDefaultNodeStateType(systemType) is NodeState;
        }

        /// <summary>
        /// Recursively collect instantiated placeholder children for diagnostics.
        /// </summary>
        private static void CollectInstantiatedPlaceholders(
            ISystemContext context,
            NodeState nodeState,
            string ownerAssembly,
            string ownerType,
            List<string> placeholders)
        {
            var children = new List<BaseInstanceState>();
            nodeState.GetChildren(context, children);

            foreach (BaseInstanceState child in children)
            {
                string browseName = child.BrowseName.Name ?? string.Empty;
                bool hasPlaceholderName =
                    browseName.Length > 1 &&
                    browseName[0] == '<' &&
                    browseName[^1] == '>';

                bool hasPlaceholderModellingRule =
                    child.ModellingRuleId == ObjectIds.ModellingRule_OptionalPlaceholder ||
                    child.ModellingRuleId == ObjectIds.ModellingRule_MandatoryPlaceholder;

                if (hasPlaceholderName || hasPlaceholderModellingRule)
                {
                    string modellingRule = child.ModellingRuleId.ToString();
                    placeholders.Add(
                        $"{ownerAssembly}: {ownerType}: {child.GetDisplayPath()} (BrowseName='{browseName}', ModellingRuleId='{modellingRule}')");
                }

                CollectInstantiatedPlaceholders(
                    context,
                    child,
                    ownerAssembly,
                    ownerType,
                    placeholders);
            }
        }

        /// <summary>
        /// Discover loadable public NodeState types from reachable Opc.Ua assemblies.
        /// </summary>
        private static IEnumerable<Type> GetOpcUaNodeStateTypes()
        {
            var assemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
            var toScan = new Queue<Assembly>();

            void TryEnqueueAssembly(Assembly assembly)
            {
                if (assembly == null || assembly.IsDynamic)
                {
                    return;
                }

                AssemblyName assemblyName = assembly.GetName();

                if (!assemblyName.Name.StartsWith("Opc.Ua", StringComparison.Ordinal))
                {
                    return;
                }

                if (assemblies.ContainsKey(assembly.FullName))
                {
                    return;
                }

                assemblies[assembly.FullName] = assembly;
                toScan.Enqueue(assembly);
            }

            TryEnqueueAssembly(typeof(NodeState).Assembly);
            TryEnqueueAssembly(typeof(OrderedListState).Assembly);
            TryEnqueueAssembly(typeof(StateTypesTests).Assembly);

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                TryEnqueueAssembly(assembly);
            }

            while (toScan.Count > 0)
            {
                Assembly assembly = toScan.Dequeue();

                foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
                {
                    if (!reference.Name.StartsWith("Opc.Ua", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    try
                    {
                        TryEnqueueAssembly(Assembly.Load(reference));
                    }
                    catch
                    {
                        // Nothing we can do if an assembly fails to load, just skip it.
                    }
                }
            }

            return assemblies.Values
                .SelectMany(GetExportedTypesSafe)
                .Where(IsNodeStateType)
                .GroupBy(type => type.AssemblyQualifiedName)
                .Select(group => group.First());
        }

        /// <summary>
        /// Return exported types while tolerating partial type-load failures.
        /// </summary>
        private static IEnumerable<Type> GetExportedTypesSafe(Assembly assembly)
        {
            try
            {
                return assembly.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                // Continue with loadable public types if some types in the assembly fail to load.
                return e.Types.Where(type => type != null && type.IsPublic);
            }
        }
    }

    /// <summary>
    /// Tests for BaseEventState.MemberwiseClone.
    /// </summary>
    [TestFixture]
    [Category("BaseEventState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BaseEventStateTests
    {
        /// <summary>
        /// Verify that Clone works correctly for BaseEventState.
        /// </summary>
        [Test]
        public void CloneBaseEventStateSucceeds()
        {
            using var parent = new BaseObjectState(null);
            using var eventState = new BaseEventState(parent);

            var clone = (BaseEventState)eventState.Clone();

            Assert.That(clone, Is.Not.Null);
            Assert.That(clone.Parent, Is.SameAs(parent));
        }

        /// <summary>
        /// Verify that Clone works correctly for BaseEventState with null parent.
        /// </summary>
        [Test]
        public void CloneBaseEventStateWithNullParentSucceeds()
        {
            using var eventState = new BaseEventState(null);

            var clone = (BaseEventState)eventState.Clone();

            Assert.That(clone, Is.Not.Null);
            Assert.That(clone.Parent, Is.Null);
        }

        /// <summary>
        /// Verify that Clone works correctly for derived event types.
        /// </summary>
        [Test]
        public void CloneNonExclusiveLimitAlarmStateSucceeds()
        {
            using var parent = new BaseObjectState(null);
            using var alarmState = new NonExclusiveLimitAlarmState(parent);

            var clone = (NonExclusiveLimitAlarmState)alarmState.Clone();

            Assert.That(clone, Is.Not.Null);
            Assert.That(clone.Parent, Is.SameAs(parent));
        }

        [Test]
        public void BaseObjectStateConstructorSetsNodeClass()
        {
            using var node = new BaseObjectState(null);
            Assert.That(node.NodeClass, Is.EqualTo(NodeClass.Object));
        }

        [Test]
        public void FolderStateConstructorSetsNodeClass()
        {
            using var node = new FolderState(null);
            Assert.That(node.NodeClass, Is.EqualTo(NodeClass.Object));
        }

        [Test]
        public void BaseVariableStateConstructorSetsNodeClass()
        {
            using var node = new BaseDataVariableState(null);
            Assert.That(node.NodeClass, Is.EqualTo(NodeClass.Variable));
        }

        [Test]
        public void MethodStateConstructorSetsNodeClass()
        {
            using var node = new MethodState(null);
            Assert.That(node.NodeClass, Is.EqualTo(NodeClass.Method));
        }

        [Test]
        public void NodeIdCanBeSetAndRetrieved()
        {
            using var node = new BaseObjectState(null)
            {
                NodeId = new NodeId(1234, 2)
            };
            Assert.That(node.NodeId, Is.EqualTo(new NodeId(1234, 2)));
        }

        [Test]
        public void BrowseNameCanBeSetAndRetrieved()
        {
            using var node = new BaseObjectState(null)
            {
                BrowseName = new QualifiedName("TestObject", 2)
            };
            Assert.That(node.BrowseName.Name, Is.EqualTo("TestObject"));
            Assert.That(node.BrowseName.NamespaceIndex, Is.EqualTo(2));
        }

        [Test]
        public void DisplayNameCanBeSetAndRetrieved()
        {
            using var node = new BaseObjectState(null)
            {
                DisplayName = new LocalizedText("en", "Test Display")
            };
            Assert.That(node.DisplayName.Text, Is.EqualTo("Test Display"));
        }

        [Test]
        public void DescriptionCanBeSetAndRetrieved()
        {
            using var node = new BaseObjectState(null)
            {
                Description = new LocalizedText("en", "A test node")
            };
            Assert.That(node.Description.Text, Is.EqualTo("A test node"));
        }

        [Test]
        public void AddChildAddsToChildren()
        {
            using var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId(1, 0),
                BrowseName = new QualifiedName("Parent")
            };
            using var child = new BaseObjectState(parent)
            {
                NodeId = new NodeId(2, 0),
                BrowseName = new QualifiedName("Child")
            };
            parent.AddChild(child);

            List<BaseInstanceState> children = [];
            parent.GetChildren(null, children);
            Assert.That(children, Has.Count.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void AddReferenceCreatesReference()
        {
            using var node = new BaseObjectState(null)
            {
                NodeId = new NodeId(100, 0),
                BrowseName = new QualifiedName("Source")
            };
            node.AddReference(
                ReferenceTypeIds.Organizes,
                false,
                new NodeId(200, 0));

            // Verify references exist
            List<IReference> references = [];
            node.GetReferences(null, references);
            Assert.That(references, Has.Count.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void AddMultipleReferences()
        {
            using var node = new BaseObjectState(null)
            {
                NodeId = new NodeId(100, 0),
                BrowseName = new QualifiedName("Source")
            };
            node.AddReference(ReferenceTypeIds.Organizes, false, new NodeId(200, 0));
            node.AddReference(ReferenceTypeIds.HasComponent, false, new NodeId(201, 0));

            List<IReference> references = [];
            node.GetReferences(null, references);
            Assert.That(references, Has.Count.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void MethodStateExecutableDefault()
        {
            using var method = new MethodState(null);
            Assert.That(method.Executable, Is.True);
        }

        [Test]
        public void MethodStateUserExecutableDefault()
        {
            using var method = new MethodState(null);
            Assert.That(method.UserExecutable, Is.True);
        }

        [Test]
        public void MethodStateExecutableCanBeSet()
        {
            using var method = new MethodState(null)
            {
                Executable = false
            };
            Assert.That(method.Executable, Is.False);
        }

        [Test]
        public void MethodStateUserExecutableCanBeSet()
        {
            using var method = new MethodState(null)
            {
                UserExecutable = false
            };
            Assert.That(method.UserExecutable, Is.False);
        }

        [Test]
        public void MethodStateConstructCreatesNewInstance()
        {
            using var parent = new BaseObjectState(null);
            using NodeState method = MethodState.Construct(parent);
            Assert.That(method, Is.Not.Null);
            Assert.That(method.NodeClass, Is.EqualTo(NodeClass.Method));
        }

        [Test]
        public void MethodStateDeepEqualsIdentical()
        {
            using var m1 = new MethodState(null)
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Method1"),
                Executable = true,
                UserExecutable = true
            };
            using var m2 = new MethodState(null)
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
            using var m1 = new MethodState(null)
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Method1"),
                Executable = true
            };
            using var m2 = new MethodState(null)
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
            using var method = new MethodState(null);
            Assert.That(method.InputArguments, Is.Null);
        }

        [Test]
        public void MethodStateDeepGetHashCodeReturnsValue()
        {
            using var method = new MethodState(null)
            {
                NodeId = new NodeId(1),
                BrowseName = new QualifiedName("Test")
            };
            int hash = method.DeepGetHashCode();
            Assert.That(hash, Is.Not.Zero);
        }

        [Test]
        public void BaseDataVariableStateValueCanBeSet()
        {
            using var variable = new BaseDataVariableState(null)
            {
                Value = 42
            };
            Assert.That(variable.Value, Is.EqualTo(42));
        }

        [Test]
        public void BaseDataVariableStateDataTypeCanBeSet()
        {
            using var variable = new BaseDataVariableState(null)
            {
                DataType = DataTypeIds.Int32
            };
            Assert.That(variable.DataType, Is.EqualTo(DataTypeIds.Int32));
        }

        [Test]
        public void BaseDataVariableStateValueRankCanBeSet()
        {
            using var variable = new BaseDataVariableState(null)
            {
                ValueRank = ValueRanks.OneDimension
            };
            Assert.That(variable.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        [Test]
        public void BaseDataVariableStateAccessLevelCanBeSet()
        {
            using var variable = new BaseDataVariableState(null)
            {
                AccessLevel = AccessLevels.CurrentRead
            };
            Assert.That(variable.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
        }

        [Test]
        public void BaseDataVariableStateUserAccessLevelCanBeSet()
        {
            using var variable = new BaseDataVariableState(null)
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
            using var variable = new BaseDataVariableState(null)
            {
                Historizing = true
            };
            Assert.That(variable.Historizing, Is.True);
        }

        [Test]
        public void BaseDataVariableStateMinimumSamplingInterval()
        {
            using var variable = new BaseDataVariableState(null)
            {
                MinimumSamplingInterval = 500.0
            };
            Assert.That(variable.MinimumSamplingInterval, Is.EqualTo(500.0));
        }

        [Test]
        public void FolderStateAddChildVariable()
        {
            using var folder = new FolderState(null)
            {
                NodeId = new NodeId(1, 0),
                BrowseName = new QualifiedName("MyFolder")
            };
            using var variable = new BaseDataVariableState(folder)
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
            using var node = new BaseObjectState(null);
            Assert.That(node.Initialized, Is.False);
        }

        [Test]
        public void MethodStateMethodDeclarationIdCanBeSet()
        {
            using var method = new MethodState(null)
            {
                MethodDeclarationId = new NodeId(999)
            };
            Assert.That(method.MethodDeclarationId, Is.EqualTo(new NodeId(999)));
        }

        [Test]
        public void MethodStateOnCallMethodHandlerCanBeAssigned()
        {
            using var method = new MethodState(null)
            {
                OnCallMethod = (context, methodState, inputArgs, outputArgs) =>
                {
                    return ServiceResult.Good;
                }
            };
            Assert.That(method.OnCallMethod, Is.Not.Null);
        }

        [Test]
        public void MethodStateOnCallMethod2HandlerCanBeAssigned()
        {
            using var method = new MethodState(null)
            {
                OnCallMethod2 = (context, methodToCall, objectId, inputArgs, outputArgs) =>
                {
                    return ServiceResult.Good;
                }
            };
            Assert.That(method.OnCallMethod2, Is.Not.Null);
        }

        [Test]
        public void BaseDataVariableStateCopyPolicyDefault()
        {
            using var variable = new BaseDataVariableState(null);
            Assert.That(
                variable.CopyPolicy,
                Is.EqualTo(VariableCopyPolicy.CopyOnRead));
        }

        [Test]
        public void BaseDataVariableStateCopyPolicyCanBeSet()
        {
            using var variable = new BaseDataVariableState(null)
            {
                CopyPolicy = VariableCopyPolicy.Never
            };
            Assert.That(variable.CopyPolicy, Is.EqualTo(VariableCopyPolicy.Never));
        }
    }
}
