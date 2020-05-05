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
using Quickstarts.Engineering;
using Quickstarts.Operations;

namespace Quickstarts.Views
{
    #region Object Identifiers
    /// <summary>
    /// A class that declares constants for all Objects in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Objects
    {
        /// <summary>
        /// The identifier for the BoilerType_WaterIn Object.
        /// </summary>
        public const uint BoilerType_WaterIn = 391;

        /// <summary>
        /// The identifier for the BoilerType_WaterIn_Flow Object.
        /// </summary>
        public const uint BoilerType_WaterIn_Flow = 392;

        /// <summary>
        /// The identifier for the BoilerType_SteamOut Object.
        /// </summary>
        public const uint BoilerType_SteamOut = 407;

        /// <summary>
        /// The identifier for the BoilerType_SteamOut_Flow Object.
        /// </summary>
        public const uint BoilerType_SteamOut_Flow = 408;

        /// <summary>
        /// The identifier for the BoilerType_Drum Object.
        /// </summary>
        public const uint BoilerType_Drum = 423;

        /// <summary>
        /// The identifier for the BoilerType_Drum_Level Object.
        /// </summary>
        public const uint BoilerType_Drum_Level = 424;

        /// <summary>
        /// The identifier for the Plant Object.
        /// </summary>
        public const uint Plant = 442;
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
        /// The identifier for the GenericControllerType ObjectType.
        /// </summary>
        public const uint GenericControllerType = 345;

        /// <summary>
        /// The identifier for the FlowControllerType ObjectType.
        /// </summary>
        public const uint FlowControllerType = 360;

        /// <summary>
        /// The identifier for the LevelControllerType ObjectType.
        /// </summary>
        public const uint LevelControllerType = 375;

        /// <summary>
        /// The identifier for the BoilerType ObjectType.
        /// </summary>
        public const uint BoilerType = 390;
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
        /// The identifier for the GenericControllerType_SerialNumber Variable.
        /// </summary>
        public const uint GenericControllerType_SerialNumber = 346;

        /// <summary>
        /// The identifier for the GenericControllerType_Manufacturer Variable.
        /// </summary>
        public const uint GenericControllerType_Manufacturer = 347;

        /// <summary>
        /// The identifier for the GenericControllerType_SetPoint Variable.
        /// </summary>
        public const uint GenericControllerType_SetPoint = 348;

        /// <summary>
        /// The identifier for the GenericControllerType_SetPoint_EURange Variable.
        /// </summary>
        public const uint GenericControllerType_SetPoint_EURange = 351;

        /// <summary>
        /// The identifier for the GenericControllerType_Measurement Variable.
        /// </summary>
        public const uint GenericControllerType_Measurement = 354;

        /// <summary>
        /// The identifier for the GenericControllerType_Measurement_EURange Variable.
        /// </summary>
        public const uint GenericControllerType_Measurement_EURange = 357;

        /// <summary>
        /// The identifier for the FlowControllerType_SetPoint_EURange Variable.
        /// </summary>
        public const uint FlowControllerType_SetPoint_EURange = 366;

        /// <summary>
        /// The identifier for the FlowControllerType_Measurement_EURange Variable.
        /// </summary>
        public const uint FlowControllerType_Measurement_EURange = 372;

        /// <summary>
        /// The identifier for the LevelControllerType_SetPoint_EURange Variable.
        /// </summary>
        public const uint LevelControllerType_SetPoint_EURange = 381;

        /// <summary>
        /// The identifier for the LevelControllerType_Measurement_EURange Variable.
        /// </summary>
        public const uint LevelControllerType_Measurement_EURange = 387;

        /// <summary>
        /// The identifier for the BoilerType_WaterIn_Flow_SerialNumber Variable.
        /// </summary>
        public const uint BoilerType_WaterIn_Flow_SerialNumber = 393;

        /// <summary>
        /// The identifier for the BoilerType_WaterIn_Flow_Manufacturer Variable.
        /// </summary>
        public const uint BoilerType_WaterIn_Flow_Manufacturer = 394;

        /// <summary>
        /// The identifier for the BoilerType_WaterIn_Flow_SetPoint Variable.
        /// </summary>
        public const uint BoilerType_WaterIn_Flow_SetPoint = 395;

        /// <summary>
        /// The identifier for the BoilerType_WaterIn_Flow_SetPoint_EURange Variable.
        /// </summary>
        public const uint BoilerType_WaterIn_Flow_SetPoint_EURange = 398;

        /// <summary>
        /// The identifier for the BoilerType_WaterIn_Flow_Measurement Variable.
        /// </summary>
        public const uint BoilerType_WaterIn_Flow_Measurement = 401;

        /// <summary>
        /// The identifier for the BoilerType_WaterIn_Flow_Measurement_EURange Variable.
        /// </summary>
        public const uint BoilerType_WaterIn_Flow_Measurement_EURange = 404;

        /// <summary>
        /// The identifier for the BoilerType_SteamOut_Flow_SerialNumber Variable.
        /// </summary>
        public const uint BoilerType_SteamOut_Flow_SerialNumber = 409;

        /// <summary>
        /// The identifier for the BoilerType_SteamOut_Flow_Manufacturer Variable.
        /// </summary>
        public const uint BoilerType_SteamOut_Flow_Manufacturer = 410;

        /// <summary>
        /// The identifier for the BoilerType_SteamOut_Flow_SetPoint Variable.
        /// </summary>
        public const uint BoilerType_SteamOut_Flow_SetPoint = 411;

        /// <summary>
        /// The identifier for the BoilerType_SteamOut_Flow_SetPoint_EURange Variable.
        /// </summary>
        public const uint BoilerType_SteamOut_Flow_SetPoint_EURange = 414;

        /// <summary>
        /// The identifier for the BoilerType_SteamOut_Flow_Measurement Variable.
        /// </summary>
        public const uint BoilerType_SteamOut_Flow_Measurement = 417;

        /// <summary>
        /// The identifier for the BoilerType_SteamOut_Flow_Measurement_EURange Variable.
        /// </summary>
        public const uint BoilerType_SteamOut_Flow_Measurement_EURange = 420;

        /// <summary>
        /// The identifier for the BoilerType_Drum_Level_SerialNumber Variable.
        /// </summary>
        public const uint BoilerType_Drum_Level_SerialNumber = 425;

        /// <summary>
        /// The identifier for the BoilerType_Drum_Level_Manufacturer Variable.
        /// </summary>
        public const uint BoilerType_Drum_Level_Manufacturer = 426;

        /// <summary>
        /// The identifier for the BoilerType_Drum_Level_SetPoint Variable.
        /// </summary>
        public const uint BoilerType_Drum_Level_SetPoint = 427;

        /// <summary>
        /// The identifier for the BoilerType_Drum_Level_SetPoint_EURange Variable.
        /// </summary>
        public const uint BoilerType_Drum_Level_SetPoint_EURange = 430;

        /// <summary>
        /// The identifier for the BoilerType_Drum_Level_Measurement Variable.
        /// </summary>
        public const uint BoilerType_Drum_Level_Measurement = 433;

        /// <summary>
        /// The identifier for the BoilerType_Drum_Level_Measurement_EURange Variable.
        /// </summary>
        public const uint BoilerType_Drum_Level_Measurement_EURange = 436;
    }
    #endregion

