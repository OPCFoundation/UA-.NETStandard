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
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;
using Opc.Ua.Test;
using Quickstarts.Servers;
using Range = Opc.Ua.Range;

namespace Quickstarts.ReferenceServer
{
    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    [NodeManager(NamespaceUri = Namespaces.ReferenceServer)]
    public partial class ReferenceNodeManager : IConformanceContributor
    {
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public ReferenceNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            bool useSamplingGroups)
            : base(
                  server,
                  configuration,
                  useSamplingGroups,
                  server.Telemetry.CreateLogger<ReferenceNodeManager>(),
                  Namespaces.ReferenceServer)
        {
            SystemContext.NodeIdFactory = this;

            // use suitable defaults if no configuration exists.
        }

        /// <summary>
        /// The conformance units this node manager enables: the reference
        /// server's always-supported base set, plus the Historical Access units
        /// once history archiving has been turned on (see
        /// <see cref="EnableHistoryArchivingAsync"/>), so the server advertises
        /// the HA facet only when the feature is actually present.
        /// </summary>
        public ArrayOf<QualifiedName> ConformanceUnits =>
            m_historian != null ? s_baseWithHistoricalConformanceUnits : s_baseConformanceUnits;

        /// <summary>
        /// The server profile URIs this node manager enables — the Historical
        /// Raw Data and Historical Aggregate facets when history archiving is on.
        /// </summary>
        public ArrayOf<string> ServerProfiles =>
            m_historian != null ? s_historicalAccessProfiles : [];

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Dispose the simulation timer first so the threadpool stops
                // scheduling DoSimulation callbacks before the semaphore is
                // disposed. DoSimulation's own try/catch swallows the racy
                // ObjectDisposedException on m_semaphore if a callback was
                // already in-flight when Timer.Dispose() returned — that's
                // an acceptable trade-off versus blocking Dispose on a
                // Timer.Dispose(WaitHandle) which itself can throw a worse
                // unhandled ObjectDisposedException when the supplied
                // WaitHandle is collected before the runtime signals it.
                m_simulationTimer?.Dispose();
                m_simulationTimer = null;
                m_historian?.Dispose();
                m_historian = null;

                m_semaphore?.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (node is BaseInstanceState instance &&
                instance.Parent != null &&
                instance.Parent.NodeId.TryGetValue(out string id))
            {
                return new NodeId(
                    id + "_" + instance.SymbolicName,
                    instance.Parent.NodeId.NamespaceIndex);
            }

