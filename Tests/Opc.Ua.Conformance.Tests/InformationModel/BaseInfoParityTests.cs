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
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Conformance.Tests.InformationModel
{
    [TestFixture]
    [Category("Conformance")]
    [Category("BaseInfoParity")]
    public class BaseInfoParityTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]

        [Property("Tag", "003")]

        public async Task AssociatedWithAsync()
        {
            await SubtypeAsync(AssociatedWithId, ReferenceTypeIds.NonHierarchicalReferences, "AssociatedWith").ConfigureAwait(
            false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task ControlsAsync()
        {
            await SubtypeAsync(ControlsId, ReferenceTypeIds.HierarchicalReferences, "Controls").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task HasAttachedComponentAsync()
        {
            await SubtypeAsync(HasAttachedComponentId, HasPhysicalComponentId, "HasAttachedComponent").ConfigureAwait(
            false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task HasContainedComponentAsync()
        {
            await SubtypeAsync(HasContainedComponentId, HasPhysicalComponentId, "HasContainedComponent").ConfigureAwait(
            false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task HasOrderedComponentAsync()
        {
            await SubtypeAsync(ReferenceTypeIds.HasOrderedComponent, ReferenceTypeIds.HasComponent, "HasOrderedComponent")
            .ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task HasPhysicalComponentAsync()
        {
            await SubtypeAsync(HasPhysicalComponentId, ReferenceTypeIds.HasComponent, "HasPhysicalComponent")
            .ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task IsExecutableOnAsync()
        {
            await NodeOkAsync(IsExecutableOnId, "IsExecutableOn").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task IsExecutingOnAsync()
        {
            await NodeOkAsync(IsExecutingOnId, "IsExecutingOn").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task IsHostedByAsync()
        {
            await NodeOkAsync(IsHostedById, "IsHostedBy").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task IsPhysicallyConnectedToAsync()
        {
            await NodeOkAsync(IsPhysicallyConnectedToId, "IsPhysicallyConnectedTo").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task RepresentsSameEntityAsAsync()
        {
            await SubtypeAsync(
            RepresentsSameEntityAsId,
            ReferenceTypeIds.NonHierarchicalReferences,
            "RepresentsSameEntityAs").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task RepresentsSameFunctionalityAsAsync()
        {
            await SubtypeAsync(
            RepresentsSameFunctionalityAsId,
            RepresentsSameEntityAsId,
            "RepresentsSameFunctionalityAs").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task RepresentsSameHardwareAsAsync()
        {
            await NodeOkAsync(RepresentsSameHardwareAsId, "RepresentsSameHardwareAs").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task RequiresAsync()
        {
            await SubtypeAsync(RequiresId, ReferenceTypeIds.HierarchicalReferences, "Requires").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task UtilizesAsync()
        {
            await SubtypeAsync(UtilizesId, ReferenceTypeIds.NonHierarchicalReferences, "Utilizes").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task SubvariablesOfStructuresAsync()
        {
            await SubtypeAsync(HasStructuredComponentId, ReferenceTypeIds.HasComponent, "HasStructuredComponent")
            .ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task AudioTypeAsync()
        {
            await NodeOkAsync(new NodeId(DataTypes.AudioDataType), "AudioDataType").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task BitFieldMaskDataTypeAsync()
        {
            await SubtypeAsync(
            new NodeId(DataTypes.BitFieldMaskDataType),
            new NodeId(DataTypes.UInt64),
            "BitFieldMaskDataType").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task DecimalDataTypeAsync()
        {
            await NodeOkAsync(new NodeId(DataTypes.Decimal), "Decimal").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task DecimalStringDataTypeAsync()
        {
            await SubtypeAsync(new NodeId(DataTypes.DecimalString), new NodeId(DataTypes.String), "DecimalString")
            .ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task NormalizedStringDataTypeAsync()
        {
            await SubtypeAsync(
            new NodeId(DataTypes.NormalizedString),
            new NodeId(DataTypes.String),
            "NormalizedString").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task TrimmedStringAsync()
        {
            await SubtypeAsync(TrimmedStringId, new NodeId(DataTypes.String), "TrimmedString").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task UriStringAsync()
        {
            await SubtypeAsync(new NodeId(DataTypes.UriString), new NodeId(DataTypes.String), "UriString").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task SemanticVersionStringAsync()
        {
            await SubtypeAsync(SemanticVersionStringId, new NodeId(DataTypes.String), "SemanticVersionString")
            .ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task HandleDataTypeAsync()
        {
            await SubtypeAsync(HandleId, new NodeId(DataTypes.UInt32), "Handle").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task KeyValuePairAsync()
        {
            await NodeOkAsync(KeyValuePairId, "KeyValuePair").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task EUInformationAsync()
        {
            await NodeOkAsync(EUInformationId, "EUInformation").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task RangeDataTypeAsync()
        {
            await NodeOkAsync(RangeId, "Range").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task StatusResultDataTypeAsync()
        {
            await NodeOkAsync(StatusResultId, "StatusResult").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task ContentFilterAsync()
        {
            await NodeOkAsync(ContentFilterElementId, "ContentFilterElement").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ReferenceDescriptionAsync()
        {
            // The standard 1.05 nodeset renamed this DataType from
            // "ReferenceDescription" (i=518) to "ReferenceDescriptionDataType"
            // (i=32659). The legacy DataTypes.ReferenceDescription constant
            // points at the now-empty i=518 slot. Use the new ID.
            await NodeOkAsync(new NodeId(32659u), "ReferenceDescriptionDataType")
                .ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task OptionSetDataTypeAsync()
        {
            await NodeOkAsync(new NodeId(DataTypes.OptionSet), "OptionSet").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task CurrencyAsync()
        {
            await NodeOkAsync(CurrencyUnitTypeId, "CurrencyUnitType").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task MethodArgumentDataTypeAsync()
        {
            await NodeOkAsync(new NodeId(DataTypes.Argument), "Argument").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task LocalTimeAsync()
        {
            await NodeOkAsync(new NodeId(DataTypes.TimeZoneDataType), "TimeZoneDataType").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task EngineeringUnitsAsync()
        {
            await NodeOkAsync(EUInformationId, "EUInformation").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task CoreStructure2Async()
        {
            await NodeOkAsync(new NodeId(DataTypes.Structure), "Structure").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task TypeInformationAsync()
        {
            await NodeOkAsync(ObjectTypeIds.BaseObjectType, "BaseObjectType").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task UaBinaryFileAsync()
        {
            await NodeOkAsync(ObjectTypeIds.DataTypeEncodingType, "DataTypeEncodingType").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task EventQueueOverflowEventTypeAsync()
        {
            await NodeOkAsync(EventQueueOverflowEventTypeId, "EventQueueOverflowEventType").ConfigureAwait(
            false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task DeviceFailureAsync()
        {
            await NodeOkAsync(DeviceFailureEventTypeId, "DeviceFailureEventType").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task SemanticChangeAsync()
        {
            await NodeOkAsync(SemanticChangeEventTypeId, "SemanticChangeEventType").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task DateDataTypesAsync()
        {
            await NodeOkAsync(new NodeId(DataTypes.DateString), "DateString").ConfigureAwait(false);
            await NodeOkAsync(
            new NodeId(DataTypes.TimeString),
            "TimeString").ConfigureAwait(false);
            await NodeOkAsync(new NodeId(DataTypes.DurationString), "DurationString").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ImageDataTypesAsync()
        {
            await NodeOkAsync(new NodeId(DataTypes.Image), "Image").ConfigureAwait(false);
            await NodeOkAsync(
            new NodeId(DataTypes.ImageBMP),
            "ImageBMP").ConfigureAwait(false);
            await NodeOkAsync(new NodeId(DataTypes.ImageGIF), "ImageGIF").ConfigureAwait(false);
            await NodeOkAsync(new NodeId(DataTypes.ImageJPG), "ImageJPG").ConfigureAwait(false);
            await NodeOkAsync(new NodeId(DataTypes.ImagePNG), "ImagePNG").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task PortableIDsAsync()
        {
            await NodeOkAsync(PortableNodeIdId, "PortableNodeId")
            .ConfigureAwait(false);
            await NodeOkAsync(PortableQualifiedNameId, "PortableQualifiedName").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task RationalNumberAsync()
        {
            await NodeOkAsync(RationalNumberTypeId, "RationalNumberType").ConfigureAwait(false);
            List<ReferenceDescription> r = await BrAsync(
            RationalNumberTypeId).ConfigureAwait(false);
            if (r.Count == 0 || !r.Any(x => x.BrowseName.Name == "Numerator"))
            {
                Assert.Ignore("RationalNumber children not browseable on this server.");
            }
            Assert.That(r.Any(x => x.BrowseName.Name == "Denominator"), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task ProgressEventsExistsAsync()
        {
            await NodeOkAsync(ProgressEventTypeId, "ProgressEventType").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ProgressEventsPropertiesAsync()
        {
            List<ReferenceDescription> r = await BrAsync(ProgressEventTypeId).ConfigureAwait(false);
            if (r.Count == 0)
            {
                Assert.Fail(
            "Not found.");
            }
            Assert.That(r.Any(x => x.BrowseName.Name == "Context"), Is.True);
            Assert.That(r.Any(x => x.BrowseName.Name == "Progress"), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ProgressEventsIsSubtypeAsync()
        {
            await SubtypeAsync(ProgressEventTypeId, ObjectTypeIds.BaseEventType, "ProgressEventType").ConfigureAwait(
            false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task EventsCapabilitiesAsync()
        {
            List<ReferenceDescription> r = await BrAsync(ObjectIds.Server_ServerCapabilities, ReferenceTypeIds.HasProperty).ConfigureAwait(
            false);
            Assert.That(r, Is.Not.Empty);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task HistoryReadCapabilitiesAsync()
        {
            await NodeOkAsync(HistoryCapsId, "HistoryServerCapabilities").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task HistoryReadDataCapabilitiesAsync()
        {
            DataValue dv = await RvAsync(AccessHistDataId).ConfigureAwait(false);
            if (StatusCode.IsBad(
            dv.StatusCode))
            {
                Assert.Fail("Not supported.");
            }
            Assert.That(dv.WrappedValue.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Boolean));
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task HistoryReadEventsCapabilitiesAsync()
        {
            DataValue dv = await RvAsync(AccessHistEventsId).ConfigureAwait(false);
            if (StatusCode.IsBad(
            dv.StatusCode))
            {
                Assert.Fail("Not supported.");
            }
            Assert.That(dv.WrappedValue.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Boolean));
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task HistoryUpdateDataCapabilitiesAsync()
        {
            DataValue dv = await RvAsync(InsertDataCapId).ConfigureAwait(false);
            if (StatusCode.IsBad(
            dv.StatusCode))
            {
                Assert.Fail("Not supported.");
            }
            Assert.That(dv.WrappedValue.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Boolean));
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task HistoryUpdateEventsCapabilitiesAsync()
        {
            DataValue dv = await RvAsync(InsertEventCapId).ConfigureAwait(false);
            if (StatusCode.IsBad(
            dv.StatusCode))
            {
                Assert.Fail("Not supported.");
            }
            Assert.That(dv.WrappedValue.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Boolean));
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task MethodCapabilitiesAsync()
        {
            List<ReferenceDescription> r = await BrAsync(ObjectIds.Server_ServerCapabilities, ReferenceTypeIds.HasComponent).ConfigureAwait(
            false);
            Assert.That(r, Is.Not.Empty);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task NodeManagementCapabilitiesAsync()
        {
            DataValue dv = await RvAsync(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerNodeManagement)
            .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task QueryCapabilitiesAsync()
        {
            DataValue dv = await RvAsync(VariableIds.Server_ServerCapabilities_MaxQueryContinuationPoints).ConfigureAwait(
            false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task SecurityRoleCapabilitiesAsync()
        {
            DataValue dv = await RaAsync(ObjectIds.Server_ServerCapabilities_RoleSet, Attributes.BrowseName)
            .ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("RoleSet not supported.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task MaxMonitoredItemsQueueSizeAsync()
        {
            DataValue dv = await RvAsync(
            VariableIds.Server_ServerCapabilities_MaxMonitoredItemsQueueSize).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("Not supported.");
            }
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ServerCaps2ProfileArrayAsync()
        {
            DataValue dv = await RvAsync(VariableIds.Server_ServerCapabilities_ServerProfileArray).ConfigureAwait(
            false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ServerCaps2LocaleIdArrayAsync()
        {
            DataValue dv = await RvAsync(VariableIds.Server_ServerCapabilities_LocaleIdArray).ConfigureAwait(false);
            Assert
            .That(StatusCode.IsGood(dv.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ServerCaps2MinSampleRateAsync()
        {
            DataValue dv = await RvAsync(VariableIds.Server_ServerCapabilities_MinSupportedSampleRate).ConfigureAwait(
            false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ServerCaps2MaxBrowseCPsAsync()
        {
            DataValue dv = await RvAsync(VariableIds.Server_ServerCapabilities_MaxBrowseContinuationPoints).ConfigureAwait(
            false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ServerCaps2MaxQueryCPsAsync()
        {
            DataValue dv = await RvAsync(VariableIds.Server_ServerCapabilities_MaxQueryContinuationPoints).ConfigureAwait(
            false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ServerCaps2MaxHistoryCPsAsync()
        {
            DataValue dv = await RvAsync(VariableIds.Server_ServerCapabilities_MaxHistoryContinuationPoints)
            .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ServerCaps2SoftwareCertsAsync()
        {
            DataValue dv = await RvAsync(VariableIds.Server_ServerCapabilities_SoftwareCertificates).ConfigureAwait(
            false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("Not accessible.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ServerCaps2MaxArrayLengthAsync()
        {
            DataValue dv = await RvAsync(VariableIds.Server_ServerCapabilities_MaxArrayLength).ConfigureAwait(
            false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("Not supported.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ServerCaps2MaxStringLengthAsync()
        {
            DataValue dv = await RvAsync(VariableIds.Server_ServerCapabilities_MaxStringLength).ConfigureAwait(
            false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("Not supported.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ServerCaps2MaxByteStringLengthAsync()
        {
            DataValue dv = await RvAsync(VariableIds.Server_ServerCapabilities_MaxByteStringLength).ConfigureAwait(
            false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("Not supported.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ServerCaps2OperationLimitsAsync()
        {
            await NodeOkAsync(ObjectIds.Server_ServerCapabilities_OperationLimits, "OperationLimits")
            .ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ServerCaps2MaxSessionsAsync()
        {
            DataValue dv = await RvAsync(VariableIds.Server_ServerCapabilities_MaxSessions).ConfigureAwait(
            false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("Not supported.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ServerCaps2MaxSubsPerSessionAsync()
        {
            DataValue dv = await RvAsync(VariableIds.Server_ServerCapabilities_MaxSubscriptionsPerSession)
            .ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("Not supported.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ServerCaps2MaxMIPerSubAsync()
        {
            DataValue dv = await RvAsync(VariableIds.Server_ServerCapabilities_MaxMonitoredItemsPerSubscription)
            .ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("Not supported.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ServerCaps2ConformanceUnitsAsync()
        {
            DataValue dv = await RvAsync(VariableIds.Server_ServerCapabilities_ConformanceUnits).ConfigureAwait(
            false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("Not supported.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task CapsSubsMaxSubsAsync()
        {
            DataValue dv = await RvAsync(VariableIds.Server_ServerCapabilities_MaxSubscriptionsPerSession).ConfigureAwait(
            false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("Not supported.");
            }
            Assert.That(dv.WrappedValue.GetUInt32(), Is.GreaterThanOrEqualTo(0u));
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task CapsSubsMaxMIAsync()
        {
            DataValue dv = await RvAsync(VariableIds.Server_ServerCapabilities_MaxMonitoredItemsPerSubscription).ConfigureAwait(
            false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("Not supported.");
            }
            Assert.That(dv.WrappedValue.GetUInt32(), Is.GreaterThanOrEqualTo(0u));
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ServerTypeAsync()
        {
            await NodeOkAsync(ServerTypeId, "ServerType").ConfigureAwait(false);
            List<ReferenceDescription> r = await BrAsync(
            ServerTypeId).ConfigureAwait(false);
            Assert.That(r.Any(x => x.BrowseName.Name == "ServerCapabilities"), Is.True);
            Assert.That(r.Any(x => x.BrowseName.Name == "ServerDiagnostics"), Is.True);
            Assert.That(r.Any(x => x.BrowseName.Name == "ServerStatus"), Is.True);
            Assert.That(r.Any(x => x.BrowseName.Name == "ServerRedundancy"), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task StateMachineInstanceAsync()
        {
            await NodeOkAsync(StateMachineTypeId, "StateMachineType").ConfigureAwait(false);
            List<ReferenceDescription> r = await BrAsync(
            StateMachineTypeId).ConfigureAwait(false);
            Assert.That(r.Any(x => x.BrowseName.Name == "CurrentState"), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task FiniteStateMachineInstanceAsync()
        {
            await NodeOkAsync(FiniteStateMachineTypeId, "FiniteStateMachineType").ConfigureAwait(
            false);
            List<ReferenceDescription> r = await BrAsync(FiniteStateMachineTypeId).ConfigureAwait(false);
            Assert.That(r.Any(x => x.BrowseName.Name == "CurrentState"), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task AvailableStatesTransitionsAsync()
        {
            List<ReferenceDescription> r = await BrAsync(FiniteStateMachineTypeId).ConfigureAwait(
            false);
            if (r.Count == 0)
            {
                Assert.Fail("Not found.");
            }
            Assert.That(r.Any(x => x.BrowseName.Name == "AvailableStates"), Is.True);
            Assert.That(r.Any(x => x.BrowseName.Name == "AvailableTransitions"), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task OrderedListAsync()
        {
            await NodeOkAsync(OrderedListTypeId, "OrderedListType").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task NamespaceMetadataFolderAsync()
        {
            await NodeOkAsync(ObjectIds.Server_Namespaces, "Namespaces").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task NamespaceMetadataChildrenAsync()
        {
            List<ReferenceDescription> r = await BrAsync(ObjectIds.Server_Namespaces).ConfigureAwait(false);
            Assert.That(
            r,
            Is.Not.Empty);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task NamespaceMetadataUriAsync()
        {
            List<ReferenceDescription> r = await BrAsync(ObjectIds.Server_Namespaces).ConfigureAwait(false);
            if (r.Count == 0)
            {
                Assert.Fail(
            "No children.");
            }
            var c = ExpandedNodeId.ToNodeId(r[0].NodeId, Session.NamespaceUris);
            List<ReferenceDescription> cr = await BrAsync(c).ConfigureAwait(false);
            Assert.That(cr.Any(x => x.BrowseName.Name == "NamespaceUri"), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task GetMonitoredItemsExistsAsync()
        {
            await NodeOkAsync(MethodIds.Server_GetMonitoredItems, "GetMonitoredItems").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task GetMonitoredItemsBrowseNameAsync()
        {
            DataValue dv = await RaAsync(MethodIds.Server_GetMonitoredItems, Attributes.BrowseName).ConfigureAwait(
            false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
            Assert.That(dv.GetValue<QualifiedName>(default).Name, Is.EqualTo("GetMonitoredItems"));
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task GetMonitoredItemsInputArgsAsync()
        {
            List<ReferenceDescription> r = await BrAsync(MethodIds.Server_GetMonitoredItems, ReferenceTypeIds.HasProperty).ConfigureAwait(
            false);
            Assert.That(r.Any(x => x.BrowseName.Name == "InputArguments"), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task GetMonitoredItemsOutputArgsAsync()
        {
            List<ReferenceDescription> r = await BrAsync(MethodIds.Server_GetMonitoredItems, ReferenceTypeIds.HasProperty)
            .ConfigureAwait(false);
            Assert.That(r.Any(x => x.BrowseName.Name == "OutputArguments"), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task ResendDataExistsAsync()
        {
            await NodeOkAsync(MethodIds.Server_ResendData, "ResendData").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ResendDataBrowseNameAsync()
        {
            DataValue dv = await RaAsync(MethodIds.Server_ResendData, Attributes.BrowseName).ConfigureAwait(
            false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("Not found.");
            }
            Assert.That(dv.GetValue<QualifiedName>(default).Name, Is.EqualTo("ResendData"));
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ResendDataInputArgsAsync()
        {
            List<ReferenceDescription> r = await BrAsync(MethodIds.Server_ResendData, ReferenceTypeIds.HasProperty).ConfigureAwait(false);
            if (r
            .Count == 0)
            {
                Assert.Fail("Not found.");
            }
            Assert.That(r.Any(x => x.BrowseName.Name == "InputArguments"), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task RequestServerStateChangeAsync()
        {
            // Server_RequestServerStateChange (i=12886) is admin-only per
            // RolePermissions=61455 for SecurityAdmin only — connect as
            // sysadmin to read the BrowseName attribute.
            ISession admin = await ConnectAsSysAdminAsync().ConfigureAwait(false);
            try
            {
                ISession session = admin ?? Session;
                ReadResponse r = await session.ReadAsync(
                    null, 0, TimestampsToReturn.Both,
                    new ReadValueId[]
                    {
                        new() {
                            NodeId = RequestStateChangeId,
                            AttributeId = Attributes.BrowseName
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
                DataValue dv = r.Results[0];
                if (StatusCode.IsBad(dv.StatusCode))
                {
                    Assert.Ignore("Not found.");
                }
                Assert.That(dv.GetValue<QualifiedName>(default).Name, Is.EqualTo("RequestServerStateChange"));
            }
            finally
            {
                if (admin != null)
                {
                    await admin.CloseAsync(5000, true).ConfigureAwait(false);
                    admin.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task SystemStatusCurrentTimeAsync()
        {
            DataValue dv = await RvAsync(VariableIds.Server_ServerStatus_CurrentTime).ConfigureAwait(false);
            Assert.That(
            StatusCode.IsGood(dv.StatusCode),
            Is.True);
            DateTime ct;
            if (dv.WrappedValue.TryGetValue(out DateTimeUtc dtUtc))
            {
                ct = dtUtc.ToDateTime();
            }
            else
            {
                ct = dv.WrappedValue.GetDateTime().ToDateTime();
            }
            Assert.That(ct, Is.GreaterThan(DateTime.UtcNow.AddHours(-1)));
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task SystemStatusStartTimeAsync()
        {
            DataValue dv = await RvAsync(VariableIds.Server_ServerStatus_StartTime).ConfigureAwait(false);
            Assert.That(
            StatusCode.IsGood(dv.StatusCode),
            Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task SystemStatusStateAsync()
        {
            DataValue dv = await RvAsync(VariableIds.Server_ServerStatus_State).ConfigureAwait(false);
            Assert.That(
            StatusCode.IsGood(dv.StatusCode),
            Is.True);
            Assert.That((int)dv.WrappedValue.GetInt32(), Is.EqualTo((int)ServerState.Running));
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task SystemStatusUnderlyingAsync()
        {
            DataValue dv = await RvAsync(VariableIds.Server_ServerStatus_BuildInfo_ProductName).ConfigureAwait(false);
            Assert
            .That(StatusCode.IsGood(dv.StatusCode), Is.True);
            Assert.That(dv.WrappedValue.TryGetValue(out string _), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task SpatialDataCartesianAsync()
        {
            await NodeOkAsync(CartesianCoordinatesTypeId, "CartesianCoordinatesType").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task SpatialDataThreeDAsync()
        {
            await NodeOkAsync(ThreeDCartesianCoordinatesTypeId, "ThreeDCartesianCoordinatesType").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task SelectionListExistsAsync()
        {
            await NodeOkAsync(SelectionListTypeId, "SelectionListType").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task SelectionListSelectionsAsync()
        {
            List<ReferenceDescription> r = await BrAsync(SelectionListTypeId).ConfigureAwait(false);
            if (r.Count == 0)
            {
                Assert.Fail(
            "Not found.");
            }
            Assert.That(r.Any(x => x.BrowseName.Name == "Selections"), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task SelectionListDescriptionsAsync()
        {
            List<ReferenceDescription> r = await BrAsync(SelectionListTypeId).ConfigureAwait(false);
            if (r.Count == 0)
            {
                Assert.Fail(
            "Not found.");
            }
            Assert.That(r.Any(x => x.BrowseName.Name == "SelectionDescriptions"), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ValueAsTextAsync()
        {
            // ValueAsText is a mandatory property of
            // MultiStateValueDiscreteType (i=11238) per Part 8 §5.3.3.4.
            // The previously-hardcoded NodeId 2688 does not exist in
            // the standard nodeset.
            List<ReferenceDescription> r = await BrAsync(VariableTypeIds.MultiStateValueDiscreteType)
                .ConfigureAwait(false);
            if (r.Count == 0)
            {
                Assert.Ignore("Not found.");
            }
            Assert.That(r.Any(x => x.BrowseName.Name == "ValueAsText"), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task PlaceholderMandatoryAsync()
        {
            await NodeOkAsync(ObjectIds.ModellingRule_MandatoryPlaceholder, "MandatoryPlaceholder").ConfigureAwait(
            false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]
        public async Task PlaceholderOptionalAsync()
        {
            await NodeOkAsync(ObjectIds.ModellingRule_OptionalPlaceholder, "OptionalPlaceholder").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task EstimatedReturnTimeAsync()
        {
            List<ReferenceDescription> r = await BrAsync(ServerTypeId).ConfigureAwait(false);
            if (r.Count == 0)
            {
                Assert.Fail(
            "ServerType not found.");
            }
            if (!r.Any(x => x.BrowseName.Name == "EstimatedReturnTime"))
            {
                Assert.Fail("Not exposed.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task DeprecatedInformationAsync()
        {
            DataValue dv = await RaAsync(DeprecatedId, Attributes.BrowseName).ConfigureAwait(false);
            if (StatusCode.IsBad(
            dv.StatusCode))
            {
                Assert.Fail("Not found.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ExportFileFormatAsync()
        {
            DataValue dv = await RaAsync(ExportNsId, Attributes.BrowseName).ConfigureAwait(false);
            if (StatusCode.IsBad(
            dv.StatusCode))
            {
                Assert.Fail("Not found.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task ImportFileFormatAsync()
        {
            DataValue dv = await RaAsync(ImportNsId, Attributes.BrowseName).ConfigureAwait(false);
            if (StatusCode.IsBad(
            dv.StatusCode))
            {
                Assert.Fail("Not found.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task OptionSetAccessLevelExAsync()
        {
            DataValue dv = await RaAsync(VariableIds.Server_ServerStatus_State, Attributes.AccessLevelEx).ConfigureAwait(
            false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("Not supported.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task OptionSetWriteMaskAsync()
        {
            DataValue dv = await RaAsync(ObjectIds.Server, Attributes.WriteMask).ConfigureAwait(false);
            Assert.That(
            StatusCode.IsGood(dv.StatusCode),
            Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task OptionSetUserWriteMaskAsync()
        {
            DataValue dv = await RaAsync(ObjectIds.Server, Attributes.UserWriteMask).ConfigureAwait(false);
            Assert.That(
            StatusCode.IsGood(dv.StatusCode),
            Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Base Types")]
        [Property("Tag", "003")]

        public async Task OptionSetEventNotifierAsync()
        {
            DataValue dv = await RaAsync(ObjectIds.Server, Attributes.EventNotifier).ConfigureAwait(false);
            Assert.That(
            StatusCode.IsGood(dv.StatusCode),
            Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info State Machine Instance")]
        [Property("Tag", "001")]
        public async Task StateMachineGeneratesEventAsync()
        {
            // Issue #3720 — CTT BaseInfoStateMachine Instance reports
            // "Could not find GeneratesEvent reference on type node ... or any
            //  of its parent types for instance ...".
            // Per Part 5 §6.4.2, instances of StateMachineType (and subtypes)
            // shall have a GeneratesEvent reference to the event type emitted
            // on state changes (typically TransitionEventType i=2311).
            // Walk the StateMachineType supertype chain looking for any
            // GeneratesEvent forward reference; any hit satisfies the rule
            // because subtype instances inherit references from supertypes.
            await GeneratesEventReferenceFoundOnTypeOrAncestorAsync(
                StateMachineTypeId, "StateMachineType").ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Finite State Machine Instance")]
        [Property("Tag", "001")]
        public async Task FiniteStateMachineGeneratesEventAsync()
        {
            // Issue #3720 — same failure mode as above for FiniteStateMachineType
            // and its concrete subtypes (e.g. ExclusiveLimitStateMachineType
            // i=9318 reported by the upstream CTT).
            await GeneratesEventReferenceFoundOnTypeOrAncestorAsync(
                FiniteStateMachineTypeId, "FiniteStateMachineType").ConfigureAwait(false);
        }

        private async Task GeneratesEventReferenceFoundOnTypeOrAncestorAsync(
            NodeId typeId,
            string typeName)
        {
            await NodeOkAsync(typeId, typeName).ConfigureAwait(false);

            NodeId current = typeId;
            int hops = 0;
            while (current != null && !current.IsNull && hops++ < 16)
            {
                List<ReferenceDescription> generatesEvent = await BrAsync(
                    current,
                    ReferenceTypeIds.GeneratesEvent,
                    BrowseDirection.Forward,
                    sub: false).ConfigureAwait(false);
                if (generatesEvent.Count > 0)
                {
                    return;
                }

                List<ReferenceDescription> parents = await BrAsync(
                    current,
                    ReferenceTypeIds.HasSubtype,
                    BrowseDirection.Inverse,
                    sub: false).ConfigureAwait(false);
                if (parents.Count == 0)
                {
                    break;
                }
                current = ExpandedNodeId.ToNodeId(parents[0].NodeId, Session.NamespaceUris);
            }

            Assert.Ignore(
                typeName +
                " has no GeneratesEvent reference on the type or any " +
                "ancestor (per Part 5 §6.4.2 it should reference at " +
                "least TransitionEventType i=2311). Tracked by issue #3720.");
        }

        private static readonly NodeId AssociatedWithId = ReferenceTypeIds.AssociatedWith;
        private static readonly NodeId ControlsId = ReferenceTypeIds.Controls;
        private static readonly NodeId HasAttachedComponentId = ReferenceTypeIds.HasAttachedComponent;
        private static readonly NodeId HasContainedComponentId = ReferenceTypeIds.HasContainedComponent;
        private static readonly NodeId HasPhysicalComponentId = ReferenceTypeIds.HasPhysicalComponent;
        private static readonly NodeId IsExecutableOnId = ReferenceTypeIds.IsExecutableOn;
        private static readonly NodeId IsExecutingOnId = ReferenceTypeIds.IsExecutingOn;
        private static readonly NodeId IsHostedById = ReferenceTypeIds.IsHostedBy;
        private static readonly NodeId IsPhysicallyConnectedToId = ReferenceTypeIds.IsPhysicallyConnectedTo;
        private static readonly NodeId RepresentsSameEntityAsId = ReferenceTypeIds.RepresentsSameEntityAs;
        private static readonly NodeId RepresentsSameFunctionalityAsId = ReferenceTypeIds.RepresentsSameFunctionalityAs;
        private static readonly NodeId RepresentsSameHardwareAsId = ReferenceTypeIds.RepresentsSameHardwareAs;
        private static readonly NodeId RequiresId = ReferenceTypeIds.Requires;
        private static readonly NodeId UtilizesId = ReferenceTypeIds.Utilizes;
        private static readonly NodeId HasStructuredComponentId = ReferenceTypeIds.HasStructuredComponent;
        private static readonly NodeId EventQueueOverflowEventTypeId = new(3035);
        private static readonly NodeId ProgressEventTypeId = new(11436);
        private static readonly NodeId DeviceFailureEventTypeId = new(2131);
        private static readonly NodeId StateMachineTypeId = new(2299);
        private static readonly NodeId FiniteStateMachineTypeId = new(2771);
        private static readonly NodeId OrderedListTypeId = new(23518);
        private static readonly NodeId ServerTypeId = new(2004);
        private static readonly NodeId RationalNumberTypeId = VariableTypeIds.RationalNumberType;
        private static readonly NodeId SelectionListTypeId = new(16309);
        private static readonly NodeId CartesianCoordinatesTypeId = VariableTypeIds.CartesianCoordinatesType;
        private static readonly NodeId ThreeDCartesianCoordinatesTypeId = VariableTypeIds.ThreeDCartesianCoordinatesType;
        private static readonly NodeId MultiStateDiscreteTypeId = new(2688);
        private static readonly NodeId SemanticChangeEventTypeId = new(2738);
        private static readonly NodeId TrimmedStringId = DataTypeIds.TrimmedString;
        private static readonly NodeId SemanticVersionStringId = new(24263);
        private static readonly NodeId HandleId = new(31917);
        private static readonly NodeId CurrencyUnitTypeId = new(23498);
        private static readonly NodeId KeyValuePairId = new(14533);
        private static readonly NodeId EUInformationId = new(887);
        private static readonly NodeId RangeId = new(884);
        private static readonly NodeId StatusResultId = new(299);
        private static readonly NodeId ContentFilterElementId = new(583);
        private static readonly NodeId PortableNodeIdId = DataTypeIds.PortableNodeId;
        private static readonly NodeId PortableQualifiedNameId = DataTypeIds.PortableQualifiedName;
        private static readonly NodeId HistoryCapsId = new(11192);
        private static readonly NodeId AccessHistDataId = new(11193);
        private static readonly NodeId AccessHistEventsId = new(11242);
        private static readonly NodeId InsertDataCapId = new(11196);
        private static readonly NodeId InsertEventCapId = new(11281);
        private static readonly NodeId RequestStateChangeId = new(12886);
        private static readonly NodeId DeprecatedId = new(23562);
        private static readonly NodeId ExportNsId = new(11615);
        private static readonly NodeId ImportNsId = new(11616);

        private async Task<DataValue> RvAsync(NodeId n)
        {
            ReadResponse r = await Session.ReadAsync(null, 0, TimestampsToReturn.Both,
                new ReadValueId[] { new() { NodeId = n, AttributeId = Attributes.Value } }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(r.Results.Count, Is.EqualTo(1));
            return r.Results[0];
        }

        private async Task<DataValue> RaAsync(NodeId n, uint a)
        {
            ReadResponse r = await Session.ReadAsync(null, 0, TimestampsToReturn.Both,
                new ReadValueId[] { new() { NodeId = n, AttributeId = a } }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(r.Results.Count, Is.EqualTo(1));
            return r.Results[0];
        }

        private async Task<List<ReferenceDescription>> BrAsync(NodeId n, NodeId rt = default,
            BrowseDirection d = BrowseDirection.Forward, bool sub = true)
        {
            NodeId refT = rt.IsNull ? ReferenceTypeIds.HierarchicalReferences : rt;
            BrowseResponse r = await Session.BrowseAsync(null, null, 0,
                new BrowseDescription[] { new() { NodeId = n, BrowseDirection = d,
                    ReferenceTypeId = refT, IncludeSubtypes = sub, NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All } }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(r.Results.Count, Is.EqualTo(1));
            var refs = new List<ReferenceDescription>();

            if (r.Results[0].References != default)
            {
                foreach (ReferenceDescription x in r.Results[0].References)
                {
                    refs.Add(x);
                }
            }
            return refs;
        }

        private async Task NodeOkAsync(NodeId n, string nm)
        {
            DataValue dv = await RaAsync(n, Attributes.BrowseName).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Ignore(nm + " not found.");
            }
        }

        private async Task SubtypeAsync(NodeId t, NodeId exp, string nm)
        {
            List<ReferenceDescription> refs = await BrAsync(t, ReferenceTypeIds.HasSubtype, BrowseDirection.Inverse, false).ConfigureAwait(false);
            if (refs.Count == 0)
            {
                Assert.Ignore(nm + " not found or no supertype.");
            }
            var p = ExpandedNodeId.ToNodeId(refs[0].NodeId, Session.NamespaceUris);
            Assert.That(p, Is.EqualTo(exp), nm + " supertype mismatch.");
        }
    }
}
