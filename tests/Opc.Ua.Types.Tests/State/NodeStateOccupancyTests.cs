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

#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Checks callback and optional-property occupancy and emits CSV diagnostics on failure.
    /// </summary>
    /// <remarks>
    /// Counts 10 behavior callbacks, two event backing fields, 20 base-attribute callbacks,
    /// eight value callbacks, and 16 variable-attribute callbacks. Optional properties cover
    /// the inline description, three security properties, and six design-metadata properties.
    /// </remarks>
    [TestFixture]
    [Category("NodeStateOccupancy")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class NodeStateOccupancyTests
    {
        /// <summary>
        /// Clears the diagnostic rows before each test.
        /// </summary>
        [SetUp]
        public void ClearRows()
        {
            m_rows.Clear();
        }

        /// <summary>
        /// Outputs buffered CSV rows only when the test does not pass.
        /// </summary>
        [TearDown]
        public void PrintRowsOnFailure()
        {
            if (TestContext.CurrentContext.Result.Outcome.Status != TestStatus.Passed)
            {
                TestContext.Out.WriteLine(
                    k_csvHeader +
                    Environment.NewLine +
                    string.Join(Environment.NewLine, m_rows));
            }
        }

        /// <summary>
        /// Fresh <see cref="BaseObjectState"/> with no parent and no initialization —
        /// all callback and optional-data slots are null/default.
        /// </summary>
        [Test]
        public void FreshBaseObjectStateAllCallbacksNull()
        {
            var node = new BaseObjectState(null);
            string row = BuildRow("FreshBaseObject", node);
            m_rows.Add(row);

            Assert.That(BehaviorNonNull(node), Is.Zero,
                "Fresh BaseObjectState: expected 0 non-null behavior callbacks.");
            Assert.That(EventBackingFieldsNonNull(node), Is.Zero);
            Assert.That(BaseAttrNonNull(node), Is.Zero,
                "Fresh BaseObjectState: expected 0 non-null attribute callbacks.");
            Assert.That(node.Description.IsNull, Is.True,
                "Fresh BaseObjectState: Description should be null.");
            Assert.That(node.RolePermissions.IsNull, Is.True,
                "Fresh BaseObjectState: RolePermissions should be null.");
            Assert.That(node.UserRolePermissions.IsNull, Is.True);
            Assert.That(node.AccessRestrictions, Is.Null);
            Assert.That(node.DesignMetadataSet(), Is.False,
                "Fresh BaseObjectState: design metadata should be at default.");
        }

        /// <summary>
        /// Fresh <see cref="BaseDataVariableState"/> with no parent — all 32 base plus
        /// 24 variable callback slots are null.
        /// </summary>
        [Test]
        public void FreshBaseDataVariableStateAllCallbacksNull()
        {
            var node = new BaseDataVariableState(null);
            string row = BuildRow("FreshBaseDataVariable", node);
            m_rows.Add(row);

            Assert.That(BehaviorNonNull(node), Is.Zero,
                "Fresh BaseDataVariableState: expected 0 non-null behavior callbacks.");
            Assert.That(EventBackingFieldsNonNull(node), Is.Zero);
            Assert.That(BaseAttrNonNull(node), Is.Zero,
                "Fresh BaseDataVariableState: expected 0 non-null attribute callbacks.");
            Assert.That(ValueNonNull(node), Is.Zero,
                "Fresh BaseDataVariableState: expected 0 non-null value callbacks.");
            Assert.That(VarAttrNonNull(node), Is.Zero,
                "Fresh BaseDataVariableState: expected 0 non-null variable-attribute callbacks.");
        }

        /// <summary>
        /// Simulates data-change monitoring: only <see cref="NodeState.OnStateChangedAsync"/>
        /// is set — the pattern used by <c>MonitoredNode2.Add(IDataChangeMonitoredItem2)</c>.
        /// </summary>
        [Test]
        public void OnStateChangedAsyncOnlyOneBehaviorSlotOccupied()
        {
            var node = new BaseDataVariableState(null)
            {
                OnStateChangedAsync = s_noopChangedAsync
            };
            string row = BuildRow("DataChangeMonitored", node);
            m_rows.Add(row);

            Assert.That(BehaviorNonNull(node), Is.EqualTo(1),
                "Data-change monitoring: exactly 1 behavior callback expected.");
            Assert.That(BaseAttrNonNull(node), Is.Zero);
            Assert.That(ValueNonNull(node), Is.Zero);
        }

        /// <summary>
        /// Simulates value-companion binding: only <see cref="BaseVariableState.OnReadValue"/>
        /// and <see cref="BaseVariableState.OnWriteValue"/> are set.
        /// </summary>
        [Test]
        public void OnReadWriteValueOnlyTwoValueSlotsOccupied()
        {
            var node = new BaseDataVariableState(null)
            {
                OnReadValue = s_noopReadValue,
                OnWriteValue = s_noopWriteValue
            };
            string row = BuildRow("ValueCompanion", node);
            m_rows.Add(row);

            Assert.That(ValueNonNull(node), Is.EqualTo(2),
                "Value companion: exactly 2 value callbacks expected.");
            Assert.That(BehaviorNonNull(node), Is.Zero);
            Assert.That(VarAttrNonNull(node), Is.Zero);
        }

        /// <summary>
        /// Simulates combined data-change + value-companion: three delegate slots.
        /// </summary>
        [Test]
        public void DataChangeAndValueCallbacksThreeCallbackSlotsOccupied()
        {
            var node = new BaseDataVariableState(null)
            {
                OnStateChangedAsync = s_noopChangedAsync,
                OnReadValue = s_noopReadValue,
                OnWriteValue = s_noopWriteValue
            };
            string row = BuildRow("DataChangeAndValue", node);
            m_rows.Add(row);

            Assert.That(BehaviorNonNull(node), Is.EqualTo(1));
            Assert.That(ValueNonNull(node), Is.EqualTo(2));
            Assert.That(BaseAttrNonNull(node) + VarAttrNonNull(node), Is.Zero,
                "Attribute callbacks must remain zero in combined monitoring scenario.");
        }

        /// <summary>
        /// Sets all 10 behavior callbacks without subscribing to events.
        /// </summary>
        [Test]
        public void AllBehaviorCallbacksTenSlotsOccupied()
        {
            var node = new BaseObjectState(null);
            SetAllBehaviorCallbacks(node);
            string row = BuildRow("AllBehaviorCallbacks", node);
            m_rows.Add(row);

            Assert.That(BehaviorNonNull(node), Is.EqualTo(10));
            Assert.That(EventBackingFieldsNonNull(node), Is.Zero);
        }

        /// <summary>
        /// Counts event subscriptions independently of the directly assigned callback fields.
        /// </summary>
        [Test]
        public void StateChangedEventsOccupyIndependentSlotsUntilUnsubscribed()
        {
            var node = new BaseObjectState(null);
            Assert.That(EventBackingFieldsNonNull(node), Is.Zero);

            node.StateChanged += NoopChanged;
            Assert.That(EventBackingFieldsNonNull(node), Is.EqualTo(1));
            node.StateChangedAsync += s_noopChangedAsync;
            Assert.That(EventBackingFieldsNonNull(node), Is.EqualTo(2));
            Assert.That(BehaviorNonNull(node), Is.Zero);
            m_rows.Add(BuildRow("EventsSubscribed", node));

            node.StateChanged -= NoopChanged;
            Assert.That(EventBackingFieldsNonNull(node), Is.EqualTo(1));
            node.StateChangedAsync -= s_noopChangedAsync;
            Assert.That(EventBackingFieldsNonNull(node), Is.Zero);
            Assert.That(BehaviorNonNull(node), Is.Zero);
            m_rows.Add(BuildRow("EventsUnsubscribed", node));
        }

        /// <summary>
        /// Sets all 20 base-attribute callbacks.
        /// </summary>
        [Test]
        public void AllBaseAttributeCallbacksTwentySlotsOccupied()
        {
            var node = new BaseObjectState(null);
            SetAllBaseAttributeCallbacks(node);
            string row = BuildRow("AllBaseAttrCallbacks", node);
            m_rows.Add(row);

            Assert.That(BaseAttrNonNull(node), Is.EqualTo(20));
        }

        /// <summary>
        /// Sets all 24 variable-specific callbacks.
        /// </summary>
        [Test]
        public void AllVariableCallbacksTwentyFourVariableSlotsOccupied()
        {
            var node = new BaseDataVariableState(null);
            SetAllVariableCallbacks(node);
            string row = BuildRow("AllVariableCallbacks", node);
            m_rows.Add(row);

            Assert.That(ValueNonNull(node), Is.EqualTo(8));
            Assert.That(VarAttrNonNull(node), Is.EqualTo(16));
        }

        /// <summary>
        /// All callbacks cleared after being set — confirms slots return to zero after reset.
        /// </summary>
        [Test]
        public void ClearAllCallbacksSlotsReturnToZero()
        {
            var node = new BaseDataVariableState(null);
            SetAllBehaviorCallbacks(node);
            SetAllBaseAttributeCallbacks(node);
            SetAllVariableCallbacks(node);
            Assert.That(BehaviorNonNull(node), Is.EqualTo(10));
            Assert.That(BaseAttrNonNull(node), Is.EqualTo(20));
            Assert.That(ValueNonNull(node), Is.EqualTo(8));
            Assert.That(VarAttrNonNull(node), Is.EqualTo(16));
            ClearAllCallbacks(node);
            string row = BuildRow("AllCallbacksCleared", node);
            m_rows.Add(row);

            Assert.That(BehaviorNonNull(node), Is.Zero);
            Assert.That(BaseAttrNonNull(node), Is.Zero);
            Assert.That(ValueNonNull(node), Is.Zero);
            Assert.That(VarAttrNonNull(node), Is.Zero);
        }

        /// <summary>
        /// Description null vs. set vs. reset — tracks the inline description field occupancy.
        /// </summary>
        [Test]
        public void DescriptionNullSetReset()
        {
            var node = new BaseObjectState(null);
            Assert.That(node.Description.IsNull, Is.True, "Null before set.");

            node.Description = new LocalizedText("A description.");
            string rowSet = BuildRow("DescriptionSet", node);
            m_rows.Add(rowSet);
            Assert.That(node.Description.IsNull, Is.False, "Description set.");

            node.Description = default;
            string rowReset = BuildRow("DescriptionReset", node);
            m_rows.Add(rowReset);
            Assert.That(node.Description.IsNull, Is.True, "Description reset to null.");
        }

        /// <summary>
        /// Security fields: null/default, present-empty, populated, and reset.
        /// Tracks RolePermissions, UserRolePermissions, and AccessRestrictions.
        /// </summary>
        [Test]
        public void SecurityFieldsNullEmptyPopulatedReset()
        {
            var node = new BaseObjectState(null);
            m_rows.Add(BuildRow("SecurityDefault", node));
            Assert.That(node.RolePermissions.IsNull, Is.True);
            Assert.That(node.UserRolePermissions.IsNull, Is.True);
            Assert.That(node.AccessRestrictions, Is.Null);

            node.RolePermissions = [];
            node.UserRolePermissions = [];
            node.AccessRestrictions = AccessRestrictionType.None;
            m_rows.Add(BuildRow("SecurityEmptyArrays", node));
            Assert.That(node.RolePermissions.IsNull, Is.False);
            Assert.That(node.UserRolePermissions.IsNull, Is.False);
            Assert.That(node.RolePermissions.Count, Is.Zero);
            Assert.That(node.UserRolePermissions.Count, Is.Zero);
            Assert.That(node.AccessRestrictions, Is.EqualTo(AccessRestrictionType.None));

            node.RolePermissions = ArrayOf.Wrapped(
                new RolePermissionType { RoleId = new NodeId(1u, 0), Permissions = 0xFF });
            node.UserRolePermissions = node.RolePermissions;
            m_rows.Add(BuildRow("SecurityPopulated", node));
            Assert.That(node.RolePermissions.IsNull, Is.False);
            Assert.That(node.UserRolePermissions.IsNull, Is.False);
            Assert.That(node.RolePermissions.Count, Is.EqualTo(1));
            Assert.That(node.UserRolePermissions.Count, Is.EqualTo(1));

            node.RolePermissions = default;
            node.UserRolePermissions = default;
            node.AccessRestrictions = null;
            m_rows.Add(BuildRow("SecurityReset", node));
            Assert.That(node.RolePermissions.IsNull, Is.True);
            Assert.That(node.UserRolePermissions.IsNull, Is.True);
            Assert.That(node.AccessRestrictions, Is.Null);
        }

        /// <summary>
        /// Design metadata fields: default (all null/default), populated, and reset.
        /// Tracks Extensions, Categories, Specification, NodeSetDocumentation,
        /// ReleaseStatus (non-Released = set), and DesignToolOnly.
        /// </summary>
        [Test]
        public void DesignMetadataDefaultPopulatedReset()
        {
            var node = new BaseObjectState(null);
            m_rows.Add(BuildRow("DesignMetaDefault", node));
            Assert.That(node.DesignMetadataSet(), Is.False);
            Assert.That(node.ReleaseStatus, Is.EqualTo(Export.ReleaseStatus.Released));
            Assert.That(node.DesignToolOnly, Is.False);

            node.Extensions = [XmlElement.From(new System.Xml.XmlDocument().CreateElement("ext"))];
            node.Categories = ["TestCategory"];
            node.Specification = "http://example.org/spec";
            node.NodeSetDocumentation = "http://example.org/docs";
            node.ReleaseStatus = Export.ReleaseStatus.Draft;
            node.DesignToolOnly = true;
            m_rows.Add(BuildRow("DesignMetaPopulated", node));
            Assert.That(node.DesignMetadataSet(), Is.True);

            node.Extensions = null;
            node.Categories = null;
            node.Specification = null;
            node.NodeSetDocumentation = null;
            node.ReleaseStatus = Export.ReleaseStatus.Released;
            node.DesignToolOnly = false;
            m_rows.Add(BuildRow("DesignMetaReset", node));
            Assert.That(node.DesignMetadataSet(), Is.False);
        }

        /// <summary>
        /// Checks reference counts and verifies that adding references does not install callbacks.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        [TestCase(8)]
        [TestCase(16)]
        public void ExplicitReferenceCountParameterizedSizes(int count)
        {
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId(1u, 1)
            };
            for (int i = 0; i < count; i++)
            {
                node.AddReference(ReferenceTypeIds.HasComponent, false, new NodeId((uint)(i + 100), 1));
            }

            string row = BuildRow($"Refs_{count}", node);
            m_rows.Add(row);

            var refs = new List<IReference>();
            node.GetReferences(null!, refs);
            Assert.That(refs, Has.Count.EqualTo(count),
                $"Expected {count} references; got {refs.Count}.");
            Assert.That(BehaviorNonNull(node), Is.Zero,
                "Adding references must not install any callbacks.");
        }

        /// <summary>
        /// Counts non-null behavior delegate fields (10 directly-accessible OnXxx
        /// fields; does not include the 2 event backing fields).
        /// </summary>
        private static int BehaviorNonNull(NodeState n)
        {
            return B(n.OnValidate) +
                B(n.OnStateChanged) +
                B(n.OnStateChangedAsync) +
                B(n.OnReferenceAdded) +
                B(n.OnReferenceRemoved) +
                B(n.OnReportEvent) +
                B(n.OnReportEventAsync) +
                B(n.OnConditionRefresh) +
                B(n.OnCreateBrowser) +
                B(n.OnPopulateBrowser);
        }

        /// <summary>
        /// Counts non-null base-attribute callbacks (10 read/write pairs).
        /// </summary>
        private static int BaseAttrNonNull(NodeState n)
        {
            return B(n.OnReadNodeId) +
                B(n.OnWriteNodeId) +
                B(n.OnReadNodeClass) +
                B(n.OnWriteNodeClass) +
                B(n.OnReadBrowseName) +
                B(n.OnWriteBrowseName) +
                B(n.OnReadDisplayName) +
                B(n.OnWriteDisplayName) +
                B(n.OnReadDescription) +
                B(n.OnWriteDescription) +
                B(n.OnReadWriteMask) +
                B(n.OnWriteWriteMask) +
                B(n.OnReadUserWriteMask) +
                B(n.OnWriteUserWriteMask) +
                B(n.OnReadRolePermissions) +
                B(n.OnWriteRolePermissions) +
                B(n.OnReadUserRolePermissions) +
                B(n.OnWriteUserRolePermissions) +
                B(n.OnReadAccessRestrictions) +
                B(n.OnWriteAccessRestrictions);
        }

        /// <summary>
        /// Counts non-null variable value delegate fields (8 value-specific callbacks).
        /// Returns 0 if node is not a <see cref="BaseVariableState"/>.
        /// </summary>
        private static int ValueNonNull(NodeState n)
        {
            if (n is not BaseVariableState v)
            {
                return 0;
            }

            return B(v.OnSimpleReadValue) +
                B(v.OnSimpleWriteValue) +
                B(v.OnReadValue) +
                B(v.OnWriteValue) +
                B(v.OnReadValueAsync) +
                B(v.OnSimpleReadValueAsync) +
                B(v.OnWriteValueAsync) +
                B(v.OnSimpleWriteValueAsync);
        }

        /// <summary>
        /// Counts non-null variable-attribute callbacks (eight read/write pairs).
        /// Returns 0 if node is not a <see cref="BaseVariableState"/>.
        /// </summary>
        private static int VarAttrNonNull(NodeState n)
        {
            if (n is not BaseVariableState v)
            {
                return 0;
            }

            return B(v.OnReadDataType) +
                B(v.OnWriteDataType) +
                B(v.OnReadValueRank) +
                B(v.OnWriteValueRank) +
                B(v.OnReadArrayDimensions) +
                B(v.OnWriteArrayDimensions) +
                B(v.OnReadAccessLevel) +
                B(v.OnWriteAccessLevel) +
                B(v.OnReadUserAccessLevel) +
                B(v.OnWriteUserAccessLevel) +
                B(v.OnReadMinimumSamplingInterval) +
                B(v.OnWriteMinimumSamplingInterval) +
                B(v.OnReadHistorizing) +
                B(v.OnWriteHistorizing) +
                B(v.OnReadAccessLevelEx) +
                B(v.OnWriteAccessLevelEx);
        }

        /// <summary>
        /// Checks the event backing fields (StateChanged, StateChangedAsync) via
        /// reflection.  Returns the count of subscribed events (0, 1, or 2).
        /// </summary>
        private static int EventBackingFieldsNonNull(NodeState n)
        {
            int count = 0;
            if (s_stateChangedField.GetValue(n) is Delegate)
            {
                count++;
            }

            if (s_stateChangedAsyncField.GetValue(n) is Delegate)
            {
                count++;
            }

            return count;
        }

        private static string BuildRow(string scenario, NodeState node)
        {
            int valueNonNull = node is BaseVariableState ? ValueNonNull(node) : -1;
            int varAttrNonNull = node is BaseVariableState ? VarAttrNonNull(node) : -1;
            return new StringBuilder()
                .Append(scenario).Append(',')
                .Append(BehaviorNonNull(node) + EventBackingFieldsNonNull(node)).Append(',')
                .Append(BaseAttrNonNull(node)).Append(',')
                .Append(valueNonNull).Append(',')
                .Append(varAttrNonNull).Append(',')
                .Append(!node.Description.IsNull).Append(',')
                .Append(node.RolePermissions.IsNull ? -1 : node.RolePermissions.Count).Append(',')
                .Append(node.UserRolePermissions.IsNull ? -1 : node.UserRolePermissions.Count).Append(',')
                .Append(node.AccessRestrictions.HasValue).Append(',')
                .Append(node.DesignMetadataSet())
                .ToString();
        }

        /// <summary>
        /// Returns 1 if <paramref name="d"/> is non-null, 0 otherwise.
        /// </summary>
        private static int B(Delegate? d)
        {
            return d is null ? 0 : 1;
        }

        private static void SetAllBehaviorCallbacks(NodeState n)
        {
            n.OnValidate = (_, _) => true;
            n.OnStateChanged = (_, _, _) => { };
            n.OnStateChangedAsync = NoopChangedAsync;
            n.OnReferenceAdded = (_, _, _, _) => { };
            n.OnReferenceRemoved = (_, _, _, _) => { };
            n.OnReportEvent = (_, _, _) => { };
            n.OnReportEventAsync = (ctx, nd, e, ct) => default;
            n.OnConditionRefresh = (_, _, _) => { };
            n.OnCreateBrowser = (_, _, _, _, _, _, _, _, _) => null!;
            n.OnPopulateBrowser = (_, _, _) => { };
        }

        private static void SetAllBaseAttributeCallbacks(NodeState n)
        {
            n.OnReadNodeId = NoopAttr;
            n.OnWriteNodeId = NoopAttr;
            n.OnReadNodeClass = NoopAttr;
            n.OnWriteNodeClass = NoopAttr;
            n.OnReadBrowseName = NoopAttr;
            n.OnWriteBrowseName = NoopAttr;
            n.OnReadDisplayName = NoopAttr;
            n.OnWriteDisplayName = NoopAttr;
            n.OnReadDescription = NoopAttr;
            n.OnWriteDescription = NoopAttr;
            n.OnReadWriteMask = NoopAttr;
            n.OnWriteWriteMask = NoopAttr;
            n.OnReadUserWriteMask = NoopAttr;
            n.OnWriteUserWriteMask = NoopAttr;
            n.OnReadRolePermissions = NoopAttr;
            n.OnWriteRolePermissions = NoopAttr;
            n.OnReadUserRolePermissions = NoopAttr;
            n.OnWriteUserRolePermissions = NoopAttr;
            n.OnReadAccessRestrictions = NoopAttr;
            n.OnWriteAccessRestrictions = NoopAttr;
        }

        private static void SetAllVariableCallbacks(BaseVariableState v)
        {
            v.OnSimpleReadValue = NoopSimpleValue;
            v.OnSimpleWriteValue = NoopSimpleValue;
            v.OnReadValue = NoopFullValue;
            v.OnWriteValue = NoopFullValue;
            v.OnReadValueAsync = NoopReadValueAsync;
            v.OnSimpleReadValueAsync = NoopSimpleReadValueAsync;
            v.OnWriteValueAsync = NoopWriteValueAsync;
            v.OnSimpleWriteValueAsync = NoopSimpleWriteValueAsync;

            v.OnReadDataType = NoopAttr;
            v.OnWriteDataType = NoopAttr;
            v.OnReadValueRank = NoopAttr;
            v.OnWriteValueRank = NoopAttr;
            v.OnReadArrayDimensions = NoopAttr;
            v.OnWriteArrayDimensions = NoopAttr;
            v.OnReadAccessLevel = NoopAttr;
            v.OnWriteAccessLevel = NoopAttr;
            v.OnReadUserAccessLevel = NoopAttr;
            v.OnWriteUserAccessLevel = NoopAttr;
            v.OnReadMinimumSamplingInterval = NoopAttr;
            v.OnWriteMinimumSamplingInterval = NoopAttr;
            v.OnReadHistorizing = NoopAttr;
            v.OnWriteHistorizing = NoopAttr;
            v.OnReadAccessLevelEx = NoopAttr;
            v.OnWriteAccessLevelEx = NoopAttr;
        }

        private static void ClearAllCallbacks(NodeState n)
        {
            n.OnValidate = null;
            n.OnStateChanged = null;
            n.OnStateChangedAsync = null;
            n.OnReferenceAdded = null;
            n.OnReferenceRemoved = null;
            n.OnReportEvent = null;
            n.OnReportEventAsync = null;
            n.OnConditionRefresh = null;
            n.OnCreateBrowser = null;
            n.OnPopulateBrowser = null;

            n.OnReadNodeId = null;
            n.OnWriteNodeId = null;
            n.OnReadNodeClass = null;
            n.OnWriteNodeClass = null;
            n.OnReadBrowseName = null;
            n.OnWriteBrowseName = null;
            n.OnReadDisplayName = null;
            n.OnWriteDisplayName = null;
            n.OnReadDescription = null;
            n.OnWriteDescription = null;
            n.OnReadWriteMask = null;
            n.OnWriteWriteMask = null;
            n.OnReadUserWriteMask = null;
            n.OnWriteUserWriteMask = null;
            n.OnReadRolePermissions = null;
            n.OnWriteRolePermissions = null;
            n.OnReadUserRolePermissions = null;
            n.OnWriteUserRolePermissions = null;
            n.OnReadAccessRestrictions = null;
            n.OnWriteAccessRestrictions = null;

            if (n is not BaseVariableState v)
            {
                return;
            }

            v.OnSimpleReadValue = null;
            v.OnSimpleWriteValue = null;
            v.OnReadValue = null;
            v.OnWriteValue = null;
            v.OnReadValueAsync = null;
            v.OnSimpleReadValueAsync = null;
            v.OnWriteValueAsync = null;
            v.OnSimpleWriteValueAsync = null;

            v.OnReadDataType = null;
            v.OnWriteDataType = null;
            v.OnReadValueRank = null;
            v.OnWriteValueRank = null;
            v.OnReadArrayDimensions = null;
            v.OnWriteArrayDimensions = null;
            v.OnReadAccessLevel = null;
            v.OnWriteAccessLevel = null;
            v.OnReadUserAccessLevel = null;
            v.OnWriteUserAccessLevel = null;
            v.OnReadMinimumSamplingInterval = null;
            v.OnWriteMinimumSamplingInterval = null;
            v.OnReadHistorizing = null;
            v.OnWriteHistorizing = null;
            v.OnReadAccessLevelEx = null;
            v.OnWriteAccessLevelEx = null;
        }

        private static void NoopChanged(
            ISystemContext context,
            NodeState node,
            NodeStateChangeMasks masks)
        {
        }

        private static ValueTask NoopChangedAsync(
            ISystemContext context, NodeState node,
            NodeStateChangeMasks masks, CancellationToken cancellationToken)
        {
            return default;
        }

        private static ServiceResult NoopFullValue(
            ISystemContext context, NodeState node,
            NumericRange range, QualifiedName encoding,
            ref Variant value, ref StatusCode statusCode, ref DateTimeUtc timestamp)
        {
            return ServiceResult.Good;
        }

        private static ServiceResult NoopAttr<T>(ISystemContext context, NodeState node, ref T value)
        {
            return ServiceResult.Good;
        }

        private static ServiceResult NoopSimpleValue(
            ISystemContext context, NodeState node, ref Variant value)
        {
            return ServiceResult.Good;
        }

        private static ValueTask<AttributeReadResult> NoopReadValueAsync(
            ISystemContext context, NodeState node,
            NumericRange range, QualifiedName encoding, CancellationToken cancellationToken)
        {
            return new ValueTask<AttributeReadResult>(
                new AttributeReadResult(ServiceResult.Good, default, default, default));
        }

        private static ValueTask<AttributeSimpleReadResult> NoopSimpleReadValueAsync(
            ISystemContext context, NodeState node, CancellationToken cancellationToken)
        {
            return new ValueTask<AttributeSimpleReadResult>(
                new AttributeSimpleReadResult(ServiceResult.Good, default));
        }

        private static ValueTask<AttributeWriteResult> NoopWriteValueAsync(
            ISystemContext context, NodeState node,
            NumericRange range, Variant value, CancellationToken cancellationToken)
        {
            return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
        }

        private static ValueTask<AttributeWriteResult> NoopSimpleWriteValueAsync(
            ISystemContext context, NodeState node,
            Variant value, CancellationToken cancellationToken)
        {
            return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
        }

        private const string k_csvHeader =
            "Scenario," +
            "BehaviorNonNull," +
            "BaseAttrNonNull," +
            "ValueNonNull," +
            "VarAttrNonNull," +
            "DescriptionSet," +
            "RolePermissionsCount," +
            "UserRolePermissionsCount," +
            "AccessRestrictionsSet," +
            "DesignMetadataSet";

        private static readonly FieldInfo s_stateChangedField =
            typeof(NodeState).GetField("StateChanged", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(NodeState).FullName, "StateChanged");

        private static readonly FieldInfo s_stateChangedAsyncField =
            typeof(NodeState).GetField("StateChangedAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(NodeState).FullName, "StateChangedAsync");

        private static readonly NodeStateChangedAsyncHandler s_noopChangedAsync = NoopChangedAsync;
        private static readonly NodeValueEventHandler s_noopReadValue = NoopFullValue;
        private static readonly NodeValueEventHandler s_noopWriteValue = NoopFullValue;
        private readonly List<string> m_rows = [];
    }

    /// <summary>
    /// Extension methods for <see cref="NodeState"/> occupancy helpers.
    /// </summary>
    internal static class NodeStateOccupancyExtensions
    {
        /// <summary>
        /// Returns whether any design-metadata property has a non-default value.
        /// </summary>
        public static bool DesignMetadataSet(this NodeState node)
        {
            return node.Extensions is not null ||
                node.Categories is not null ||
                node.Specification is not null ||
                node.NodeSetDocumentation is not null ||
                node.ReleaseStatus != Export.ReleaseStatus.Released ||
                node.DesignToolOnly;
        }
    }
}