            return node.NodeId;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Enables the OPC UA NodeManagement service set (AddNodes /
        /// DeleteNodes / AddReferences / DeleteReferences) so that conformance
        /// tests and clients can mutate the address space at runtime. New
        /// nodes live in this node manager's namespace; cross-NodeManager
        /// references are written through <see cref="MasterNodeManager"/>.
        /// </remarks>
        public override bool AllowNodeManagement => true;

        private static bool IsAnalogType(BuiltInType builtInType)
        {
            switch (builtInType)
            {
                case BuiltInType.Byte:
                case BuiltInType.UInt16:
                case BuiltInType.UInt32:
                case BuiltInType.UInt64:
                case BuiltInType.SByte:
                case BuiltInType.Int16:
                case BuiltInType.Int32:
                case BuiltInType.Int64:
                case BuiltInType.Float:
                case BuiltInType.Double:
                    return true;
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    return false;
                default:
                    Debug.Fail($"Unexpected BuiltInType {builtInType}");
                    return false;
            }
        }

        private static Range GetAnalogRange(BuiltInType builtInType)
        {
            switch (builtInType)
            {
                case BuiltInType.UInt16:
                    return new Range(ushort.MaxValue, ushort.MinValue);
                case BuiltInType.UInt32:
                    return new Range(uint.MaxValue, uint.MinValue);
                case BuiltInType.UInt64:
                    return new Range(ulong.MaxValue, ulong.MinValue);
                case BuiltInType.SByte:
                    return new Range(sbyte.MaxValue, sbyte.MinValue);
                case BuiltInType.Int16:
                    return new Range(short.MaxValue, short.MinValue);
                case BuiltInType.Int32:
                    return new Range(int.MaxValue, int.MinValue);
                case BuiltInType.Int64:
                    return new Range(long.MaxValue, long.MinValue);
                case BuiltInType.Float:
                    return new Range(float.MaxValue, float.MinValue);
                case BuiltInType.Double:
                    return new Range(double.MaxValue, double.MinValue);
                case BuiltInType.Byte:
                    return new Range(byte.MaxValue, byte.MinValue);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    return new Range(sbyte.MaxValue, sbyte.MinValue);
                default:
                    Debug.Fail($"Unexpected BuiltInType {builtInType}");
                    return new Range(sbyte.MaxValue, sbyte.MinValue);
            }
        }

        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        /// <remarks>
        /// The externalReferences is an out parameter that allows the node manager to link to nodes
        /// in other node managers. For example, the 'Objects' node is managed by the CoreNodeManager and
        /// should have a reference to the root folder node(s) exposed by this node manager.
        /// </remarks>
        protected override async ValueTask AddReverseReferencesAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.AddReverseReferencesAsync(externalReferences, cancellationToken).ConfigureAwait(false);

            await m_semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                FolderState root = CreateFolder("CTT");
                root.EventNotifier = EventNotifiers.SubscribeToEvents;
                await AddRootNotifierAsync(root, cancellationToken).ConfigureAwait(false);

                try
                {
                    ResetRandomGenerator(1);
                    BaseDataVariableState scalarInstructions = CreateVariable("Scalar_Instructions");
                    scalarInstructions.Value
                        = "A library of Read/Write Variables of all supported data-types.";

                    const string scalarStatic = "Scalar_Static_";
                    BaseDataVariableState floatVal = CreateVariable(scalarStatic + "Float")
                            .MinimumSamplingInterval(100);
                    floatVal.Value = (float)5;


                    CreateVariable(scalarStatic + "XmlElement")
                            .MinimumSamplingInterval(100);

                    BaseDataVariableState decimalVariable = CreateVariable(scalarStatic + "Decimal");
                    // Set an arbitrary precision decimal value.
                    var largeInteger = BigInteger.Parse(
                        "1234567890123546789012345678901234567890123456789012345",
                        CultureInfo.InvariantCulture);
                    decimalVariable.Value = Variant.FromStructure(new DecimalDataType
                    {
                        Scale = 100,
                        Value = largeInteger.ToByteString()
                    });

                    // Enumeration variable (NodeClass is a concrete subtype of Enumeration)
                    BaseDataVariableState enumerationVariable = CreateVariable(scalarStatic + "Enumeration");
                    enumerationVariable.Value = new Variant((int)NodeClass.Object);

                    // Image type variables (ByteString subtypes)

                    // A node that advertises the NonatomicRead and NonatomicWrite
                    // extension flags in its AccessLevelEx attribute so clients (and
                    // the CTT) can exercise the extended access-level bits.
                    BaseDataVariableState nonatomicVariable = CreateVariable(scalarStatic + "NonatomicReadWrite");
                    nonatomicVariable.AccessLevel = AccessLevels.CurrentReadOrWrite;
                    nonatomicVariable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
                    nonatomicVariable.AccessLevelEx =
                        AccessLevels.CurrentReadOrWrite
                        | (uint)AccessLevelExType.NonatomicRead
                        | (uint)AccessLevelExType.NonatomicWrite;

                    // A historizing node whose historian does not support server
                    // timestamps (registered with ServerTimestampSupported = false in
                    // EnableHistoryArchivingAsync). Backs the CTT
                    // "HA Profile > NodeDoesNotSupportServerTimestamp" slot.

                    ResetRandomGenerator(2);
                    const string staticArrays = "Scalar_Static_Arrays_";


                    BaseDataVariableState doubleArrayVar = CreateVariable(staticArrays + "Double");
                    // Set the first elements of the array to a smaller value.
                    double[] doubleArrayVal = ((ArrayOf<double>)doubleArrayVar.Value).ToArray()!;
                    doubleArrayVal[0] %= 10E+10;
                    doubleArrayVal[1] %= 10E+10;
                    doubleArrayVal[2] %= 10E+10;
                    doubleArrayVal[3] %= 10E+10;
                    doubleArrayVar.Value = Variant.From(doubleArrayVal.ToArrayOf());


                    BaseDataVariableState floatArrayVar = CreateVariable(staticArrays + "Float");
                    // Set the first elements of the array to a smaller value.
                    float[] floatArrayVal = ((ArrayOf<float>)floatArrayVar.Value).ToArray()!;
                    floatArrayVal[0] %= 0xf10E + 4;
                    floatArrayVal[1] %= 0xf10E + 4;
                    floatArrayVal[2] %= 0xf10E + 4;
                    floatArrayVal[3] %= 0xf10E + 4;
                    floatArrayVar.Value = Variant.From(floatArrayVal.ToArrayOf());


                    BaseDataVariableState stringArrayVar = CreateVariable(staticArrays + "String");
                    stringArrayVar.Value = Variant.From(
                    [
                        "Лошадь_ Пурпурово( Змейка( Слон",
                        "猪 绿色 绵羊 大象~ 狗 菠萝 猪鼠猪 绿色 绵羊 大象~ 狗 菠萝 猪鼠",
                        "Лошадь Овцы Голубика Овцы Змейка",
                        "Чернота` Дракон Бело Дракон",
                        "Horse# Black Lemon Lemon Grape",
                        "猫< パイナップル; ドラゴン 犬 モモ",
                        "레몬} 빨간% 자주색 쥐 백색; 들",
                        "Yellow Sheep Peach Elephant Cow",
                        "Крыса Корова Свинья Собака Кот",
                        "龙_ 绵羊 大象 芒果; 猫'"
                    ]);


                    ResetRandomGenerator(3);
                    const string staticArrays2D = "Scalar_Static_Arrays2D_";
                    CreateVariable(staticArrays2D + "LocalizedText")
                            .MinimumSamplingInterval(1000);
                    CreateVariable(staticArrays2D + "XmlElement")
                            .MinimumSamplingInterval(1000);

                    ResetRandomGenerator(4);
                    const string staticArraysDynamic = "Scalar_Static_ArrayDynamic_";
                    CreateVariable(staticArraysDynamic + "LocalizedText")
                            .MinimumSamplingInterval(1000);
                    CreateVariable(staticArraysDynamic + "QualifiedName")
                            .MinimumSamplingInterval(1000);
                    CreateVariable(staticArraysDynamic + "XmlElement")
                            .MinimumSamplingInterval(1000);

                    ResetRandomGenerator(5);
                    FolderState massFolder = CreateFolder("Scalar_Static_Mass");

                    ResetRandomGenerator(6);
                    FolderState simulationFolder = CreateFolder("Scalar_Simulation");
                    const string scalarSimulation = "Scalar_Simulation_";
                    CreateDynamicVariable(scalarSimulation + "Boolean");
                    CreateDynamicVariable(scalarSimulation + "Byte");
                    CreateDynamicVariable(scalarSimulation + "ByteString");
                    CreateDynamicVariable(scalarSimulation + "DateTime");
                    CreateDynamicVariable(scalarSimulation + "Double");
                    CreateDynamicVariable(scalarSimulation + "Duration");
                    CreateDynamicVariable(scalarSimulation + "Float");
                    CreateDynamicVariable(scalarSimulation + "Guid");
                    CreateDynamicVariable(scalarSimulation + "Int16");
                    CreateDynamicVariable(scalarSimulation + "Int32");
                    CreateDynamicVariable(scalarSimulation + "Int64");
                    CreateDynamicVariable(scalarSimulation + "Integer");
                    CreateDynamicVariable(scalarSimulation + "LocaleId");
                    CreateDynamicVariable(scalarSimulation + "LocalizedText");
                    CreateDynamicVariable(scalarSimulation + "NodeId");
                    CreateDynamicVariable(scalarSimulation + "Number");
                    CreateDynamicVariable(scalarSimulation + "QualifiedName");
                    CreateDynamicVariable(scalarSimulation + "SByte");
                    CreateDynamicVariable(scalarSimulation + "String");
                    CreateDynamicVariable(scalarSimulation + "UInt16");
                    CreateDynamicVariable(scalarSimulation + "UInt32");
                    CreateDynamicVariable(scalarSimulation + "UInt64");
                    CreateDynamicVariable(scalarSimulation + "UInteger");
                    CreateDynamicVariable(scalarSimulation + "UtcTime");
                    CreateDynamicVariable(scalarSimulation + "Variant");
                    CreateDynamicVariable(scalarSimulation + "XmlElement");

                    BaseDataVariableState intervalVariable = CreateVariable(scalarSimulation + "Interval");
                    intervalVariable.Value = m_simulationInterval;
                    intervalVariable.OnSimpleWriteValue = OnWriteInterval;

                    BaseDataVariableState enabledVariable = CreateVariable(scalarSimulation + "Enabled");
                    enabledVariable.Value = m_simulationEnabled;
                    enabledVariable.OnSimpleWriteValue = OnWriteEnabled;

                    ResetRandomGenerator(7);
                    const string simulationArrays = "Scalar_Simulation_Arrays_";
                    CreateDynamicVariable(simulationArrays + "Boolean");
                    CreateDynamicVariable(simulationArrays + "Byte");
                    CreateDynamicVariable(simulationArrays + "ByteString");
                    CreateDynamicVariable(simulationArrays + "DateTime");
                    CreateDynamicVariable(simulationArrays + "Double");
                    CreateDynamicVariable(simulationArrays + "Duration");
                    CreateDynamicVariable(simulationArrays + "Float");
                    CreateDynamicVariable(simulationArrays + "Guid");
                    CreateDynamicVariable(simulationArrays + "Int16");
                    CreateDynamicVariable(simulationArrays + "Int32");
                    CreateDynamicVariable(simulationArrays + "Int64");
                    CreateDynamicVariable(simulationArrays + "Integer");
                    CreateDynamicVariable(simulationArrays + "LocaleId");
                    CreateDynamicVariable(simulationArrays + "LocalizedText");
                    CreateDynamicVariable(simulationArrays + "NodeId");
                    CreateDynamicVariable(simulationArrays + "Number");
                    CreateDynamicVariable(simulationArrays + "QualifiedName");
                    CreateDynamicVariable(simulationArrays + "SByte");
                    CreateDynamicVariable(simulationArrays + "String");
                    CreateDynamicVariable(simulationArrays + "UInt16");
                    CreateDynamicVariable(simulationArrays + "UInt32");
                    CreateDynamicVariable(simulationArrays + "UInt64");
                    CreateDynamicVariable(simulationArrays + "UInteger");
                    CreateDynamicVariable(simulationArrays + "UtcTime");
                    CreateDynamicVariable(simulationArrays + "Variant");
                    CreateDynamicVariable(simulationArrays + "XmlElement");

                    ResetRandomGenerator(8);
                    const string massSimulation = "Scalar_Simulation_Mass_";
                    CreateDynamicVariables(
                        massSimulation + "Boolean",
                        "Boolean",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "Byte",
                        "Byte",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "ByteString",
                        "ByteString",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "DateTime",
                        "DateTime",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "Double",
                        "Double",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "Duration",
                        "Duration",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "Float",
                        "Float",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "Guid",
                        "Guid",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "Int16",
                        "Int16",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "Int32",
                        "Int32",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "Int64",
                        "Int64",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "Integer",
                        "Integer",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "LocaleId",
                        "LocaleId",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "LocalizedText",
                        "LocalizedText",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "NodeId",
                        "NodeId",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "Number",
                        "Number",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "QualifiedName",
                        "QualifiedName",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "SByte",
                        "SByte",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "String",
                        "String",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "UInt16",
                        "UInt16",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "UInt32",
                        "UInt32",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "UInt64",
                        "UInt64",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "UInteger",
                        "UInteger",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "UtcTime",
                        "UtcTime",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "Variant",
                        "Variant",
                        100);
                    CreateDynamicVariables(
                        massSimulation + "XmlElement",
                        "XmlElement",
                        100);

                    ResetRandomGenerator(9);
                    BaseDataVariableState daInstructions = CreateVariable("DataAccess_Instructions");
                    daInstructions.Value
                        = "A library of Read/Write Variables of all supported data-types.";

                    const string daDataItem = "DataAccess_DataItem_";

#if NET8_0_OR_GREATER
                    BuiltInType[] builtInTypes = Enum.GetValues<BuiltInType>();
#else
                    var builtInTypes = (BuiltInType[])Enum.GetValues(typeof(BuiltInType));
#endif
                    foreach (BuiltInType builtInType in builtInTypes)
                    {
                        string name = builtInType.ToString();
                        DataItemState item = CreateDataItemVariable(
                            daDataItem + name,
                            builtInType,
                            ValueRanks.Scalar);

                        // set initial value to String.Empty for String node.
                        if (builtInType == BuiltInType.String)
                        {
                            item.Value = string.Empty;
                        }
                    }

                    ResetRandomGenerator(10);
                    const string daAnalogItem = "DataAccess_AnalogType_";

                    foreach (BuiltInType builtInType in builtInTypes)
                    {
                        if (IsAnalogType(builtInType))
                        {
                            string name = builtInType.ToString();
                            AnalogItemState item = CreateAnalogItemVariable(
                                daAnalogItem + name,
                                builtInType,
                                ValueRanks.Scalar);

                            if (builtInType is BuiltInType.Int64 or BuiltInType.UInt64)
                            {
                                // make test case without optional ranges
                                item.EngineeringUnits = null;
                                item.InstrumentRange = null;
                            }
                            else if (builtInType == BuiltInType.Float)
                            {
                                item.EURange!.Value.High = 0;
                                item.EURange.Value.Low = 0;
                            }

                            //set default value for Definition property
                            item.Definition?.Value = string.Empty;
                        }
                    }

                    ResetRandomGenerator(11);
                    const string daAnalogArray = "DataAccess_AnalogType_Array_";

                    CreateAnalogItemVariable(
                        daAnalogArray + "Byte",
                        BuiltInType.Byte,
                        ValueRanks.OneDimension,
                        new byte[] { 0,
                        1,
                        2,
                        3,
                        4,
                        5,
                        6,
                        7,
                        8,
                        9 }.ToArrayOf());
                    CreateAnalogItemVariable(
                        daAnalogArray + "Double",
                        BuiltInType.Double,
                        ValueRanks.OneDimension,
                        s_doubleArray);
                    CreateAnalogItemVariable(
                        daAnalogArray + "Duration",
                        DataTypeIds.Duration,
                        ValueRanks.OneDimension,
                        s_doubleArray,
                        null);
                    CreateAnalogItemVariable(
                        daAnalogArray + "Float",
                        BuiltInType.Float,
                        ValueRanks.OneDimension,
                        s_singleArray);
                    CreateAnalogItemVariable(
                        daAnalogArray + "Int16",
                        BuiltInType.Int16,
                        ValueRanks.OneDimension,
                        s_shortArray);
                    CreateAnalogItemVariable(
                        daAnalogArray + "Int32",
                        BuiltInType.Int32,
                        ValueRanks.OneDimension,
                        s_int32Array);
                    CreateAnalogItemVariable(
                        daAnalogArray + "Int64",
                        BuiltInType.Int64,
                        ValueRanks.OneDimension,
                        new long[] { 10,
                        11,
                        12,
                        13,
                        14,
                        15,
                        16,
                        17,
                        18,
                        19 }.ToArrayOf());
                    CreateAnalogItemVariable(
                        daAnalogArray + "Integer",
                        BuiltInType.Integer,
                        ValueRanks.OneDimension,
                        new long[] { 10,
                        11,
                        12,
                        13,
                        14,
                        15,
                        16,
                        17,
                        18,
                        19 }.ToArrayOf());
                    CreateAnalogItemVariable(
                        daAnalogArray + "Number",
                        BuiltInType.Number,
                        ValueRanks.OneDimension,
                        s_shortArray);
                    CreateAnalogItemVariable(
                        daAnalogArray + "SByte",
                        BuiltInType.SByte,
                        ValueRanks.OneDimension,
                        new sbyte[] { 10,
                        20,
                        30,
                        40,
                        50,
                        60,
                        70,
                        80,
                        90 }.ToArrayOf());
                    CreateAnalogItemVariable(
                        daAnalogArray + "UInt16",
                        BuiltInType.UInt16,
                        ValueRanks.OneDimension,
                        new ushort[] { 20,
                        21,
                        22,
                        23,
                        24,
                        25,
                        26,
                        27,
                        28,
                        29 }.ToArrayOf());
                    CreateAnalogItemVariable(
                        daAnalogArray + "UInt32",
                        BuiltInType.UInt32,
                        ValueRanks.OneDimension,
                        new uint[] { 30,
                        31,
                        32,
                        33,
                        34,
                        35,
                        36,
                        37,
                        38,
                        39 }.ToArrayOf());
                    CreateAnalogItemVariable(
                        daAnalogArray + "UInt64",
                        BuiltInType.UInt64,
                        ValueRanks.OneDimension,
                        new ulong[] { 10,
                        11,
                        12,
                        13,
                        14,
                        15,
                        16,
                        17,
                        18,
                        19 }.ToArrayOf());
                    CreateAnalogItemVariable(
                        daAnalogArray + "UInteger",
                        BuiltInType.UInteger,
                        ValueRanks.OneDimension,
                        new ulong[] { 10,
                        11,
                        12,
                        13,
                        14,
                        15,
                        16,
                        17,
                        18,
                        19 }.ToArrayOf());
                    var doc1 = new XmlDocument();

                    ResetRandomGenerator(12);
                    const string daTwoStateDiscrete = "DataAccess_TwoStateDiscreteType_";

                    // Add our Nodes to the folder, and specify their customized discrete enumerations
                    CreateTwoStateDiscreteItemVariable(
                        daTwoStateDiscrete + "001",
                        "red",
                        "blue");
                    CreateTwoStateDiscreteItemVariable(
                        daTwoStateDiscrete + "002",
                        "open",
                        "close");
                    CreateTwoStateDiscreteItemVariable(
                        daTwoStateDiscrete + "003",
                        "up",
                        "down");
                    CreateTwoStateDiscreteItemVariable(
                        daTwoStateDiscrete + "004",
                        "left",
                        "right");
                    CreateTwoStateDiscreteItemVariable(
                        daTwoStateDiscrete + "005",
                        "circle",
                        "cross");

                    const string daMultiStateDiscrete = "DataAccess_MultiStateDiscreteType_";

                    // Add our Nodes to the folder, and specify their customized discrete enumerations
                    CreateMultiStateDiscreteItemVariable(
                        daMultiStateDiscrete + "001",
                        "open",
                        "closed",
                        "jammed");
                    CreateMultiStateDiscreteItemVariable(
                        daMultiStateDiscrete + "002",
                        "red",
                        "green",
                        "blue",
                        "cyan");
                    CreateMultiStateDiscreteItemVariable(
                        daMultiStateDiscrete + "003",
                        "lolo",
                        "lo",
                        "normal",
                        "hi",
                        "hihi");
                    CreateMultiStateDiscreteItemVariable(
                        daMultiStateDiscrete + "004",
                        "left",
                        "right",
                        "center");
                    CreateMultiStateDiscreteItemVariable(
                        daMultiStateDiscrete + "005",
                        "circle",
                        "cross",
                        "triangle");

                    ResetRandomGenerator(13);
                    const string daMultiStateValueDiscrete
                        = "DataAccess_MultiStateValueDiscreteType_";

                    // Add our Nodes to the folder, and specify their customized discrete enumerations
                    CreateMultiStateValueDiscreteItemVariable(
                        daMultiStateValueDiscrete + "001",
                        s_stringArray1);
                    CreateMultiStateValueDiscreteItemVariable(
                        daMultiStateValueDiscrete + "002",
                        s_stringArray2);
                    CreateMultiStateValueDiscreteItemVariable(
                        daMultiStateValueDiscrete + "003",
                        s_stringArray3);
                    CreateMultiStateValueDiscreteItemVariable(
                        daMultiStateValueDiscrete + "004",
                        s_stringArray4);
                    CreateMultiStateValueDiscreteItemVariable(
                        daMultiStateValueDiscrete + "005",
                        s_stringArray5);

                    // Add our Nodes to the folder and specify varying data types
                    CreateMultiStateValueDiscreteItemVariable(
                        daMultiStateValueDiscrete + "Byte",
                        DataTypeIds.Byte,
                        s_stringArray1);
                    CreateMultiStateValueDiscreteItemVariable(
                        daMultiStateValueDiscrete + "Int16",
                        DataTypeIds.Int16,
                        s_stringArray2);
                    CreateMultiStateValueDiscreteItemVariable(
                        daMultiStateValueDiscrete + "Int32",
                        DataTypeIds.Int32,
                        s_stringArray3);
                    CreateMultiStateValueDiscreteItemVariable(
                        daMultiStateValueDiscrete + "Int64",
                        DataTypeIds.Int64,
                        s_stringArray4);
                    CreateMultiStateValueDiscreteItemVariable(
                        daMultiStateValueDiscrete + "SByte",
                        DataTypeIds.SByte,
                        s_stringArray6);
                    CreateMultiStateValueDiscreteItemVariable(
                        daMultiStateValueDiscrete + "UInt16",
                        DataTypeIds.UInt16,
                        s_stringArray7);
                    CreateMultiStateValueDiscreteItemVariable(
                        daMultiStateValueDiscrete + "UInt32",
                        DataTypeIds.UInt32,
                        s_stringArray8);
                    CreateMultiStateValueDiscreteItemVariable(
                        daMultiStateValueDiscrete + "UInt64",
                        DataTypeIds.UInt64,
                        s_stringArray9);

                    FolderState arrayItemTypeFolder = CreateFolder("DataAccess_ArrayItemType");
                    const string daArrayItemType = "DataAccess_ArrayItemType_";

                    CreateYArrayItemVariable(arrayItemTypeFolder, daArrayItemType + "YArray", "YArray");
                    CreateXYArrayItemVariable(arrayItemTypeFolder, daArrayItemType + "XYArray", "XYArray");
                    CreateImageItemVariable(arrayItemTypeFolder, daArrayItemType + "Image", "Image");
                    CreateCubeItemVariable(arrayItemTypeFolder, daArrayItemType + "Cube", "Cube");
                    CreateNDimensionArrayItemVariable(arrayItemTypeFolder, daArrayItemType + "NDimension", "NDimension");

                    CreateSelectionListVariable("DataAccess_SelectionList_Colors");

                    CreateCurrencyVariable("DataAccess_Currency_Amount");

                    ResetRandomGenerator(14);

                    BaseDataVariableState referencesInstructions = CreateVariable("References_Instructions");
                    referencesInstructions.Value =
                        "This folder will contain nodes that have specific Reference configurations.";

                    ResetRandomGenerator(15);
                    const string accessRights = "AccessRights_";

                    BaseDataVariableState accessRightsInstructions = CreateVariable(accessRights + "Instructions");
                    accessRightsInstructions.Value =
                        "This folder will be accessible to all who enter, but contents therein will be secured.";

                    // sub-folder for "AccessAll"
                    const string accessRightsAccessAll = "AccessRights_AccessAll_";

                    BaseDataVariableState arAllRO = CreateVariable(accessRightsAccessAll + "RO");
                    arAllRO.AccessLevel = AccessLevels.CurrentRead | AccessLevels.HistoryRead;
                    arAllRO.UserAccessLevel = AccessLevels.CurrentRead | AccessLevels.HistoryRead;

                    BaseDataVariableState arAllWO = CreateVariable(accessRightsAccessAll + "WO");
                    arAllWO.AccessLevel = AccessLevels.CurrentWrite | AccessLevels.HistoryWrite;
                    arAllWO.UserAccessLevel = AccessLevels.CurrentWrite | AccessLevels.HistoryWrite;

                    BaseDataVariableState arAllRW = CreateVariable(accessRightsAccessAll + "RW");
                    arAllRW.AccessLevel = AccessLevels.CurrentReadOrWrite;
                    arAllRW.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

                    BaseDataVariableState arAllNoAccess = CreateVariable(accessRightsAccessAll + "NoAccess");
                    arAllNoAccess.AccessLevel = AccessLevels.None;
                    arAllNoAccess.UserAccessLevel = AccessLevels.None;

                    BaseDataVariableState arAllRONotUser = CreateVariable(accessRightsAccessAll + "RO_NotUser");
                    arAllRONotUser.AccessLevel = AccessLevels.CurrentRead | AccessLevels.HistoryRead;
                    arAllRONotUser.UserAccessLevel = AccessLevels.None;

                    BaseDataVariableState arAllWONotUser = CreateVariable(accessRightsAccessAll + "WO_NotUser");
                    arAllWONotUser.AccessLevel = AccessLevels.CurrentWrite;
                    arAllWONotUser.UserAccessLevel = AccessLevels.None;

                    BaseDataVariableState arAllRWNotUser = CreateVariable(accessRightsAccessAll + "RW_NotUser");
                    arAllRWNotUser.AccessLevel = AccessLevels.CurrentReadOrWrite | AccessLevels.HistoryReadOrWrite;
                    arAllRWNotUser.UserAccessLevel = AccessLevels.CurrentRead;

                    BaseDataVariableState arAllROUserRW = CreateVariable(accessRightsAccessAll + "RO_User1_RW");
                    arAllROUserRW.AccessLevel = AccessLevels.CurrentRead;
                    arAllROUserRW.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

                    BaseDataVariableState arAllROGroupRW = CreateVariable(accessRightsAccessAll + "RO_Group1_RW");
                    arAllROGroupRW.AccessLevel = AccessLevels.CurrentRead;
                    arAllROGroupRW.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

                    // sub-folder for "AccessUser1"
                    const string accessRightsAccessUser1 = "AccessRights_AccessUser1_";

                    BaseDataVariableState arUserRO = CreateVariable(accessRightsAccessUser1 + "RO");
                    arUserRO.AccessLevel = AccessLevels.CurrentRead;
                    arUserRO.UserAccessLevel = AccessLevels.CurrentRead | AccessLevels.HistoryRead;

                    BaseDataVariableState arUserWO = CreateVariable(accessRightsAccessUser1 + "WO");
                    arUserWO.AccessLevel = AccessLevels.CurrentWrite;
                    arUserWO.UserAccessLevel = AccessLevels.CurrentWrite;

                    BaseDataVariableState arUserRW = CreateVariable(accessRightsAccessUser1 + "RW");
                    arUserRW.AccessLevel = AccessLevels.CurrentReadOrWrite;
                    arUserRW.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

                    // sub-folder for "AccessGroup1"
                    const string accessRightsAccessGroup1 = "AccessRights_AccessGroup1_";

                    BaseDataVariableState arGroupRO = CreateVariable(accessRightsAccessGroup1 + "RO");
                    arGroupRO.AccessLevel = AccessLevels.CurrentRead;
                    arGroupRO.UserAccessLevel = AccessLevels.CurrentRead;

                    BaseDataVariableState arGroupWO = CreateVariable(accessRightsAccessGroup1 + "WO");
                    arGroupWO.AccessLevel = AccessLevels.CurrentWrite;
                    arGroupWO.UserAccessLevel = AccessLevels.CurrentWrite;

                    BaseDataVariableState arGroupRW = CreateVariable(accessRightsAccessGroup1 + "RW");
                    arGroupRW.AccessLevel = AccessLevels.CurrentReadOrWrite;
                    arGroupRW.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

                    // sub folder for "RolePermissions"
                    const string rolePermissions = "AccessRights_RolePermissions_";

                    BaseDataVariableState rpAnonymous = CreateVariable(rolePermissions + "AnonymousAccess");
                    rpAnonymous.Description = LocalizedText.From(
                        "This node can be accessed by users that have Anonymous Role");
                    rpAnonymous.RolePermissions =
                    [
                        // allow access to users with Anonymous role
                        new RolePermissionType
                        {
                            RoleId = Opc.Ua.ObjectIds.WellKnownRole_Anonymous,
                            Permissions = (uint)(
                                PermissionType.Browse |
                                PermissionType.Read |
                                PermissionType.ReadRolePermissions |
                                PermissionType.Write)
                        }
                    ];

                    BaseDataVariableState rpAuthenticatedUser = CreateVariable(rolePermissions + "AuthenticatedUser");
                    rpAuthenticatedUser.Description =
                        LocalizedText.From("This node can be accessed by users that have AuthenticatedUser Role");
                    rpAuthenticatedUser.RolePermissions =
                    [
                        // allow access to users with AuthenticatedUser role
                        new RolePermissionType
                        {
                            RoleId = Opc.Ua.ObjectIds.WellKnownRole_AuthenticatedUser,
                            Permissions = (uint)(
                                PermissionType.Browse |
                                PermissionType.Read |
                                PermissionType.ReadRolePermissions |
                                PermissionType.Write)
                        }
                    ];

                    BaseDataVariableState rpSecurityAdminUser = CreateVariable(rolePermissions + "SecurityAdmin");
                    rpSecurityAdminUser.Description = LocalizedText.From(
                        "This node can be accessed by users that have SecurityAdmin Role over an encrypted connection");
                    rpSecurityAdminUser.AccessRestrictions
                        = AccessRestrictionType.EncryptionRequired;
                    rpSecurityAdminUser.RolePermissions =
                    [
                        // allow access to users with SecurityAdmin role
                        new RolePermissionType
                        {
                            RoleId = Opc.Ua.ObjectIds.WellKnownRole_SecurityAdmin,
                            Permissions = (uint)(
                                PermissionType.Browse |
                                PermissionType.Read |
                                PermissionType.ReadRolePermissions |
                                PermissionType.Write)
                        }
                    ];

                    BaseDataVariableState rpConfigAdminUser = CreateVariable(rolePermissions + "ConfigureAdmin");
                    rpConfigAdminUser.Description = LocalizedText.From(
                        "This node can be accessed by users that have ConfigureAdmin Role over an encrypted connection");
                    rpConfigAdminUser.AccessRestrictions = AccessRestrictionType.EncryptionRequired;
                    rpConfigAdminUser.RolePermissions =
                    [
                        // allow access to users with ConfigureAdmin role
                        new RolePermissionType
                        {
                            RoleId = Opc.Ua.ObjectIds.WellKnownRole_ConfigureAdmin,
                            Permissions = (uint)(
                                PermissionType.Browse |
                                PermissionType.Read |
                                PermissionType.ReadRolePermissions |
                                PermissionType.Write)
                        }
                    ];

                    // sub-folder for "AccessRestrictions"
                    const string accessRestrictions = "AccessRights_AccessRestrictions_";

                    BaseDataVariableState arNone = CreateVariable(accessRestrictions + "None");
                    arNone.AccessLevel = AccessLevels.CurrentRead;
                    arNone.UserAccessLevel = AccessLevels.CurrentRead;
                    arNone.AccessRestrictions = AccessRestrictionType.None;

                    BaseDataVariableState arSigningRequired = CreateVariable(accessRestrictions + "SigningRequired");
                    arSigningRequired.AccessLevel = AccessLevels.CurrentRead;
                    arSigningRequired.UserAccessLevel = AccessLevels.CurrentRead;
                    arSigningRequired.AccessRestrictions = AccessRestrictionType.SigningRequired;

                    BaseDataVariableState arEncryptionRequired = CreateVariable(accessRestrictions + "EncryptionRequired");
                    arEncryptionRequired.AccessLevel = AccessLevels.CurrentRead;
                    arEncryptionRequired.UserAccessLevel = AccessLevels.CurrentRead;
                    arEncryptionRequired.AccessRestrictions
                        = AccessRestrictionType.EncryptionRequired;

                    BaseDataVariableState arSessionRequired = CreateVariable(accessRestrictions + "SessionRequired");
                    arSessionRequired.AccessLevel = AccessLevels.CurrentRead;
                    arSessionRequired.UserAccessLevel = AccessLevels.CurrentRead;
                    arSessionRequired.AccessRestrictions = AccessRestrictionType.SessionRequired;

                    ResetRandomGenerator(16);
                    const string nodeIds = "NodeIds_";

                    BaseDataVariableState nodeIdsInstructions = CreateVariable(nodeIds + "Instructions");
                    nodeIdsInstructions.Value =
                        "All supported Node types are available except whichever is in use for the other nodes.";





                    const string nodeIdsEvents = "NodeIds_Events_";

                    BaseDataVariableState triggerNode01 = CreateVariable(nodeIdsEvents + "TriggerNode01");
                    triggerNode01.OnSimpleWriteValue = OnWriteTriggerNode;

                    BaseDataVariableState triggerNode02 = CreateVariable(nodeIdsEvents + "TriggerNode02");
                    triggerNode02.OnSimpleWriteValue = OnWriteTriggerNode;

                    ResetRandomGenerator(18);
                    FolderState viewsFolder = CreateFolder("Views");
                    const string views = "Views_";
                    ViewState viewStateOperations = await CreateViewAsync(
                        viewsFolder,
                        externalReferences,
                        views + "Operations",
                        "Operations",
                        cancellationToken).ConfigureAwait(false);
                    viewStateOperations.AddReference(
                        ReferenceTypeIds.Organizes,
                        false,
                        massFolder.NodeId);
                    massFolder.AddReference(
                        ReferenceTypeIds.Organizes,
                        true,
                        viewStateOperations.NodeId);

                    ViewState viewStateEngineering = await CreateViewAsync(
                        viewsFolder,
                        externalReferences,
                        views + "Engineering",
                        "Engineering",
                        cancellationToken).ConfigureAwait(false);
                    viewStateEngineering.AddReference(
                        ReferenceTypeIds.Organizes,
                        false,
                        simulationFolder.NodeId);
                    simulationFolder.AddReference(
                        ReferenceTypeIds.Organizes,
                        true,
                        viewStateEngineering.NodeId);

                    ResetRandomGenerator(19);
                    const string locales = "Locales_";

                    BaseDataVariableState qnEnglishVariable = CreateVariable(locales + "QNEnglish");
                    qnEnglishVariable.Description = new LocalizedText("en", "English");
                    qnEnglishVariable.Value = new QualifiedName("Hello World", NamespaceIndex);

                    BaseDataVariableState ltEnglishVariable = CreateVariable(locales + "LTEnglish");
                    ltEnglishVariable.Description = new LocalizedText("en", "English");
                    ltEnglishVariable.Value = new LocalizedText("en", "Hello World");

                    BaseDataVariableState qnFrancaisVariable = CreateVariable(locales + "QNFrancais");
                    qnFrancaisVariable.Description = new LocalizedText("en", "Francais");
                    qnFrancaisVariable.Value
                        = new QualifiedName("Salut tout le monde", NamespaceIndex);

                    BaseDataVariableState ltFrancaisVariable = CreateVariable(locales + "LTFrancais");
                    ltFrancaisVariable.Description = new LocalizedText("en", "Francais");
                    ltFrancaisVariable.Value = new LocalizedText("fr", "Salut tout le monde");

                    BaseDataVariableState qnDeutschVariable = CreateVariable(locales + "QNDeutsch");
                    qnDeutschVariable.Description = new LocalizedText("en", "Deutsch");
                    qnDeutschVariable.Value = new QualifiedName("Hallo Welt", NamespaceIndex);

                    BaseDataVariableState ltDeutschVariable = CreateVariable(locales + "LTDeutsch");
                    ltDeutschVariable.Description = new LocalizedText("en", "Deutsch");
                    ltDeutschVariable.Value = new LocalizedText("de", "Hallo Welt");

                    BaseDataVariableState qnEspanolVariable = CreateVariable(locales + "QNEspanol");
                    qnEspanolVariable.Description = new LocalizedText("en", "Espanol");
                    qnEspanolVariable.Value = new QualifiedName("Hola mundo", NamespaceIndex);

                    BaseDataVariableState ltEspanolVariable = CreateVariable(locales + "LTEspanol");
                    ltEspanolVariable.Description = new LocalizedText("en", "Espanol");
                    ltEspanolVariable.Value = new LocalizedText("es", "Hola mundo");

                    BaseDataVariableState qnJapaneseVariable = CreateVariable(locales + "QN日本の");
                    qnJapaneseVariable.Description = new LocalizedText("en", "Japanese");
                    qnJapaneseVariable.Value = new QualifiedName("ハローワールド", NamespaceIndex);

                    BaseDataVariableState ltJapaneseVariable = CreateVariable(locales + "LT日本の");
                    ltJapaneseVariable.Description = new LocalizedText("en", "Japanese");
                    ltJapaneseVariable.Value = new LocalizedText("jp", "ハローワールド");

                    BaseDataVariableState qnChineseVariable = CreateVariable(locales + "QN中國的");
                    qnChineseVariable.Description = new LocalizedText("en", "Chinese");
                    qnChineseVariable.Value = new QualifiedName("世界您好", NamespaceIndex);

                    BaseDataVariableState ltChineseVariable = CreateVariable(locales + "LT中國的");
                    ltChineseVariable.Description = new LocalizedText("en", "Chinese");
                    ltChineseVariable.Value = new LocalizedText("ch", "世界您好");

                    BaseDataVariableState qnRussianVariable = CreateVariable(locales + "QNрусский");
                    qnRussianVariable.Description = new LocalizedText("en", "Russian");
                    qnRussianVariable.Value = new QualifiedName("LTрусский", NamespaceIndex);

                    BaseDataVariableState ltRussianVariable = CreateVariable(locales + "LTрусский");
                    ltRussianVariable.Description = new LocalizedText("en", "Russian");
                    ltRussianVariable.Value = new LocalizedText("ru", "LTрусский");

                    BaseDataVariableState qnArabicVariable = CreateVariable(locales + "QNالعربية");
                    qnArabicVariable.Description = new LocalizedText("en", "Arabic");
                    qnArabicVariable.Value = new QualifiedName("مرحبا بالعال", NamespaceIndex);

                    BaseDataVariableState ltArabicVariable = CreateVariable(locales + "LTالعربية");
                    ltArabicVariable.Description = new LocalizedText("en", "Arabic");
                    ltArabicVariable.Value = new LocalizedText("ae", "مرحبا بالعال");

                    BaseDataVariableState qnKlingonVariable = CreateVariable(locales + "QNtlhIngan");
                    qnKlingonVariable.Description = new LocalizedText("en", "Klingon");
                    qnKlingonVariable.Value = new QualifiedName("qo' vIvan", NamespaceIndex);

                    BaseDataVariableState ltKlingonVariable = CreateVariable(locales + "LTtlhIngan");
                    ltKlingonVariable.Description = new LocalizedText("en", "Klingon");
                    ltKlingonVariable.Value = new LocalizedText("ko", "qo' vIvan");

                    ResetRandomGenerator(20);

                    const string attributesAccessAll = "Attributes_AccessAll_";

                    BaseDataVariableState accessLevelAccessAll = CreateVariable(attributesAccessAll + "AccessLevel");
                    accessLevelAccessAll.WriteMask = AttributeWriteMask.AccessLevel;
                    accessLevelAccessAll.UserWriteMask = AttributeWriteMask.AccessLevel;

                    BaseDataVariableState arrayDimensionsAccessLevel = CreateVariable(attributesAccessAll + "ArrayDimensions");
                    arrayDimensionsAccessLevel.WriteMask = AttributeWriteMask.ArrayDimensions;
                    arrayDimensionsAccessLevel.UserWriteMask = AttributeWriteMask.ArrayDimensions;

                    BaseDataVariableState browseNameAccessLevel = CreateVariable(attributesAccessAll + "BrowseName");
                    browseNameAccessLevel.WriteMask = AttributeWriteMask.BrowseName;
                    browseNameAccessLevel.UserWriteMask = AttributeWriteMask.BrowseName;

                    BaseDataVariableState containsNoLoopsAccessLevel = CreateVariable(attributesAccessAll + "ContainsNoLoops");
                    containsNoLoopsAccessLevel.WriteMask = AttributeWriteMask.ContainsNoLoops;
                    containsNoLoopsAccessLevel.UserWriteMask = AttributeWriteMask.ContainsNoLoops;

                    BaseDataVariableState dataTypeAccessLevel = CreateVariable(attributesAccessAll + "DataType");
                    dataTypeAccessLevel.WriteMask = AttributeWriteMask.DataType;
                    dataTypeAccessLevel.UserWriteMask = AttributeWriteMask.DataType;

                    BaseDataVariableState descriptionAccessLevel = CreateVariable(attributesAccessAll + "Description");
                    descriptionAccessLevel.WriteMask = AttributeWriteMask.Description;
                    descriptionAccessLevel.UserWriteMask = AttributeWriteMask.Description;

                    BaseDataVariableState eventNotifierAccessLevel = CreateVariable(attributesAccessAll + "EventNotifier");
                    eventNotifierAccessLevel.WriteMask = AttributeWriteMask.EventNotifier;
                    eventNotifierAccessLevel.UserWriteMask = AttributeWriteMask.EventNotifier;

                    BaseDataVariableState executableAccessLevel = CreateVariable(attributesAccessAll + "Executable");
                    executableAccessLevel.WriteMask = AttributeWriteMask.Executable;
                    executableAccessLevel.UserWriteMask = AttributeWriteMask.Executable;

                    BaseDataVariableState historizingAccessLevel = CreateVariable(attributesAccessAll + "Historizing");
                    historizingAccessLevel.WriteMask = AttributeWriteMask.Historizing;
                    historizingAccessLevel.UserWriteMask = AttributeWriteMask.Historizing;

                    BaseDataVariableState inverseNameAccessLevel = CreateVariable(attributesAccessAll + "InverseName");
                    inverseNameAccessLevel.WriteMask = AttributeWriteMask.InverseName;
                    inverseNameAccessLevel.UserWriteMask = AttributeWriteMask.InverseName;

                    BaseDataVariableState isAbstractAccessLevel = CreateVariable(attributesAccessAll + "IsAbstract");
                    isAbstractAccessLevel.WriteMask = AttributeWriteMask.IsAbstract;
                    isAbstractAccessLevel.UserWriteMask = AttributeWriteMask.IsAbstract;

                    BaseDataVariableState minimumSamplingIntervalAccessLevel = CreateVariable(attributesAccessAll + "MinimumSamplingInterval");
                    minimumSamplingIntervalAccessLevel.WriteMask
                        = AttributeWriteMask.MinimumSamplingInterval;
                    minimumSamplingIntervalAccessLevel.UserWriteMask
                        = AttributeWriteMask.MinimumSamplingInterval;

                    BaseDataVariableState nodeClassIntervalAccessLevel = CreateVariable(attributesAccessAll + "NodeClass");
                    nodeClassIntervalAccessLevel.WriteMask = AttributeWriteMask.NodeClass;
                    nodeClassIntervalAccessLevel.UserWriteMask = AttributeWriteMask.NodeClass;

                    BaseDataVariableState nodeIdAccessLevel = CreateVariable(attributesAccessAll + "NodeId");
                    nodeIdAccessLevel.WriteMask = AttributeWriteMask.NodeId;
                    nodeIdAccessLevel.UserWriteMask = AttributeWriteMask.NodeId;

                    BaseDataVariableState symmetricAccessLevel = CreateVariable(attributesAccessAll + "Symmetric");
                    symmetricAccessLevel.WriteMask = AttributeWriteMask.Symmetric;
                    symmetricAccessLevel.UserWriteMask = AttributeWriteMask.Symmetric;

                    BaseDataVariableState userAccessLevelAccessLevel = CreateVariable(attributesAccessAll + "UserAccessLevel");
                    userAccessLevelAccessLevel.WriteMask = AttributeWriteMask.UserAccessLevel;
                    userAccessLevelAccessLevel.UserWriteMask = AttributeWriteMask.UserAccessLevel;

                    BaseDataVariableState userExecutableAccessLevel = CreateVariable(attributesAccessAll + "UserExecutable");
                    userExecutableAccessLevel.WriteMask = AttributeWriteMask.UserExecutable;
                    userExecutableAccessLevel.UserWriteMask = AttributeWriteMask.UserExecutable;

                    BaseDataVariableState valueRankAccessLevel = CreateVariable(attributesAccessAll + "ValueRank");
                    valueRankAccessLevel.WriteMask = AttributeWriteMask.ValueRank;
                    valueRankAccessLevel.UserWriteMask = AttributeWriteMask.ValueRank;

                    BaseDataVariableState writeMaskAccessLevel = CreateVariable(attributesAccessAll + "WriteMask");
                    writeMaskAccessLevel.WriteMask = AttributeWriteMask.WriteMask;
                    writeMaskAccessLevel.UserWriteMask = AttributeWriteMask.WriteMask;

                    BaseDataVariableState valueForVariableTypeAccessLevel = CreateVariable(attributesAccessAll + "ValueForVariableType");
                    valueForVariableTypeAccessLevel.WriteMask
                        = AttributeWriteMask.ValueForVariableType;
                    valueForVariableTypeAccessLevel.UserWriteMask
                        = AttributeWriteMask.ValueForVariableType;

                    BaseDataVariableState allAccessLevel = CreateVariable(attributesAccessAll + "All");
                    allAccessLevel.WriteMask =
                        AttributeWriteMask.AccessLevel |
                        AttributeWriteMask.ArrayDimensions |
                        AttributeWriteMask.BrowseName |
                        AttributeWriteMask.ContainsNoLoops |
                        AttributeWriteMask.DataType |
                        AttributeWriteMask.Description |
                        AttributeWriteMask.DisplayName |
                        AttributeWriteMask.EventNotifier |
                        AttributeWriteMask.Executable |
                        AttributeWriteMask.Historizing |
                        AttributeWriteMask.InverseName |
                        AttributeWriteMask.IsAbstract |
                        AttributeWriteMask.MinimumSamplingInterval |
                        AttributeWriteMask.NodeClass |
                        AttributeWriteMask.NodeId |
                        AttributeWriteMask.Symmetric |
                        AttributeWriteMask.UserAccessLevel |
                        AttributeWriteMask.UserExecutable |
                        AttributeWriteMask.UserWriteMask |
                        AttributeWriteMask.ValueForVariableType |
                        AttributeWriteMask.ValueRank |
                        AttributeWriteMask.WriteMask;
                    allAccessLevel.UserWriteMask =
                        AttributeWriteMask.AccessLevel |
                        AttributeWriteMask.ArrayDimensions |
                        AttributeWriteMask.BrowseName |
                        AttributeWriteMask.ContainsNoLoops |
                        AttributeWriteMask.DataType |
                        AttributeWriteMask.Description |
                        AttributeWriteMask.DisplayName |
                        AttributeWriteMask.EventNotifier |
                        AttributeWriteMask.Executable |
                        AttributeWriteMask.Historizing |
                        AttributeWriteMask.InverseName |
                        AttributeWriteMask.IsAbstract |
                        AttributeWriteMask.MinimumSamplingInterval |
                        AttributeWriteMask.NodeClass |
                        AttributeWriteMask.NodeId |
                        AttributeWriteMask.Symmetric |
                        AttributeWriteMask.UserAccessLevel |
                        AttributeWriteMask.UserExecutable |
                        AttributeWriteMask.UserWriteMask |
                        AttributeWriteMask.ValueForVariableType |
                        AttributeWriteMask.ValueRank |
                        AttributeWriteMask.WriteMask;

                    const string attributesAccessUser1 = "Attributes_AccessUser1_";

                    accessLevelAccessAll.WriteMask = AttributeWriteMask.AccessLevel;
                    accessLevelAccessAll.UserWriteMask = AttributeWriteMask.AccessLevel;

                    BaseDataVariableState arrayDimensionsAccessUser1 = CreateVariable(attributesAccessUser1 + "ArrayDimensions");
                    arrayDimensionsAccessUser1.WriteMask = AttributeWriteMask.ArrayDimensions;
                    arrayDimensionsAccessUser1.UserWriteMask = AttributeWriteMask.ArrayDimensions;

                    BaseDataVariableState browseNameAccessUser1 = CreateVariable(attributesAccessUser1 + "BrowseName");
                    browseNameAccessUser1.WriteMask = AttributeWriteMask.BrowseName;
                    browseNameAccessUser1.UserWriteMask = AttributeWriteMask.BrowseName;

                    BaseDataVariableState containsNoLoopsAccessUser1 = CreateVariable(attributesAccessUser1 + "ContainsNoLoops");
                    containsNoLoopsAccessUser1.WriteMask = AttributeWriteMask.ContainsNoLoops;
                    containsNoLoopsAccessUser1.UserWriteMask = AttributeWriteMask.ContainsNoLoops;

                    BaseDataVariableState dataTypeAccessUser1 = CreateVariable(attributesAccessUser1 + "DataType");
                    dataTypeAccessUser1.WriteMask = AttributeWriteMask.DataType;
                    dataTypeAccessUser1.UserWriteMask = AttributeWriteMask.DataType;

                    BaseDataVariableState descriptionAccessUser1 = CreateVariable(attributesAccessUser1 + "Description");
                    descriptionAccessUser1.WriteMask = AttributeWriteMask.Description;
                    descriptionAccessUser1.UserWriteMask = AttributeWriteMask.Description;

                    BaseDataVariableState eventNotifierAccessUser1 = CreateVariable(attributesAccessUser1 + "EventNotifier");
                    eventNotifierAccessUser1.WriteMask = AttributeWriteMask.EventNotifier;
                    eventNotifierAccessUser1.UserWriteMask = AttributeWriteMask.EventNotifier;

                    BaseDataVariableState executableAccessUser1 = CreateVariable(attributesAccessUser1 + "Executable");
                    executableAccessUser1.WriteMask = AttributeWriteMask.Executable;
                    executableAccessUser1.UserWriteMask = AttributeWriteMask.Executable;

                    BaseDataVariableState historizingAccessUser1 = CreateVariable(attributesAccessUser1 + "Historizing");
                    historizingAccessUser1.WriteMask = AttributeWriteMask.Historizing;
                    historizingAccessUser1.UserWriteMask = AttributeWriteMask.Historizing;

                    BaseDataVariableState inverseNameAccessUser1 = CreateVariable(attributesAccessUser1 + "InverseName");
                    inverseNameAccessUser1.WriteMask = AttributeWriteMask.InverseName;
                    inverseNameAccessUser1.UserWriteMask = AttributeWriteMask.InverseName;

                    BaseDataVariableState isAbstractAccessUser1 = CreateVariable(attributesAccessUser1 + "IsAbstract");
                    isAbstractAccessUser1.WriteMask = AttributeWriteMask.IsAbstract;
                    isAbstractAccessUser1.UserWriteMask = AttributeWriteMask.IsAbstract;

                    BaseDataVariableState minimumSamplingIntervalAccessUser1 = CreateVariable(attributesAccessUser1 + "MinimumSamplingInterval");
                    minimumSamplingIntervalAccessUser1.WriteMask
                        = AttributeWriteMask.MinimumSamplingInterval;
                    minimumSamplingIntervalAccessUser1.UserWriteMask
                        = AttributeWriteMask.MinimumSamplingInterval;

                    BaseDataVariableState nodeClassIntervalAccessUser1 = CreateVariable(attributesAccessUser1 + "NodeClass");
                    nodeClassIntervalAccessUser1.WriteMask = AttributeWriteMask.NodeClass;
                    nodeClassIntervalAccessUser1.UserWriteMask = AttributeWriteMask.NodeClass;

                    BaseDataVariableState nodeIdAccessUser1 = CreateVariable(attributesAccessUser1 + "NodeId");
                    nodeIdAccessUser1.WriteMask = AttributeWriteMask.NodeId;
                    nodeIdAccessUser1.UserWriteMask = AttributeWriteMask.NodeId;

                    BaseDataVariableState symmetricAccessUser1 = CreateVariable(attributesAccessUser1 + "Symmetric");
                    symmetricAccessUser1.WriteMask = AttributeWriteMask.Symmetric;
                    symmetricAccessUser1.UserWriteMask = AttributeWriteMask.Symmetric;

                    BaseDataVariableState userAccessUser1AccessUser1 = CreateVariable(attributesAccessUser1 + "UserAccessUser1");
                    userAccessUser1AccessUser1.WriteMask = AttributeWriteMask.UserAccessLevel;
                    userAccessUser1AccessUser1.UserWriteMask = AttributeWriteMask.UserAccessLevel;

                    BaseDataVariableState userExecutableAccessUser1 = CreateVariable(attributesAccessUser1 + "UserExecutable");
                    userExecutableAccessUser1.WriteMask = AttributeWriteMask.UserExecutable;
                    userExecutableAccessUser1.UserWriteMask = AttributeWriteMask.UserExecutable;

                    BaseDataVariableState valueRankAccessUser1 = CreateVariable(attributesAccessUser1 + "ValueRank");
                    valueRankAccessUser1.WriteMask = AttributeWriteMask.ValueRank;
                    valueRankAccessUser1.UserWriteMask = AttributeWriteMask.ValueRank;

                    BaseDataVariableState writeMaskAccessUser1 = CreateVariable(attributesAccessUser1 + "WriteMask");
                    writeMaskAccessUser1.WriteMask = AttributeWriteMask.WriteMask;
                    writeMaskAccessUser1.UserWriteMask = AttributeWriteMask.WriteMask;

                    BaseDataVariableState valueForVariableTypeAccessUser1 = CreateVariable(attributesAccessUser1 + "ValueForVariableType");
                    valueForVariableTypeAccessUser1.WriteMask
                        = AttributeWriteMask.ValueForVariableType;
                    valueForVariableTypeAccessUser1.UserWriteMask
                        = AttributeWriteMask.ValueForVariableType;

                    BaseDataVariableState allAccessUser1 = CreateVariable(attributesAccessUser1 + "All");
                    allAccessUser1.WriteMask =
                        AttributeWriteMask.AccessLevel |
                        AttributeWriteMask.ArrayDimensions |
                        AttributeWriteMask.BrowseName |
                        AttributeWriteMask.ContainsNoLoops |
                        AttributeWriteMask.DataType |
                        AttributeWriteMask.Description |
                        AttributeWriteMask.DisplayName |
                        AttributeWriteMask.EventNotifier |
                        AttributeWriteMask.Executable |
                        AttributeWriteMask.Historizing |
                        AttributeWriteMask.InverseName |
                        AttributeWriteMask.IsAbstract |
                        AttributeWriteMask.MinimumSamplingInterval |
                        AttributeWriteMask.NodeClass |
                        AttributeWriteMask.NodeId |
                        AttributeWriteMask.Symmetric |
                        AttributeWriteMask.UserAccessLevel |
                        AttributeWriteMask.UserExecutable |
                        AttributeWriteMask.UserWriteMask |
                        AttributeWriteMask.ValueForVariableType |
                        AttributeWriteMask.ValueRank |
                        AttributeWriteMask.WriteMask;
                    allAccessUser1.UserWriteMask =
                        AttributeWriteMask.AccessLevel |
                        AttributeWriteMask.ArrayDimensions |
                        AttributeWriteMask.BrowseName |
                        AttributeWriteMask.ContainsNoLoops |
                        AttributeWriteMask.DataType |
                        AttributeWriteMask.Description |
                        AttributeWriteMask.DisplayName |
                        AttributeWriteMask.EventNotifier |
                        AttributeWriteMask.Executable |
                        AttributeWriteMask.Historizing |
                        AttributeWriteMask.InverseName |
                        AttributeWriteMask.IsAbstract |
                        AttributeWriteMask.MinimumSamplingInterval |
                        AttributeWriteMask.NodeClass |
                        AttributeWriteMask.NodeId |
                        AttributeWriteMask.Symmetric |
                        AttributeWriteMask.UserAccessLevel |
                        AttributeWriteMask.UserExecutable |
                        AttributeWriteMask.UserWriteMask |
                        AttributeWriteMask.ValueForVariableType |
                        AttributeWriteMask.ValueRank |
                        AttributeWriteMask.WriteMask;

                    ResetRandomGenerator(21);
                    const string myCompany = "MyCompany_";

                    BaseDataVariableState myCompanyInstructions = CreateVariable(myCompany + "Instructions");
                    myCompanyInstructions.Value
                        = "A place for the vendor to describe their address-space.";
                }
                catch (Exception e)
                {
                    m_logger.ErrorCreatingAddressSpace(e);
                }

                await AddPredefinedNodeAsync(SystemContext, root, cancellationToken).ConfigureAwait(false);

                // Enable history archiving for selected scalar variables.
                await EnableHistoryArchivingAsync(cancellationToken).ConfigureAwait(false);

                if (m_simulationEnabled)
                {
                    // reset random generator and generate boundary values
                    ResetRandomGenerator(100, 1);

                    TimeProvider timeProvider = (Server as ITimeProviderProvider)?.TimeProvider
                        ?? TimeProvider.System;
                    m_simulationTimer?.Dispose();
                    m_simulationTimer = timeProvider.CreateTimer(
                        DoSimulation,
                        null,
                        TimeSpan.FromMilliseconds(m_simulationInterval),
                        TimeSpan.FromMilliseconds(m_simulationInterval));
                }
            }
            finally
            {
                m_semaphore.Release();
            }
        }

        private ServiceResult OnWriteInterval(
            ISystemContext context,
            NodeState node,
            ref Variant value)
        {
            try
            {
                m_simulationInterval = (ushort)value;

                if (m_simulationEnabled)
                {
                    m_simulationTimer!.Change(
                        TimeSpan.FromMilliseconds(100),
                        TimeSpan.FromMilliseconds(m_simulationInterval));
                }

                return ServiceResult.Good;
            }
            catch (Exception e)
            {
                m_logger.ErrorWritingIntervalVariable(e);
                return ServiceResult.Create(e, StatusCodes.Bad, "Error writing Interval variable.");
            }
        }

        private ServiceResult OnWriteEnabled(
            ISystemContext context,
            NodeState node,
            ref Variant value)
        {
            try
            {
                m_simulationEnabled = (bool)value;

                if (m_simulationEnabled)
                {
                    m_simulationTimer!.Change(
                        TimeSpan.FromMilliseconds(100),
                        TimeSpan.FromMilliseconds(m_simulationInterval));
                }
                else
                {
                    m_simulationTimer!.Change(
                        TimeSpan.FromMilliseconds(100),
                        TimeSpan.Zero);
                }

                return ServiceResult.Good;
            }
            catch (Exception e)
            {
                m_logger.ErrorWritingEnabledVariable(e);
                return ServiceResult.Create(e, StatusCodes.Bad, "Error writing Enabled variable.");
            }
        }

        /// <summary>
        /// Creates a new folder.
        /// </summary>
        private FolderState CreateFolder(string path)
        {
            return FindPredefinedNode<FolderState>(new NodeId(path, NamespaceIndex));
        }

        /// <summary>
        /// Creates a new variable.
        /// </summary>
        private DataItemState CreateDataItemVariable(
            string path,
            BuiltInType dataType,
            int valueRank)
        {
            DataItemState variable = FindPredefinedNode<DataItemState>(new NodeId(path, NamespaceIndex));
            if (variable == null)
            {
                return null!;
            }

            variable.Value = TypeInfo.GetDefaultVariantValue((NodeId)(uint)dataType, valueRank, Server.TypeTree);
            variable.StatusCode = StatusCodes.Good;

            if (variable.ValuePrecision != null)
            {
                variable.ValuePrecision.Value = 2;
            }

            if (variable.Definition != null)
            {
                variable.Definition.Value = string.Empty;
            }

            return variable;
        }

        /// <summary>
        /// Creates a new variable.
        /// </summary>
        private AnalogItemState CreateAnalogItemVariable(
            string path,
            BuiltInType dataType,
            int valueRank)
        {
            return CreateAnalogItemVariable(path, dataType, valueRank, default);
        }

        private AnalogItemState CreateAnalogItemVariable(
            string path,
            BuiltInType dataType,
            int valueRank,
            Variant initialValues)
        {
            return CreateAnalogItemVariable(
                path,
                dataType,
                valueRank,
                initialValues,
                null);
        }

        private AnalogItemState CreateAnalogItemVariable(
            string path,
            BuiltInType dataType,
            int valueRank,
            Variant initialValues,
            Range? customRange)
        {
            return CreateAnalogItemVariable(
                path,
                (NodeId)(uint)dataType,
                valueRank,
                initialValues,
                customRange);
        }

        private AnalogItemState CreateAnalogItemVariable(
            string path,
            NodeId dataType,
            int valueRank,
            Variant initialValues,
            Range? customRange)
        {
            AnalogItemState variable = FindPredefinedNode<AnalogItemState>(new NodeId(path, NamespaceIndex));
            if (variable == null)
            {
                return null!;
            }

            BuiltInType builtInType = TypeInfo.GetBuiltInType(dataType, Server.TypeTree);

            if (!TypeInfo.IsNumericType(builtInType))
            {
                throw new ArgumentException("AnalogItem must have a numeric DataType.", nameof(dataType));
            }

            // Simulate a mV Voltmeter
            Range newRange = GetAnalogRange(builtInType);
            // Using anything but 120,-10 fails a few tests
            newRange.High = Math.Min(newRange.High, 120);
            newRange.Low = Math.Max(newRange.Low, -10);
            if (variable.InstrumentRange != null)
            {
                variable.InstrumentRange.Value = newRange;
            }

            variable.EURange!.Value = customRange ?? new Range(100, 0);

            variable.Value = initialValues;
            if (variable.Value.IsNull)
            {
                variable.Value = TypeInfo.GetDefaultVariantValue(dataType, valueRank, Server.TypeTree);
            }

            variable.StatusCode = StatusCodes.Good;
            // The latest UNECE version (Rev 11, published in 2015) is available here:
            // http://www.opcfoundation.org/UA/EngineeringUnits/UNECE/rec20_latest_08052015.zip
            if (variable.EngineeringUnits != null)
            {
                variable.EngineeringUnits.Value = new EUInformation(
                    "mV",
                    "millivolt",
                    "http://www.opcfoundation.org/UA/units/un/cefact")
                {
                    // The mapping of the UNECE codes to OPC UA(EUInformation.unitId) is available here:
                    // http://www.opcfoundation.org/UA/EngineeringUnits/UNECE/UNECE_to_OPCUA.csv
                    UnitId = 12890 // "2Z"
                };
                variable.EngineeringUnits.AccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.EngineeringUnits.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            }

            variable.OnWriteValue = OnWriteAnalog;
            variable.EURange.OnWriteValue = OnWriteAnalogRange;
            variable.EURange.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.EURange.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            if (variable.InstrumentRange != null)
            {
                variable.InstrumentRange.OnWriteValue = OnWriteAnalogRange;
                variable.InstrumentRange.AccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.InstrumentRange.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            }

            return variable;
        }

        /// <summary>
        /// Creates a new variable.
        /// </summary>
        private TwoStateDiscreteState CreateTwoStateDiscreteItemVariable(
            string path,
            string trueState,
            string falseState)
        {
            TwoStateDiscreteState variable = FindPredefinedNode<TwoStateDiscreteState>(
                new NodeId(path, NamespaceIndex));
            if (variable == null)
            {
                return null!;
            }

            variable.Value = (bool)GetNewValue(variable);
            variable.StatusCode = StatusCodes.Good;

            if (variable.TrueState != null)
            {
                variable.TrueState.Value = LocalizedText.From(trueState);
                variable.TrueState.AccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.TrueState.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            }

            if (variable.FalseState != null)
            {
                variable.FalseState.Value = LocalizedText.From(falseState);
                variable.FalseState.AccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.FalseState.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            }

            return variable;
        }

        /// <summary>
        /// Creates a new variable.
        /// </summary>
        private MultiStateDiscreteState CreateMultiStateDiscreteItemVariable(
            string path,
            params string[] values)
        {
            MultiStateDiscreteState variable = FindPredefinedNode<MultiStateDiscreteState>(
                new NodeId(path, NamespaceIndex));
            if (variable == null)
            {
                return null!;
            }

            variable.Value = (uint)0;
            variable.StatusCode = StatusCodes.Good;
            variable.OnWriteValue = OnWriteDiscrete;

            var strings = new LocalizedText[values.Length];

            for (int ii = 0; ii < strings.Length; ii++)
            {
                strings[ii] = LocalizedText.From(values[ii]);
            }

            if (variable.EnumStrings != null)
            {
                variable.EnumStrings.Value = strings;
                variable.EnumStrings.AccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.EnumStrings.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            }

            return variable;
        }

        /// <summary>
        /// Creates a new UInt32 variable.
        /// </summary>
        private MultiStateValueDiscreteState CreateMultiStateValueDiscreteItemVariable(
            string path,
            ArrayOf<string> enumNames)
        {
            return CreateMultiStateValueDiscreteItemVariable(path, default, enumNames);
        }

        /// <summary>
        /// Creates a new variable.
        /// </summary>
        private MultiStateValueDiscreteState CreateMultiStateValueDiscreteItemVariable(
            string path,
            NodeId nodeId,
            ArrayOf<string> enumNames)
        {
            MultiStateValueDiscreteState variable = FindPredefinedNode<MultiStateValueDiscreteState>(
                new NodeId(path, NamespaceIndex));
            if (variable == null)
            {
                return null!;
            }

            variable.Value = (uint)0;
            variable.StatusCode = StatusCodes.Good;
            variable.OnWriteValue = OnWriteValueDiscrete;

            // there are two enumerations for this type:
            // EnumStrings = the string representations for enumerated values
            // ValueAsText = the actual enumerated value

            // set the enumerated strings
            var strings = new LocalizedText[enumNames.Count];
            for (int ii = 0; ii < strings.Length; ii++)
            {
                strings[ii] = LocalizedText.From(enumNames[ii]);
            }

            // set the enumerated values
            var values = new EnumValueType[enumNames.Count];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = new EnumValueType
                {
                    Value = ii,
                    Description = strings[ii],
                    DisplayName = strings[ii]
                };
            }

            if (variable.EnumValues != null)
            {
                variable.EnumValues.Value = values;
                variable.EnumValues.AccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.EnumValues.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

                if (variable.ValueAsText != null)
                {
                    variable.ValueAsText.Value = variable.EnumValues.Value[0].DisplayName;
                }
            }

            return variable;
        }

        /// <summary>
        /// Creates a SelectionListType instance with a set of
        /// selectable values, their localized descriptions and RestrictToList set.
        /// </summary>
        private SelectionListState CreateSelectionListVariable(string path)
        {
            SelectionListState variable = FindPredefinedNode<SelectionListState>(
                new NodeId(path, NamespaceIndex));
            if (variable == null)
            {
                return null!;
            }

            variable.Value = Variant.From("Red");
            variable.StatusCode = StatusCodes.Good;
            variable.Description = LocalizedText.From("Default Description");
            variable.OnWriteValue = OnWriteSelectionList;

            if (variable.FindChild(
                SystemContext,
                new QualifiedName(Opc.Ua.BrowseNames.Selections)) is BaseInstanceState existingSelections)
            {
                variable.RemoveChild(existingSelections);
            }
            // Nulling the generated Variant-typed property allows FindChild to
            // resolve the String[] Selections property added below for this
            // instance.
            variable.Selections = null!;

            var selections = PropertyState<ArrayOf<string>>
                .With<VariantBuilder>(variable);
            selections.NodeId = new NodeId(path + "_Selections", NamespaceIndex);
            selections.BrowseName = new QualifiedName(Opc.Ua.BrowseNames.Selections);
            selections.DisplayName = LocalizedText.From(Opc.Ua.BrowseNames.Selections);
            selections.TypeDefinitionId = VariableTypeIds.PropertyType;
            selections.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            selections.DataType = DataTypeIds.String;
            selections.ValueRank = ValueRanks.OneDimension;
            selections.AccessLevel = AccessLevels.CurrentRead;
            selections.UserAccessLevel = AccessLevels.CurrentRead;
            selections.Value =
            [
                "Red",
                "Green",
                "Blue"
            ];
            variable.AddChild(selections);

            if (variable.FindChild(
                SystemContext,
                new QualifiedName(Opc.Ua.BrowseNames.SelectionDescriptions)) is BaseInstanceState existingDescriptions)
            {
                variable.RemoveChild(existingDescriptions);
            }
            variable.SelectionDescriptions = null!;
            variable.AddSelectionDescriptions(
                SystemContext,
                new NodeId(path + "_SelectionDescriptions", NamespaceIndex));

            PropertyState<ArrayOf<LocalizedText>> selectionDescriptions =
                variable.SelectionDescriptions ??
                throw new InvalidOperationException(
                    "SelectionDescriptions property is null after calling AddSelectionDescriptions. " +
                    "Expected AddSelectionDescriptions to populate variable.SelectionDescriptions " +
                    "with a non-null PropertyState<ArrayOf<LocalizedText>>.");
            selectionDescriptions.NodeId = new NodeId(
                path + "_SelectionDescriptions",
                NamespaceIndex);
            selectionDescriptions.BrowseName = new QualifiedName(
                Opc.Ua.BrowseNames.SelectionDescriptions);
            selectionDescriptions.DisplayName = LocalizedText.From(
                Opc.Ua.BrowseNames.SelectionDescriptions);
            selectionDescriptions.TypeDefinitionId = VariableTypeIds.PropertyType;
            selectionDescriptions.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            selectionDescriptions.DataType = DataTypeIds.LocalizedText;
            selectionDescriptions.ValueRank = ValueRanks.OneDimension;
            selectionDescriptions.AccessLevel = AccessLevels.CurrentRead;
            selectionDescriptions.UserAccessLevel = AccessLevels.CurrentRead;
            selectionDescriptions.Value =
            [
                new LocalizedText("en-US", "The color red"),
                new LocalizedText("en-US", "The color green"),
                new LocalizedText("en-US", "The color blue")
            ];

            if (variable.FindChild(
                SystemContext,
                new QualifiedName(Opc.Ua.BrowseNames.RestrictToList)) is BaseInstanceState existingRestrictToList)
            {
                variable.RemoveChild(existingRestrictToList);
            }
            variable.RestrictToList = null!;
            variable.AddRestrictToList(
                SystemContext,
                new NodeId(path + "_RestrictToList", NamespaceIndex));

            PropertyState<bool> restrictToList = variable.RestrictToList ??
                throw new InvalidOperationException(
                    "RestrictToList property is null after calling AddRestrictToList. " +
                    "Expected AddRestrictToList to populate variable.RestrictToList " +
                    "with a non-null PropertyState<bool>.");
            restrictToList.NodeId = new NodeId(path + "_RestrictToList", NamespaceIndex);
            restrictToList.BrowseName = new QualifiedName(Opc.Ua.BrowseNames.RestrictToList);
            restrictToList.DisplayName = LocalizedText.From(Opc.Ua.BrowseNames.RestrictToList);
            restrictToList.TypeDefinitionId = VariableTypeIds.PropertyType;
            restrictToList.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            restrictToList.DataType = DataTypeIds.Boolean;
            restrictToList.ValueRank = ValueRanks.Scalar;
            restrictToList.AccessLevel = AccessLevels.CurrentRead;
            restrictToList.UserAccessLevel = AccessLevels.CurrentRead;
            restrictToList.Value = true;

            return variable;
        }

        private static ServiceResult OnWriteSelectionList(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref Variant value,
            ref StatusCode statusCode,
            ref DateTimeUtc timestamp)
        {
            if (!indexRange.IsNull)
            {
                return StatusCodes.BadIndexRangeInvalid;
            }

            if (!value.TryGetValue(out string? selection))
            {
                return StatusCodes.BadTypeMismatch;
            }

            if (node.FindChild(
                context,
                new QualifiedName(Opc.Ua.BrowseNames.Selections)) is not
                PropertyState<ArrayOf<string>> selections ||
                selections.Value.IsNull)
            {
                return StatusCodes.BadConfigurationError;
            }

            foreach (string allowedSelection in selections.Value)
            {
                if (string.Equals(selection, allowedSelection, StringComparison.Ordinal))
                {
                    return ServiceResult.Good;
                }
            }

            return StatusCodes.BadOutOfRange;
        }

        /// <summary>
        /// Creates a data variable that carries a CurrencyUnit property of DataType
        /// <see cref="DataTypeIds.CurrencyUnitType"/>.
        /// </summary>
        private BaseDataVariableState CreateCurrencyVariable(string path)
        {
            BaseDataVariableState variable = CreateVariable(path);
            if (variable == null)
            {
                return null!;
            }

            variable.Value = 42.0;

            if (variable.FindChild(
                SystemContext,
                new QualifiedName("CurrencyUnit", 0)) is BaseInstanceState existingCurrencyUnit)
            {
                variable.RemoveChild(existingCurrencyUnit);
            }

            var currencyUnit =
                new PropertyState<CurrencyUnitType>.Implementation<StructureBuilder<CurrencyUnitType>>(variable)
                {
                    NodeId = new NodeId(path + "_CurrencyUnit", NamespaceIndex),
                    BrowseName = new QualifiedName("CurrencyUnit", 0)
                };
            currencyUnit.DisplayName = LocalizedText.From(currencyUnit.BrowseName.Name!);
            currencyUnit.TypeDefinitionId = VariableTypeIds.PropertyType;
            currencyUnit.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            currencyUnit.DataType = DataTypeIds.CurrencyUnitType;
            currencyUnit.ValueRank = ValueRanks.Scalar;
            currencyUnit.AccessLevel = AccessLevels.CurrentRead;
            currencyUnit.UserAccessLevel = AccessLevels.CurrentRead;
            currencyUnit.Value = new CurrencyUnitType
            {
                NumericCode = 978,
                Exponent = 2,
                AlphabeticCode = "EUR",
                Currency = new LocalizedText("Euro")
            };

            variable.AddChild(currencyUnit);
            return variable;
        }

        private ServiceResult OnWriteDiscrete(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref Variant value,
            ref StatusCode statusCode,
            ref DateTimeUtc timestamp)
        {
            var variable = node as MultiStateDiscreteState;

            // verify data type.
            var typeInfo = TypeInfo.IsInstanceOfDataType(
                value,
                variable!.DataType,
                variable.ValueRank,
                context.NamespaceUris,
                context.TypeTable);

            if (typeInfo.IsUnknown)
            {
                return StatusCodes.BadTypeMismatch;
            }

            if (!indexRange.IsNull)
            {
                return StatusCodes.BadIndexRangeInvalid;
            }

            double number = value.GetDouble();

            if (number >= variable.EnumStrings!.Value.Count || number < 0)
            {
                return StatusCodes.BadOutOfRange;
            }

            return ServiceResult.Good;
        }

        private ServiceResult OnWriteValueDiscrete(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref Variant value,
            ref StatusCode statusCode,
            ref DateTimeUtc timestamp)
        {
            TypeInfo typeInfo = value.TypeInfo;

            if (node is not MultiStateValueDiscreteState variable ||
                typeInfo.IsUnknown ||
                !TypeInfo.IsNumericType(typeInfo.BuiltInType))
            {
                return StatusCodes.BadTypeMismatch;
            }

            if (!indexRange.IsNull)
            {
                return StatusCodes.BadIndexRangeInvalid;
            }

            int number = (int)value.GetUInt32();
            if (number >= variable.EnumValues!.Value.Count || number < 0)
            {
                return StatusCodes.BadOutOfRange;
            }

            if (!node.SetChildValue(
                context,
                Opc.Ua.BrowseNames.ValueAsText,
                variable.EnumValues.Value[number].DisplayName,
                true
            ))
            {
                return StatusCodes.BadOutOfRange;
            }

            node.ClearChangeMasks(context, true);

            return ServiceResult.Good;
        }

        private ServiceResult OnWriteAnalog(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref Variant value,
            ref StatusCode statusCode,
            ref DateTimeUtc timestamp)
        {
            var variable = node as AnalogItemState;

            // verify data type.
            var typeInfo = TypeInfo.IsInstanceOfDataType(
                value,
                variable!.DataType,
                variable.ValueRank,
                context.NamespaceUris,
                context.TypeTable);

            if (typeInfo.IsUnknown)
            {
                return StatusCodes.BadTypeMismatch;
            }

            // check index range.
            if (variable.ValueRank >= 0)
            {
                if (!indexRange.IsNull)
                {
                    Variant target = variable.Value;
                    ServiceResult result = indexRange.UpdateRange(ref target, value);

                    if (ServiceResult.IsBad(result))
                    {
                        return result;
                    }

                    value = target;
                }
            }
            // check instrument range.
            else
            {
                if (!indexRange.IsNull)
                {
                    return StatusCodes.BadIndexRangeInvalid;
                }

                double number = value.GetDouble();

                if (variable.InstrumentRange != null &&
                    (number < variable.InstrumentRange.Value.Low ||
                        number > variable.InstrumentRange.Value.High))
                {
                    return StatusCodes.BadOutOfRange;
                }
            }

            return ServiceResult.Good;
        }

        private ServiceResult OnWriteAnalogRange(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref Variant value,
            ref StatusCode statusCode,
            ref DateTimeUtc timestamp)
        {
            TypeInfo typeInfo = value.TypeInfo;

            if (node is not PropertyState<Range> variable ||
                !value.TryGetValue(out ExtensionObject extensionObject) ||
                typeInfo.IsUnknown)
            {
                return StatusCodes.BadTypeMismatch;
            }
            if (!extensionObject.TryGetValue(out Range? newRange) ||
                variable.Parent is not AnalogItemState parent)
            {
                return StatusCodes.BadTypeMismatch;
            }

            if (!indexRange.IsNull)
            {
                return StatusCodes.BadIndexRangeInvalid;
            }

            TypeInfo parentTypeInfo = parent.Value.TypeInfo;
            Range parentRange = GetAnalogRange(parentTypeInfo.BuiltInType);
            if (parentRange.High < newRange.High || parentRange.Low > newRange.Low)
            {
                return StatusCodes.BadOutOfRange;
            }

            value = Variant.FromStructure(newRange);

            return ServiceResult.Good;
        }

        /// <summary>
        /// Fires a base event whenever the trigger node is written to.
        /// </summary>
        private ServiceResult OnWriteTriggerNode(
            ISystemContext context,
            NodeState node,
            ref Variant value)
        {
            var e = new BaseEventState(null);
            e.Initialize(
                context,
                node,
                EventSeverity.Medium,
                new LocalizedText($"Trigger event from '{node.DisplayName.Text}'"));
            Server.ReportEvent(context, e);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Creates a default <see cref="AxisInformation"/> instance with the given title.
        /// </summary>
        private static AxisInformation CreateDefaultAxisInformation(string title)
        {
            return new()
            {
                EngineeringUnits = new EUInformation("s", "seconds", "http://www.opcfoundation.org/UA/units/un/cefact"),
                EURange = new Range(100, 0),
                Title = new LocalizedText("en", title),
                AxisScaleType = AxisScaleEnumeration.Linear
            };
        }

        /// <summary>
        /// Applies common read/write access settings to a mandatory child property of an array item variable.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private static void SetArrayItemChildAccess<T>(PropertyState<T> property)
        {
            if (property != null)
            {
                property.AccessLevel = AccessLevels.CurrentReadOrWrite;
                property.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            }
        }

        /// <summary>
        /// Creates and adds a <see cref="YArrayItemState"/> variable to the address space.
        /// </summary>
        private YArrayItemState CreateYArrayItemVariable(NodeState parent, string path, string name)
        {
            var variable = new YArrayItemState(parent)
            {
                BrowseName = new QualifiedName(path, NamespaceIndex)
            };
            variable.Create(
                SystemContext,
                new NodeId(path, NamespaceIndex),
                variable.BrowseName,
                default,
                true);

            variable.NodeId = new NodeId(path, NamespaceIndex);
            variable.SymbolicName = name;
            variable.DisplayName = new LocalizedText("en", name);
            variable.WriteMask = AttributeWriteMask.None;
            variable.UserWriteMask = AttributeWriteMask.None;
            variable.ReferenceTypeId = ReferenceTypeIds.Organizes;
            variable.DataType = DataTypeIds.Double;
            variable.ValueRank = ValueRanks.OneDimension;
            variable.ArrayDimensions = [0];
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;
            variable.Value = Variant.From(s_doubleArray);
            variable.StatusCode = StatusCodes.Good;

            if (variable.XAxisDefinition != null)
            {
                variable.XAxisDefinition.Value = CreateDefaultAxisInformation("X Axis");
                SetArrayItemChildAccess(variable.XAxisDefinition);
            }
            if (variable.EURange != null)
            {
                variable.EURange.Value = new Range(100, 0);
                SetArrayItemChildAccess(variable.EURange);
            }
            if (variable.InstrumentRange != null)
            {
                variable.InstrumentRange.Value = new Range(120, -10);
                SetArrayItemChildAccess(variable.InstrumentRange);
            }

            parent?.AddChild(variable);

            return variable;
        }

        /// <summary>
        /// Creates and adds a <see cref="XYArrayItemState"/> variable to the address space.
        /// </summary>
        private XYArrayItemState CreateXYArrayItemVariable(NodeState parent, string path, string name)
        {
            var variable = new XYArrayItemState(parent)
            {
                BrowseName = new QualifiedName(path, NamespaceIndex)
            };
            variable.Create(
                SystemContext,
                new NodeId(path, NamespaceIndex),
                variable.BrowseName,
                default,
                true);

            variable.NodeId = new NodeId(path, NamespaceIndex);
            variable.SymbolicName = name;
            variable.DisplayName = new LocalizedText("en", name);
            variable.WriteMask = AttributeWriteMask.None;
            variable.UserWriteMask = AttributeWriteMask.None;
            variable.ReferenceTypeId = ReferenceTypeIds.Organizes;
            variable.DataType = new NodeId(DataTypes.XVType);
            variable.ValueRank = ValueRanks.OneDimension;
            variable.ArrayDimensions = [0];
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;
            variable.Value = new XVType[]
            {
                new() { X = 0.0, Value = 0.0f },
                new() { X = 1.0, Value = 1.0f },
                new() { X = 2.0, Value = 4.0f },
                new() { X = 3.0, Value = 9.0f },
                new() { X = 4.0, Value = 16.0f }
            }.ToMatrixOf(5);
            variable.StatusCode = StatusCodes.Good;

            if (variable.XAxisDefinition != null)
            {
                variable.XAxisDefinition.Value = CreateDefaultAxisInformation("X Axis");
                SetArrayItemChildAccess(variable.XAxisDefinition);
            }
            if (variable.EURange != null)
            {
                variable.EURange.Value = new Range(100, 0);
                SetArrayItemChildAccess(variable.EURange);
            }
            if (variable.InstrumentRange != null)
            {
                variable.InstrumentRange.Value = new Range(120, -10);
                SetArrayItemChildAccess(variable.InstrumentRange);
            }

            parent?.AddChild(variable);
            return variable;
        }

        /// <summary>
        /// Creates and adds an <see cref="ImageItemState"/> variable to the address space.
        /// </summary>
        private ImageItemState CreateImageItemVariable(NodeState parent, string path, string name)
        {
            var variable = new ImageItemState(parent)
            {
                BrowseName = new QualifiedName(path, NamespaceIndex)
            };
            variable.Create(
                SystemContext,
                new NodeId(path, NamespaceIndex),
                variable.BrowseName,
                default,
                true);

            variable.NodeId = new NodeId(path, NamespaceIndex);
            variable.SymbolicName = name;
            variable.DisplayName = new LocalizedText("en", name);
            variable.WriteMask = AttributeWriteMask.None;
            variable.UserWriteMask = AttributeWriteMask.None;
            variable.ReferenceTypeId = ReferenceTypeIds.Organizes;
            variable.DataType = DataTypeIds.Double;
            variable.ValueRank = ValueRanks.TwoDimensions;
            variable.ArrayDimensions = [0, 0];
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;
            variable.Value = Variant.From(
                MatrixOf<double>.CreateFromArray(new double[,]
                {
                        { 0.0, 1.0, 2.0 },
                        { 3.0, 4.0, 5.0 }
                }));
            variable.StatusCode = StatusCodes.Good;

            if (variable.XAxisDefinition != null)
            {
                variable.XAxisDefinition.Value = CreateDefaultAxisInformation("X Axis");
                SetArrayItemChildAccess(variable.XAxisDefinition);
            }
            if (variable.YAxisDefinition != null)
            {
                variable.YAxisDefinition.Value = CreateDefaultAxisInformation("Y Axis");
                SetArrayItemChildAccess(variable.YAxisDefinition);
            }
            if (variable.EURange != null)
            {
                variable.EURange.Value = new Range(100, 0);
                SetArrayItemChildAccess(variable.EURange);
            }
            if (variable.InstrumentRange != null)
            {
                variable.InstrumentRange.Value = new Range(120, -10);
                SetArrayItemChildAccess(variable.InstrumentRange);
            }

            parent?.AddChild(variable);
            return variable;
        }

        /// <summary>
        /// Creates and adds a <see cref="CubeItemState"/> variable to the address space.
        /// </summary>
        private CubeItemState CreateCubeItemVariable(NodeState parent, string path, string name)
        {
            var variable = new CubeItemState(parent)
            {
                BrowseName = new QualifiedName(path, NamespaceIndex)
            };
            variable.Create(
                SystemContext,
                new NodeId(path, NamespaceIndex),
                variable.BrowseName,
                default,
                true);

            variable.NodeId = new NodeId(path, NamespaceIndex);
            variable.SymbolicName = name;
            variable.DisplayName = new LocalizedText("en", name);
            variable.WriteMask = AttributeWriteMask.None;
            variable.UserWriteMask = AttributeWriteMask.None;
            variable.ReferenceTypeId = ReferenceTypeIds.Organizes;
            variable.DataType = DataTypeIds.Double;
            variable.ValueRank = 3;
            variable.ArrayDimensions = [0, 0, 0];
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;
            variable.Value = Variant.From(
                MatrixOf<double>.CreateFromArray(new double[,,]
                {
                    { { 0.0, 1.0 }, { 2.0, 3.0 } },
                    { { 4.0, 5.0 }, { 6.0, 7.0 } }
                }));
            variable.StatusCode = StatusCodes.Good;

            if (variable.XAxisDefinition != null)
            {
                variable.XAxisDefinition.Value = CreateDefaultAxisInformation("X Axis");
                SetArrayItemChildAccess(variable.XAxisDefinition);
            }
            if (variable.YAxisDefinition != null)
            {
                variable.YAxisDefinition.Value = CreateDefaultAxisInformation("Y Axis");
                SetArrayItemChildAccess(variable.YAxisDefinition);
            }
            if (variable.ZAxisDefinition != null)
            {
                variable.ZAxisDefinition.Value = CreateDefaultAxisInformation("Z Axis");
                SetArrayItemChildAccess(variable.ZAxisDefinition);
            }
            if (variable.EURange != null)
            {
                variable.EURange.Value = new Range(100, 0);
                SetArrayItemChildAccess(variable.EURange);
            }
            if (variable.InstrumentRange != null)
            {
                variable.InstrumentRange.Value = new Range(120, -10);
                SetArrayItemChildAccess(variable.InstrumentRange);
            }

            parent?.AddChild(variable);
            return variable;
        }

        /// <summary>
        /// Creates and adds an <see cref="NDimensionArrayItemState"/> variable to the address space.
        /// </summary>
        private NDimensionArrayItemState CreateNDimensionArrayItemVariable(NodeState parent, string path, string name)
        {
            var variable = new NDimensionArrayItemState(parent)
            {
                BrowseName = new QualifiedName(path, NamespaceIndex)
            };
            variable.Create(
                SystemContext,
                new NodeId(path, NamespaceIndex),
                variable.BrowseName,
                default,
                true);

            variable.NodeId = new NodeId(path, NamespaceIndex);
            variable.SymbolicName = name;
            variable.DisplayName = new LocalizedText("en", name);
            variable.WriteMask = AttributeWriteMask.None;
            variable.UserWriteMask = AttributeWriteMask.None;
            variable.ReferenceTypeId = ReferenceTypeIds.Organizes;
            variable.DataType = DataTypeIds.Double;
            variable.ValueRank = ValueRanks.TwoDimensions;
            variable.ArrayDimensions = [0, 0];
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;
            variable.Value = Variant.From(
                MatrixOf<double>.CreateFromArray(new double[,]
                {
                    { 0.0, 1.0, 2.0 },
                    { 3.0, 4.0, 5.0 }
                }));
            variable.StatusCode = StatusCodes.Good;

            if (variable.AxisDefinition != null)
            {
                variable.AxisDefinition.Value = new AxisInformation[]
                {
                    CreateDefaultAxisInformation("X Axis"),
                    CreateDefaultAxisInformation("Y Axis")
                }.ToArrayOf();
                SetArrayItemChildAccess(variable.AxisDefinition);
            }
            if (variable.EURange != null)
            {
                variable.EURange.Value = new Range(100, 0);
                SetArrayItemChildAccess(variable.EURange);
            }
            if (variable.InstrumentRange != null)
            {
                variable.InstrumentRange.Value = new Range(120, -10);
                SetArrayItemChildAccess(variable.InstrumentRange);
            }

            parent?.AddChild(variable);
            return variable;
        }

        /// <summary>
        /// Finds a variable materialized from the model.
        /// </summary>
        private BaseDataVariableState CreateVariable(string path)
        {
            BaseDataVariableState variable = FindPredefinedNode<BaseDataVariableState>(
                new NodeId(path, NamespaceIndex));
            if (variable == null)
            {
                return null!;
            }

            return variable;
        }

        /// <summary>
        /// Finds a variable materialized from the model, and registers
        /// it for the value simulation.
        /// </summary>
        private BaseDataVariableState CreateDynamicVariable(string path)
        {
            BaseDataVariableState variable = CreateVariable(path);
            m_dynamicNodes.Add(variable);
            return variable;
        }

        /// <summary>
        /// Re-seeds and registers a numbered set of dynamic variables materialized from the model.
        /// </summary>
        private BaseDataVariableState[] CreateDynamicVariables(string path, string name, uint numVariables)
        {
            var itemsCreated = new List<BaseDataVariableState>();
            for (uint i = 0; i < numVariables; i++)
            {
                string newName = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}_{1}",
                    name,
                    i.ToString("00", CultureInfo.InvariantCulture));
                string newPath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}_{1}",
                    path,
                    newName);
                itemsCreated.Add(CreateDynamicVariable(newPath));
            } //for i
            return [.. itemsCreated];
        }

        /// <summary>
        /// Creates a new view.
        /// </summary>
        private async ValueTask<ViewState> CreateViewAsync(
            NodeState parent,
            IDictionary<NodeId, IList<IReference>> externalReferences,
            string path,
            string name,
            CancellationToken cancellationToken = default)
        {
            var type = new ViewState
            {
                SymbolicName = name,
                NodeId = new NodeId(path, NamespaceIndex),
                BrowseName = new QualifiedName(name, NamespaceIndex)
            };
            type.DisplayName = LocalizedText.From(type.BrowseName.Name!);
            type.WriteMask = AttributeWriteMask.None;
            type.UserWriteMask = AttributeWriteMask.None;
            type.ContainsNoLoops = true;

            if (!externalReferences.TryGetValue(
                Opc.Ua.ObjectIds.ViewsFolder,
                out IList<IReference>? references))
            {
                externalReferences[Opc.Ua.ObjectIds.ViewsFolder] = references = [];
            }

            type.AddReference(ReferenceTypeIds.Organizes, true, Opc.Ua.ObjectIds.ViewsFolder);
            references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, type.NodeId));

            if (parent != null)
            {
                parent.AddReference(ReferenceTypeIds.Organizes, false, type.NodeId);
                type.AddReference(ReferenceTypeIds.Organizes, true, parent.NodeId);
            }

            await AddPredefinedNodeAsync(SystemContext, type, cancellationToken).ConfigureAwait(false);
            return type;
        }

        private void ResetRandomGenerator(int seed, int boundaryValueFrequency = 0)
        {
            m_randomSource = new RandomSource(seed);
            m_generator = new DataGenerator(m_randomSource, Server.Telemetry)
            {
                BoundaryValueFrequency = boundaryValueFrequency
            };
        }

        private Variant GetNewValue(BaseVariableState variable)
        {
            Debug.Assert(m_generator != null, "Need a random generator!");

            // Supply a concrete size for every dimension of a multi-dimensional
            // array so the generated matrix is self-consistent with the node's
            // ArrayDimensions attribute. Without this the CTT multi-dimensional
            // read/index-range tests skip the node ("Length of second dimension:
            // 0"). Scalars and single-dimension arrays keep the historic random
            // length.
            uint[] dimensions = variable.ValueRank >= ValueRanks.TwoDimensions
                ? CreateFixedArrayDimensions(variable.ValueRank)
                : [DefaultArrayLength];

            // For variables whose DataType is Variant (BaseDataType) the CTT
            // only accepts values whose concrete BuiltInType is a simple type
            // (< XmlElement, i.e. BuiltInType value 16). The DataGenerator can
            // return any BuiltInType, so keep retrying until it produces an
            // acceptable one alongside the existing null-value retry.
            bool isVariantDataType = TypeInfo.GetBuiltInType(variable.DataType, Server.TypeTree)
                == BuiltInType.Variant;

            Variant value = default;
            for (int retryCount = 0;
                (value.IsNull
                 || (isVariantDataType && value.TypeInfo.BuiltInType >= BuiltInType.XmlElement)) &&
                retryCount < 10;
                retryCount++)
            {
                value = m_generator!.GetRandom(
                    variable.DataType,
                    variable.ValueRank,
                    dimensions,
                    Server.TypeTree);
            }

            // The CTT requires the leading ByteString array elements to be at
            // least four bytes long. The random generator can produce shorter
            // values, so pad indexes 0..2 up to the minimum length.
            if (variable.DataType == DataTypeIds.ByteString &&
                variable.ValueRank == ValueRanks.OneDimension &&
                value.TryGetValue(out ArrayOf<ByteString> byteStringArray))
            {
                ByteString[] byteStrings = byteStringArray.ToArray()!;
                for (int ii = 0; ii < 3 && ii < byteStrings.Length; ii++)
                {
                    byteStrings[ii] = EnsureMinimumByteStringLength(byteStrings[ii], 4);
                }
                value = Variant.From(byteStrings.ToArrayOf());
            }

            return value;
        }

        /// <summary>
        /// Returns a ByteString that is at least <paramref name="minimumLength"/>
        /// bytes long, right-padding the original bytes with zeros when needed.
        /// </summary>
        private static ByteString EnsureMinimumByteStringLength(ByteString value, int minimumLength)
        {
            ReadOnlySpan<byte> bytes = value.IsNull ? default : value.Span;
            if (bytes.Length >= minimumLength)
            {
                return value;
            }
            byte[] padded = new byte[minimumLength];
            bytes.CopyTo(padded);
            return ByteString.From(padded);
        }

        /// <summary>
        /// Creates a fixed-size dimension array (one entry per dimension) for a
        /// multi-dimensional array so its value and ArrayDimensions attribute stay
        /// deterministic and consistent.
        /// </summary>
        private static uint[] CreateFixedArrayDimensions(int valueRank)
        {
            uint[] dimensions = new uint[valueRank];
            for (int ii = 0; ii < valueRank; ii++)
            {
                dimensions[ii] = MultiDimensionalArrayLength;
            }
            return dimensions;
        }

        private void DoSimulation(object? state)
        {
            if (!m_simulationEnabled)
            {
                return;
            }
            int running = Interlocked.Increment(ref m_simulationsRunning);
            try
            {
                if (running > 0)
                {
                    LogLevel logLevel = running > 1 ?
                        running > 4 ? LogLevel.Warning : LogLevel.Information :
                        LogLevel.Debug;
                    if (m_logger.IsEnabled(logLevel))
                    {
                        m_logger.Log(logLevel,
                            "Simulation timer fired while {Count} simulations are already queued to run.",
                            running);
                    }
                }
                m_semaphore.Wait();
                try
                {
                    DateTimeUtc timeStamp = DateTimeUtc.Now;
                    foreach (BaseDataVariableState variable in m_dynamicNodes)
                    {
                        variable.Value = GetNewValue(variable);
                        variable.Timestamp = timeStamp;
                        variable.ClearChangeMasks(SystemContext, false);
                    }
                }
                finally
                {
                    m_semaphore.Release();
                }
            }
            catch (ObjectDisposedException) when (m_simulationTimer is null)
            {
                // Expected during teardown: Dispose() nulls m_simulationTimer and
                // then disposes m_semaphore (see the Dispose() comment). A timer
                // callback already in flight past the m_simulationEnabled guard
                // will see the disposed semaphore - not a bug, just a documented
                // race. Filter it out so the test log doesn't get a misleading
                // "Unexpected error doing simulation" entry on every server teardown.
            }
            catch (Exception e)
            {
                m_logger.UnexpectedErrorDoingSimulation(e, running);
            }
            finally
            {
                Interlocked.Decrement(ref m_simulationsRunning);
            }
        }

        /// <summary>
        /// Returns a unique handle for the node.
        /// </summary>
        protected override ValueTask<NodeHandle> GetManagerHandleAsync(
            ServerSystemContext context,
            NodeId nodeId,
            IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            // quickly exclude nodes that are not in the namespace.
            if (!IsNodeIdInNamespace(nodeId))
            {
                return default;
            }

            if (!PredefinedNodes.TryGetValue(nodeId, out NodeState? node))
            {
                return default;
            }

            return new ValueTask<NodeHandle>(new NodeHandle
            {
                NodeId = nodeId,
                Node = node,
                Validated = true
            });
        }

        /// <summary>
        /// Verifies that the specified node exists.
        /// </summary>
        protected override ValueTask<NodeState> ValidateNodeAsync(
            ServerSystemContext context,
            NodeHandle handle,
            IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            // not valid if no root.
            if (handle == null)
            {
                return default;
            }

            // check if previously validated.
            if (handle.Validated)
            {
                return new ValueTask<NodeState>(handle.Node);
            }

            // TBD

            return default;
        }

        private readonly SemaphoreSlim m_semaphore = new(1, 1);
        private RandomSource m_randomSource = null!;
        private DataGenerator m_generator = null!;
        private ITimer? m_simulationTimer;
        private ushort m_simulationInterval = 1000;
        private bool m_simulationEnabled = true;
        private int m_simulationsRunning;
        private readonly List<BaseDataVariableState> m_dynamicNodes = [];

        /// <summary>
        /// Default random length used when generating single-dimension array values.
        /// </summary>
        private const uint DefaultArrayLength = 10;

        /// <summary>
        /// Fixed length used for every dimension of a generated multi-dimensional
        /// array so its value and ArrayDimensions attribute stay deterministic.
        /// </summary>
        private const uint MultiDimensionalArrayLength = 3;

        /// <summary>
        /// NodeId identifier of the historizing node whose historian intentionally
        /// does not support server timestamps.
        /// </summary>
        private const string NodeDoesNotSupportServerTimestampNodeName
            = "Scalar_Static_NodeDoesNotSupportServerTimestamp";

        private InMemoryHistorianProvider? m_historian;

        /// <summary>
        /// Base set of conformance units the reference server always supports
        /// (core address space, attribute, base info, discovery, method,
        /// monitoring, security, session, subscription, transport and view
        /// facets). Feature-specific units — e.g. Historical Access — are added
        /// on top when the corresponding feature is enabled. Sourced from the
        /// OPC UA profile registry (UACore 1.05 ProfileSet).
        /// </summary>
        private static readonly QualifiedName[] s_baseConformanceUnitNames =
        [
            new("Address Space Atomicity"),
            new("Address Space Base"),
            new("Address Space Full Array Only"),
            new("Address Space Method"),
            new("Attribute Read"),
            new("Base Info Base Types"),
            new("Base Info Core Structure 2"),
            new("Base Info Core Types Folders"),
            new("Base Info Date DataTypes"),
            new("Base Info Decimal DataType"),
            new("Base Info GetMonitoredItems Method"),
            new("Base Info Method Argument DataType"),
            new("Base Info Method Capabilities"),
            new("Base Info ResendData Method"),
            new("Base Info SemanticChange Bit"),
            new("Base Info Server Capabilities 2"),
            new("Base Info Server Capabilities MaxMonitoredItemsQueueSize"),
            new("Base Info Server Capabilities Subscriptions"),
            new("Base Info ServerType"),
            new("Base Info Type Information"),
            new("Data Access DataItems"),
            new("Discovery Find Servers Self"),
            new("Discovery Get Endpoints"),
            new("Discovery Register"),
            new("Discovery Register2"),
            new("Documentation - Core Capacities"),
            new("Method Call"),
            new("Monitor Basic"),
            new("Monitor Items 2"),
            new("Monitor Queueing"),
            new("Monitor Triggering"),
            new("Monitor Value Change V2"),
            new("Monitored Items Deadband Filter"),
            new("Protocol Reverse Connect Server"),
            new("Protocol UA TCP"),
            new("Push Model for Global Certificate and TrustList Management"),
            new("Security Default ApplicationInstance Certificate"),
            new("Security ECC Policy"),
            new("Security Invalid user token"),
            new("Security Policy Required"),
            new("Security User Name Password 2"),
            new("Security User X509"),
            new("SecurityPolicy Support"),
            new("Session Base"),
            new("Session Cancel"),
            new("Session General Service Behaviour"),
            new("Session Multiple"),
            new("Subscription Basic"),
            new("Subscription Multiple"),
            new("Subscription Publish Basic"),
            new("Subscription PublishRequest Queue Overflow"),
            new("Subscription Retransmission Queue"),
            new("Subscription Transfer"),
            new("Time Sync - Support"),
            new("UA Binary Encoding"),
            new("UA Secure Conversation"),
            new("View Basic 2"),
            new("View RegisterNodes"),
            new("View TranslateBrowsePath")
        ];

        /// <summary>
        /// Historical Access conformance units advertised while history
        /// archiving is enabled.
        /// </summary>
        private static readonly QualifiedName[] s_historicalAccessConformanceUnitNames =
        [
            new("Aggregate Master Configuration"),
            new("Attribute Historical Read"),
            new("Base Info History Read Capabilities"),
            new("Base Info History ReadData Capabilities"),
            new("Historical Access Aggregates"),
            new("Historical Access Read Raw")
        ];

        /// <summary>
        /// The always-supported base conformance units.
        /// </summary>
        private static readonly ArrayOf<QualifiedName> s_baseConformanceUnits =
            s_baseConformanceUnitNames.ToArrayOf();

        /// <summary>
        /// The base conformance units plus the Historical Access units, advertised
        /// while history archiving is enabled.
        /// </summary>
        private static readonly ArrayOf<QualifiedName> s_baseWithHistoricalConformanceUnits =
            new QualifiedName[][]
                {
                    s_baseConformanceUnitNames,
                    s_historicalAccessConformanceUnitNames
                }
                .SelectMany(names => names)
                .ToArrayOf();

        /// <summary>
        /// Historical Access server profile URIs advertised while history
        /// archiving is enabled.
        /// </summary>
        private static readonly ArrayOf<string> s_historicalAccessProfiles = new[]
        {
            "http://opcfoundation.org/UA-Profile/Server/HistoricalRawData2022",
            "http://opcfoundation.org/UA-Profile/Server/AggregateHistorical2022"
        }.ToArrayOf();

        /// <summary>
        /// Identifiers of the nodes that support history archiving.
        /// </summary>
        private static readonly string[] HistoricalNodeNames =
        [
            "Scalar_Static_Boolean",
            "Scalar_Static_SByte",
            "Scalar_Static_Byte",
            "Scalar_Static_Int16",
            "Scalar_Static_UInt16",
            "Scalar_Static_Int32",
            "Scalar_Static_UInt32",
            "Scalar_Static_Int64",
            "Scalar_Static_UInt64",
            "Scalar_Static_Float",
            "Scalar_Static_Double",
            "Scalar_Static_String",
            "Scalar_Static_DateTime",
            "Scalar_Static_Guid",
            "Scalar_Static_ByteString",
            "Aggregates_Boolean",
            "Aggregates_Int32",
            "Aggregates_Float",
            "Aggregates_Double",
            "Aggregates_String"
        ];

        /// <summary>
        /// Identifiers of the one-dimensional array nodes that support history
        /// archiving. These map to the CTT "HA Profile > Arrays" node ids and
        /// mirror the element types historized for the scalar nodes.
        /// </summary>
        private static readonly string[] HistoricalArrayNodeNames =
        [
            "Scalar_Static_Arrays_Boolean",
            "Scalar_Static_Arrays_SByte",
            "Scalar_Static_Arrays_Byte",
            "Scalar_Static_Arrays_Int16",
            "Scalar_Static_Arrays_UInt16",
            "Scalar_Static_Arrays_Int32",
            "Scalar_Static_Arrays_UInt32",
            "Scalar_Static_Arrays_Int64",
            "Scalar_Static_Arrays_UInt64",
            "Scalar_Static_Arrays_Float",
            "Scalar_Static_Arrays_Double",
            "Scalar_Static_Arrays_String",
            "Scalar_Static_Arrays_DateTime",
            "Scalar_Static_Arrays_ByteString"
        ];

        /// <summary>
        /// Identifiers of the two-dimensional array (matrix) nodes that support
        /// history archiving. These map to the CTT "HA Profile > Arrays" 2D node
        /// ids and mirror the element types historized for the one-dimensional
        /// array nodes.
        /// </summary>
        private static readonly string[] HistoricalMatrixNodeNames =
        [
            "Scalar_Static_Arrays2D_Boolean",
            "Scalar_Static_Arrays2D_SByte",
            "Scalar_Static_Arrays2D_Byte",
            "Scalar_Static_Arrays2D_Int16",
            "Scalar_Static_Arrays2D_UInt16",
            "Scalar_Static_Arrays2D_Int32",
            "Scalar_Static_Arrays2D_UInt32",
            "Scalar_Static_Arrays2D_Int64",
            "Scalar_Static_Arrays2D_UInt64",
            "Scalar_Static_Arrays2D_Float",
            "Scalar_Static_Arrays2D_Double",
            "Scalar_Static_Arrays2D_String",
            "Scalar_Static_Arrays2D_DateTime",
            "Scalar_Static_Arrays2D_ByteString"
        ];

        /// <summary>
        /// Identifiers of the structure nodes that support history archiving.
        /// These back the CTT "HA Profile > StructureNodeSupportingHistory"
        /// slot.
        /// </summary>
        private static readonly string[] HistoricalStructureNodeNames =
        [
            "Scalar_Static_Decimal"
        ];

        /// <summary>
        /// Identifiers of the AccessRights nodes that are marked as supporting
        /// history archiving so History Access clients (and the CTT) can
        /// exercise access-right handling on historizing nodes. These nodes are
        /// registered with the historian and seeded with the same deterministic
        /// sample set as the other historized test nodes.
        /// </summary>
        private static readonly string[] AccessRightsHistoricalNodeNames =
        [
            "AccessRights_AccessAll_RO",
            "AccessRights_AccessAll_WO",
            "AccessRights_AccessAll_NoAccess",
            "AccessRights_AccessAll_RW_NotUser",
            "AccessRights_AccessAll_RO_NotUser"
        ];

        /// <inheritdoc/>
        protected override IHistorianProvider? GetHistorianProvider(NodeState node)
        {
            return m_historian;
        }

        /// <summary>
        /// Enables history archiving on selected scalar variables using
        /// the fluent <see cref="HistorianBuilder"/> API.
        /// </summary>
        private async Task EnableHistoryArchivingAsync(CancellationToken cancellationToken)
        {
            m_historian = new InMemoryHistorianProvider();

            // Make the provider discoverable to the server-wide registry so
            // server capabilities (HistoryServerCapabilities) reflect what the
            // provider supports. The dispatcher will still prefer the
            // per-node-manager override returned by GetHistorianProvider.
            if (Server is IHistorianRegistryProvider registry)
            {
                registry.HistorianRegistry.RegisterDefault(m_historian);
            }

            // Capabilities advertised per historizing node. StartOfArchive is set
            // slightly before the earliest seeded sample so History Access clients
            // (and the CTT) can discover the archive window via the installed
            // HistoricalDataConfigurationType companion object.
            var capabilities = new HistorianNodeCapabilities
            {
                InsertData = true,
                ReplaceData = true,
                UpdateData = true,
                DeleteRaw = true,
                DeleteAtTime = true,
                InsertAnnotation = true,
                ServerTimestampSupported = true,
                Stepped = false,
                StartOfArchive = new DateTimeUtc(DateTime.UtcNow.AddSeconds(-10000)),
                StartOfOnlineArchive = new DateTimeUtc(DateTime.UtcNow.AddSeconds(-10000))
            };

            // The dedicated node whose historian does not support server
            // timestamps reuses the shared capabilities with
            // ServerTimestampSupported cleared, so History Read never returns a
            // ServerTimestamp for it (backs the CTT
            // "HA Profile > NodeDoesNotSupportServerTimestamp" slot).
            HistorianNodeCapabilities noServerTimestampCapabilities =
                capabilities with { ServerTimestampSupported = false };

            foreach (string name in HistoricalNodeNames
                .Concat(HistoricalArrayNodeNames)
                .Concat(HistoricalMatrixNodeNames)
                .Concat(HistoricalStructureNodeNames)
                .Concat(AccessRightsHistoricalNodeNames)
                .Append(NodeDoesNotSupportServerTimestampNodeName)
                )
            {
                var nodeId = new NodeId(name, NamespaceIndex);

                if (!PredefinedNodes.TryGetValue(nodeId, out NodeState? node))
                {
                    continue;
                }

                if (node is not BaseVariableState variable)
                {
                    continue;
                }

                variable.Historizing = true;
                if (!AccessRightsHistoricalNodeNames.Contains(name))
                {
                    variable.AccessLevel = (byte)(variable.AccessLevel | AccessLevels.HistoryRead | AccessLevels.HistoryWrite);
                    variable.UserAccessLevel = (byte)(variable.UserAccessLevel | AccessLevels.HistoryRead | AccessLevels.HistoryWrite);
                }

                m_historian.Register(
                    nodeId,
                    name == NodeDoesNotSupportServerTimestampNodeName
                        ? noServerTimestampCapabilities
                        : capabilities);

                await SeedHistoricalNodeAsync(variable, cancellationToken).ConfigureAwait(false);

                // Attach a HistoricalDataConfigurationType companion object
                // (browse name "HA Configuration") and wire it via the
                // HasHistoricalConfiguration reference so History Access aggregate
                // clients and the CTT can discover the node's configuration
                // (OPC UA Part 11 5.2.3).
                HistoricalDataConfigurationState config = await HistoricalDataConfigurationInstaller
                    .EnsureInstalledAsync(SystemContext, variable, m_historian, cancellationToken)
                    .ConfigureAwait(false);
                await AddPredefinedNodeAsync(SystemContext, config, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task SeedHistoricalNodeAsync(BaseVariableState variable, CancellationToken cancellationToken)
        {
            var nodeId = (NodeId)variable.NodeId;
            BuiltInType dataType = TypeInfo.GetBuiltInType(variable.DataType);
            bool isStructure = variable.DataType == DataTypeIds.DecimalDataType;
            bool isMatrix = variable.ValueRank >= ValueRanks.TwoDimensions;
            bool isArray = variable.ValueRank == ValueRanks.OneDimension;
            DateTime now = DateTime.UtcNow;
            var seed = new List<DataValue>(1001);
            for (int ii = 1000; ii >= 0; ii--)
            {
                int value = 1000 - ii;
                Variant variant;
                if (isStructure)
                {
                    variant = CreateHistoricalStructureValue(value);
                }
                else if (isMatrix)
                {
                    variant = CreateHistoricalMatrixValue(dataType, value, now);
                }
                else if (isArray)
                {
                    variant = CreateHistoricalArrayValue(dataType, value, now);
                }
                else
                {
                    variant = CreateHistoricalScalarValue(dataType, value, now);
                }
                StatusCode statusCode = GetSeededStatusCode(value);
                seed.Add(new DataValue(
                    variant,
                    statusCode,
                    sourceTimestamp: now.AddSeconds(-(ii * 10)).AddMilliseconds(1234),
                    serverTimestamp: now.AddSeconds(-(ii * 10))));
            }
            using var opContext = new OperationContext(new RequestHeader(), null, RequestType.HistoryUpdate, RequestLifetime.None);
            var systemContext = new ServerSystemContext(Server, opContext);
            var historianContext = new HistorianOperationContext(systemContext, opContext, null, HistoryUpdateType.Insert);
            _ = await m_historian!.InsertAsync(historianContext, nodeId, seed, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Returns a deterministic status code for a seeded historical value so
        /// that the recorded data contains Good, Bad, and Uncertain quality.
        /// The pattern repeats every 10 samples: index 7 is Bad, index 9 is
        /// Uncertain, and the remaining 8 are Good.
        /// </summary>
        private static StatusCode GetSeededStatusCode(int sampleIndex)
        {
            return (sampleIndex % 10) switch
            {
                7 => StatusCodes.BadDataUnavailable,
                9 => StatusCodes.UncertainSubstituteValue,
                _ => StatusCodes.Good
            };
        }

        private static Variant CreateHistoricalScalarValue(BuiltInType dataType, int value, DateTime now)
        {
            return dataType switch
            {
                BuiltInType.Boolean => new Variant((value & 1) == 0),
                BuiltInType.SByte => new Variant((sbyte)(value % 100)),
                BuiltInType.Byte => new Variant((byte)(value % 200)),
                BuiltInType.Int16 => new Variant((short)value),
                BuiltInType.UInt16 => new Variant((ushort)value),
                BuiltInType.Int32 => new Variant(value),
                BuiltInType.UInt32 => new Variant((uint)value),
                BuiltInType.Int64 => new Variant((long)value),
                BuiltInType.UInt64 => new Variant((ulong)value),
                BuiltInType.Float => new Variant((float)value),
                BuiltInType.Double => new Variant((double)value),
                BuiltInType.String => new Variant(value.ToString(CultureInfo.InvariantCulture)),
                BuiltInType.DateTime => new Variant(new DateTimeUtc(now.AddSeconds(value))),
                BuiltInType.Guid => new Variant(new Uuid(new Guid(value, 0, 0, new byte[8]))),
                BuiltInType.ByteString => new Variant(new ByteString(BitConverter.GetBytes(value))),
                _ => new Variant(value)
            };
        }

        private static Variant CreateHistoricalArrayValue(
            BuiltInType dataType,
            int value,
            DateTime now)
        {
            // Build a small, deterministic one-dimensional array per historical
            // sample. Element values derive from the sample index so History Read
            // Raw of the array nodes returns self-consistent data for the CTT.
            const int length = 5;
            return dataType switch
            {
                BuiltInType.Boolean => Variant.From(
                    CreateArray(length, i => ((value + i) & 1) == 0).ToArrayOf()),
                BuiltInType.SByte => Variant.From(
                    CreateArray(length, i => (sbyte)((value + i) % 100)).ToArrayOf()),
                BuiltInType.Byte => Variant.From(
                    CreateArray(length, i => (byte)((value + i) % 200)).ToArrayOf()),
                BuiltInType.Int16 => Variant.From(
                    CreateArray(length, i => (short)(value + i)).ToArrayOf()),
                BuiltInType.UInt16 => Variant.From(
                    CreateArray(length, i => (ushort)(value + i)).ToArrayOf()),
                BuiltInType.Int32 => Variant.From(
                    CreateArray(length, i => value + i).ToArrayOf()),
                BuiltInType.UInt32 => Variant.From(
                    CreateArray(length, i => (uint)(value + i)).ToArrayOf()),
                BuiltInType.Int64 => Variant.From(
                    CreateArray(length, i => (long)(value + i)).ToArrayOf()),
                BuiltInType.UInt64 => Variant.From(
                    CreateArray(length, i => (ulong)(value + i)).ToArrayOf()),
                BuiltInType.Float => Variant.From(
                    CreateArray(length, i => (float)(value + i)).ToArrayOf()),
                BuiltInType.Double => Variant.From(
                    CreateArray(length, i => (double)(value + i)).ToArrayOf()),
                BuiltInType.String => Variant.From(
                    CreateArray(length, i => (value + i).ToString(CultureInfo.InvariantCulture)).ToArrayOf()),
                BuiltInType.DateTime => Variant.From(
                    CreateArray(length, i => new DateTimeUtc(now.AddSeconds(value + i))).ToArrayOf()),
                BuiltInType.ByteString => Variant.From(
                    CreateArray(length, i => new ByteString(BitConverter.GetBytes(value + i))).ToArrayOf()),
                _ => Variant.From(CreateArray(length, i => value + i).ToArrayOf())
            };
        }

        private static Variant CreateHistoricalMatrixValue(
            BuiltInType dataType,
            int value,
            DateTime now)
        {
            // Build a small, deterministic two-dimensional array (matrix) per
            // historical sample. Element values derive from the row/column
            // indexes so History Read Raw of the 2D array nodes returns
            // self-consistent multi-dimensional data for the CTT.
            const int rows = 2;
            const int cols = 2;
            return dataType switch
            {
                BuiltInType.Boolean => Variant.From(
                    CreateMatrix(rows, cols, (r, c) => ((value + r + c) & 1) == 0)),
                BuiltInType.SByte => Variant.From(
                    CreateMatrix(rows, cols, (r, c) => (sbyte)((value + r + c) % 100))),
                BuiltInType.Byte => Variant.From(
                    CreateMatrix(rows, cols, (r, c) => (byte)((value + r + c) % 200))),
                BuiltInType.Int16 => Variant.From(
                    CreateMatrix(rows, cols, (r, c) => (short)(value + r + c))),
                BuiltInType.UInt16 => Variant.From(
                    CreateMatrix(rows, cols, (r, c) => (ushort)(value + r + c))),
                BuiltInType.Int32 => Variant.From(
                    CreateMatrix(rows, cols, (r, c) => value + r + c)),
                BuiltInType.UInt32 => Variant.From(
                    CreateMatrix(rows, cols, (r, c) => (uint)(value + r + c))),
                BuiltInType.Int64 => Variant.From(
                    CreateMatrix(rows, cols, (r, c) => (long)(value + r + c))),
                BuiltInType.UInt64 => Variant.From(
                    CreateMatrix(rows, cols, (r, c) => (ulong)(value + r + c))),
                BuiltInType.Float => Variant.From(
                    CreateMatrix(rows, cols, (r, c) => (float)(value + r + c))),
                BuiltInType.Double => Variant.From(
                    CreateMatrix(rows, cols, (r, c) => (double)(value + r + c))),
                BuiltInType.String => Variant.From(
                    CreateMatrix(rows, cols, (r, c) => (value + r + c).ToString(CultureInfo.InvariantCulture))),
                BuiltInType.DateTime => Variant.From(
                    CreateMatrix(rows, cols, (r, c) => new DateTimeUtc(now.AddSeconds(value + r + c)))),
                BuiltInType.ByteString => Variant.From(
                    CreateMatrix(rows, cols, (r, c) => new ByteString(BitConverter.GetBytes(value + r + c)))),
                _ => Variant.From(CreateMatrix(rows, cols, (r, c) => value + r + c))
            };
        }

        private static Variant CreateHistoricalStructureValue(int value)
        {
            // Emit a structure (DecimalDataType) value per historical sample so
            // the CTT "StructureNodeSupportingHistory" node returns encodeable
            // structured data on History Read Raw.
            return Variant.FromStructure(new DecimalDataType
            {
                Scale = 100,
                Value = new BigInteger(value).ToByteString()
            });
        }

        private static T[] CreateArray<T>(int length, Func<int, T> factory)
        {
            var result = new T[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = factory(i);
            }
            return result;
        }

        private static MatrixOf<T> CreateMatrix<T>(int rows, int cols, Func<int, int, T> factory)
        {
            var result = new T[rows, cols];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    result[r, c] = factory(r, c);
                }
            }
            return result.ToMatrixOf();
        }

        private static readonly ArrayOf<double> s_doubleArray =
        [
            9.00001d,
            9.0002d,
            9.003d,
            9.04d,
            9.5d,
            9.06d,
            9.007d,
            9.008d,
            9.0009d
        ];

        private static readonly ArrayOf<float> s_singleArray
            = [0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 1.1f, 2.2f, 3.3f, 4.4f, 5.5f];

        private static readonly ArrayOf<short> s_shortArray = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
        private static readonly ArrayOf<int> s_int32Array = [10, 11, 12, 13, 14, 15, 16, 17, 18, 19];

        private static readonly ArrayOf<string> s_stringArray1 = ["open", "closed", "jammed"];
        private static readonly ArrayOf<string> s_stringArray2 = ["red", "green", "blue", "cyan"];
        private static readonly ArrayOf<string> s_stringArray3 = ["lolo", "lo", "normal", "hi", "hihi"];
        private static readonly ArrayOf<string> s_stringArray4 = ["left", "right", "center"];
        private static readonly ArrayOf<string> s_stringArray5 = ["circle", "cross", "triangle"];
        private static readonly ArrayOf<string> s_stringArray6 = ["open", "closed", "jammed"];
        private static readonly ArrayOf<string> s_stringArray7 = ["red", "green", "blue", "cyan"];
        private static readonly ArrayOf<string> s_stringArray8 = ["lolo", "lo", "normal", "hi", "hihi"];
        private static readonly ArrayOf<string> s_stringArray9 = ["left", "right", "center"];
    }

    public static class VariableExtensions
    {
        public static BaseDataVariableState MinimumSamplingInterval(
            this BaseDataVariableState variable,
            int minimumSamplingInterval)
        {
            variable.MinimumSamplingInterval = minimumSamplingInterval;
            return variable;
        }
    }

    internal static partial class ReferenceNodeManagerLog
    {
        [LoggerMessage(
            EventId = QuickstartsServersEventIds.ReferenceNodeManager + 0, Level = LogLevel.Error,
            Message = "Error creating the ReferenceNodeManager address space.")]
        public static partial void ErrorCreatingAddressSpace(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = QuickstartsServersEventIds.ReferenceNodeManager + 1, Level = LogLevel.Error,
            Message = "Error writing Interval variable.")]
        public static partial void ErrorWritingIntervalVariable(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = QuickstartsServersEventIds.ReferenceNodeManager + 2, Level = LogLevel.Error,
            Message = "Error writing Enabled variable.")]
        public static partial void ErrorWritingEnabledVariable(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = QuickstartsServersEventIds.ReferenceNodeManager + 3, Level = LogLevel.Error,
            Message = "Unexpected error doing simulation #{Count}.")]
        public static partial void UnexpectedErrorDoingSimulation(
            this ILogger logger,
            Exception exception,
            int count);
    }

}
