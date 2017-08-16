using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PubSubBase.Definitions
{
    [Serializable]
    public class ClientReferenceDescription
    {
        public string ReferenceTypeId { get; set; }

        public bool IsForward { get; set; }

        public string NodeId { get; set; }

        public string NodeClass { get; set; }

        public string BrowseName { get; set; }

        public string DisplayName { get; set; }

        public string TypeDefinition { get; set; }

        public string StatusCode { get; set; }
        public string Value { get; set; }
    }
}
