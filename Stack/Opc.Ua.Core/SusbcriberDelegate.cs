using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Opc.Ua.Core
{
    public delegate void SubscriberDelegate(NodeId targetNodeId, DataValue datavalue);
     
}
