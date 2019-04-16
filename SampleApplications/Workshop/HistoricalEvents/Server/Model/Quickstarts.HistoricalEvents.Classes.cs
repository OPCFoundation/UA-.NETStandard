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

namespace Quickstarts.HistoricalEvents
{
    #region WellTestReportState Class
    #if (!OPCUA_EXCLUDE_WellTestReportState)
    /// <summary>
    /// Stores an instance of the WellTestReportType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class WellTestReportState : BaseEventState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public WellTestReportState(NodeState parent) : base(parent)
        {
        }
        
        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.HistoricalEvents.ObjectTypes.WellTestReportType, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents, namespaceUris);
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
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString = 
           "AQAAADUAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvSGlzdG9yaWNhbEV2ZW50" +
           "c/////8kYIAAAQAAAAEAGgAAAFdlbGxUZXN0UmVwb3J0VHlwZUluc3RhbmNlAQH7AAMAAAAALwAAAEEg" +
           "cmVwb3J0IGNvbnRhaW5pbmcgdGhlIHJlc3VsdHMgb2YgYSB3ZWxsIHRlc3QuAQH7AP////8NAAAANWCJ" +
           "CgIAAAAAAAcAAABFdmVudElkAQH8AAMAAAAAKwAAAEEgZ2xvYmFsbHkgdW5pcXVlIGlkZW50aWZpZXIg" +
           "Zm9yIHRoZSBldmVudC4ALgBE/AAAAAAP/////wEB/////wAAAAA1YIkKAgAAAAAACQAAAEV2ZW50VHlw" +
           "ZQEB/QADAAAAACIAAABUaGUgaWRlbnRpZmllciBmb3IgdGhlIGV2ZW50IHR5cGUuAC4ARP0AAAAAEf//" +
           "//8BAf////8AAAAANWCJCgIAAAAAAAoAAABTb3VyY2VOb2RlAQH+AAMAAAAAGAAAAFRoZSBzb3VyY2Ug" +
           "b2YgdGhlIGV2ZW50LgAuAET+AAAAABH/////AQH/////AAAAADVgiQoCAAAAAAAKAAAAU291cmNlTmFt" +
           "ZQEB/wADAAAAACkAAABBIGRlc2NyaXB0aW9uIG9mIHRoZSBzb3VyY2Ugb2YgdGhlIGV2ZW50LgAuAET/" +
           "AAAAAAz/////AQH/////AAAAADVgiQoCAAAAAAAEAAAAVGltZQEBAAEDAAAAABgAAABXaGVuIHRoZSBl" +
           "dmVudCBvY2N1cnJlZC4ALgBEAAEAAAEAJgH/////AQH/////AAAAADVgiQoCAAAAAAALAAAAUmVjZWl2" +
           "ZVRpbWUBAQEBAwAAAAA+AAAAV2hlbiB0aGUgc2VydmVyIHJlY2VpdmVkIHRoZSBldmVudCBmcm9tIHRo" +
           "ZSB1bmRlcmx5aW5nIHN5c3RlbS4ALgBEAQEAAAEAJgH/////AQH/////AAAAADVgiQoCAAAAAAAJAAAA" +
           "TG9jYWxUaW1lAQECAQMAAAAAPAAAAEluZm9ybWF0aW9uIGFib3V0IHRoZSBsb2NhbCB0aW1lIHdoZXJl" +
           "IHRoZSBldmVudCBvcmlnaW5hdGVkLgAuAEQCAQAAAQDQIv////8BAf////8AAAAANWCJCgIAAAAAAAcA" +
           "AABNZXNzYWdlAQEDAQMAAAAAJQAAAEEgbG9jYWxpemVkIGRlc2NyaXB0aW9uIG9mIHRoZSBldmVudC4A" +
           "LgBEAwEAAAAV/////wEB/////wAAAAA1YIkKAgAAAAAACAAAAFNldmVyaXR5AQEEAQMAAAAAIQAAAElu" +
           "ZGljYXRlcyBob3cgdXJnZW50IGFuIGV2ZW50IGlzLgAuAEQEAQAAAAX/////AQH/////AAAAADVgiQoC" +
           "AAAAAQAIAAAATmFtZVdlbGwBAQUBAwAAAABEAAAASHVtYW4gcmVjb2duaXphYmxlIGNvbnRleHQgZm9y" +
           "IHRoZSB3ZWxsIHRoYXQgY29udGFpbnMgdGhlIHdlbGwgdGVzdC4ALgBEBQEAAAAM/////wEB/////wAA" +
           "AAA1YIkKAgAAAAEABwAAAFVpZFdlbGwBAQYBAwAAAABzAAAAVW5pcXVlIGlkZW50aWZpZXIgZm9yIHRo" +
           "ZSB3ZWxsLiBUaGlzIHVuaXF1ZWx5IHJlcHJlc2VudHMgdGhlIHdlbGwgcmVmZXJlbmNlZCBieSB0aGUg" +
           "KHBvc3NpYmx5IG5vbi11bmlxdWUpIE5hbWVXZWxsLgAuAEQGAQAAAAz/////AQH/////AAAAADVgiQoC" +
           "AAAAAQAIAAAAVGVzdERhdGUBAQcBAwAAAAAbAAAAVGhlIGRhdGUtdGltZSBvZiB3ZWxsIHRlc3QuAC4A" +
           "RAcBAAAADf////8BAf////8AAAAANWCJCgIAAAABAAoAAABUZXN0UmVhc29uAQEIAQMAAAAAOgAAAFRo" +
           "ZSByZWFzb24gZm9yIHRoZSB3ZWxsIHRlc3Q6IGluaXRpYWwsIHBlcmlvZGljLCByZXZpc2lvbi4ALgBE" +
           "CAEAAAAM/////wEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// Human recognizable context for the well that contains the well test.
        /// </summary>
        public PropertyState<string> NameWell
        {
            get
            { 
                return m_nameWell;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_nameWell, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_nameWell = value;
            }
        }

        /// <summary>
        /// Unique identifier for the well. This uniquely represents the well referenced by the (possibly non-unique) NameWell.
        /// </summary>
        public PropertyState<string> UidWell
        {
            get
            { 
                return m_uidWell;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_uidWell, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uidWell = value;
            }
        }

        /// <summary>
        /// The date-time of well test.
        /// </summary>
        public PropertyState<DateTime> TestDate
        {
            get
            { 
                return m_testDate;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_testDate, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_testDate = value;
            }
        }

        /// <summary>
        /// The reason for the well test: initial, periodic, revision.
        /// </summary>
        public PropertyState<string> TestReason
        {
            get
            { 
                return m_testReason;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_testReason, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_testReason = value;
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
            if (m_nameWell != null)
            {
                children.Add(m_nameWell);
            }

            if (m_uidWell != null)
            {
                children.Add(m_uidWell);
            }

            if (m_testDate != null)
            {
                children.Add(m_testDate);
            }

            if (m_testReason != null)
            {
                children.Add(m_testReason);
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
                case Quickstarts.HistoricalEvents.BrowseNames.NameWell:
                {
                    if (createOrReplace)
                    {
                        if (NameWell == null)
                        {
                            if (replacement == null)
                            {
                                NameWell = new PropertyState<string>(this);
                            }
                            else
                            {
                                NameWell = (PropertyState<string>)replacement;
                            }
                        }
                    }

                    instance = NameWell;
                    break;
                }

                case Quickstarts.HistoricalEvents.BrowseNames.UidWell:
                {
                    if (createOrReplace)
                    {
                        if (UidWell == null)
                        {
                            if (replacement == null)
                            {
                                UidWell = new PropertyState<string>(this);
                            }
                            else
                            {
                                UidWell = (PropertyState<string>)replacement;
                            }
                        }
                    }

                    instance = UidWell;
                    break;
                }

                case Quickstarts.HistoricalEvents.BrowseNames.TestDate:
                {
                    if (createOrReplace)
                    {
                        if (TestDate == null)
                        {
                            if (replacement == null)
                            {
                                TestDate = new PropertyState<DateTime>(this);
                            }
                            else
                            {
                                TestDate = (PropertyState<DateTime>)replacement;
                            }
                        }
                    }

                    instance = TestDate;
                    break;
                }

                case Quickstarts.HistoricalEvents.BrowseNames.TestReason:
                {
                    if (createOrReplace)
                    {
                        if (TestReason == null)
                        {
                            if (replacement == null)
                            {
                                TestReason = new PropertyState<string>(this);
                            }
                            else
                            {
                                TestReason = (PropertyState<string>)replacement;
                            }
                        }
                    }

                    instance = TestReason;
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
        private PropertyState<string> m_nameWell;
        private PropertyState<string> m_uidWell;
        private PropertyState<DateTime> m_testDate;
        private PropertyState<string> m_testReason;
        #endregion
    }
    #endif
    #endregion

    #region FluidLevelTestReportState Class
    #if (!OPCUA_EXCLUDE_FluidLevelTestReportState)
    /// <summary>
    /// Stores an instance of the FluidLevelTestReportType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class FluidLevelTestReportState : WellTestReportState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public FluidLevelTestReportState(NodeState parent) : base(parent)
        {
        }
        
        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.HistoricalEvents.ObjectTypes.FluidLevelTestReportType, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents, namespaceUris);
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
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString = 
           "AQAAADUAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvSGlzdG9yaWNhbEV2ZW50" +
           "c/////8kYIAAAQAAAAEAIAAAAEZsdWlkTGV2ZWxUZXN0UmVwb3J0VHlwZUluc3RhbmNlAQEJAQMAAAAA" +
           "IAAAAEEgcmVwb3J0IGZvciBhIGZsdWlkIGxldmVsIHRlc3QuAQEJAf////8PAAAANWCJCgIAAAAAAAcA" +
           "AABFdmVudElkAQEKAQMAAAAAKwAAAEEgZ2xvYmFsbHkgdW5pcXVlIGlkZW50aWZpZXIgZm9yIHRoZSBl" +
           "dmVudC4ALgBECgEAAAAP/////wEB/////wAAAAA1YIkKAgAAAAAACQAAAEV2ZW50VHlwZQEBCwEDAAAA" +
           "ACIAAABUaGUgaWRlbnRpZmllciBmb3IgdGhlIGV2ZW50IHR5cGUuAC4ARAsBAAAAEf////8BAf////8A" +
           "AAAANWCJCgIAAAAAAAoAAABTb3VyY2VOb2RlAQEMAQMAAAAAGAAAAFRoZSBzb3VyY2Ugb2YgdGhlIGV2" +
           "ZW50LgAuAEQMAQAAABH/////AQH/////AAAAADVgiQoCAAAAAAAKAAAAU291cmNlTmFtZQEBDQEDAAAA" +
           "ACkAAABBIGRlc2NyaXB0aW9uIG9mIHRoZSBzb3VyY2Ugb2YgdGhlIGV2ZW50LgAuAEQNAQAAAAz/////" +
           "AQH/////AAAAADVgiQoCAAAAAAAEAAAAVGltZQEBDgEDAAAAABgAAABXaGVuIHRoZSBldmVudCBvY2N1" +
           "cnJlZC4ALgBEDgEAAAEAJgH/////AQH/////AAAAADVgiQoCAAAAAAALAAAAUmVjZWl2ZVRpbWUBAQ8B" +
           "AwAAAAA+AAAAV2hlbiB0aGUgc2VydmVyIHJlY2VpdmVkIHRoZSBldmVudCBmcm9tIHRoZSB1bmRlcmx5" +
           "aW5nIHN5c3RlbS4ALgBEDwEAAAEAJgH/////AQH/////AAAAADVgiQoCAAAAAAAJAAAATG9jYWxUaW1l" +
           "AQEQAQMAAAAAPAAAAEluZm9ybWF0aW9uIGFib3V0IHRoZSBsb2NhbCB0aW1lIHdoZXJlIHRoZSBldmVu" +
           "dCBvcmlnaW5hdGVkLgAuAEQQAQAAAQDQIv////8BAf////8AAAAANWCJCgIAAAAAAAcAAABNZXNzYWdl" +
           "AQERAQMAAAAAJQAAAEEgbG9jYWxpemVkIGRlc2NyaXB0aW9uIG9mIHRoZSBldmVudC4ALgBEEQEAAAAV" +
           "/////wEB/////wAAAAA1YIkKAgAAAAAACAAAAFNldmVyaXR5AQESAQMAAAAAIQAAAEluZGljYXRlcyBo" +
           "b3cgdXJnZW50IGFuIGV2ZW50IGlzLgAuAEQSAQAAAAX/////AQH/////AAAAADVgiQoCAAAAAQAIAAAA" +
           "TmFtZVdlbGwBARMBAwAAAABEAAAASHVtYW4gcmVjb2duaXphYmxlIGNvbnRleHQgZm9yIHRoZSB3ZWxs" +
           "IHRoYXQgY29udGFpbnMgdGhlIHdlbGwgdGVzdC4ALgBEEwEAAAAM/////wEB/////wAAAAA1YIkKAgAA" +
           "AAEABwAAAFVpZFdlbGwBARQBAwAAAABzAAAAVW5pcXVlIGlkZW50aWZpZXIgZm9yIHRoZSB3ZWxsLiBU" +
           "aGlzIHVuaXF1ZWx5IHJlcHJlc2VudHMgdGhlIHdlbGwgcmVmZXJlbmNlZCBieSB0aGUgKHBvc3NpYmx5" +
           "IG5vbi11bmlxdWUpIE5hbWVXZWxsLgAuAEQUAQAAAAz/////AQH/////AAAAADVgiQoCAAAAAQAIAAAA" +
           "VGVzdERhdGUBARUBAwAAAAAbAAAAVGhlIGRhdGUtdGltZSBvZiB3ZWxsIHRlc3QuAC4ARBUBAAAADf//" +
           "//8BAf////8AAAAANWCJCgIAAAABAAoAAABUZXN0UmVhc29uAQEWAQMAAAAAOgAAAFRoZSByZWFzb24g" +
           "Zm9yIHRoZSB3ZWxsIHRlc3Q6IGluaXRpYWwsIHBlcmlvZGljLCByZXZpc2lvbi4ALgBEFgEAAAAM////" +
           "/wEB/////wAAAAA1YIkKAgAAAAEACgAAAEZsdWlkTGV2ZWwBARcBAwAAAABiAAAAVGhlIGZsdWlkIGxl" +
           "dmVsIGFjaGlldmVkIGluIHRoZSB3ZWxsLiBUaGUgdmFsdWUgaXMgZ2l2ZW4gYXMgbGVuZ3RoIHVuaXRz" +
           "IGZyb20gdGhlIHRvcCBvZiB0aGUgd2VsbC4ALwEAQAkXAQAAAAv/////AQH/////AQAAABVgiQoCAAAA" +
           "AAAHAAAARVVSYW5nZQEBMAEALgBEMAEAAAEAdAP/////AQH/////AAAAADVgiQoCAAAAAQAIAAAAVGVz" +
           "dGVkQnkBARsBAwAAAABLAAAAVGhlIGJ1c2luZXNzIGFzc29jaWF0ZSB0aGF0IGNvbmR1Y3RlZCB0aGUg" +
           "dGVzdC4gVGhpcyBpcyBnZW5lcmFsbHkgYSBwZXJzb24uAC4ARBsBAAAADP////8BAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// The fluid level achieved in the well. The value is given as length units from the top of the well.
        /// </summary>
        public AnalogItemState<double> FluidLevel
        {
            get
            { 
                return m_fluidLevel;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_fluidLevel, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_fluidLevel = value;
            }
        }

        /// <summary>
        /// The business associate that conducted the test. This is generally a person.
        /// </summary>
        public PropertyState<string> TestedBy
        {
            get
            { 
                return m_testedBy;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_testedBy, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_testedBy = value;
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
            if (m_fluidLevel != null)
            {
                children.Add(m_fluidLevel);
            }

            if (m_testedBy != null)
            {
                children.Add(m_testedBy);
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
                case Quickstarts.HistoricalEvents.BrowseNames.FluidLevel:
                {
                    if (createOrReplace)
                    {
                        if (FluidLevel == null)
                        {
                            if (replacement == null)
                            {
                                FluidLevel = new AnalogItemState<double>(this);
                            }
                            else
                            {
                                FluidLevel = (AnalogItemState<double>)replacement;
                            }
                        }
                    }

                    instance = FluidLevel;
                    break;
                }

                case Quickstarts.HistoricalEvents.BrowseNames.TestedBy:
                {
                    if (createOrReplace)
                    {
                        if (TestedBy == null)
                        {
                            if (replacement == null)
                            {
                                TestedBy = new PropertyState<string>(this);
                            }
                            else
                            {
                                TestedBy = (PropertyState<string>)replacement;
                            }
                        }
                    }

                    instance = TestedBy;
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
        private AnalogItemState<double> m_fluidLevel;
        private PropertyState<string> m_testedBy;
        #endregion
    }
    #endif
    #endregion

    #region InjectionTestReportState Class
    #if (!OPCUA_EXCLUDE_InjectionTestReportState)
    /// <summary>
    /// Stores an instance of the InjectionTestReportType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class InjectionTestReportState : WellTestReportState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public InjectionTestReportState(NodeState parent) : base(parent)
        {
        }
        
        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.HistoricalEvents.ObjectTypes.InjectionTestReportType, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents, namespaceUris);
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
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString = 
           "AQAAADUAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvSGlzdG9yaWNhbEV2ZW50" +
           "c/////8kYIAAAQAAAAEAHwAAAEluamVjdGlvblRlc3RSZXBvcnRUeXBlSW5zdGFuY2UBARwBAwAAAAAg" +
           "AAAAQSByZXBvcnQgZm9yIGEgZmx1aWQgbGV2ZWwgdGVzdC4BARwB/////w8AAAA1YIkKAgAAAAAABwAA" +
           "AEV2ZW50SWQBAR0BAwAAAAArAAAAQSBnbG9iYWxseSB1bmlxdWUgaWRlbnRpZmllciBmb3IgdGhlIGV2" +
           "ZW50LgAuAEQdAQAAAA//////AQH/////AAAAADVgiQoCAAAAAAAJAAAARXZlbnRUeXBlAQEeAQMAAAAA" +
           "IgAAAFRoZSBpZGVudGlmaWVyIGZvciB0aGUgZXZlbnQgdHlwZS4ALgBEHgEAAAAR/////wEB/////wAA" +
           "AAA1YIkKAgAAAAAACgAAAFNvdXJjZU5vZGUBAR8BAwAAAAAYAAAAVGhlIHNvdXJjZSBvZiB0aGUgZXZl" +
           "bnQuAC4ARB8BAAAAEf////8BAf////8AAAAANWCJCgIAAAAAAAoAAABTb3VyY2VOYW1lAQEgAQMAAAAA" +
           "KQAAAEEgZGVzY3JpcHRpb24gb2YgdGhlIHNvdXJjZSBvZiB0aGUgZXZlbnQuAC4ARCABAAAADP////8B" +
           "Af////8AAAAANWCJCgIAAAAAAAQAAABUaW1lAQEhAQMAAAAAGAAAAFdoZW4gdGhlIGV2ZW50IG9jY3Vy" +
           "cmVkLgAuAEQhAQAAAQAmAf////8BAf////8AAAAANWCJCgIAAAAAAAsAAABSZWNlaXZlVGltZQEBIgED" +
           "AAAAAD4AAABXaGVuIHRoZSBzZXJ2ZXIgcmVjZWl2ZWQgdGhlIGV2ZW50IGZyb20gdGhlIHVuZGVybHlp" +
           "bmcgc3lzdGVtLgAuAEQiAQAAAQAmAf////8BAf////8AAAAANWCJCgIAAAAAAAkAAABMb2NhbFRpbWUB" +
           "ASMBAwAAAAA8AAAASW5mb3JtYXRpb24gYWJvdXQgdGhlIGxvY2FsIHRpbWUgd2hlcmUgdGhlIGV2ZW50" +
           "IG9yaWdpbmF0ZWQuAC4ARCMBAAABANAi/////wEB/////wAAAAA1YIkKAgAAAAAABwAAAE1lc3NhZ2UB" +
           "ASQBAwAAAAAlAAAAQSBsb2NhbGl6ZWQgZGVzY3JpcHRpb24gb2YgdGhlIGV2ZW50LgAuAEQkAQAAABX/" +
           "////AQH/////AAAAADVgiQoCAAAAAAAIAAAAU2V2ZXJpdHkBASUBAwAAAAAhAAAASW5kaWNhdGVzIGhv" +
           "dyB1cmdlbnQgYW4gZXZlbnQgaXMuAC4ARCUBAAAABf////8BAf////8AAAAANWCJCgIAAAABAAgAAABO" +
           "YW1lV2VsbAEBJgEDAAAAAEQAAABIdW1hbiByZWNvZ25pemFibGUgY29udGV4dCBmb3IgdGhlIHdlbGwg" +
           "dGhhdCBjb250YWlucyB0aGUgd2VsbCB0ZXN0LgAuAEQmAQAAAAz/////AQH/////AAAAADVgiQoCAAAA" +
           "AQAHAAAAVWlkV2VsbAEBJwEDAAAAAHMAAABVbmlxdWUgaWRlbnRpZmllciBmb3IgdGhlIHdlbGwuIFRo" +
           "aXMgdW5pcXVlbHkgcmVwcmVzZW50cyB0aGUgd2VsbCByZWZlcmVuY2VkIGJ5IHRoZSAocG9zc2libHkg" +
           "bm9uLXVuaXF1ZSkgTmFtZVdlbGwuAC4ARCcBAAAADP////8BAf////8AAAAANWCJCgIAAAABAAgAAABU" +
           "ZXN0RGF0ZQEBKAEDAAAAABsAAABUaGUgZGF0ZS10aW1lIG9mIHdlbGwgdGVzdC4ALgBEKAEAAAAN////" +
           "/wEB/////wAAAAA1YIkKAgAAAAEACgAAAFRlc3RSZWFzb24BASkBAwAAAAA6AAAAVGhlIHJlYXNvbiBm" +
           "b3IgdGhlIHdlbGwgdGVzdDogaW5pdGlhbCwgcGVyaW9kaWMsIHJldmlzaW9uLgAuAEQpAQAAAAz/////" +
           "AQH/////AAAAADVgiQoCAAAAAQAMAAAAVGVzdER1cmF0aW9uAQEqAQMAAAAALAAAAFRoZSB0aW1lIGxl" +
           "bmd0aCAod2l0aCB1b20pIG9mIHRoZSB3ZWxsIHRlc3QuAC8BAEAJKgEAAAAL/////wEB/////wEAAAAV" +
           "YIkKAgAAAAAABwAAAEVVUmFuZ2UBATIBAC4ARDIBAAABAHQD/////wEB/////wAAAAA1YIkKAgAAAAEA" +
           "DQAAAEluamVjdGVkRmx1aWQBAS4BAwAAAAAjAAAAVGhlIGZsdWlkIHRoYXQgaXMgYmVpbmcgaW5qZWN0" +
           "ZWQuIC4ALgBELgEAAAAM/////wEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// The time length (with uom) of the well test.
        /// </summary>
        public AnalogItemState<double> TestDuration
        {
            get
            { 
                return m_testDuration;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_testDuration, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_testDuration = value;
            }
        }

        /// <summary>
        /// The fluid that is being injected. .
        /// </summary>
        public PropertyState<string> InjectedFluid
        {
            get
            { 
                return m_injectedFluid;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_injectedFluid, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_injectedFluid = value;
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
            if (m_testDuration != null)
            {
                children.Add(m_testDuration);
            }

            if (m_injectedFluid != null)
            {
                children.Add(m_injectedFluid);
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
                case Quickstarts.HistoricalEvents.BrowseNames.TestDuration:
                {
                    if (createOrReplace)
                    {
                        if (TestDuration == null)
                        {
                            if (replacement == null)
                            {
                                TestDuration = new AnalogItemState<double>(this);
                            }
                            else
                            {
                                TestDuration = (AnalogItemState<double>)replacement;
                            }
                        }
                    }

                    instance = TestDuration;
                    break;
                }

                case Quickstarts.HistoricalEvents.BrowseNames.InjectedFluid:
                {
                    if (createOrReplace)
                    {
                        if (InjectedFluid == null)
                        {
                            if (replacement == null)
                            {
                                InjectedFluid = new PropertyState<string>(this);
                            }
                            else
                            {
                                InjectedFluid = (PropertyState<string>)replacement;
                            }
                        }
                    }

                    instance = InjectedFluid;
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
        private AnalogItemState<double> m_testDuration;
        private PropertyState<string> m_injectedFluid;
        #endregion
    }
    #endif
    #endregion

    #region WellState Class
    #if (!OPCUA_EXCLUDE_WellState)
    /// <summary>
    /// Stores an instance of the WellType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class WellState : BaseObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public WellState(NodeState parent) : base(parent)
        {
        }
        
        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.HistoricalEvents.ObjectTypes.WellType, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents, namespaceUris);
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
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString = 
           "AQAAADUAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvSGlzdG9yaWNhbEV2ZW50" +
           "c/////8kYIAAAQAAAAEAEAAAAFdlbGxUeXBlSW5zdGFuY2UBATQBAwAAAAAQAAAAQSBwaHlzaWNhbCB3" +
           "ZWxsLgEBNAH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        #endregion

        #region Private Fields
        #endregion
    }
    #endif
    #endregion
}
