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

using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Benchmarks for <see cref="NodeState.ReadAttributes"/>.
    /// </summary>
    [TestFixture]
    [Category("NodeStateReadAttributes")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    [MemoryDiagnoser]
    public class NodeStateReadAttributesBenchmarks
    {
        private static readonly uint[] s_commonAttributeIds =
        [
            Attributes.NodeId,
            Attributes.NodeClass,
            Attributes.BrowseName,
            Attributes.DisplayName,
            Attributes.Description,
            Attributes.WriteMask,
            Attributes.UserWriteMask
        ];

        private static readonly uint[] s_allNonValueAttributeIds =
        [
            Attributes.NodeId,
            Attributes.NodeClass,
            Attributes.BrowseName,
            Attributes.DisplayName,
            Attributes.Description,
            Attributes.WriteMask,
            Attributes.UserWriteMask,
            Attributes.RolePermissions,
            Attributes.UserRolePermissions,
            Attributes.AccessRestrictions
        ];

        private static readonly uint[] s_variableAttributeIds =
        [
            Attributes.NodeId,
            Attributes.NodeClass,
            Attributes.BrowseName,
            Attributes.DisplayName,
            Attributes.Value,
            Attributes.DataType,
            Attributes.ValueRank,
            Attributes.AccessLevel,
            Attributes.UserAccessLevel
        ];

        private SystemContext m_context;
        private BaseObjectState m_objectNode;
        private BaseDataVariableState m_variableNode;
        private Variant[] m_commonVariants;
        private Variant[] m_allNonValueVariants;
        private Variant[] m_variableVariants;

        [GlobalSetup]
        [OneTimeSetUp]
        public void Setup()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.CreateForBenchmarks();
            m_context = new SystemContext(telemetry)
            {
                NamespaceUris = new NamespaceTable()
            };
            m_context.NamespaceUris.GetIndexOrAppend("http://test.org/UA/");

            m_objectNode = new BaseObjectState(null)
            {
                NodeId = new NodeId(1, 1),
                BrowseName = new QualifiedName("TestObject", 1),
                DisplayName = new LocalizedText("TestObject"),
                Description = new LocalizedText("A test object node"),
                WriteMask = AttributeWriteMask.DisplayName,
                UserWriteMask = AttributeWriteMask.DisplayName,
                RolePermissions =
                [
                    new RolePermissionType { RoleId = new NodeId(15656), Permissions = (uint)PermissionType.Browse }
                ]
            };

            m_variableNode = new BaseDataVariableState(null)
            {
                NodeId = new NodeId(2, 1),
                BrowseName = new QualifiedName("TestVariable", 1),
                DisplayName = new LocalizedText("TestVariable"),
                Description = new LocalizedText("A test variable node"),
                Value = 42.0,
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead
            };

            m_commonVariants = new Variant[s_commonAttributeIds.Length];
            m_allNonValueVariants = new Variant[s_allNonValueAttributeIds.Length];
            m_variableVariants = new Variant[s_variableAttributeIds.Length];
        }

        [GlobalCleanup]
        [OneTimeTearDown]
        public void TearDown()
        {
            m_objectNode?.Dispose();
            m_variableNode?.Dispose();
            m_commonVariants = null;
            m_allNonValueVariants = null;
            m_variableVariants = null;
        }

        /// <summary>
        /// Benchmark reading common non-value attributes from an object node.
        /// </summary>
        [Test]
        [Benchmark]
        public void ReadCommonAttributesObjectNode()
        {
            _ = m_objectNode.ReadAttributes(m_context, s_commonAttributeIds);
        }

        /// <summary>
        /// Benchmark reading all non-value attributes from an object node,
        /// including optional ones (RolePermissions, UserRolePermissions, AccessRestrictions).
        /// </summary>
        [Test]
        [Benchmark]
        public void ReadAllNonValueAttributesObjectNode()
        {
            _ = m_objectNode.ReadAttributes(m_context, s_allNonValueAttributeIds);
        }

        /// <summary>
        /// Benchmark reading typical variable attributes including the Value attribute.
        /// </summary>
        [Test]
        [Benchmark]
        public void ReadVariableAttributes()
        {
            _ = m_variableNode.ReadAttributes(m_context, s_variableAttributeIds);
        }

        /// <summary>
        /// Benchmark reading a single attribute (NodeId) to measure per-call overhead.
        /// </summary>
        [Test]
        [Benchmark]
        public void ReadSingleAttribute()
        {
            _ = m_objectNode.ReadAttributes(m_context, Attributes.NodeId);
        }

        /// <summary>
        /// Benchmark reading common non-value attributes using the zero-allocation
        /// <c>ref Variant[]</c> overload with a pre-allocated caller-owned array.
        /// </summary>
        [Test]
        [Benchmark]
        public void ReadCommonAttributesObjectNodeVariant()
        {
            m_objectNode.ReadAttributes(m_context, ref m_commonVariants, s_commonAttributeIds);
        }

        /// <summary>
        /// Benchmark reading all non-value attributes using the zero-allocation
        /// <c>ref Variant[]</c> overload with a pre-allocated caller-owned array.
        /// </summary>
        [Test]
        [Benchmark]
        public void ReadAllNonValueAttributesObjectNodeVariant()
        {
            m_objectNode.ReadAttributes(m_context, ref m_allNonValueVariants, s_allNonValueAttributeIds);
        }

        /// <summary>
        /// Benchmark reading typical variable attributes using the zero-allocation
        /// <c>ref Variant[]</c> overload with a pre-allocated caller-owned array.
        /// </summary>
        [Test]
        [Benchmark]
        public void ReadVariableAttributesVariant()
        {
            m_variableNode.ReadAttributes(m_context, ref m_variableVariants, s_variableAttributeIds);
        }
    }
}
