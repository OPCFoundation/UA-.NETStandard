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

namespace Quickstarts.SimpleEvents
{
    #region DataType Identifiers
    /// <summary>
    /// A class that declares constants for all DataTypes in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class DataTypes
    {
        /// <summary>
        /// The identifier for the CycleStepDataType DataType.
        /// </summary>
        public const uint CycleStepDataType = 183;
    }
    #endregion

    #region Object Identifiers
    /// <summary>
    /// A class that declares constants for all Objects in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Objects
    {
        /// <summary>
        /// The identifier for the CycleStepDataType_Encoding_DefaultXml Object.
        /// </summary>
        public const uint CycleStepDataType_Encoding_DefaultXml = 221;

        /// <summary>
        /// The identifier for the CycleStepDataType_Encoding_DefaultBinary Object.
        /// </summary>
        public const uint CycleStepDataType_Encoding_DefaultBinary = 228;
    }
    #endregion

    #region ObjectType Identifiers
    /// <summary>
    /// A class that declares constants for all ObjectTypes in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectTypes
    {
        /// <summary>
        /// The identifier for the SystemCycleStatusEventType ObjectType.
        /// </summary>
        public const uint SystemCycleStatusEventType = 235;

        /// <summary>
        /// The identifier for the SystemCycleStartedEventType ObjectType.
        /// </summary>
        public const uint SystemCycleStartedEventType = 184;

        /// <summary>
        /// The identifier for the SystemCycleAbortedEventType ObjectType.
        /// </summary>
        public const uint SystemCycleAbortedEventType = 197;

        /// <summary>
        /// The identifier for the SystemCycleFinishedEventType ObjectType.
        /// </summary>
        public const uint SystemCycleFinishedEventType = 210;
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
        /// The identifier for the SystemCycleStatusEventType_CycleId Variable.
        /// </summary>
        public const uint SystemCycleStatusEventType_CycleId = 245;

        /// <summary>
        /// The identifier for the SystemCycleStatusEventType_CurrentStep Variable.
        /// </summary>
        public const uint SystemCycleStatusEventType_CurrentStep = 246;

        /// <summary>
        /// The identifier for the SystemCycleStartedEventType_Steps Variable.
        /// </summary>
        public const uint SystemCycleStartedEventType_Steps = 196;

        /// <summary>
        /// The identifier for the SystemCycleAbortedEventType_Error Variable.
        /// </summary>
        public const uint SystemCycleAbortedEventType_Error = 249;

        /// <summary>
        /// The identifier for the SimpleEvents_XmlSchema Variable.
        /// </summary>
        public const uint SimpleEvents_XmlSchema = 229;

        /// <summary>
        /// The identifier for the SimpleEvents_XmlSchema_NamespaceUri Variable.
        /// </summary>
        public const uint SimpleEvents_XmlSchema_NamespaceUri = 231;

        /// <summary>
        /// The identifier for the SimpleEvents_XmlSchema_CycleStepDataType Variable.
        /// </summary>
        public const uint SimpleEvents_XmlSchema_CycleStepDataType = 232;

        /// <summary>
        /// The identifier for the SimpleEvents_BinarySchema Variable.
        /// </summary>
        public const uint SimpleEvents_BinarySchema = 222;

        /// <summary>
        /// The identifier for the SimpleEvents_BinarySchema_NamespaceUri Variable.
        /// </summary>
        public const uint SimpleEvents_BinarySchema_NamespaceUri = 224;

        /// <summary>
        /// The identifier for the SimpleEvents_BinarySchema_CycleStepDataType Variable.
        /// </summary>
        public const uint SimpleEvents_BinarySchema_CycleStepDataType = 225;
    }
    #endregion

    #region DataType Node Identifiers
    /// <summary>
    /// A class that declares constants for all DataTypes in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class DataTypeIds
    {
        /// <summary>
        /// The identifier for the CycleStepDataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId CycleStepDataType = new ExpandedNodeId(Quickstarts.SimpleEvents.DataTypes.CycleStepDataType, Quickstarts.SimpleEvents.Namespaces.SimpleEvents);
    }
    #endregion

