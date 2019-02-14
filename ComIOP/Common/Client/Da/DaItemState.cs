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
using System.Xml;
using System.IO;
using System.Reflection;
using System.Threading;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Com;

namespace Opc.Ua.Com.Client
{    
    /// <summary>
    /// A object which maps a COM DA item to a UA variable.
    /// </summary>
    public partial class DaItemState : BaseDataVariableState
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="DaItemState"/> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="element">The element.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        public DaItemState(
            ISystemContext context, 
            DaElement element, 
            ushort namespaceIndex) 
        : 
            base(null)
        {
            this.TypeDefinitionId = Opc.Ua.VariableTypeIds.DataItemType;
            this.Description = null;
            this.WriteMask = 0;
            this.UserWriteMask = 0;

            if (element != null)
            {
                Initialize(context, element, namespaceIndex);               
            }
        }
        #endregion

        #region Public Interface
        /// <summary>
        /// Gets the item id.
        /// </summary>
        /// <value>The item id.</value>
        public string ItemId
        {
            get
            {
                if (m_element != null)
                {
                    return m_element.ItemId;
                }

                return null;
            }
        }

        /// <summary>
        /// Gets the element.
        /// </summary>
        /// <value>The element.</value>
        public DaElement Element
        {
            get { return m_element; }
        }

        /// <summary>
        /// Initializes the node from the element.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="element">The element.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        public void Initialize(ISystemContext context, DaElement element, ushort namespaceIndex)
        {
            m_element = element;

            if (element == null)
            {
                return;
            }

            this.NodeId = DaModelUtils.ConstructIdForDaElement(element.ItemId, -1, namespaceIndex);
            this.BrowseName = new QualifiedName(element.Name, namespaceIndex);
            this.DisplayName = new LocalizedText(element.Name);

            // check if TimeZone is supported.
            if (element.TimeZone != null)
            {
                PropertyState property = this.AddProperty<Range>(Opc.Ua.BrowseNames.LocalTime, DataTypeIds.TimeZoneDataType, ValueRanks.Scalar);
                property.NodeId = DaModelUtils.ConstructIdForComponent(property, namespaceIndex);
                property.Value = new Range(element.HighIR, element.LowIR);
            }

            // set the TypeDefinition based on the ElementType.
            switch (element.ElementType)
            {
                case DaElementType.AnalogItem:
                {
                    this.TypeDefinitionId = Opc.Ua.VariableTypeIds.AnalogItemType;

                    // EURange is always present.
                    PropertyState property = this.AddProperty<Range>(Opc.Ua.BrowseNames.EURange, DataTypeIds.Range, ValueRanks.Scalar);
                    property.NodeId = DaModelUtils.ConstructIdForComponent(property, namespaceIndex); 
                    property.Value = new Range(element.HighEU, element.LowEU);

                    // check if InstrumentRange is supported.
                    if (element.HighIR != 0 || element.LowIR != 0)
                    {
                        property = this.AddProperty<Range>(Opc.Ua.BrowseNames.InstrumentRange, DataTypeIds.Range, ValueRanks.Scalar);
                        property.NodeId = DaModelUtils.ConstructIdForComponent(property, namespaceIndex);
                        property.Value = new Range(element.HighIR, element.LowIR);
                    }

                    // check if EngineeringUnits is supported.
                    if (element.EngineeringUnits != null)
                    {
                        property = this.AddProperty<EUInformation>(Opc.Ua.BrowseNames.EngineeringUnits, DataTypeIds.EUInformation, ValueRanks.Scalar);
                        property.NodeId = DaModelUtils.ConstructIdForComponent(property, namespaceIndex); 

                        // use the server's namespace uri to qualify the engineering units.
                        string namespaceUri = context.NamespaceUris.GetString(namespaceIndex);
                        property.Value = new EUInformation(element.EngineeringUnits, namespaceUri);
                    }

                    break;
                }

                case DaElementType.DigitalItem:
                {
                    this.TypeDefinitionId = Opc.Ua.VariableTypeIds.TwoStateDiscreteType;

                    // check if CloseLabel is supported.
                    if (element.CloseLabel != null)
                    {
                        PropertyState property = this.AddProperty<LocalizedText>(Opc.Ua.BrowseNames.TrueState, DataTypeIds.LocalizedText, ValueRanks.Scalar);
                        property.NodeId = DaModelUtils.ConstructIdForComponent(property, namespaceIndex); 
                        property.Value = element.CloseLabel;
                    }

                    // check if OpenLabel is supported.
                    if (element.OpenLabel != null)
                    {
                        PropertyState property = this.AddProperty<LocalizedText>(Opc.Ua.BrowseNames.FalseState, DataTypeIds.LocalizedText, ValueRanks.Scalar);
                        property.NodeId = DaModelUtils.ConstructIdForComponent(property, namespaceIndex); 
                        property.Value = element.OpenLabel;
                    }

                    break;
                }

                case DaElementType.EnumeratedItem:
                {
                    this.TypeDefinitionId = Opc.Ua.VariableTypeIds.MultiStateDiscreteType;

                    // check if EuInfo is supported.
                    if (element.EuInfo != null)
                    {
                        PropertyState property = this.AddProperty<LocalizedText[]>(Opc.Ua.BrowseNames.EnumStrings, DataTypeIds.LocalizedText, ValueRanks.OneDimension);
                        property.NodeId = DaModelUtils.ConstructIdForComponent(property, namespaceIndex); 

                        LocalizedText[] strings = new LocalizedText[element.EuInfo.Length];

                        for (int ii = 0; ii < strings.Length; ii++)
                        {
                            strings[ii] = element.EuInfo[ii];
                        }

                        property.Value = strings;
                    }

                    break;
                }
            }

            if (element.Description != null)
            {
                this.Description = element.Description;
            }

            this.Value = null;
            this.StatusCode = StatusCodes.BadWaitingForInitialData;
            this.Timestamp = DateTime.UtcNow;

            bool isArray = false;
            this.DataType = ComUtils.GetDataTypeId(element.DataType, out isArray);
            this.ValueRank = (isArray)?ValueRanks.OneOrMoreDimensions:ValueRanks.Scalar;

            this.AccessLevel = AccessLevels.None;

            if ((element.AccessRights & OpcRcw.Da.Constants.OPC_READABLE) != 0)
            {
                this.AccessLevel |= AccessLevels.CurrentRead;
            }

            if ((element.AccessRights & OpcRcw.Da.Constants.OPC_WRITEABLE) != 0)
            {
                this.AccessLevel |= AccessLevels.CurrentWrite;
            }

            this.UserAccessLevel = this.AccessLevel;
            this.MinimumSamplingInterval = element.ScanRate;
        }

