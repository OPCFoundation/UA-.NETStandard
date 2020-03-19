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

namespace Quickstarts.Operations
{
    #region Variable Identifiers
    /// <summary>
    /// A class that declares constants for all Variables in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Variables
    {
        /// <summary>
        /// The identifier for the SetPoint Variable.
        /// </summary>
        public const uint SetPoint = 443;

        /// <summary>
        /// The identifier for the SetPoint_EURange Variable.
        /// </summary>
        public const uint SetPoint_EURange = 446;

        /// <summary>
        /// The identifier for the Measurement Variable.
        /// </summary>
        public const uint Measurement = 449;

        /// <summary>
        /// The identifier for the Measurement_EURange Variable.
        /// </summary>
        public const uint Measurement_EURange = 452;
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
        /// The identifier for the SetPoint Variable.
        /// </summary>
        public static readonly ExpandedNodeId SetPoint = new ExpandedNodeId(Quickstarts.Operations.Variables.SetPoint, Quickstarts.Operations.Namespaces.Operations);

        /// <summary>
        /// The identifier for the SetPoint_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId SetPoint_EURange = new ExpandedNodeId(Quickstarts.Operations.Variables.SetPoint_EURange, Quickstarts.Operations.Namespaces.Operations);

        /// <summary>
        /// The identifier for the Measurement Variable.
        /// </summary>
        public static readonly ExpandedNodeId Measurement = new ExpandedNodeId(Quickstarts.Operations.Variables.Measurement, Quickstarts.Operations.Namespaces.Operations);

        /// <summary>
        /// The identifier for the Measurement_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Measurement_EURange = new ExpandedNodeId(Quickstarts.Operations.Variables.Measurement_EURange, Quickstarts.Operations.Namespaces.Operations);
    }
    #endregion

    #region BrowseName Declarations
    /// <summary>
    /// Declares all of the BrowseNames used in the Model Design.
    /// </summary>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class BrowseNames
    {
        /// <summary>
        /// The BrowseName for the Measurement component.
        /// </summary>
        public const string Measurement = "Measurement";

        /// <summary>
        /// The BrowseName for the SetPoint component.
        /// </summary>
        public const string SetPoint = "SetPoint";
    }
    #endregion

    #region Namespace Declarations
    /// <summary>
    /// Defines constants for all namespaces referenced by the model design.
    /// </summary>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
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
        /// The URI for the Operations namespace (.NET code namespace is 'Quickstarts.Operations').
        /// </summary>
        public const string Operations = "http://opcfoundation.org/UA/Quickstarts/Operations";
    }
    #endregion
}