    #region Object Node Identifiers
    /// <summary>
    /// A class that declares constants for all Objects in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectIds
    {
        /// <summary>
        /// The identifier for the CycleStepDataType_Encoding_DefaultXml Object.
        /// </summary>
        public static readonly ExpandedNodeId CycleStepDataType_Encoding_DefaultXml = new ExpandedNodeId(Quickstarts.SimpleEvents.Objects.CycleStepDataType_Encoding_DefaultXml, Quickstarts.SimpleEvents.Namespaces.SimpleEvents);

        /// <summary>
        /// The identifier for the CycleStepDataType_Encoding_DefaultBinary Object.
        /// </summary>
        public static readonly ExpandedNodeId CycleStepDataType_Encoding_DefaultBinary = new ExpandedNodeId(Quickstarts.SimpleEvents.Objects.CycleStepDataType_Encoding_DefaultBinary, Quickstarts.SimpleEvents.Namespaces.SimpleEvents);
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
        /// The identifier for the SystemCycleStatusEventType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId SystemCycleStatusEventType = new ExpandedNodeId(Quickstarts.SimpleEvents.ObjectTypes.SystemCycleStatusEventType, Quickstarts.SimpleEvents.Namespaces.SimpleEvents);

        /// <summary>
        /// The identifier for the SystemCycleStartedEventType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId SystemCycleStartedEventType = new ExpandedNodeId(Quickstarts.SimpleEvents.ObjectTypes.SystemCycleStartedEventType, Quickstarts.SimpleEvents.Namespaces.SimpleEvents);

        /// <summary>
        /// The identifier for the SystemCycleAbortedEventType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId SystemCycleAbortedEventType = new ExpandedNodeId(Quickstarts.SimpleEvents.ObjectTypes.SystemCycleAbortedEventType, Quickstarts.SimpleEvents.Namespaces.SimpleEvents);

        /// <summary>
        /// The identifier for the SystemCycleFinishedEventType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId SystemCycleFinishedEventType = new ExpandedNodeId(Quickstarts.SimpleEvents.ObjectTypes.SystemCycleFinishedEventType, Quickstarts.SimpleEvents.Namespaces.SimpleEvents);
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
        /// The identifier for the SystemCycleStatusEventType_CycleId Variable.
        /// </summary>
        public static readonly ExpandedNodeId SystemCycleStatusEventType_CycleId = new ExpandedNodeId(Quickstarts.SimpleEvents.Variables.SystemCycleStatusEventType_CycleId, Quickstarts.SimpleEvents.Namespaces.SimpleEvents);

        /// <summary>
        /// The identifier for the SystemCycleStatusEventType_CurrentStep Variable.
        /// </summary>
        public static readonly ExpandedNodeId SystemCycleStatusEventType_CurrentStep = new ExpandedNodeId(Quickstarts.SimpleEvents.Variables.SystemCycleStatusEventType_CurrentStep, Quickstarts.SimpleEvents.Namespaces.SimpleEvents);

        /// <summary>
        /// The identifier for the SystemCycleStartedEventType_Steps Variable.
        /// </summary>
        public static readonly ExpandedNodeId SystemCycleStartedEventType_Steps = new ExpandedNodeId(Quickstarts.SimpleEvents.Variables.SystemCycleStartedEventType_Steps, Quickstarts.SimpleEvents.Namespaces.SimpleEvents);

        /// <summary>
        /// The identifier for the SystemCycleAbortedEventType_Error Variable.
        /// </summary>
        public static readonly ExpandedNodeId SystemCycleAbortedEventType_Error = new ExpandedNodeId(Quickstarts.SimpleEvents.Variables.SystemCycleAbortedEventType_Error, Quickstarts.SimpleEvents.Namespaces.SimpleEvents);

        /// <summary>
        /// The identifier for the SimpleEvents_XmlSchema Variable.
        /// </summary>
        public static readonly ExpandedNodeId SimpleEvents_XmlSchema = new ExpandedNodeId(Quickstarts.SimpleEvents.Variables.SimpleEvents_XmlSchema, Quickstarts.SimpleEvents.Namespaces.SimpleEvents);

        /// <summary>
        /// The identifier for the SimpleEvents_XmlSchema_NamespaceUri Variable.
        /// </summary>
        public static readonly ExpandedNodeId SimpleEvents_XmlSchema_NamespaceUri = new ExpandedNodeId(Quickstarts.SimpleEvents.Variables.SimpleEvents_XmlSchema_NamespaceUri, Quickstarts.SimpleEvents.Namespaces.SimpleEvents);

        /// <summary>
        /// The identifier for the SimpleEvents_XmlSchema_CycleStepDataType Variable.
        /// </summary>
        public static readonly ExpandedNodeId SimpleEvents_XmlSchema_CycleStepDataType = new ExpandedNodeId(Quickstarts.SimpleEvents.Variables.SimpleEvents_XmlSchema_CycleStepDataType, Quickstarts.SimpleEvents.Namespaces.SimpleEvents);

        /// <summary>
        /// The identifier for the SimpleEvents_BinarySchema Variable.
        /// </summary>
        public static readonly ExpandedNodeId SimpleEvents_BinarySchema = new ExpandedNodeId(Quickstarts.SimpleEvents.Variables.SimpleEvents_BinarySchema, Quickstarts.SimpleEvents.Namespaces.SimpleEvents);

        /// <summary>
        /// The identifier for the SimpleEvents_BinarySchema_NamespaceUri Variable.
        /// </summary>
        public static readonly ExpandedNodeId SimpleEvents_BinarySchema_NamespaceUri = new ExpandedNodeId(Quickstarts.SimpleEvents.Variables.SimpleEvents_BinarySchema_NamespaceUri, Quickstarts.SimpleEvents.Namespaces.SimpleEvents);

        /// <summary>
        /// The identifier for the SimpleEvents_BinarySchema_CycleStepDataType Variable.
        /// </summary>
        public static readonly ExpandedNodeId SimpleEvents_BinarySchema_CycleStepDataType = new ExpandedNodeId(Quickstarts.SimpleEvents.Variables.SimpleEvents_BinarySchema_CycleStepDataType, Quickstarts.SimpleEvents.Namespaces.SimpleEvents);
    }
    #endregion