    #region View Identifiers
    /// <summary>
    /// A class that declares constants for all Views in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Views
    {
        /// <summary>
        /// The identifier for the Engineering View.
        /// </summary>
        public const uint Engineering = 439;

        /// <summary>
        /// The identifier for the Operations View.
        /// </summary>
        public const uint Operations = 441;
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
        /// The identifier for the BoilerType_WaterIn Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_WaterIn = new ExpandedNodeId(Quickstarts.Views.Objects.BoilerType_WaterIn, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_WaterIn_Flow Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_WaterIn_Flow = new ExpandedNodeId(Quickstarts.Views.Objects.BoilerType_WaterIn_Flow, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_SteamOut Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_SteamOut = new ExpandedNodeId(Quickstarts.Views.Objects.BoilerType_SteamOut, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_SteamOut_Flow Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_SteamOut_Flow = new ExpandedNodeId(Quickstarts.Views.Objects.BoilerType_SteamOut_Flow, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_Drum Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_Drum = new ExpandedNodeId(Quickstarts.Views.Objects.BoilerType_Drum, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_Drum_Level Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_Drum_Level = new ExpandedNodeId(Quickstarts.Views.Objects.BoilerType_Drum_Level, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the Plant Object.
        /// </summary>
        public static readonly ExpandedNodeId Plant = new ExpandedNodeId(Quickstarts.Views.Objects.Plant, Quickstarts.Views.Namespaces.Views);
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
        /// The identifier for the GenericControllerType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId GenericControllerType = new ExpandedNodeId(Quickstarts.Views.ObjectTypes.GenericControllerType, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the FlowControllerType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId FlowControllerType = new ExpandedNodeId(Quickstarts.Views.ObjectTypes.FlowControllerType, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the LevelControllerType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId LevelControllerType = new ExpandedNodeId(Quickstarts.Views.ObjectTypes.LevelControllerType, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType = new ExpandedNodeId(Quickstarts.Views.ObjectTypes.BoilerType, Quickstarts.Views.Namespaces.Views);
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
        /// The identifier for the GenericControllerType_SerialNumber Variable.
        /// </summary>
        public static readonly ExpandedNodeId GenericControllerType_SerialNumber = new ExpandedNodeId(Quickstarts.Views.Variables.GenericControllerType_SerialNumber, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the GenericControllerType_Manufacturer Variable.
        /// </summary>
        public static readonly ExpandedNodeId GenericControllerType_Manufacturer = new ExpandedNodeId(Quickstarts.Views.Variables.GenericControllerType_Manufacturer, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the GenericControllerType_SetPoint Variable.
        /// </summary>
        public static readonly ExpandedNodeId GenericControllerType_SetPoint = new ExpandedNodeId(Quickstarts.Views.Variables.GenericControllerType_SetPoint, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the GenericControllerType_SetPoint_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId GenericControllerType_SetPoint_EURange = new ExpandedNodeId(Quickstarts.Views.Variables.GenericControllerType_SetPoint_EURange, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the GenericControllerType_Measurement Variable.
        /// </summary>
        public static readonly ExpandedNodeId GenericControllerType_Measurement = new ExpandedNodeId(Quickstarts.Views.Variables.GenericControllerType_Measurement, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the GenericControllerType_Measurement_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId GenericControllerType_Measurement_EURange = new ExpandedNodeId(Quickstarts.Views.Variables.GenericControllerType_Measurement_EURange, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the FlowControllerType_SetPoint_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId FlowControllerType_SetPoint_EURange = new ExpandedNodeId(Quickstarts.Views.Variables.FlowControllerType_SetPoint_EURange, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the FlowControllerType_Measurement_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId FlowControllerType_Measurement_EURange = new ExpandedNodeId(Quickstarts.Views.Variables.FlowControllerType_Measurement_EURange, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the LevelControllerType_SetPoint_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId LevelControllerType_SetPoint_EURange = new ExpandedNodeId(Quickstarts.Views.Variables.LevelControllerType_SetPoint_EURange, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the LevelControllerType_Measurement_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId LevelControllerType_Measurement_EURange = new ExpandedNodeId(Quickstarts.Views.Variables.LevelControllerType_Measurement_EURange, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_WaterIn_Flow_SerialNumber Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_WaterIn_Flow_SerialNumber = new ExpandedNodeId(Quickstarts.Views.Variables.BoilerType_WaterIn_Flow_SerialNumber, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_WaterIn_Flow_Manufacturer Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_WaterIn_Flow_Manufacturer = new ExpandedNodeId(Quickstarts.Views.Variables.BoilerType_WaterIn_Flow_Manufacturer, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_WaterIn_Flow_SetPoint Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_WaterIn_Flow_SetPoint = new ExpandedNodeId(Quickstarts.Views.Variables.BoilerType_WaterIn_Flow_SetPoint, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_WaterIn_Flow_SetPoint_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_WaterIn_Flow_SetPoint_EURange = new ExpandedNodeId(Quickstarts.Views.Variables.BoilerType_WaterIn_Flow_SetPoint_EURange, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_WaterIn_Flow_Measurement Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_WaterIn_Flow_Measurement = new ExpandedNodeId(Quickstarts.Views.Variables.BoilerType_WaterIn_Flow_Measurement, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_WaterIn_Flow_Measurement_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_WaterIn_Flow_Measurement_EURange = new ExpandedNodeId(Quickstarts.Views.Variables.BoilerType_WaterIn_Flow_Measurement_EURange, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_SteamOut_Flow_SerialNumber Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_SteamOut_Flow_SerialNumber = new ExpandedNodeId(Quickstarts.Views.Variables.BoilerType_SteamOut_Flow_SerialNumber, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_SteamOut_Flow_Manufacturer Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_SteamOut_Flow_Manufacturer = new ExpandedNodeId(Quickstarts.Views.Variables.BoilerType_SteamOut_Flow_Manufacturer, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_SteamOut_Flow_SetPoint Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_SteamOut_Flow_SetPoint = new ExpandedNodeId(Quickstarts.Views.Variables.BoilerType_SteamOut_Flow_SetPoint, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_SteamOut_Flow_SetPoint_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_SteamOut_Flow_SetPoint_EURange = new ExpandedNodeId(Quickstarts.Views.Variables.BoilerType_SteamOut_Flow_SetPoint_EURange, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_SteamOut_Flow_Measurement Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_SteamOut_Flow_Measurement = new ExpandedNodeId(Quickstarts.Views.Variables.BoilerType_SteamOut_Flow_Measurement, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_SteamOut_Flow_Measurement_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_SteamOut_Flow_Measurement_EURange = new ExpandedNodeId(Quickstarts.Views.Variables.BoilerType_SteamOut_Flow_Measurement_EURange, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_Drum_Level_SerialNumber Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_Drum_Level_SerialNumber = new ExpandedNodeId(Quickstarts.Views.Variables.BoilerType_Drum_Level_SerialNumber, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_Drum_Level_Manufacturer Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_Drum_Level_Manufacturer = new ExpandedNodeId(Quickstarts.Views.Variables.BoilerType_Drum_Level_Manufacturer, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_Drum_Level_SetPoint Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_Drum_Level_SetPoint = new ExpandedNodeId(Quickstarts.Views.Variables.BoilerType_Drum_Level_SetPoint, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_Drum_Level_SetPoint_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_Drum_Level_SetPoint_EURange = new ExpandedNodeId(Quickstarts.Views.Variables.BoilerType_Drum_Level_SetPoint_EURange, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_Drum_Level_Measurement Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_Drum_Level_Measurement = new ExpandedNodeId(Quickstarts.Views.Variables.BoilerType_Drum_Level_Measurement, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the BoilerType_Drum_Level_Measurement_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_Drum_Level_Measurement_EURange = new ExpandedNodeId(Quickstarts.Views.Variables.BoilerType_Drum_Level_Measurement_EURange, Quickstarts.Views.Namespaces.Views);
    }
    #endregion

    #region View Node Identifiers
    /// <summary>
    /// A class that declares constants for all Views in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ViewIds
    {
        /// <summary>
        /// The identifier for the Engineering View.
        /// </summary>
        public static readonly ExpandedNodeId Engineering = new ExpandedNodeId(Quickstarts.Views.Views.Engineering, Quickstarts.Views.Namespaces.Views);

        /// <summary>
        /// The identifier for the Operations View.
        /// </summary>
        public static readonly ExpandedNodeId Operations = new ExpandedNodeId(Quickstarts.Views.Views.Operations, Quickstarts.Views.Namespaces.Views);
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
        /// The BrowseName for the BoilerType component.
        /// </summary>
        public const string BoilerType = "BoilerType";

        /// <summary>
        /// The BrowseName for the Drum component.
        /// </summary>
        public const string Drum = "Drum";

        /// <summary>
        /// The BrowseName for the Engineering component.
        /// </summary>
        public const string Engineering = "Engineering";

        /// <summary>
        /// The BrowseName for the FlowControllerType component.
        /// </summary>
        public const string FlowControllerType = "FlowControllerType";

        /// <summary>
        /// The BrowseName for the GenericControllerType component.
        /// </summary>
        public const string GenericControllerType = "GenericControllerType";

        /// <summary>
        /// The BrowseName for the LevelControllerType component.
        /// </summary>
        public const string LevelControllerType = "LevelControllerType";

        /// <summary>
        /// The BrowseName for the Operations component.
        /// </summary>
        public const string Operations = "Operations";

        /// <summary>
        /// The BrowseName for the Plant component.
        /// </summary>
        public const string Plant = "Plant";

        /// <summary>
        /// The BrowseName for the SteamOut component.
        /// </summary>
        public const string SteamOut = "SteamOut";

        /// <summary>
        /// The BrowseName for the WaterIn component.
        /// </summary>
        public const string WaterIn = "WaterIn";
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
        /// The URI for the Engineering namespace (.NET code namespace is 'Quickstarts.Engineering').
        /// </summary>
        public const string Engineering = "http://opcfoundation.org/UA/Quickstarts/Engineering";

        /// <summary>
        /// The URI for the Operations namespace (.NET code namespace is 'Quickstarts.Operations').
        /// </summary>
        public const string Operations = "http://opcfoundation.org/UA/Quickstarts/Operations";

        /// <summary>
        /// The URI for the Views namespace (.NET code namespace is 'Quickstarts.Views').
        /// </summary>
        public const string Views = "http://opcfoundation.org/UA/Quickstarts/Views";
    }
    #endregion
}