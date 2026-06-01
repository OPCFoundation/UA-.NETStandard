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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.History.Tests
{
    /// <summary>
    /// Base fixture for Alarms &amp; Conditions tests. Discovers alarm
    /// instances exposed by the AlarmNodeManager (started via
    /// <c>ApplyCTTModeAsync</c>) and provides shared helpers for
    /// browsing, reading state, and calling condition methods.
    /// </summary>
    public abstract class AlarmsAndConditionsTestFixture : TestFixture
    {
        /// <summary>
        /// NodeId of the Alarms folder created by AlarmNodeManager.
        /// Discovered on first use by browsing the Objects folder.
        /// </summary>
        protected NodeId AlarmsFolderId
        {
            get
            {
                if (!m_alarmsFolderDiscovered)
                {
                    m_alarmsFolderId = DiscoverAlarmsFolderAsync()
                        .GetAwaiter().GetResult();
                    m_alarmsFolderDiscovered = true;
                }
                return m_alarmsFolderId;
            }
        }

        /// <summary>
        /// Cached alarm instances discovered under the Alarms folder.
        /// Maps alarm BrowseName to NodeId.
        /// </summary>
        protected IReadOnlyDictionary<string, NodeId> AlarmInstances
        {
            get
            {
                m_alarmInstances ??= DiscoverAlarmInstancesAsync()
                    .GetAwaiter().GetResult();
                return m_alarmInstances;
            }
        }

        /// <summary>
        /// Returns the first alarm instance whose BrowseName starts with
        /// the supplied alarm type name. Returns NodeId.Null when no
        /// matching alarm is found.
        /// </summary>
        protected NodeId FindAlarmByTypeName(string alarmTypeName)
        {
            foreach (KeyValuePair<string, NodeId> kvp in AlarmInstances)
            {
                if (kvp.Key.StartsWith(
                    alarmTypeName, StringComparison.Ordinal))
                {
                    return kvp.Value;
                }
            }
            return NodeId.Null;
        }

        /// <summary>
        /// Returns any active alarm condition NodeId, or NodeId.Null
        /// when none has been discovered.
        /// </summary>
        protected NodeId FindAnyAlarm()
        {
            foreach (NodeId id in AlarmInstances.Values)
            {
                return id;
            }
            return NodeId.Null;
        }

        /// <summary>
        /// Skips the test with Assert.Ignore if no live alarm instance
        /// can be located in the address space.
        /// </summary>
        protected NodeId RequireAlarm(string typeName = null)
        {
            NodeId alarmId = typeName != null
                ? FindAlarmByTypeName(typeName)
                : FindAnyAlarm();
            if (alarmId.IsNull)
            {
                Assert.Ignore(
                    "Server does not expose a live alarm condition " +
                    "instance for this test.");
            }
            return alarmId;
        }

        /// <summary>
        /// Reads the boolean Id of a TwoStateVariable child by browse
        /// name (e.g. "AckedState" -&gt; reads "AckedState/Id"). Returns
        /// the DataValue from the server.
        /// </summary>
        protected async Task<DataValue> ReadStateIdAsync(
            NodeId conditionId, string stateName)
        {
            NodeId stateId = await TranslateBrowsePathAsync(
                conditionId, stateName, "Id").ConfigureAwait(false);
            if (stateId.IsNull)
            {
                return DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown);
            }
            return await ReadAttributeAsync(stateId, Attributes.Value)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Reads a child variable's value by relative browse name path.
        /// </summary>
        protected async Task<DataValue> ReadChildValueAsync(
            NodeId parent, params string[] path)
        {
            NodeId targetId = await TranslateBrowsePathAsync(
                parent, path).ConfigureAwait(false);
            if (targetId.IsNull)
            {
                return DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown);
            }
            return await ReadAttributeAsync(targetId, Attributes.Value)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the alarm's current EventId (used as input to
        /// Acknowledge / Confirm / AddComment method calls).
        /// </summary>
        protected async Task<ByteString> ReadEventIdAsync(NodeId conditionId)
        {
            DataValue dv = await ReadChildValueAsync(conditionId, "EventId")
                .ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                return default;
            }
            if (dv.WrappedValue.TryGetValue(out ByteString eventId))
            {
                return eventId;
            }
            return default;
        }

        /// <summary>
        /// Calls a method on the supplied condition object and returns
        /// the CallMethodResult.
        /// </summary>
        protected async Task<CallMethodResult> CallMethodOnAlarmAsync(
            NodeId conditionId,
            NodeId methodId,
            params Variant[] inputArguments)
        {
            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = conditionId,
                        MethodId = methodId,
                        InputArguments = inputArguments.ToArrayOf()
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        /// <summary>
        /// Reads any attribute of a node.
        /// </summary>
        protected async Task<DataValue> ReadAttributeAsync(
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

        /// <summary>
        /// Browses the supplied node forward (HierarchicalReferences).
        /// </summary>
        protected async Task<BrowseResult> BrowseForwardAsync(NodeId nodeId)
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId =
                            ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        /// <summary>
        /// Translates a relative browse path (a sequence of browse names)
        /// from a starting node to the resolved NodeId. Returns
        /// NodeId.Null when the path cannot be translated.
        /// </summary>
        protected async Task<NodeId> TranslateBrowsePathAsync(
            NodeId startingNode, params string[] segments)
        {
            var elementsArray = new RelativePathElement[segments.Length];
            for (int i = 0; i < segments.Length; i++)
            {
                elementsArray[i] = new RelativePathElement
                {
                    ReferenceTypeId =
                        ReferenceTypeIds.HierarchicalReferences,
                    IsInverse = false,
                    IncludeSubtypes = true,
                    TargetName = new QualifiedName(
                        segments[i], FindBrowseNameNamespace(segments[i]))
                };
            }

            var browsePath = new BrowsePath
            {
                StartingNode = startingNode,
                RelativePath = new RelativePath
                {
                    Elements = elementsArray.ToArrayOf()
                }
            };

            TranslateBrowsePathsToNodeIdsResponse response =
                await Session.TranslateBrowsePathsToNodeIdsAsync(
                    null,
                    new BrowsePath[] { browsePath }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            if (response.Results.Count == 0)
            {
                return NodeId.Null;
            }
            BrowsePathResult result = response.Results[0];
            if (StatusCode.IsBad(result.StatusCode) ||
                result.Targets.Count == 0)
            {
                return NodeId.Null;
            }
            return ToNodeId(result.Targets[0].TargetId);
        }

        /// <summary>
        /// All standard condition properties (EventId, AckedState,
        /// ConfirmedState, EnabledState, Comment, ShelvingState, etc.)
        /// live in the OPC UA core namespace.
        /// </summary>
        protected static ushort FindBrowseNameNamespace(string name)
        {
            return 0;
        }

        /// <summary>
        /// Discovers the Alarms folder by browsing the Objects folder.
        /// </summary>
        private async Task<NodeId> DiscoverAlarmsFolderAsync()
        {
            BrowseResult result = await BrowseForwardAsync(
                ObjectIds.ObjectsFolder).ConfigureAwait(false);

            int count = result.References.Count;
            for (int i = 0; i < count; i++)
            {
                ReferenceDescription r = result.References[i];
                if (r.BrowseName.Name == "Alarms")
                {
                    return ToNodeId(r.NodeId);
                }
            }
            return NodeId.Null;
        }

        /// <summary>
        /// Browses forward from the Alarms folder and returns nodes
        /// whose type definition is a subtype of ConditionType. Alarm
        /// conditions are reachable via the HasCondition reference from
        /// the source variables (AnalogSource, BooleanSource, etc.).
        /// </summary>
        private async Task<Dictionary<string, NodeId>> DiscoverAlarmInstancesAsync()
        {
            var instances = new Dictionary<string, NodeId>();
            NodeId folder = AlarmsFolderId;
            if (folder.IsNull)
            {
                return instances;
            }

            BrowseResult result = await BrowseForwardAsync(folder)
                .ConfigureAwait(false);

            // First pass: collect every variable child and the direct
            // hierarchical Object children (in case the alarms are
            // exposed both ways).
            var sourceCandidates = new List<NodeId>();
            var directCandidates = new List<(string, NodeId, NodeId)>();
            int count = result.References.Count;
            for (int i = 0; i < count; i++)
            {
                ReferenceDescription r = result.References[i];
                NodeId nodeId = ToNodeId(r.NodeId);
                if (nodeId.IsNull)
                {
                    continue;
                }
                if (r.NodeClass == NodeClass.Variable)
                {
                    sourceCandidates.Add(nodeId);
                }
                else if (r.NodeClass == NodeClass.Object)
                {
                    NodeId typeDef = ToNodeId(r.TypeDefinition);
                    if (!typeDef.IsNull)
                    {
                        directCandidates.Add((r.BrowseName.Name, nodeId, typeDef));
                    }
                }
            }

            // Browse each source via HasCondition for alarm targets.
            foreach (NodeId source in sourceCandidates)
            {
                BrowseResponse resp = await Session.BrowseAsync(
                    null, null, 0,
                    new BrowseDescription[]
                    {
                        new() {
                            NodeId = source,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = ReferenceTypeIds.HasCondition,
                            IncludeSubtypes = true,
                            NodeClassMask = 0,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                if (resp.Results.Count == 0)
                {
                    continue;
                }
                int condCount = resp.Results[0].References.Count;
                for (int i = 0; i < condCount; i++)
                {
                    ReferenceDescription r = resp.Results[0].References[i];
                    NodeId nodeId = ToNodeId(r.NodeId);
                    if (nodeId.IsNull)
                    {
                        continue;
                    }
                    if (!instances.ContainsKey(r.BrowseName.Name))
                    {
                        instances[r.BrowseName.Name] = nodeId;
                    }
                }
            }

            // Also include any objects that are direct children whose
            // type-def is a ConditionType subtype.
            foreach ((string name, NodeId nodeId, NodeId typeDef) in directCandidates)
            {
                if (await IsConditionSubtypeAsync(typeDef)
                    .ConfigureAwait(false))
                {
                    instances[name] = nodeId;
                }
            }

            return instances;
        }

        /// <summary>
        /// Walks the supertype chain of <paramref name="typeId"/> until
        /// it reaches ConditionType or a known root. Returns true if
        /// ConditionType is encountered.
        /// </summary>
        private async Task<bool> IsConditionSubtypeAsync(NodeId typeId)
        {
            NodeId current = typeId;
            for (int hop = 0; hop < 10 && !current.IsNull; hop++)
            {
                if (current == ObjectTypeIds.ConditionType)
                {
                    return true;
                }
                if (current == ObjectTypeIds.BaseObjectType ||
                    current == ObjectTypeIds.BaseEventType)
                {
                    return false;
                }

                BrowseResponse response = await Session.BrowseAsync(
                    null, null, 0,
                    new BrowseDescription[]
                    {
                        new() {
                            NodeId = current,
                            BrowseDirection = BrowseDirection.Inverse,
                            ReferenceTypeId =
                                ReferenceTypeIds.HasSubtype,
                            IncludeSubtypes = false,
                            NodeClassMask = 0,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                if (response.Results.Count == 0 ||
                    response.Results[0].References.Count == 0)
                {
                    return false;
                }
                current = ToNodeId(response.Results[0].References[0].NodeId);
            }
            return false;
        }

        private NodeId m_alarmsFolderId;
        private bool m_alarmsFolderDiscovered;
        private Dictionary<string, NodeId> m_alarmInstances;
    }
}
