using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Opc.Ua;

#pragma warning disable CS1591

namespace Alarms
{
    public class SourceController
    {
        public SourceController(BaseDataVariableState source, AlarmController controller)
        {
            m_source = source;
            m_controller = controller;
        }

        public AlarmController Controller { get { return m_controller; } set { m_controller = value; } }
        public BaseDataVariableState Source { get { return m_source; } set { m_source = value; } }
        AlarmController m_controller;
        BaseDataVariableState m_source;
    }
}
