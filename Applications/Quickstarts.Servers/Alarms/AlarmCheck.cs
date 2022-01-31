using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Opc.Ua;
namespace Alarms
{
    /// <summary>
    /// 
    /// </summary>
    public class AlarmCheck
    {
        /// <summary>
        /// Alarm name
        /// </summary>
        public string AlarmName { get; set; }
        /// <summary>
        /// MethodName
        /// </summary>
        public string MethodName { get; set; }
        /// <summary>
        /// ModellingRule
        /// </summary>
        public NodeId MethodDeclarationId { get; set; }
        /// <summary>
        /// ModellingRule Exists
        /// </summary>
        public bool Exists { get; set; }

    }
}
