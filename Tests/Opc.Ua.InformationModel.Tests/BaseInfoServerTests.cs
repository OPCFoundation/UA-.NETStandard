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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for Base Information server-level features:
    /// server capabilities, server objects, views, namespaces, system status,
    /// and other server-level conformance units.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("BaseInfoServer")]
    public class BaseInfoServerTests : TestFixture
    {
        [Test]
        public async Task BrowseViewsFolderExistsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectIds.ViewsFolder, Attributes.BrowseName)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                "Views folder should exist.");
        }

        [Test]
        public async Task BrowseTypesFolderSubfoldersAsync()
        {
            List<ReferenceDescription> refs = await BrowseForwardAsync(
                ObjectIds.TypesFolder).ConfigureAwait(false);

            Assert.That(
                HasChildWithName(refs, "ObjectTypes"), Is.True,
                "ObjectTypes subfolder should exist.");
            Assert.That(
                HasChildWithName(refs, "VariableTypes"), Is.True,
                "VariableTypes subfolder should exist.");
            Assert.That(
                HasChildWithName(refs, "DataTypes"), Is.True,
                "DataTypes subfolder should exist.");
            Assert.That(
                HasChildWithName(refs, "ReferenceTypes"), Is.True,
                "ReferenceTypes subfolder should exist.");
            Assert.That(
                HasChildWithName(refs, "EventTypes"), Is.True,
                "EventTypes subfolder should exist.");
        }

        [Test]
        public async Task VerifyTypeFolderContentsAsync()
        {
            List<ReferenceDescription> objectTypes = await BrowseForwardAsync(
                ObjectIds.ObjectTypesFolder).ConfigureAwait(false);
            Assert.That(
                HasChildWithName(objectTypes, "BaseObjectType"), Is.True,
                "BaseObjectType should be under ObjectTypes.");

            List<ReferenceDescription> variableTypes = await BrowseForwardAsync(
                ObjectIds.VariableTypesFolder).ConfigureAwait(false);
            Assert.That(
                HasChildWithName(variableTypes, "BaseVariableType"), Is.True,
                "BaseVariableType should be under VariableTypes.");

            List<ReferenceDescription> dataTypes = await BrowseForwardAsync(
                ObjectIds.DataTypesFolder).ConfigureAwait(false);
            Assert.That(
                HasChildWithName(dataTypes, "BaseDataType"), Is.True,
                "BaseDataType should be under DataTypes.");

            List<ReferenceDescription> refTypes = await BrowseForwardAsync(
                ObjectIds.ReferenceTypesFolder).ConfigureAwait(false);
            Assert.That(
                HasChildWithName(refTypes, "References"), Is.True,
                "References should be under ReferenceTypes.");
        }

        [Test]
        public async Task BrowseDataTypesFolderAsync()
        {
            List<ReferenceDescription> refs = await BrowseForwardAsync(
                ObjectIds.DataTypesFolder).ConfigureAwait(false);
            Assert.That(refs, Is.Not.Empty,
                "DataTypes folder should contain nodes.");
        }

        [Test]
        public async Task ReadServerProfileArrayAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_ServerProfileArray)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            string[] profiles = result.GetValue<string[]>(null);
            Assert.That(profiles, Is.Not.Null,
                "ServerProfileArray should be a string array.");
        }

        [Test]
        public async Task ReadLocaleIdArrayAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_LocaleIdArray)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadMinSupportedSampleRateAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_MinSupportedSampleRate)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadMaxBrowseContinuationPointsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_MaxBrowseContinuationPoints)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            ushort val = result.WrappedValue.GetUInt16();
            Assert.That(val, Is.GreaterThan((ushort)0));
        }

        [Test]
        public async Task ReadMaxQueryContinuationPointsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_MaxQueryContinuationPoints)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadMaxHistoryContinuationPointsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_MaxHistoryContinuationPoints)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadSoftwareCertificatesAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_SoftwareCertificates)
                .ConfigureAwait(false);
            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Fail("SoftwareCertificates not supported.");
            }

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadMaxArrayLengthAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_MaxArrayLength)
                .ConfigureAwait(false);
            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Fail("MaxArrayLength not supported.");
            }

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadMaxStringLengthAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_MaxStringLength)
                .ConfigureAwait(false);
            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Fail("MaxStringLength not supported.");
            }

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadMaxByteStringLengthAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_MaxByteStringLength)
                .ConfigureAwait(false);
            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Fail("MaxByteStringLength not supported.");
            }

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadOperationLimitsObjectExistsAsync()
        {
            await AssertNodeExistsAsync(
                ObjectIds.Server_ServerCapabilities_OperationLimits,
                "OperationLimits").ConfigureAwait(false);
        }

        [Test]
        public async Task ReadMaxSessionsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_MaxSessions)
                .ConfigureAwait(false);
            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Fail("MaxSessions not supported.");
            }

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadMaxSubscriptionsPerSessionAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_MaxSubscriptionsPerSession)
                .ConfigureAwait(false);
            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Fail(
                    "MaxSubscriptionsPerSession not supported.");
            }

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadMaxMonitoredItemsPerSubscriptionAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_MaxMonitoredItemsPerSubscription)
                .ConfigureAwait(false);
            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Fail(
                    "MaxMonitoredItemsPerSubscription not supported.");
            }

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadConformanceUnitsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                new NodeId(24101),
                Attributes.BrowseName).ConfigureAwait(false);
            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Fail("ConformanceUnits not found.");
            }

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);

            // Per OPC UA Part 5 §6.3.36, ServerCapabilitiesType.ConformanceUnits
            // must be an array of QualifiedName. Issue #3719 surfaced this when
            // the DataType attribute wasn't reported as QualifiedName (i=20).
            DataValue dataType = await ReadAttributeAsync(
                new NodeId(24101),
                Attributes.DataType).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dataType.StatusCode), Is.True,
                "DataType attribute should be readable.");
            Assert.That(
                dataType.WrappedValue.GetNodeId(),
                Is.EqualTo((NodeId)DataTypeIds.QualifiedName),
                "ConformanceUnits DataType must be QualifiedName per Part 5 §6.3.36.");
        }

        [Test]
        public async Task ReadMaxMonitoredItemsQueueSize()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_MaxMonitoredItemsQueueSize)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadMaxMonitoredItemsPerCallCreate()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_OperationLimits_MaxMonitoredItemsPerCall)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadMaxMonitoredItemsPerCallModify()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_OperationLimits_MaxMonitoredItemsPerCall)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            uint val = result.WrappedValue.GetUInt32();
            Assert.That(val, Is.GreaterThanOrEqualTo((uint)0));
        }

        [Test]
        public async Task ReadMaxMonitoredItemsPerCallSetMode()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_OperationLimits_MaxMonitoredItemsPerCall)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadMaxMonitoredItemsPerCallDelete()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_OperationLimits_MaxMonitoredItemsPerCall)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadMaxMonitoredItemsPerCallSetTriggering()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_OperationLimits_MaxMonitoredItemsPerCall)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task VerifyLimitsEnforcement()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_OperationLimits_MaxMonitoredItemsPerCall)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            uint limit = result.WrappedValue.GetUInt32();
            Assert.That(limit, Is.GreaterThanOrEqualTo((uint)0),
                "Limit should be defined.");
        }

        [Test]
        public async Task VerifySetTriggeringLinksToAddLimits()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_OperationLimits_MaxMonitoredItemsPerCall)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task VerifySetTriggeringLinksToRemoveLimits()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_OperationLimits_MaxMonitoredItemsPerCall)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task BrowseNamespacesAndReadUaMetadataAsync()
        {
            List<ReferenceDescription> refs = await BrowseForwardAsync(
                ObjectIds.Server_Namespaces).ConfigureAwait(false);
            Assert.That(refs, Is.Not.Empty,
                "Namespaces folder should have children.");

            ReferenceDescription uaNs = refs.FirstOrDefault(
                r => r.BrowseName.Name == "http://opcfoundation.org/UA/");
            if (uaNs == null)
            {
                Assert.Fail(
                    "UA namespace entry not found in Namespaces.");
            }

            var uaNsId = ExpandedNodeId.ToNodeId(
                uaNs.NodeId, Session.NamespaceUris);
            List<ReferenceDescription> children =
                await BrowseForwardAsync(uaNsId).ConfigureAwait(false);
            Assert.That(children, Is.Not.Empty);
        }

        [Test]
        public async Task BrowseNamespacesFolderTypeIsNamespacesTypeAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectIds.Server_Namespaces,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task BrowseNamespaceEntriesHaveNamespaceUriAsync()
        {
            List<ReferenceDescription> refs = await BrowseForwardAsync(
                ObjectIds.Server_Namespaces).ConfigureAwait(false);
            Assert.That(refs, Is.Not.Empty);

            var firstNsId = ExpandedNodeId.ToNodeId(
                refs[0].NodeId, Session.NamespaceUris);
            List<ReferenceDescription> props =
                await BrowseForwardAsync(firstNsId).ConfigureAwait(false);
            Assert.That(
                HasChildWithName(props, "NamespaceUri"), Is.True,
                "Namespace entry should have NamespaceUri property.");
        }

        [Test]
        public async Task ReadServerStatusStateIsRunningAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_State)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            int state = (int)result.WrappedValue.GetInt32();
            Assert.That(state,
                Is.EqualTo((int)ServerState.Running));
        }

        [Test]
        public async Task ReadServerStatusStartTimeAndCurrentTimeAsync()
        {
            DataValue startResult = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_StartTime)
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(startResult.StatusCode), Is.True);
            DateTime startTime;
            if (startResult.WrappedValue.TryGetValue(out DateTimeUtc dtuStart))
            {
                startTime = dtuStart.ToDateTime();
            }
            else
            {
                startTime = startResult.WrappedValue.GetDateTime().ToDateTime();
            }
            Assert.That(startTime, Is.Not.EqualTo(DateTime.MinValue));

            DataValue currentResult = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_CurrentTime)
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(currentResult.StatusCode), Is.True);
            DateTime currentTime;
            if (currentResult.WrappedValue.TryGetValue(out DateTimeUtc dtuCurrent))
            {
                currentTime = dtuCurrent.ToDateTime();
            }
            else
            {
                currentTime = currentResult.WrappedValue.GetDateTime().ToDateTime();
            }
            Assert.That(currentTime, Is.GreaterThanOrEqualTo(startTime));
        }

        [Test]
        public async Task ReadServerStatusSecondsTillShutdownAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_SecondsTillShutdown)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadBuildInfoProductName()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerStatus_BuildInfo_ProductName)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            string productName = result.GetValue<string>(null);
            Assert.That(productName, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public async Task ReadBuildInfoManufacturerName()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerStatus_BuildInfo_ManufacturerName)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            string manufacturerName = result.GetValue<string>(null);
            Assert.That(manufacturerName, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public async Task BrowseServerTypeChildrenAsync()
        {
            List<ReferenceDescription> refs =
                await BrowseForwardAsync(ServerTypeId)
                    .ConfigureAwait(false);
            Assert.That(
                HasChildWithName(refs, "ServerCapabilities"), Is.True,
                "ServerType should have ServerCapabilities.");
            Assert.That(
                HasChildWithName(refs, "ServerDiagnostics"), Is.True,
                "ServerType should have ServerDiagnostics.");
            Assert.That(
                HasChildWithName(refs, "ServerStatus"), Is.True,
                "ServerType should have ServerStatus.");
            Assert.That(
                HasChildWithName(refs, "ServerRedundancy"), Is.True,
                "ServerType should have ServerRedundancy.");
        }

        [Test]
        public async Task BrowseFiniteStateMachineTypeForStatesAndTransitions()
        {
            List<ReferenceDescription> refs =
                await BrowseForwardAsync(FiniteStateMachineTypeId)
                    .ConfigureAwait(false);
            Assert.That(
                HasChildWithName(refs, "AvailableStates"), Is.True,
                "FiniteStateMachineType should have AvailableStates.");
            Assert.That(
                HasChildWithName(refs, "AvailableTransitions"), Is.True,
                "FiniteStateMachineType should have AvailableTransitions.");
        }

        [Test]
        public async Task DeprecatedPropertyExistsAsync()
        {
            await AssertNodeExistsAsync(DeprecatedId, "Deprecated")
                .ConfigureAwait(false);
        }

        [Test]
        public async Task EUInformationStructureExistsAsync()
        {
            await AssertNodeExistsAsync(EUInformationId, "EUInformation")
                .ConfigureAwait(false);
        }

        [Test]
        public async Task BrowseAnalogItemTypeForEngineeringUnitsAsync()
        {
            List<ReferenceDescription> refs = await BrowseForwardAsync(
                VariableTypeIds.AnalogItemType).ConfigureAwait(false);
            if (!HasChildWithName(refs, "EngineeringUnits"))
            {
                Assert.Ignore("AnalogItemType EngineeringUnits not available in ReferenceServer.");
            }
        }

        [Test]
        public async Task ReadEngineeringUnitsValueAsync()
        {
            List<ReferenceDescription> refs = await BrowseForwardAsync(
                VariableTypeIds.AnalogItemType).ConfigureAwait(false);
            ReferenceDescription euRef = refs.FirstOrDefault(
                r => r.BrowseName.Name == "EngineeringUnits");
            if (euRef == null)
            {
                Assert.Ignore("AnalogItemType EngineeringUnits not available in ReferenceServer.");
            }

            var euId = ExpandedNodeId.ToNodeId(
                euRef.NodeId, Session.NamespaceUris);
            DataValue result = await ReadNodeValueAsync(euId)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadEURangeAndVerifyStructureAsync()
        {
            List<ReferenceDescription> refs = await BrowseForwardAsync(
                VariableTypeIds.AnalogItemType).ConfigureAwait(false);
            ReferenceDescription rangeRef = refs.FirstOrDefault(
                r => r.BrowseName.Name == "EURange");
            Assert.That(rangeRef, Is.Not.Null,
                "EURange not found on AnalogItemType.");

            var rangeId = ExpandedNodeId.ToNodeId(
                rangeRef.NodeId, Session.NamespaceUris);
            DataValue result = await ReadNodeValueAsync(rangeId)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task BrowseServerTypeForEstimatedReturnTimeAsync()
        {
            List<ReferenceDescription> refs =
                await BrowseForwardAsync(ServerTypeId)
                    .ConfigureAwait(false);
            bool found = HasChildWithName(
                refs, "EstimatedReturnTime");
            if (!found)
            {
                Assert.Fail(
                    "EstimatedReturnTime not found on ServerType.");
            }
        }

        [Test]
        public async Task ReadEstimatedReturnTimeValueAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_EstimatedReturnTime)
                .ConfigureAwait(false);
            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Fail(
                    "EstimatedReturnTime not available.");
            }

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task BrowseServerCapabilitiesForEventPropertiesAsync()
        {
            List<ReferenceDescription> refs = await BrowseForwardAsync(
                ObjectIds.Server_ServerCapabilities)
                .ConfigureAwait(false);
            Assert.That(refs, Is.Not.Empty,
                "ServerCapabilities should have children.");
        }

        [Test]
        public async Task ExportNamespaceMethodExistsAsync()
        {
            await AssertNodeExistsAsync(ExportNsId, "ExportNamespace")
                .ConfigureAwait(false);
        }

        [Test]
        public async Task ImportNamespaceMethodExistsAsync()
        {
            await AssertNodeExistsAsync(ImportNsId, "ImportNamespace")
                .ConfigureAwait(false);
        }

        [Test]
        public async Task BrowseFiniteStateMachineTypeForCurrentState()
        {
            List<ReferenceDescription> refs =
                await BrowseForwardAsync(FiniteStateMachineTypeId)
                    .ConfigureAwait(false);
            Assert.That(
                HasChildWithName(refs, "CurrentState"), Is.True,
                "FiniteStateMachineType should have CurrentState.");
        }

        [Test]
        public async Task ReadMinSupportedSampleRateFixedAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_MinSupportedSampleRate)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task HistoryServerCapabilitiesNodeExists()
        {
            await AssertNodeExistsAsync(
                HistoryCapsId, "HistoryServerCapabilities")
                .ConfigureAwait(false);
        }

        [Test]
        public async Task ReadAccessHistoryDataCapability()
        {
            DataValue result = await ReadNodeValueAsync(AccessHistDataId)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadAccessHistoryEventsCapability()
        {
            DataValue result = await ReadNodeValueAsync(
                AccessHistEventsId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadInsertDataCapability()
        {
            DataValue result = await ReadNodeValueAsync(InsertDataCapId)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadInsertEventCapability()
        {
            DataValue result = await ReadNodeValueAsync(InsertEventCapId)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task BrowseServerForLocationsObjectAsync()
        {
            // Per Part 5 §8.2.12 the standard "Locations" object (i=31915)
            // is organized from the Objects folder (i=85), not from the
            // Server node (i=2253). Browse the Objects folder forward to
            // locate it.
            List<ReferenceDescription> refs =
                await BrowseForwardAsync(ObjectIds.ObjectsFolder)
                    .ConfigureAwait(false);
            if (!HasChildWithName(refs, "Locations"))
            {
                Assert.Ignore("Locations object not found.");
            }
        }

        [Test]
        public async Task ReadMaxNodesPerMethodCallAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_OperationLimits_MaxNodesPerMethodCall)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task VerifyModelChangeStructureDataTypeExistsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                new NodeId(DataTypes.ModelChangeStructureDataType),
                Attributes.BrowseName).ConfigureAwait(false);
            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Fail(
                    "ModelChangeStructureDataType not found.");
            }

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task VerifyGeneralModelChangeEventTypeExistsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectTypeIds.GeneralModelChangeEventType,
                Attributes.BrowseName).ConfigureAwait(false);
            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Fail(
                    "GeneralModelChangeEventType not found.");
            }

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task VerifyOrderedListTypeExists()
        {
            DataValue result = await ReadAttributeAsync(
                OrderedListTypeId, Attributes.BrowseName)
                .ConfigureAwait(false);
            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Fail("OrderedListType not found.");
            }

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadMaxNodesPerNodeManagement()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_OperationLimits_MaxNodesPerNodeManagement)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadAddNodesLimit()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_OperationLimits_MaxNodesPerNodeManagement)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadDeleteReferencesLimit()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_OperationLimits_MaxNodesPerNodeManagement)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadDeleteNodesLimit()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_OperationLimits_MaxNodesPerNodeManagement)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadMaxQueryContinuationPointsQueryAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_MaxQueryContinuationPoints)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task BrowseStateMachineTypeForCurrentStateAsync()
        {
            List<ReferenceDescription> refs =
                await BrowseForwardAsync(StateMachineTypeId)
                    .ConfigureAwait(false);
            Assert.That(
                HasChildWithName(refs, "CurrentState"), Is.True,
                "StateMachineType should have CurrentState.");
        }

        [Test]
        public async Task ReadServerStatusSubvariablesExist()
        {
            DataValue statusResult = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus)
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(statusResult.StatusCode), Is.True);

            List<ReferenceDescription> refs = await BrowseForwardAsync(
                VariableIds.Server_ServerStatus)
                .ConfigureAwait(false);
            Assert.That(refs, Is.Not.Empty,
                "ServerStatus should have subvariables.");
            Assert.That(
                HasChildWithName(refs, "StartTime"), Is.True,
                "ServerStatus should have StartTime.");
            Assert.That(
                HasChildWithName(refs, "CurrentTime"), Is.True,
                "ServerStatus should have CurrentTime.");
            Assert.That(
                HasChildWithName(refs, "State"), Is.True,
                "ServerStatus should have State.");
        }

        [Test]
        public async Task SemanticChangeEventTypeExistsAsync()
        {
            await AssertNodeExistsAsync(
                SemanticChangeEventTypeId, "SemanticChangeEventType")
                .ConfigureAwait(false);
        }

        [Test]
        public async Task VerifySemanticChangeBitAsync()
        {
            DataValue result = await ReadAttributeAsync(
                SemanticChangeEventTypeId, Attributes.BrowseName)
                .ConfigureAwait(false);
            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Fail(
                    "SemanticChangeEventType not found.");
            }

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ChoiceStateTypeExistsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectTypeIds.ChoiceStateType, Attributes.BrowseName)
                .ConfigureAwait(false);
            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Fail("ChoiceStateType not found.");
            }

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task TimeZoneDataTypeExistsAsync()
        {
            await AssertNodeExistsAsync(
                new NodeId(DataTypes.TimeZoneDataType),
                "TimeZoneDataType").ConfigureAwait(false);
        }

        [Test]
        public async Task ReadServerStatusCurrentTimeAndCheckTimeZoneAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_CurrentTime)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task VerifyLocalTimeEventFieldAvailableAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectTypeIds.BaseEventType, Attributes.BrowseName)
                .ConfigureAwait(false);
            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Fail(
                    "LocalTime event field not available.");
            }

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task BrowseMultiStateDiscreteTypeForValueAsTextAsync()
        {
            // ValueAsText is a mandatory property of MultiStateValueDiscreteType
            // (i=11238) per Part 8 §5.3.3.4. The plain MultiStateDiscreteType
            // (i=2376) only declares EnumStrings. The test name is historical;
            // the conformance unit "Base Info ValueAsText" targets the
            // MultiStateValueDiscreteType. The previous hardcoded NodeId 2688
            // does not exist in the standard nodeset.
            List<ReferenceDescription> refs =
                await BrowseForwardAsync(VariableTypeIds.MultiStateValueDiscreteType)
                    .ConfigureAwait(false);
            if (!HasChildWithName(refs, "ValueAsText"))
            {
                Assert.Ignore("MultiStateValueDiscreteType ValueAsText not available in ReferenceServer.");
            }
        }

        [Test]
        public async Task VerifyMultiStateValueDiscreteTypeEnumValuesAsync()
        {
            List<ReferenceDescription> refs = await BrowseForwardAsync(
                VariableTypeIds.MultiStateValueDiscreteType)
                .ConfigureAwait(false);
            Assert.That(
                HasChildWithName(refs, "EnumValues"), Is.True,
                "MultiStateValueDiscreteType should have EnumValues.");
        }

        [Test]
        public async Task ModellingRuleMandatoryPlaceholderExists()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectIds.ModellingRule_MandatoryPlaceholder,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ModellingRuleOptionalPlaceholderExists()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectIds.ModellingRule_OptionalPlaceholder,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        private static readonly NodeId FiniteStateMachineTypeId = new(2771);
        private static readonly NodeId StateMachineTypeId = new(2299);
        private static readonly NodeId ServerTypeId = new(2004);
        private static readonly NodeId SemanticChangeEventTypeId = new(2738);
        private static readonly NodeId OrderedListTypeId = new(23518);
        private static readonly NodeId DeprecatedId = new(23562);
        private static readonly NodeId EUInformationId = new(887);
        private static readonly NodeId ExportNsId = new(11615);
        private static readonly NodeId ImportNsId = new(11616);
        private static readonly NodeId HistoryCapsId = new(11192);
        private static readonly NodeId AccessHistDataId = new(11193);
        private static readonly NodeId AccessHistEventsId = new(11242);
        private static readonly NodeId InsertDataCapId = new(11196);
        private static readonly NodeId InsertEventCapId = new(11281);

        private async Task<DataValue> ReadNodeValueAsync(NodeId nodeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private async Task<DataValue> ReadAttributeAsync(
            NodeId nodeId, uint attributeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = attributeId
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private async Task<List<ReferenceDescription>> BrowseForwardAsync(
            NodeId nodeId,
            NodeId referenceTypeId = default)
        {
            NodeId refType = referenceTypeId.IsNull
                ? ReferenceTypeIds.HierarchicalReferences
                : referenceTypeId;

            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = refType,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            var refs = new List<ReferenceDescription>();
            if (response.Results[0].References != default)
            {
                foreach (ReferenceDescription r in response.Results[0].References)
                {
                    refs.Add(r);
                }
            }

            return refs;
        }

        private async Task AssertNodeExistsAsync(NodeId nodeId, string name)
        {
            DataValue result = await ReadAttributeAsync(
                nodeId, Attributes.BrowseName).ConfigureAwait(false);
            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Ignore($"{name} not found.");
            }
        }

        private static bool HasChildWithName(
            List<ReferenceDescription> references, string name)
        {
            return references.Any(r => r.BrowseName.Name == name);
        }
    }
}
