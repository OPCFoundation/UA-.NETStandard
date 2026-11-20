// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Tests for the NodeState classes.
    /// </summary>
    [TestFixture]
    [Category("NodeState")]
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
            Context = ServiceMessageContext.CreateEmpty(Telemetry);
            NamespaceTable nameSpaceUris = Context.NamespaceUris;
            // namespace index 1 must be the ApplicationUri
            nameSpaceUris.GetIndexOrAppend(ApplicationUri);
            nameSpaceUris.GetIndexOrAppend("http://opcfoundation.org/UA/GDS/");
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
                    instance = Activator.CreateInstance(systemType, (NodeState)null!)!;
                }
                else if (systemType.IsAbstract)
                {
                    instance = null!;
                }
                else
                {
                    ConstructorInfo? defaultConstructor = systemType.GetConstructor([]);
                    if (defaultConstructor == null || !defaultConstructor.IsPublic)
                    {
                        instance = null!;
                    }
                    else
                    {
                        instance = Activator.CreateInstance(systemType)!;
                    }
                }
            }
            catch
            {
                return null!;
            }
            return instance!;
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
    }
}
