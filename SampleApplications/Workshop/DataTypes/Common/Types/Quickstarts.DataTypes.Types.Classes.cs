/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
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
using System.Xml;
using System.Runtime.Serialization;
using Opc.Ua;

namespace Quickstarts.DataTypes.Types
{
    #region DriverState Class
    #if (!OPCUA_EXCLUDE_DriverState)
    /// <summary>
    /// Stores an instance of the DriverType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class DriverState : BaseObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public DriverState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.DataTypes.Types.ObjectTypes.DriverType, Quickstarts.DataTypes.Types.Namespaces.DataTypes, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the instance with a node.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAADcAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvUXVpY2tzdGFydHMvRGF0YVR5cGVzL1R5" +
           "cGVz/////wRggAABAAAAAQASAAAARHJpdmVyVHlwZUluc3RhbmNlAQFVAQEBVQH/////AgAAABVgqQoC" +
           "AAAAAQAOAAAAUHJpbWFyeVZlaGljbGUBAVYBAC4ARFYBAAAWAQE+AQK1AAAAPENhclR5cGUgeG1sbnM9" +
           "Imh0dHA6Ly9vcGNmb3VuZGF0aW9uLm9yZy9VQS9RdWlja3N0YXJ0cy9EYXRhVHlwZXMvVHlwZXMiPjxN" +
           "YWtlPlRveW90YTwvTWFrZT48TW9kZWw+UHJpdXM8L01vZGVsPjxFbmdpbmU+SHlicmlkXzQ8L0VuZ2lu" +
           "ZT48Tm9PZlBhc3NlbmdlcnM+NDwvTm9PZlBhc3NlbmdlcnM+PC9DYXJUeXBlPgEBOgH/////AwP/////" +
           "AAAAABVgqQoCAAAAAQANAAAAT3duZWRWZWhpY2xlcwEBWAEALgBEWAEAAJYDAAAAAQE/AQK2AAAAPFRy" +
           "dWNrVHlwZSB4bWxucz0iaHR0cDovL29wY2ZvdW5kYXRpb24ub3JnL1VBL1F1aWNrc3RhcnRzL0RhdGFU" +
           "eXBlcy9UeXBlcyI+PE1ha2U+RG9kZ2U8L01ha2U+PE1vZGVsPlJhbTwvTW9kZWw+PEVuZ2luZT5EaWVz" +
           "ZWxfMjwvRW5naW5lPjxDYXJnb0NhcGFjaXR5PjUwMDwvQ2FyZ29DYXBhY2l0eT48L1RydWNrVHlwZT4B" +
           "AT4BAgoBAAA8VmVoaWNsZVR5cGUgeHNpOnR5cGU9IkNhclR5cGUiIHhtbG5zOnhzaT0iaHR0cDovL3d3" +
           "dy53My5vcmcvMjAwMS9YTUxTY2hlbWEtaW5zdGFuY2UiIHhtbG5zPSJodHRwOi8vb3BjZm91bmRhdGlv" +
           "bi5vcmcvVUEvUXVpY2tzdGFydHMvRGF0YVR5cGVzL1R5cGVzIj48TWFrZT5Qb3JzY2hlPC9NYWtlPjxN" +
           "b2RlbD5Sb2Fkc3RlcjwvTW9kZWw+PEVuZ2luZT5QZXRyb2xfMTwvRW5naW5lPjxOb09mUGFzc2VuZ2Vy" +
           "cz4yPC9Ob09mUGFzc2VuZ2Vycz48L1ZlaGljbGVUeXBlPgEBPgECCQEAADxWZWhpY2xlVHlwZSB4c2k6" +
           "dHlwZT0iQ2FyVHlwZSIgeG1sbnM6eHNpPSJodHRwOi8vd3d3LnczLm9yZy8yMDAxL1hNTFNjaGVtYS1p" +
           "bnN0YW5jZSIgeG1sbnM9Imh0dHA6Ly9vcGNmb3VuZGF0aW9uLm9yZy9VQS9RdWlja3N0YXJ0cy9EYXRh" +
           "VHlwZXMvVHlwZXMiPjxNYWtlPlRlc2xhPC9NYWtlPjxNb2RlbD5Nb2RlbCBYPC9Nb2RlbD48RW5naW5l" +
           "PkVsZWN0cmljXzM8L0VuZ2luZT48Tm9PZlBhc3NlbmdlcnM+NDwvTm9PZlBhc3NlbmdlcnM+PC9WZWhp" +
           "Y2xlVHlwZT4BAToBAQAAAAMD/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the PrimaryVehicle Property.
        /// </summary>
        public PropertyState<VehicleType> PrimaryVehicle
        {
            get
            {
                return m_primaryVehicle;
            }

            set
            {
                if (!Object.ReferenceEquals(m_primaryVehicle, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_primaryVehicle = value;
            }
        }

        /// <summary>
        /// A description for the OwnedVehicles Property.
        /// </summary>
        public PropertyState<VehicleType[]> OwnedVehicles
        {
            get
            {
                return m_ownedVehicles;
            }

            set
            {
                if (!Object.ReferenceEquals(m_ownedVehicles, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_ownedVehicles = value;
            }
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Populates a list with the children that belong to the node.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="children">The list of children to populate.</param>
        public override void GetChildren(
            ISystemContext context,
            IList<BaseInstanceState> children)
        {
            if (m_primaryVehicle != null)
            {
                children.Add(m_primaryVehicle);
            }

            if (m_ownedVehicles != null)
            {
                children.Add(m_ownedVehicles);
            }

            base.GetChildren(context, children);
        }

        /// <summary>
        /// Finds the child with the specified browse name.
        /// </summary>
        protected override BaseInstanceState FindChild(
            ISystemContext context,
            QualifiedName browseName,
            bool createOrReplace,
            BaseInstanceState replacement)
        {
            if (QualifiedName.IsNull(browseName))
            {
                return null;
            }

            BaseInstanceState instance = null;

            switch (browseName.Name)
            {
                case Quickstarts.DataTypes.Types.BrowseNames.PrimaryVehicle:
                {
                    if (createOrReplace)
                    {
                        if (PrimaryVehicle == null)
                        {
                            if (replacement == null)
                            {
                                PrimaryVehicle = new PropertyState<VehicleType>(this);
                            }
                            else
                            {
                                PrimaryVehicle = (PropertyState<VehicleType>)replacement;
                            }
                        }
                    }

                    instance = PrimaryVehicle;
                    break;
                }

                case Quickstarts.DataTypes.Types.BrowseNames.OwnedVehicles:
                {
                    if (createOrReplace)
                    {
                        if (OwnedVehicles == null)
                        {
                            if (replacement == null)
                            {
                                OwnedVehicles = new PropertyState<VehicleType[]>(this);
                            }
                            else
                            {
                                OwnedVehicles = (PropertyState<VehicleType[]>)replacement;
                            }
                        }
                    }

                    instance = OwnedVehicles;
                    break;
                }
            }

            if (instance != null)
            {
                return instance;
            }

            return base.FindChild(context, browseName, createOrReplace, replacement);
        }
        #endregion

        #region Private Fields
        private PropertyState<VehicleType> m_primaryVehicle;
        private PropertyState<VehicleType[]> m_ownedVehicles;
        #endregion
    }
    #endif
    #endregion
}