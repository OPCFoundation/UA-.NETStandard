using Opc.Ua;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PubSubConfigurationUI.Definitions
{
   public class ServerNode
    {
        public string Name { get; set; }
        public ApplicationDescription UAApplicationDescription { get; set; }
    }
}