        /// <summary>
        /// Creates a browser that finds the references to the branch.
        /// </summary>
        /// <param name="context">The system context to use.</param>
        /// <param name="view">The view which may restrict the set of references/nodes found.</param>
        /// <param name="referenceType">The type of references being followed.</param>
        /// <param name="includeSubtypes">Whether subtypes of the reference type are followed.</param>
        /// <param name="browseDirection">Which way the references are being followed.</param>
        /// <param name="browseName">The browse name of a specific target (used when translating browse paths).</param>
        /// <param name="additionalReferences">Any additional references that should be included.</param>
        /// <param name="internalOnly">If true the browser should not making blocking calls to external systems.</param>
        /// <returns>The browse object (must be disposed).</returns>
        public override INodeBrowser CreateBrowser(
            ISystemContext context, 
            ViewDescription view, 
            NodeId referenceType, 
            bool includeSubtypes, 
            BrowseDirection browseDirection, 
            QualifiedName browseName,
            IEnumerable<IReference> additionalReferences,
            bool internalOnly)
        {
            NodeBrowser browser = new DaElementBrowser(
                context,
                view,
                referenceType,
                includeSubtypes,
                browseDirection,
                browseName,
                additionalReferences,
                internalOnly,
                this.ItemId,
                this.NodeId.NamespaceIndex);

            PopulateBrowser(context, browser);

            return browser;
        }
        #endregion

        #region Private Fields
        private DaElement m_element;
        #endregion
    }
}
