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
using System.ServiceModel;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
    public partial class PubSubConnectionDataType
    {
        public object Handle { get; set; }
    }

    public partial class PubSubGroupDataType
    {
        public object Handle { get; set; }
    }

    public partial class DataSetWriterDataType
    {
        public object Handle { get; set; }
    }

    public partial class DataSetReaderDataType
    {
        public object Handle { get; set; }
    }

    public partial class PublishedDataSetDataType
    {
        public object Handle { get; set; }
    }

    public partial class JsonNetworkMessage
    {
        public object Handle { get; set; }
    }

    public partial class JsonDataSetMessage
    {
        public object Handle { get; set; }
    }
}
