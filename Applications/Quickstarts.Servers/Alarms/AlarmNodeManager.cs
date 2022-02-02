/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua;
using Opc.Ua.Sample;
using Opc.Ua.Server;

namespace Alarms
{
    public class AlarmNodeManagerFactory : INodeManagerFactory
    {
        public INodeManager Create(IServerInternal server, ApplicationConfiguration configuration)
        {
            return new AlarmNodeManager(server, configuration);
        }
    }

    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class AlarmNodeManager : CustomNodeManager2
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public AlarmNodeManager(IServerInternal server, ApplicationConfiguration configuration)
            : base(server, configuration, "http://samples.org/UA/Alarms/")
        {

           //string alarms = "http://samples.org/UA/Alarms/";

           // List<string> namespaceUris = new List<string>();
           // namespaceUris.Add(alarms);
           // namespaceUris.Add(alarms + "Instance");
           // NamespaceUris = namespaceUris;

           // m_typeNamespaceIndex = Server.NamespaceUris.GetIndexOrAppend(namespaceUris[0]);
           // m_namespaceIndex = Server.NamespaceUris.GetIndexOrAppend(namespaceUris[1]);

           // AddEncodeableNodeManagerTypes(typeof(AlarmNodeManager).Assembly, typeof(AlarmNodeManager).Namespace);

        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // TBD
            }
        }

        #endregion

        #region INodeIdFactory Members
        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            BaseInstanceState instance = node as BaseInstanceState;

            if (instance != null && instance.Parent != null)
            {
                string id = instance.Parent.NodeId.Identifier as string;

                if (id != null)
                {
                    return new NodeId(id + "_" + instance.SymbolicName, instance.Parent.NodeId.NamespaceIndex);
                }
            }

            return node.NodeId;
        }
        #endregion


        #region INodeManager Members
        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        /// <remarks>
        /// The externalReferences is an out parameter that allows the node manager to link to nodes
        /// in other node managers. For example, the 'Objects' node is managed by the CoreNodeManager and
        /// should have a reference to the root folder node(s) exposed by this node manager.  
        /// </remarks>
        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                #region Setup

                IList<IReference> references = null;

                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references))
                {
                    externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
                }

                #endregion

                try
                {
                    #region Initialize

                    string alarmsName = "Alarms";
                    string alarmsNodeName = alarmsName;

                    Type alarmControllerType = Type.GetType("Alarms.AlarmController");
                    int interval = 1000;
                    string intervalString = interval.ToString();

                    int conditionTypeIndex = 0;

                    #endregion

                    #region Create Alarm Folder

                    FolderState alarmsFolder = CreateFolder(null, alarmsNodeName, alarmsName);
                    alarmsFolder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
                    references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, alarmsFolder.NodeId));
                    alarmsFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
                    AddRootNotifier(alarmsFolder);

                    #endregion

                    #region Create Methods

                    string startMethodName = "Start";
                    string startMethodNodeName = alarmsNodeName + "." + startMethodName;
                    MethodState startMethod = AlarmHelpers.CreateMethod(alarmsFolder, NamespaceIndex, startMethodNodeName, startMethodName);
                    startMethod.OnCallMethod = new GenericMethodCalledEventHandler(OnStart);

                    string startBranchMethodName = "StartBranch";
                    string startBranchMethodNodeName = alarmsNodeName + "." + startBranchMethodName;
                    MethodState startBranchMethod = AlarmHelpers.CreateMethod(alarmsFolder, NamespaceIndex, startBranchMethodNodeName, startBranchMethodName);
                    startBranchMethod.OnCallMethod = new GenericMethodCalledEventHandler(OnStartBranch);

                    string endMethodName = "End";
                    string endMethodNodeName = alarmsNodeName + "." + endMethodName;
                    MethodState endMethod = AlarmHelpers.CreateMethod(alarmsFolder, NamespaceIndex, endMethodNodeName, endMethodName);
                    endMethod.OnCallMethod = new GenericMethodCalledEventHandler(OnEnd);

                    #endregion

                    #region Create Variables

                    Dictionary<string, SourceController> triggerMap = new Dictionary<string, SourceController>();
                    m_triggerMap.Add(alarmsName, triggerMap);

                    string analogTriggerName = "AnalogSource";
                    string analogTriggerNodeName = alarmsNodeName + "." + analogTriggerName;
                    BaseDataVariableState analogTrigger = AlarmHelpers.CreateVariable(alarmsFolder,
                        NamespaceIndex, analogTriggerNodeName, analogTriggerName);
                    analogTrigger.OnWriteValue = OnWriteAlarmTrigger;
                    AlarmController analogAlarmController = (AlarmController)Activator.CreateInstance(alarmControllerType, analogTrigger, interval, false);
                    SourceController analogSourceController = new SourceController(analogTrigger, analogAlarmController);
                    triggerMap.Add("Analog", analogSourceController);

                    string booleanTriggerName = "BooleanSource";
                    string booleanTriggerNodeName = alarmsNodeName + "." + booleanTriggerName;
                    BaseDataVariableState booleanTrigger = AlarmHelpers.CreateVariable(alarmsFolder,
                        NamespaceIndex, booleanTriggerNodeName, booleanTriggerName, boolValue: true);
                    booleanTrigger.OnWriteValue = OnWriteAlarmTrigger;
                    AlarmController booleanAlarmController = (AlarmController)Activator.CreateInstance(alarmControllerType, booleanTrigger, interval, true);
                    SourceController booleanSourceController = new SourceController(booleanTrigger, booleanAlarmController);
                    triggerMap.Add("Boolean", booleanSourceController);

                    #endregion

                    #region Create Alarms

                    AlarmHolder mandatoryExclusiveLevel = new ExclusiveLevelHolder(
                        this,
                        alarmsFolder,
                        analogSourceController,
                        intervalString,
                        GetSupportedAlarmConditionType(ref conditionTypeIndex),
                        alarmControllerType,
                        interval,
                        optional: false);

                    m_alarms.Add(mandatoryExclusiveLevel.AlarmNodeName, mandatoryExclusiveLevel);

                    AlarmHolder mandatoryNonExclusiveLevel = new NonExclusiveLevelHolder(
                        this,
                        alarmsFolder,
                        analogSourceController,
                        intervalString,
                        GetSupportedAlarmConditionType(ref conditionTypeIndex),
                        alarmControllerType,
                        interval,
                        optional: false);
                    m_alarms.Add(mandatoryNonExclusiveLevel.AlarmNodeName, mandatoryNonExclusiveLevel);

                    AlarmHolder offNormal = new OffNormalAlarmTypeHolder(
                        this,
                        alarmsFolder,
                        booleanSourceController,
                        intervalString,
                        GetSupportedAlarmConditionType(ref conditionTypeIndex),
                        alarmControllerType,
                        interval,
                        optional: false);
                    m_alarms.Add(offNormal.AlarmNodeName, offNormal);


                    #endregion

                    AddPredefinedNode(SystemContext, alarmsFolder);
                    m_simulationTimer = new Timer(DoSimulation, null, m_simulationInterval, m_simulationInterval);
                    m_allowEntry = true;

                }
                catch (Exception e)
                {
                    Utils.LogError(e, "Error creating the AlarmNodeManager address space.");
                }

            }
        }



        /// <summary>
        /// Creates a new folder.
        /// </summary>
        private FolderState CreateFolder(NodeState parent, string path, string name)
        {
            FolderState folder = new FolderState(parent);

            folder.SymbolicName = name;
            folder.ReferenceTypeId = ReferenceTypes.Organizes;
            folder.TypeDefinitionId = ObjectTypeIds.FolderType;
            folder.NodeId = new NodeId(path, NamespaceIndex);
            folder.BrowseName = new QualifiedName(path, NamespaceIndex);
            folder.DisplayName = new LocalizedText("en", name);
            folder.WriteMask = AttributeWriteMask.None;
            folder.UserWriteMask = AttributeWriteMask.None;
            folder.EventNotifier = EventNotifiers.None;

            if (parent != null)
            {
                parent.AddChild(folder);
            }

            return folder;
        }

        /// <summary>
        /// Creates a new method.
        /// </summary>
        private MethodState CreateMethod(NodeState parent, string path, string name)
        {
            MethodState method = new MethodState(parent);

            method.SymbolicName = name;
            method.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            method.NodeId = new NodeId(path, NamespaceIndex);
            method.BrowseName = new QualifiedName(path, NamespaceIndex);
            method.DisplayName = new LocalizedText("en", name);
            method.WriteMask = AttributeWriteMask.None;
            method.UserWriteMask = AttributeWriteMask.None;
            method.Executable = true;
            method.UserExecutable = true;

            if (parent != null)
            {
                parent.AddChild(method);
            }

            return method;
        }

        private void DoSimulation(object state)
        {
            if (m_allowEntry)
            {
                m_allowEntry = false;

                lock (m_alarms)
                {
                    m_success++;
                    try
                    {
                        foreach (Dictionary<string, SourceController> map in m_triggerMap.Values)
                        {
                            foreach (SourceController controller in map.Values)
                            {
                                bool updated = controller.Controller.Update(SystemContext);

                                IList<IReference> references = new List<IReference>();
                                controller.Source.GetReferences(SystemContext, references, ReferenceTypes.HasCondition, false);
                                foreach (IReference reference in references)
                                {
                                    string identifier = (string)reference.TargetId.ToString();
                                    if (m_alarms.ContainsKey(identifier))
                                    {
                                        AlarmHolder holder = m_alarms[identifier];
                                        holder.Update(updated);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Utils.LogInfo("Loop Exception " + ex.Message);

                    }
                }
                m_allowEntry = true;
            }
            else
            {
                if (m_success > 0)
                {
                    m_missed++;
                    Utils.LogInfo(DateTime.UtcNow.ToLocalTime().ToLongTimeString() + " Missed Loop " + m_missed.ToString() + " Success " + m_success.ToString());
                }
            }
        }

        #region Methods

        public ServiceResult OnStart(
            ISystemContext context,
            NodeState node,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            ServiceResult result = ServiceResult.Good;

            Dictionary<string, SourceController> sourceControllers = GetUnitAlarms(node);
            if (sourceControllers == null)
            {
                result = StatusCodes.BadNodeIdUnknown;
            }

            if (sourceControllers != null)
            {
                Utils.LogInfo("Starting up alarm group " + GetUnitFromNodeId(node.NodeId));

                lock (m_alarms)
                {
                    foreach (SourceController sourceController in sourceControllers.Values)
                    {
                        IList<IReference> references = new List<IReference>();
                        sourceController.Source.GetReferences(SystemContext, references, ReferenceTypes.HasCondition, false);
                        foreach (IReference reference in references)
                        {
                            string identifier = (string)reference.TargetId.ToString();
                            if (m_alarms.ContainsKey(identifier))
                            {
                                AlarmHolder holder = m_alarms[identifier];
                                holder.SetBranching(false);
                                holder.Start();
                                bool updated = holder.Controller.Update(SystemContext);
                                holder.Update(updated);
                            }
                        }
                    }
                }
            }


            return result;
        }

        public ServiceResult OnStartBranch(
            ISystemContext context,
            NodeState node,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            ServiceResult result = ServiceResult.Good;

            Dictionary<string, SourceController> sourceControllers = GetUnitAlarms(node);
            if (sourceControllers == null)
            {
                result = StatusCodes.BadNodeIdUnknown;
            }

            if (sourceControllers != null)
            {
                Utils.LogInfo("Starting up Branch for alarm group " + GetUnitFromNodeId(node.NodeId));

                lock (m_alarms)
                {
                    foreach (SourceController sourceController in sourceControllers.Values)
                    {
                        IList<IReference> references = new List<IReference>();
                        sourceController.Source.GetReferences(SystemContext, references, ReferenceTypes.HasCondition, false);
                        foreach (IReference reference in references)
                        {
                            string identifier = (string)reference.TargetId.ToString();
                            if (m_alarms.ContainsKey(identifier))
                            {
                                AlarmHolder holder = m_alarms[identifier];
                                holder.SetBranching(true);
                                holder.Start();
                                bool updated = holder.Controller.Update(SystemContext);
                                holder.Update(updated);
                            }
                        }
                    }
                }
            }


            return result;
        }

        public ServiceResult OnEnd(
            ISystemContext context,
            NodeState node,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            ServiceResult result = ServiceResult.Good;

            Dictionary<string, SourceController> sourceControllers = GetUnitAlarms(node);
            if (sourceControllers == null)
            {
                result = StatusCodes.BadNodeIdUnknown;
            }

            if (sourceControllers != null)
            {
                Utils.LogInfo("Stopping alarm group " + GetUnitFromNodeId(node.NodeId));

                lock (m_alarms)
                {
                    foreach (SourceController sourceController in sourceControllers.Values)
                    {
                        IList<IReference> references = new List<IReference>();
                        sourceController.Source.GetReferences(SystemContext, references, ReferenceTypes.HasCondition, false);
                        foreach (IReference reference in references)
                        {
                            string identifier = (string)reference.TargetId.ToString();
                            if (m_alarms.ContainsKey(identifier))
                            {
                                AlarmHolder holder = m_alarms[identifier];
                                holder.ClearBranches();
                            }
                        }

                        sourceController.Controller.Stop();
                    }
                }
            }

            return result;
        }

        public ServiceResult OnWriteAlarmTrigger(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            Dictionary<string, SourceController> sourceControllers = GetUnitAlarms(node);
            if (sourceControllers == null)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            if (sourceControllers != null)
            {
                SourceController sourceController = GetSourceControllerFromNodeState(node, sourceControllers);

                if (sourceController == null)
                {
                    return StatusCodes.BadNodeIdUnknown;
                }

                Utils.LogInfo("Manual Write " + value.ToString() + " to " + node.NodeId.ToString());

                lock (m_alarms)
                {
                    sourceController.Source.Value = value;
                    Type valueType = value.GetType();
                    sourceController.Controller.ManualWrite(value);
                    IList<IReference> references = new List<IReference>();
                    sourceController.Source.GetReferences(SystemContext, references, ReferenceTypes.HasCondition, false);
                    foreach (IReference reference in references)
                    {
                        string identifier = (string)reference.TargetId.ToString();
                        if (m_alarms.ContainsKey(identifier))
                        {
                            AlarmHolder holder = m_alarms[identifier];
                            holder.Update(true);
                        }
                    }
                }
            }

            return Opc.Ua.StatusCodes.Good;
        }

        #endregion

        #region Helpers
        public Dictionary<string, SourceController> GetUnitAlarms(NodeState nodeState)
        {
            return m_triggerMap["Alarms"];
        }


        public string GetUnitFromNodeState(NodeState nodeState)
        {
            return GetUnitFromNodeId(nodeState.NodeId);
        }

        public string GetUnitFromNodeId(NodeId nodeId)
        {
            string unit = "";

            if (nodeId.IdType == IdType.String)
            {
                string nodeIdString = (string)nodeId.Identifier;
                string[] splitString = nodeIdString.Split('.');
                // Alarms.UnitName.MethodName
                if (splitString.Length >= 1)
                {
                    unit = splitString[1];
                }
            }

            return unit;
        }

        public SourceController GetSourceControllerFromNodeState(NodeState nodeState, Dictionary<string, SourceController> map)
        {
            SourceController sourceController = null;

            string name = GetSourceNameFromNodeState(nodeState);
            if (map.ContainsKey(name))
            {
                sourceController = map[name];
            }

            return sourceController;
        }

        public string GetSourceNameFromNodeState(NodeState nodeState)
        {
            return GetSourceNameFromNodeId(nodeState.NodeId);
        }

        public string GetSourceNameFromNodeId(NodeId nodeId)
        {
            string sourceName = "";

            if (nodeId.IdType == IdType.String)
            {
                string nodeIdString = (string)nodeId.Identifier;
                string[] splitString = nodeIdString.Split('.');
                // Alarms.UnitName.AnalogSource
                if (splitString.Length >= 2)
                {
                    sourceName = splitString[splitString.Length - 1].Replace("Source", "");
                }
            }

            return sourceName;
        }

        public SupportedAlarmConditionType GetSupportedAlarmConditionType(ref int index)
        {
            SupportedAlarmConditionType conditionType = m_ConditionTypes[index];
            index++;
            if (index >= m_ConditionTypes.Length)
            {
                index = 0;
            }
            return conditionType;
        }



        #endregion


        #endregion

        #region Overrides

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public override void DeleteAddressSpace()
        {
            lock (Lock)
            {
                // TBD
            }
        }

        #endregion


        #region Private Fields

        Dictionary<string, AlarmHolder> m_alarms = new Dictionary<string, AlarmHolder>();

        Dictionary<string, Dictionary<string, SourceController>> m_triggerMap =
            new Dictionary<string, Dictionary<string, SourceController>>();

        private bool m_allowEntry = false;
        private uint m_success = 0;
        private uint m_missed = 0;

        private SupportedAlarmConditionType[] m_ConditionTypes = {
                    new SupportedAlarmConditionType( "Process", "ProcessConditionClassType",  ObjectTypeIds.ProcessConditionClassType ),
                    new SupportedAlarmConditionType( "Maintenance", "MaintenanceConditionClassType",  ObjectTypeIds.MaintenanceConditionClassType ),
                    new SupportedAlarmConditionType( "System", "SystemConditionClassType",  ObjectTypeIds.SystemConditionClassType ) };


        private Timer m_simulationTimer;
        private UInt16 m_simulationInterval = 100;

        #endregion

    }
}
