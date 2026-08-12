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
            bool useSamplingGroups = false)
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
                m_historian?.Dispose();
                m_historian = null;
            }

            // Let the base FluentNodeManagerBase drain the fluent simulation
            // loop (Simulations.Dispose) before the semaphore the OnTick
            // handler takes is disposed, so no in-flight tick can ever observe
            // a disposed semaphore.
            base.Dispose(disposing);

            if (disposing)
            {
                m_semaphore?.Dispose();
            }
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
        /// Loads the predefined nodes from the NodeSet2 model and then enables
        /// the runtime history archiving behaviour, once every node has been
        /// materialized and its reverse references established.
        /// </summary>
        protected override async ValueTask LoadPredefinedNodesAsync(
            ISystemContext context,
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.LoadPredefinedNodesAsync(context, externalReferences, cancellationToken).ConfigureAwait(false);

            await m_semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Prio 1 / Prio 2 not possible: the simulated variables are baked
                // into the model, but the periodic value simulation needs them
                // collected into a runtime list that the fluent
                // Simulation().OnTick() loop pushes fresh random values to. The
                // fluent surface exposes only a bare periodic callback with no
                // per-variable random-value model, so collecting the individual
                // dynamic nodes stays imperative here. The loop itself is wired
                // through the fluent builder in Configure().
                RegisterSimulationVariables();
                InitializeDataAccessDiscreteNodes();
                InitializeMissingStaticValues();

                // Reset the random generator and generate boundary values so the
                // fluent simulation loop (registered in Configure and started
                // after Seal) always has a generator ready for the first tick.
                ResetRandomGenerator(100, 1);

                // Prio 1 / Prio 2 not possible: history archiving registers an
                // in-memory historian provider, seeds the sample history and
                // refreshes the HistoricalDataConfiguration companion objects at
                // runtime. The Historizing attribute, the history access-level
                // bits and the companion nodes are all baked into the NodeSet2
                // model; the archive contents and provider wiring remain runtime
                // services and therefore run here, after every predefined node
                // has been loaded.
                //
                // The CTT root folder, its EventNotifier attribute and the
                // inverse Server -> HasNotifier reference are baked into the
                // model, so base.AddReverseReferencesAsync (invoked by
                // base.LoadPredefinedNodesAsync above) both materializes the root
                // and auto-registers it with the runtime notifier table - no
                // explicit root creation or notifier registration is required.
                await EnableHistoryArchivingAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_semaphore.Release();
            }
        }

        /// <summary>
        /// Discovers the simulated CTT variables from the loaded NodeSet2 model
        /// and collects them into the runtime dynamic-node list so the periodic
        /// <see cref="RunSimulationStepAsync"/> loop can push fresh random
        /// values to them. The simulated nodes are exactly the data variables
        /// under the "Scalar_Simulation" subtree (their string node ids are
        /// prefixed "Scalar_Simulation_"), excluding the two control variables
        /// (Interval and Enabled), so no hard-coded node list is required.
        /// </summary>
        private void RegisterSimulationVariables()
        {
            m_dynamicNodes.Clear();
            foreach (NodeState node in PredefinedNodes.Values)
            {
                if (node is BaseDataVariableState variable &&
                    variable.NodeId.TryGetValue(out string identifier) &&
                    identifier.StartsWith(SimulationNodePrefix, StringComparison.Ordinal) &&
                    !string.Equals(identifier, SimulationIntervalNodeName, StringComparison.Ordinal) &&
                    !string.Equals(identifier, SimulationEnabledNodeName, StringComparison.Ordinal))
                {
                    m_dynamicNodes.Add(variable);
                }
            }
        }

        private void InitializeDataAccessDiscreteNodes()
        {
            foreach ((string identifier, string trueState, string falseState) in s_twoStateDiscreteValues)
            {
                if (FindPredefinedNode<BaseVariableState>(new NodeId(identifier, NamespaceIndex)) is BaseVariableState node)
                {
                    node.Value = false;
                    node.StatusCode = StatusCodes.Good;
                    _ = node.SetChildValue(
                        SystemContext,
                        TrueStateBrowseName,
                        Variant.From(LocalizedText.From(trueState)),
                        false);
                    _ = node.SetChildValue(
                        SystemContext,
                        FalseStateBrowseName,
                        Variant.From(LocalizedText.From(falseState)),
                        false);
                }
            }

            foreach ((string identifier, string[] enumStrings) in s_multiStateDiscreteValues)
            {
                if (FindPredefinedNode<BaseVariableState>(new NodeId(identifier, NamespaceIndex)) is BaseVariableState node)
                {
                    node.OnWriteValue = OnWriteDiscrete;
                    node.Value = (uint)0;
                    node.StatusCode = StatusCodes.Good;
                    _ = node.SetChildValue(
                        SystemContext,
                        EnumStringsBrowseName,
                        Variant.From(CreateLocalizedTextArray(enumStrings).ToArrayOf()),
                        false);
                }
            }

            foreach ((string identifier, string[] enumNames) in s_multiStateValueDiscreteValues)
            {
                if (FindPredefinedNode<BaseVariableState>(new NodeId(identifier, NamespaceIndex)) is BaseVariableState node)
                {
                    node.OnWriteValue = OnWriteValueDiscrete;
                    node.Value = CreateZeroValue(node.DataType);
                    node.StatusCode = StatusCodes.Good;
                    ArrayOf<EnumValueType> values = CreateEnumValues(enumNames).ToArrayOf();
                    _ = node.SetChildValue(
                        SystemContext,
                        EnumValuesBrowseName,
                        Variant.FromStructure(values, false),
                        false);
                    _ = node.SetChildValue(
                        SystemContext,
                        ValueAsTextBrowseName,
                        Variant.From(values[0].DisplayName),
                        false);
                }
            }
        }

        private void InitializeMissingStaticValues()
        {
            SetPredefinedVariableValue(
                "Scalar_Static_Arrays_Integer",
                Variant.From(CreateArray(10, i => (long)i).ToArrayOf()));
            SetPredefinedVariableValue(
                "Scalar_Static_Arrays_Number",
                Variant.From(CreateArray(10, i => (double)i).ToArrayOf()));
            SetPredefinedVariableValue(
                "Scalar_Static_Arrays_UInteger",
                Variant.From(CreateArray(10, i => (ulong)i).ToArrayOf()));
            SetPredefinedVariableValue(
                "Scalar_Static_Arrays2D_Integer",
                Variant.From(CreateMatrix(2, 2, (r, c) => (long)((r * 2) + c))));
            SetPredefinedVariableValue(
                "Scalar_Static_Arrays2D_Number",
                Variant.From(CreateMatrix(2, 2, (r, c) => (double)((r * 2) + c))));
            SetPredefinedVariableValue(
                "Scalar_Static_Arrays2D_UInteger",
                Variant.From(CreateMatrix(2, 2, (r, c) => (ulong)((r * 2) + c))));
            SetPredefinedVariableValue(
                "Scalar_Static_ArrayDynamic_Integer",
                Variant.From(CreateArray(10, i => (long)i).ToArrayOf()));
            SetPredefinedVariableValue(
                "Scalar_Static_ArrayDynamic_Number",
                Variant.From(CreateArray(10, i => (double)i).ToArrayOf()));
            SetPredefinedVariableValue(
                "Scalar_Static_ArrayDynamic_UInteger",
                Variant.From(CreateArray(10, i => (ulong)i).ToArrayOf()));
            SetPredefinedVariableValue("DataAccess_ArrayItemType_YArray", Variant.From(s_doubleArray));
            SetPredefinedVariableValue(
                "DataAccess_ArrayItemType_XYArray",
                Variant.FromStructure(new XVType[]
                    {
                        new() { X = 0.0, Value = 0.0f },
                        new() { X = 1.0, Value = 1.0f },
                        new() { X = 2.0, Value = 4.0f },
                        new() { X = 3.0, Value = 9.0f },
                        new() { X = 4.0, Value = 16.0f }
                    }.ToMatrixOf(5)));
            SetPredefinedVariableValue(
                "DataAccess_ArrayItemType_Image",
                Variant.From(MatrixOf<double>.CreateFromArray(new double[,]
                {
                    { 0.0, 1.0, 2.0 },
                    { 3.0, 4.0, 5.0 }
                })));
            SetPredefinedVariableValue(
                "DataAccess_ArrayItemType_Cube",
                Variant.From(MatrixOf<double>.CreateFromArray(new double[,,]
                {
                    { { 0.0, 1.0 }, { 2.0, 3.0 } },
                    { { 4.0, 5.0 }, { 6.0, 7.0 } }
                })));
            SetPredefinedVariableValue(
                "DataAccess_ArrayItemType_NDimension",
                Variant.From(MatrixOf<double>.CreateFromArray(new double[,]
                {
                    { 0.0, 1.0, 2.0 },
                    { 3.0, 4.0, 5.0 }
                })));
        }

        private void SetPredefinedVariableValue(string identifier, Variant value)
        {
            if (FindPredefinedNode<BaseVariableState>(new NodeId(identifier, NamespaceIndex)) is not BaseVariableState node)
            {
                return;
            }

            node.Value = value;
            node.StatusCode = StatusCodes.Good;
            node.Timestamp = DateTimeUtc.Now;
            node.ClearChangeMasks(SystemContext, false);
        }

        private ServiceResult OnWriteInterval(ISystemContext context, NodeState node, ref Variant value)
        {
            try
            {
                if (!value.TryGetValue(out ushort interval) || interval == 0)
                {
                    return StatusCodes.BadOutOfRange;
                }

                Volatile.Write(ref m_simulationIntervalMilliseconds, interval);
                return ServiceResult.Good;
            }
            catch (Exception e)
            {
                m_logger.ErrorWritingIntervalVariable(e);
                return ServiceResult.Create(e, StatusCodes.Bad, "Error writing Interval variable.");
            }
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
            if (node is not BaseVariableState variable)
            {
                return StatusCodes.BadTypeMismatch;
            }

            TypeInfo typeInfo = TypeInfo.IsInstanceOfDataType(
                value,
                variable.DataType,
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
            if (number >= GetLocalizedTextChildCount(node, context, EnumStringsBrowseName) || number < 0)
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
            if (node is not BaseVariableState ||
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
            if (!TryGetEnumValueDisplayName(node, context, number, out LocalizedText displayName))
            {
                return StatusCodes.BadOutOfRange;
            }

            if (!node.SetChildValue(
                context,
                ValueAsTextBrowseName,
                Variant.From(displayName),
                true))
            {
                return StatusCodes.BadOutOfRange;
            }

            node.ClearChangeMasks(context, true);
            return ServiceResult.Good;
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

        /// <summary>
        /// Executes a single simulation tick: pushes a fresh random value to
        /// every registered dynamic node. Invoked from the fluent
        /// <c>Simulation().OnTick(...)</c> loop wired in <c>Configure</c>. The
        /// loop serializes its ticks, so this never re-enters itself; the
        /// semaphore only guards against concurrent address-space mutation
        /// (history archiving, node loading).
        /// </summary>
        private async ValueTask RunSimulationStepAsync(TimeSpan elapsed, CancellationToken cancellationToken)
        {
            if (!Volatile.Read(ref m_simulationEnabled))
            {
                m_simulationElapsedTicks = 0;
                return;
            }

            int intervalMilliseconds = Volatile.Read(ref m_simulationIntervalMilliseconds);
            long intervalTicks = TimeSpan.FromMilliseconds(intervalMilliseconds).Ticks;
            m_simulationElapsedTicks += elapsed.Ticks;
            if (m_simulationElapsedTicks < intervalTicks)
            {
                return;
            }
            m_simulationElapsedTicks = 0;

            await m_semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
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

        private static Variant CreateZeroValue(NodeId dataType)
        {
            if (dataType == DataTypeIds.Byte)
            {
                return new Variant((byte)0);
            }
            if (dataType == DataTypeIds.Int16)
            {
                return new Variant((short)0);
            }
            if (dataType == DataTypeIds.Int32)
            {
                return new Variant(0);
            }
            if (dataType == DataTypeIds.Int64)
            {
                return new Variant((long)0);
            }
            if (dataType == DataTypeIds.SByte)
            {
                return new Variant((sbyte)0);
            }
            if (dataType == DataTypeIds.UInt16)
            {
                return new Variant((ushort)0);
            }
            if (dataType == DataTypeIds.UInt64)
            {
                return new Variant((ulong)0);
            }

            return new Variant((uint)0);
        }

        private static LocalizedText[] CreateLocalizedTextArray(string[] values)
        {
            var result = new LocalizedText[values.Length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                result[ii] = LocalizedText.From(values[ii]);
            }
            return result;
        }

        private static EnumValueType[] CreateEnumValues(string[] enumNames)
        {
            var values = new EnumValueType[enumNames.Length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                LocalizedText text = LocalizedText.From(enumNames[ii]);
                values[ii] = new EnumValueType
                {
                    Value = ii,
                    Description = text,
                    DisplayName = text
                };
            }
            return values;
        }

        private static int GetLocalizedTextChildCount(
            NodeState node,
            ISystemContext context,
            string browseName)
        {
            return node.FindChild(context, new QualifiedName(browseName)) is BaseVariableState child
                ? child.Value.TryGetValue(out ArrayOf<LocalizedText> values) ? values.Count : 0
                : 0;
        }

        private static bool TryGetEnumValueDisplayName(
            NodeState node,
            ISystemContext context,
            int index,
            out LocalizedText displayName)
        {
            displayName = LocalizedText.Null;
            if (index < 0 ||
                node.FindChild(context, new QualifiedName(EnumValuesBrowseName)) is not BaseVariableState child)
            {
                return false;
            }

            if (!child.Value.TryGetValue(out ArrayOf<EnumValueType> values, null) ||
                index >= values.Count)
            {
                return false;
            }

            displayName = values[index].DisplayName;
            return true;
        }

        private readonly SemaphoreSlim m_semaphore = new(1, 1);
        private RandomSource m_randomSource = null!;
        private DataGenerator m_generator = null!;
        private bool m_simulationEnabled = true;
        private int m_simulationIntervalMilliseconds = 1000;
        private long m_simulationElapsedTicks;
        private readonly List<BaseDataVariableState> m_dynamicNodes = [];

        /// <summary>
        /// Tick resolution used to emulate the old reschedulable timer on top
        /// of the fluent simulation loop.
        /// </summary>
        private static readonly TimeSpan s_simulationTickInterval = TimeSpan.FromMilliseconds(100);

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
        /// String node-id prefix shared by every variable under the
        /// "Scalar_Simulation" subtree. Used to discover the simulated nodes
        /// from the loaded model.
        /// </summary>
        private const string SimulationNodePrefix = "Scalar_Simulation_";

        /// <summary>
        /// String node id of the read-only simulation Interval control variable,
        /// excluded from the discovered dynamic-node set.
        /// </summary>
        private const string SimulationIntervalNodeName = "Scalar_Simulation_Interval";

        /// <summary>
        /// String node id of the simulation Enabled control variable, excluded
        /// from the discovered dynamic-node set.
        /// </summary>
        private const string SimulationEnabledNodeName = "Scalar_Simulation_Enabled";

        private const string TrueStateBrowseName = "TrueState";
        private const string FalseStateBrowseName = "FalseState";
        private const string EnumStringsBrowseName = "EnumStrings";
        private const string EnumValuesBrowseName = "EnumValues";
        private const string ValueAsTextBrowseName = "ValueAsText";

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
            m_historian = new InMemoryHistorianProvider(new InMemoryHistorianOptions
            {
                // The reference server supports conformance tests that write
                // arbitrary historical timestamps outside the seeded window.
                RawDataRetentionPeriod = TimeSpan.Zero
            });

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

            // Discover the historized nodes directly from the loaded model:
            // every variable that carries Historizing="true" (baked into the
            // NodeSet2 model together with the HistoryRead / HistoryWrite
            // access-level bits) is a history node. Snapshot them first because
            // installing each node's HA Configuration companion below adds nodes
            // to PredefinedNodes, which would otherwise invalidate the
            // enumerator.
            List<BaseVariableState> historizedNodes = [];
            foreach (NodeState node in PredefinedNodes.Values)
            {
                if (node is BaseVariableState variable && variable.Historizing)
                {
                    historizedNodes.Add(variable);
                }
            }

            foreach (BaseVariableState variable in historizedNodes)
            {
                var nodeId = (NodeId)variable.NodeId;

                // The dedicated node whose historian does not support server
                // timestamps is identified by its node id; everything else uses
                // the shared capabilities. Only the runtime historian
                // registration and seeding remain here.
                bool noServerTimestamp = nodeId.TryGetValue(out string identifier) &&
                    string.Equals(identifier, NodeDoesNotSupportServerTimestampNodeName, StringComparison.Ordinal);

                m_historian.Register(
                    nodeId,
                    noServerTimestamp ? noServerTimestampCapabilities : capabilities);

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

        private static readonly (string Identifier, string TrueState, string FalseState)[] s_twoStateDiscreteValues =
        [
            ("DataAccess_TwoStateDiscreteType_DataAccess_TwoStateDiscreteType_001", "red", "blue"),
            ("DataAccess_TwoStateDiscreteType_DataAccess_TwoStateDiscreteType_002", "open", "close"),
            ("DataAccess_TwoStateDiscreteType_DataAccess_TwoStateDiscreteType_003", "up", "down"),
            ("DataAccess_TwoStateDiscreteType_DataAccess_TwoStateDiscreteType_004", "left", "right"),
            ("DataAccess_TwoStateDiscreteType_DataAccess_TwoStateDiscreteType_005", "circle", "cross")
        ];

        private static readonly (string Identifier, string[] EnumStrings)[] s_multiStateDiscreteValues =
        [
            ("DataAccess_MultiStateDiscreteType_DataAccess_MultiStateDiscreteType_001", ["open", "closed", "jammed"]),
            ("DataAccess_MultiStateDiscreteType_DataAccess_MultiStateDiscreteType_002", ["red", "green", "blue", "cyan"]),
            ("DataAccess_MultiStateDiscreteType_DataAccess_MultiStateDiscreteType_003", ["lolo", "lo", "normal", "hi", "hihi"]),
            ("DataAccess_MultiStateDiscreteType_DataAccess_MultiStateDiscreteType_004", ["left", "right", "center"]),
            ("DataAccess_MultiStateDiscreteType_DataAccess_MultiStateDiscreteType_005", ["circle", "cross", "triangle"])
        ];

        private static readonly (string Identifier, string[] EnumNames)[] s_multiStateValueDiscreteValues =
        [
            ("DataAccess_MultiStateValueDiscreteType_DataAccess_MultiStateValueDiscreteType_001", ["open", "closed", "jammed"]),
            ("DataAccess_MultiStateValueDiscreteType_DataAccess_MultiStateValueDiscreteType_002", ["red", "green", "blue", "cyan"]),
            ("DataAccess_MultiStateValueDiscreteType_DataAccess_MultiStateValueDiscreteType_003", ["lolo", "lo", "normal", "hi", "hihi"]),
            ("DataAccess_MultiStateValueDiscreteType_DataAccess_MultiStateValueDiscreteType_004", ["left", "right", "center"]),
            ("DataAccess_MultiStateValueDiscreteType_DataAccess_MultiStateValueDiscreteType_005", ["circle", "cross", "triangle"]),
            ("DataAccess_MultiStateValueDiscreteType_DataAccess_MultiStateValueDiscreteType_Byte", ["open", "closed", "jammed"]),
            ("DataAccess_MultiStateValueDiscreteType_DataAccess_MultiStateValueDiscreteType_Int16", ["red", "green", "blue", "cyan"]),
            ("DataAccess_MultiStateValueDiscreteType_DataAccess_MultiStateValueDiscreteType_Int32", ["lolo", "lo", "normal", "hi", "hihi"]),
            ("DataAccess_MultiStateValueDiscreteType_DataAccess_MultiStateValueDiscreteType_Int64", ["left", "right", "center"]),
            ("DataAccess_MultiStateValueDiscreteType_DataAccess_MultiStateValueDiscreteType_SByte", ["open", "closed", "jammed"]),
            ("DataAccess_MultiStateValueDiscreteType_DataAccess_MultiStateValueDiscreteType_UInt16", ["red", "green", "blue", "cyan"]),
            ("DataAccess_MultiStateValueDiscreteType_DataAccess_MultiStateValueDiscreteType_UInt32", ["lolo", "lo", "normal", "hi", "hihi"]),
            ("DataAccess_MultiStateValueDiscreteType_DataAccess_MultiStateValueDiscreteType_UInt64", ["left", "right", "center"])
        ];
    }
    internal static partial class ReferenceNodeManagerLog
    {
        [LoggerMessage(
            EventId = QuickstartsServersEventIds.ReferenceNodeManager + 2, Level = LogLevel.Error,
            Message = "Error writing Enabled variable.")]
        public static partial void ErrorWritingEnabledVariable(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = QuickstartsServersEventIds.ReferenceNodeManager + 6, Level = LogLevel.Error,
            Message = "Error writing Interval variable.")]
        public static partial void ErrorWritingIntervalVariable(this ILogger logger, Exception exception);
    }

}
