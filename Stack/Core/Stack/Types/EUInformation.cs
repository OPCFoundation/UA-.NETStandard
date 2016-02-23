/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Stores information about engineering units.
    /// </summary>
    public partial class EUInformation
    {
        /// <summary>
        /// Initializes the object with the unitName and namespaceUri.
        /// </summary>
        public EUInformation(string unitName, string namespaceUri)
        {
            Initialize();

            m_displayName  = new LocalizedText(unitName);
            m_description  = new LocalizedText(unitName);
            m_namespaceUri = namespaceUri;
        }

        /// <summary>
        /// Initializes the object with the unitName and namespaceUri.
        /// </summary>
        public EUInformation(string shortName, string longName, string namespaceUri)
        {
            Initialize();

            m_displayName  = new LocalizedText(shortName);
            m_description  = new LocalizedText(longName);
            m_namespaceUri = namespaceUri;
        }
    }
}
