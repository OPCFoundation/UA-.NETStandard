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
using Assert = NUnit.Framework.Legacy.ClassicAssert;

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
            Context = new ServiceMessageContext(Telemetry);
            NamespaceTable nameSpaceUris = Context.NamespaceUris;
            // namespace index 1 must be the ApplicationUri
            nameSpaceUris.GetIndexOrAppend(ApplicationUri);
            nameSpaceUris.GetIndexOrAppend(Namespaces.OpcUaGds);
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            Utils.SilentDispose(Context);
        }

        /// <summary>
        /// Verify activation of a NodeState type.
        /// </summary>
        [Theory]
        public void ActivateNodeStateType(Type systemType)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var testObject = CreateDefaultNodeStateType(systemType) as NodeState;
            Assert.NotNull(testObject);
            var context = new SystemContext(telemetry) { NamespaceUris = Context.NamespaceUris };
            Assert.AreEqual(0, context.NamespaceUris.GetIndexOrAppend(OpcUa));
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
            Type[] nodeStateTypesToScan = [.. GetOpcUaNodeStateTypes().OrderBy(type => type.FullName)];

            foreach (Type systemType in nodeStateTypesToScan)
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
            var parent = new BaseObjectState(null);
            var eventState = new BaseEventState(parent);

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
            var eventState = new BaseEventState(null);

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
            var parent = new BaseObjectState(null);
            var alarmState = new NonExclusiveLimitAlarmState(parent);

            var clone = (NonExclusiveLimitAlarmState)alarmState.Clone();

            Assert.That(clone, Is.Not.Null);
            Assert.That(clone.Parent, Is.SameAs(parent));
        }
    }
}
