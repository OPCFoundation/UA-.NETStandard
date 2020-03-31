/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Reflection;
using System.Xml;
using System.Runtime.Serialization;
using Opc.Ua;

namespace AggregationModel
{
    #region ObjectType Identifiers
    /// <summary>
    /// A class that declares constants for all ObjectTypes in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectTypes
    {
        /// <summary>
        /// The identifier for the AggregatedServerStatusType ObjectType.
        /// </summary>
        public const uint AggregatedServerStatusType = 245;
    }
    #endregion

    #region Variable Identifiers
    /// <summary>
    /// A class that declares constants for all Variables in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Variables
    {
        /// <summary>
        /// The identifier for the AggregatedServerStatusType_EndpointUrl Variable.
        /// </summary>
        public const uint AggregatedServerStatusType_EndpointUrl = 246;

        /// <summary>
        /// The identifier for the AggregatedServerStatusType_Status Variable.
        /// </summary>
        public const uint AggregatedServerStatusType_Status = 247;

        /// <summary>
        /// The identifier for the AggregatedServerStatusType_ConnectTime Variable.
        /// </summary>
        public const uint AggregatedServerStatusType_ConnectTime = 248;
    }
    #endregion

    #region ObjectType Node Identifiers
    /// <summary>
    /// A class that declares constants for all ObjectTypes in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectTypeIds
    {
        /// <summary>
        /// The identifier for the AggregatedServerStatusType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId AggregatedServerStatusType = new ExpandedNodeId(AggregationModel.ObjectTypes.AggregatedServerStatusType, AggregationModel.Namespaces.Aggregation);
    }
    #endregion

    #region Variable Node Identifiers
    /// <summary>
    /// A class that declares constants for all Variables in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class VariableIds
    {
        /// <summary>
        /// The identifier for the AggregatedServerStatusType_EndpointUrl Variable.
        /// </summary>
        public static readonly ExpandedNodeId AggregatedServerStatusType_EndpointUrl = new ExpandedNodeId(AggregationModel.Variables.AggregatedServerStatusType_EndpointUrl, AggregationModel.Namespaces.Aggregation);

        /// <summary>
        /// The identifier for the AggregatedServerStatusType_Status Variable.
        /// </summary>
        public static readonly ExpandedNodeId AggregatedServerStatusType_Status = new ExpandedNodeId(AggregationModel.Variables.AggregatedServerStatusType_Status, AggregationModel.Namespaces.Aggregation);

        /// <summary>
        /// The identifier for the AggregatedServerStatusType_ConnectTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId AggregatedServerStatusType_ConnectTime = new ExpandedNodeId(AggregationModel.Variables.AggregatedServerStatusType_ConnectTime, AggregationModel.Namespaces.Aggregation);
    }
    #endregion

    #region BrowseName Declarations
    /// <summary>
    /// Declares all of the BrowseNames used in the Model Design.
    /// </summary>
    public static partial class BrowseNames
    {
        /// <summary>
        /// The BrowseName for the AggregatedServerStatusType component.
        /// </summary>
        public const string AggregatedServerStatusType = "AggregatedServerStatusType";

        /// <summary>
        /// The BrowseName for the ConnectTime component.
        /// </summary>
        public const string ConnectTime = "ConnectTime";

        /// <summary>
        /// The BrowseName for the EndpointUrl component.
        /// </summary>
        public const string EndpointUrl = "EndpointUrl";

        /// <summary>
        /// The BrowseName for the Status component.
        /// </summary>
        public const string Status = "Status";
    }
    #endregion

    #region Namespace Declarations
    /// <summary>
    /// Defines constants for all namespaces referenced by the model design.
    /// </summary>
    public static partial class Namespaces
    {
        /// <summary>
        /// The URI for the OpcUa namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUa = "http://opcfoundation.org/UA/";

        /// <summary>
        /// The URI for the OpcUaXsd namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUaXsd = "http://opcfoundation.org/UA/2008/02/Types.xsd";

        /// <summary>
        /// The URI for the Aggregation namespace (.NET code namespace is 'AggregationModel').
        /// </summary>
        public const string Aggregation = "http://opcfoundation.org/AggregationModel";

        /// <summary>
        /// Returns a namespace table with all of the URIs defined.
        /// </summary>
        /// <remarks>
        /// This table is was used to create any relative paths in the model design.
        /// </remarks>
        public static NamespaceTable GetNamespaceTable()
        {
            FieldInfo[] fields = typeof(Namespaces).GetFields(BindingFlags.Public | BindingFlags.Static);

            NamespaceTable namespaceTable = new NamespaceTable();

            foreach (FieldInfo field in fields)
            {
                string namespaceUri = (string)field.GetValue(typeof(Namespaces));

                if (namespaceTable.GetIndex(namespaceUri) == -1)
                {
                    namespaceTable.Append(namespaceUri);
                }
            }

            return namespaceTable;
        }
    }
    #endregion
}
