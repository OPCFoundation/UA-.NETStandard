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

                // Prio 1 / Prio 2 not possible: registering the CTT root with
                // the node manager's runtime notifier table so events raised on
                // the folder are routed to subscriptions. The EventNotifier
                // attribute and the Server -> HasNotifier reference are baked
                // into the NodeSet2 model, but the in-memory notifier
                // registration is a runtime-only operation.
                await AddRootNotifierAsync(root, cancellationToken).ConfigureAwait(false);

                // The entire CTT address space - every folder, variable, value
                // and static attribute (AccessLevel, UserAccessLevel,
                // AccessLevelEx, WriteMask, UserWriteMask, AccessRestrictions,
                // RolePermissions, Description, MinimumSamplingInterval,
                // EventNotifier, the analog EURange / EngineeringUnits /
                // InstrumentRange / Definition properties, the static array
                // element values and the two browse Views) - is defined
                // directly in the NodeSet2 model (Prio 1) and materialized by
                // the base node manager. The method call handlers and the four
                // value-write handlers are wired through the fluent builder in
                // Configure() (Prio 2). Only the runtime-only behaviour below
                // cannot be expressed in either place and therefore stays
                // imperative.
                try
                {
                    // Prio 1 / Prio 2 not possible: the simulated variables are
                    // baked into the model, but the periodic value simulation
                    // needs them collected into a runtime list that the
                    // DoSimulation timer pushes fresh random values to. The
                    // fluent Simulation().OnTick() only exposes a bare periodic
                    // callback with no per-variable random-value model, so the
                    // registration of the individual dynamic nodes stays
                    // imperative.
                    RegisterSimulationVariables();
                }
                catch (Exception e)
                {
                    m_logger.ErrorCreatingAddressSpace(e);
                }

                await AddPredefinedNodeAsync(SystemContext, root, cancellationToken).ConfigureAwait(false);

                // Prio 1 / Prio 2 not possible: history archiving registers an
                // in-memory historian provider, seeds the sample history and
                // installs the HistoricalDataConfiguration companion objects at
                // runtime. The Historizing attribute is baked into the model;
                // the archive contents and provider wiring are runtime services.
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
        /// Raises an event when an event trigger variable is written.
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
        /// Validates writes to a selection-list variable, rejecting values that
        /// are not contained in the node's baked <c>Selections</c> array.
        /// </summary>
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
                BaseVariableState selectionsNode ||
                !selectionsNode.WrappedValue.TryGetValue(out ArrayOf<string> allowedSelections) ||
                allowedSelections.IsNull)
            {
                return StatusCodes.BadConfigurationError;
            }

            foreach (string allowedSelection in allowedSelections)
            {
                if (string.Equals(selection, allowedSelection, StringComparison.Ordinal))
                {
                    return ServiceResult.Good;
                }
            }

            return StatusCodes.BadOutOfRange;
        }

        /// <summary>
        /// Creates a new folder.
        /// </summary>
        private FolderState CreateFolder(string path)
        {
            return FindPredefinedNode<FolderState>(new NodeId(path, NamespaceIndex));
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
        /// Registers the simulated CTT variables (all baked into the NodeSet2
        /// model) with the runtime dynamic-node list so the periodic
        /// <see cref="DoSimulation"/> timer can push fresh random values to them.
        /// </summary>
        private void RegisterSimulationVariables()
        {
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

            const string massSimulation = "Scalar_Simulation_Mass_";
            CreateDynamicVariables(massSimulation + "Boolean", "Boolean", 100);
            CreateDynamicVariables(massSimulation + "Byte", "Byte", 100);
            CreateDynamicVariables(massSimulation + "ByteString", "ByteString", 100);
            CreateDynamicVariables(massSimulation + "DateTime", "DateTime", 100);
            CreateDynamicVariables(massSimulation + "Double", "Double", 100);
            CreateDynamicVariables(massSimulation + "Duration", "Duration", 100);
            CreateDynamicVariables(massSimulation + "Float", "Float", 100);
            CreateDynamicVariables(massSimulation + "Guid", "Guid", 100);
            CreateDynamicVariables(massSimulation + "Int16", "Int16", 100);
            CreateDynamicVariables(massSimulation + "Int32", "Int32", 100);
            CreateDynamicVariables(massSimulation + "Int64", "Int64", 100);
            CreateDynamicVariables(massSimulation + "Integer", "Integer", 100);
            CreateDynamicVariables(massSimulation + "LocaleId", "LocaleId", 100);
            CreateDynamicVariables(massSimulation + "LocalizedText", "LocalizedText", 100);
            CreateDynamicVariables(massSimulation + "NodeId", "NodeId", 100);
            CreateDynamicVariables(massSimulation + "Number", "Number", 100);
            CreateDynamicVariables(massSimulation + "QualifiedName", "QualifiedName", 100);
            CreateDynamicVariables(massSimulation + "SByte", "SByte", 100);
            CreateDynamicVariables(massSimulation + "String", "String", 100);
            CreateDynamicVariables(massSimulation + "UInt16", "UInt16", 100);
            CreateDynamicVariables(massSimulation + "UInt32", "UInt32", 100);
            CreateDynamicVariables(massSimulation + "UInt64", "UInt64", 100);
            CreateDynamicVariables(massSimulation + "UInteger", "UInteger", 100);
            CreateDynamicVariables(massSimulation + "UtcTime", "UtcTime", 100);
            CreateDynamicVariables(massSimulation + "Variant", "Variant", 100);
            CreateDynamicVariables(massSimulation + "XmlElement", "XmlElement", 100);
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
