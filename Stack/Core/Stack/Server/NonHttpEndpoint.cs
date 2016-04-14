/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Text;
using System.ServiceModel;

namespace Opc.Ua
{
    #if USE_WCF_FOR_UATCP
    /// <summary>
    /// A endpoint object used by clients to access a UA service via non-HTTP endpoints.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.CodeGenerator", "1.0.0.0")]
    [ServiceBehavior(Namespace = Namespaces.OpcUaWsdl, InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public partial class NonHttpSessionEndpoint : SessionEndpoint
    {
    }

    /// <summary>
    /// A endpoint object used by clients to access a UA service via non-HTTP endpoints.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.CodeGenerator", "1.0.0.0")]
    [ServiceBehavior(Namespace = Namespaces.OpcUaWsdl, InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public partial class NonHttpDiscoveryEndpoint : DiscoveryEndpoint
    {
    }
    #endif
}