    #region BrowseName Declarations
    /// <summary>
    /// Declares all of the BrowseNames used in the Model Design.
    /// </summary>
    public static partial class BrowseNames
    {
        /// <summary>
        /// The BrowseName for the CurrentStep component.
        /// </summary>
        public const string CurrentStep = "CurrentStep";

        /// <summary>
        /// The BrowseName for the CycleId component.
        /// </summary>
        public const string CycleId = "CycleId";

        /// <summary>
        /// The BrowseName for the CycleStepDataType component.
        /// </summary>
        public const string CycleStepDataType = "CycleStepDataType";

        /// <summary>
        /// The BrowseName for the Error component.
        /// </summary>
        public const string Error = "Error";

        /// <summary>
        /// The BrowseName for the SimpleEvents_BinarySchema component.
        /// </summary>
        public const string SimpleEvents_BinarySchema = "Quickstarts.SimpleEvents";

        /// <summary>
        /// The BrowseName for the SimpleEvents_XmlSchema component.
        /// </summary>
        public const string SimpleEvents_XmlSchema = "Quickstarts.SimpleEvents";

        /// <summary>
        /// The BrowseName for the Steps component.
        /// </summary>
        public const string Steps = "Steps";

        /// <summary>
        /// The BrowseName for the SystemCycleAbortedEventType component.
        /// </summary>
        public const string SystemCycleAbortedEventType = "SystemCycleAbortedEventType";

        /// <summary>
        /// The BrowseName for the SystemCycleFinishedEventType component.
        /// </summary>
        public const string SystemCycleFinishedEventType = "SystemCycleFinishedEventType";

        /// <summary>
        /// The BrowseName for the SystemCycleStartedEventType component.
        /// </summary>
        public const string SystemCycleStartedEventType = "SystemCycleStartedEventType";

        /// <summary>
        /// The BrowseName for the SystemCycleStatusEventType component.
        /// </summary>
        public const string SystemCycleStatusEventType = "SystemCycleStatusEventType";
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
        /// The URI for the SimpleEvents namespace (.NET code namespace is 'Quickstarts.SimpleEvents').
        /// </summary>
        public const string SimpleEvents = "http://opcfoundation.org/Quickstarts/SimpleEvents";

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
