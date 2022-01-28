using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Opc.Ua;

#pragma warning disable CS1591

namespace Quickstarts.ReferenceServer
{
    public class SupportedAlarmConditionType
    {
        public SupportedAlarmConditionType(string name, string conditionName, NodeId nodeId)
        {
            m_name = name;
            m_conditionName = conditionName;
            m_nodeId = nodeId;
        }

        public string Name
        {
            get { return m_name; }
        }

        public string ConditionName
        {
            get { return m_conditionName; }
        }

        public NodeId Node
        {
            get { return m_nodeId; }
        }

        private string m_name;
        private string m_conditionName;
        private NodeId m_nodeId;
    }
}
