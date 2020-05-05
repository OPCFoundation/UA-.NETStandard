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
           "AQAAADUAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvSGlzdG9yaWNhbEV2ZW50" +
           "c/////8EYIACAQAAAAEAGgAAAFdlbGxUZXN0UmVwb3J0VHlwZUluc3RhbmNlAQH7AAEB+wD7AAAA////" +
           "/wwAAAAVYIkKAgAAAAAABwAAAEV2ZW50SWQBAfwAAC4ARPwAAAAAD/////8BAf////8AAAAAFWCJCgIA" +
           "AAAAAAkAAABFdmVudFR5cGUBAf0AAC4ARP0AAAAAEf////8BAf////8AAAAAFWCJCgIAAAAAAAoAAABT" +
           "b3VyY2VOb2RlAQH+AAAuAET+AAAAABH/////AQH/////AAAAABVgiQoCAAAAAAAKAAAAU291cmNlTmFt" +
           "ZQEB/wAALgBE/wAAAAAM/////wEB/////wAAAAAVYIkKAgAAAAAABAAAAFRpbWUBAQABAC4ARAABAAAB" +
           "ACYB/////wEB/////wAAAAAVYIkKAgAAAAAACwAAAFJlY2VpdmVUaW1lAQEBAQAuAEQBAQAAAQAmAf//" +
           "//8BAf////8AAAAAFWCJCgIAAAAAAAcAAABNZXNzYWdlAQEDAQAuAEQDAQAAABX/////AQH/////AAAA" +
           "ABVgiQoCAAAAAAAIAAAAU2V2ZXJpdHkBAQQBAC4ARAQBAAAABf////8BAf////8AAAAANWCJCgIAAAAB" +
           "AAgAAABOYW1lV2VsbAEBBQEDAAAAAEQAAABIdW1hbiByZWNvZ25pemFibGUgY29udGV4dCBmb3IgdGhl" +
           "IHdlbGwgdGhhdCBjb250YWlucyB0aGUgd2VsbCB0ZXN0LgAuAEQFAQAAAAz/////AQH/////AAAAADVg" +
           "iQoCAAAAAQAHAAAAVWlkV2VsbAEBBgEDAAAAAHMAAABVbmlxdWUgaWRlbnRpZmllciBmb3IgdGhlIHdl" +
           "bGwuIFRoaXMgdW5pcXVlbHkgcmVwcmVzZW50cyB0aGUgd2VsbCByZWZlcmVuY2VkIGJ5IHRoZSAocG9z" +
           "c2libHkgbm9uLXVuaXF1ZSkgTmFtZVdlbGwuAC4ARAYBAAAADP////8BAf////8AAAAANWCJCgIAAAAB" +
           "AAgAAABUZXN0RGF0ZQEBBwEDAAAAABsAAABUaGUgZGF0ZS10aW1lIG9mIHdlbGwgdGVzdC4ALgBEBwEA" +
           "AAAN/////wEB/////wAAAAA1YIkKAgAAAAEACgAAAFRlc3RSZWFzb24BAQgBAwAAAAA6AAAAVGhlIHJl" +
           "YXNvbiBmb3IgdGhlIHdlbGwgdGVzdDogaW5pdGlhbCwgcGVyaW9kaWMsIHJldmlzaW9uLgAuAEQIAQAA" +
           "AAz/////AQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
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

        /// <remarks />
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

        /// <remarks />
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

        /// <remarks />
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
           "AQAAADUAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvSGlzdG9yaWNhbEV2ZW50" +
           "c/////8EYIACAQAAAAEAIAAAAEZsdWlkTGV2ZWxUZXN0UmVwb3J0VHlwZUluc3RhbmNlAQEJAQEBCQEJ" +
           "AQAA/////w4AAAAVYIkKAgAAAAAABwAAAEV2ZW50SWQBAQoBAC4ARAoBAAAAD/////8BAf////8AAAAA" +
           "FWCJCgIAAAAAAAkAAABFdmVudFR5cGUBAQsBAC4ARAsBAAAAEf////8BAf////8AAAAAFWCJCgIAAAAA" +
           "AAoAAABTb3VyY2VOb2RlAQEMAQAuAEQMAQAAABH/////AQH/////AAAAABVgiQoCAAAAAAAKAAAAU291" +
           "cmNlTmFtZQEBDQEALgBEDQEAAAAM/////wEB/////wAAAAAVYIkKAgAAAAAABAAAAFRpbWUBAQ4BAC4A" +
           "RA4BAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAACwAAAFJlY2VpdmVUaW1lAQEPAQAuAEQPAQAA" +
           "AQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABNZXNzYWdlAQERAQAuAEQRAQAAABX/////AQH/" +
           "////AAAAABVgiQoCAAAAAAAIAAAAU2V2ZXJpdHkBARIBAC4ARBIBAAAABf////8BAf////8AAAAANWCJ" +
           "CgIAAAABAAgAAABOYW1lV2VsbAEBEwEDAAAAAEQAAABIdW1hbiByZWNvZ25pemFibGUgY29udGV4dCBm" +
           "b3IgdGhlIHdlbGwgdGhhdCBjb250YWlucyB0aGUgd2VsbCB0ZXN0LgAuAEQTAQAAAAz/////AQH/////" +
           "AAAAADVgiQoCAAAAAQAHAAAAVWlkV2VsbAEBFAEDAAAAAHMAAABVbmlxdWUgaWRlbnRpZmllciBmb3Ig" +
           "dGhlIHdlbGwuIFRoaXMgdW5pcXVlbHkgcmVwcmVzZW50cyB0aGUgd2VsbCByZWZlcmVuY2VkIGJ5IHRo" +
           "ZSAocG9zc2libHkgbm9uLXVuaXF1ZSkgTmFtZVdlbGwuAC4ARBQBAAAADP////8BAf////8AAAAANWCJ" +
           "CgIAAAABAAgAAABUZXN0RGF0ZQEBFQEDAAAAABsAAABUaGUgZGF0ZS10aW1lIG9mIHdlbGwgdGVzdC4A" +
           "LgBEFQEAAAAN/////wEB/////wAAAAA1YIkKAgAAAAEACgAAAFRlc3RSZWFzb24BARYBAwAAAAA6AAAA" +
           "VGhlIHJlYXNvbiBmb3IgdGhlIHdlbGwgdGVzdDogaW5pdGlhbCwgcGVyaW9kaWMsIHJldmlzaW9uLgAu" +
           "AEQWAQAAAAz/////AQH/////AAAAADVgiQoCAAAAAQAKAAAARmx1aWRMZXZlbAEBFwEDAAAAAGIAAABU" +
           "aGUgZmx1aWQgbGV2ZWwgYWNoaWV2ZWQgaW4gdGhlIHdlbGwuIFRoZSB2YWx1ZSBpcyBnaXZlbiBhcyBs" +
           "ZW5ndGggdW5pdHMgZnJvbSB0aGUgdG9wIG9mIHRoZSB3ZWxsLgAvAQBACRcBAAAAC/////8BAf////8C" +
           "AAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQEwAQAuAEQwAQAAAQB0A/////8BAf////8AAAAAFWCJCgIA" +
           "AAAAABAAAABFbmdpbmVlcmluZ1VuaXRzAQEaAQAuAEQaAQAAAQB3A/////8BAf////8AAAAANWCJCgIA" +
           "AAABAAgAAABUZXN0ZWRCeQEBGwEDAAAAAEsAAABUaGUgYnVzaW5lc3MgYXNzb2NpYXRlIHRoYXQgY29u" +
           "ZHVjdGVkIHRoZSB0ZXN0LiBUaGlzIGlzIGdlbmVyYWxseSBhIHBlcnNvbi4ALgBEGwEAAAAM/////wEB" +
           "/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
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

        /// <remarks />
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
           "AQAAADUAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvSGlzdG9yaWNhbEV2ZW50" +
           "c/////8EYIACAQAAAAEAHwAAAEluamVjdGlvblRlc3RSZXBvcnRUeXBlSW5zdGFuY2UBARwBAQEcARwB" +
           "AAD/////DgAAABVgiQoCAAAAAAAHAAAARXZlbnRJZAEBHQEALgBEHQEAAAAP/////wEB/////wAAAAAV" +
           "YIkKAgAAAAAACQAAAEV2ZW50VHlwZQEBHgEALgBEHgEAAAAR/////wEB/////wAAAAAVYIkKAgAAAAAA" +
           "CgAAAFNvdXJjZU5vZGUBAR8BAC4ARB8BAAAAEf////8BAf////8AAAAAFWCJCgIAAAAAAAoAAABTb3Vy" +
           "Y2VOYW1lAQEgAQAuAEQgAQAAAAz/////AQH/////AAAAABVgiQoCAAAAAAAEAAAAVGltZQEBIQEALgBE" +
           "IQEAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAALAAAAUmVjZWl2ZVRpbWUBASIBAC4ARCIBAAAB" +
           "ACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAE1lc3NhZ2UBASQBAC4ARCQBAAAAFf////8BAf//" +
           "//8AAAAAFWCJCgIAAAAAAAgAAABTZXZlcml0eQEBJQEALgBEJQEAAAAF/////wEB/////wAAAAA1YIkK" +
           "AgAAAAEACAAAAE5hbWVXZWxsAQEmAQMAAAAARAAAAEh1bWFuIHJlY29nbml6YWJsZSBjb250ZXh0IGZv" +
           "ciB0aGUgd2VsbCB0aGF0IGNvbnRhaW5zIHRoZSB3ZWxsIHRlc3QuAC4ARCYBAAAADP////8BAf////8A" +
           "AAAANWCJCgIAAAABAAcAAABVaWRXZWxsAQEnAQMAAAAAcwAAAFVuaXF1ZSBpZGVudGlmaWVyIGZvciB0" +
           "aGUgd2VsbC4gVGhpcyB1bmlxdWVseSByZXByZXNlbnRzIHRoZSB3ZWxsIHJlZmVyZW5jZWQgYnkgdGhl" +
           "IChwb3NzaWJseSBub24tdW5pcXVlKSBOYW1lV2VsbC4ALgBEJwEAAAAM/////wEB/////wAAAAA1YIkK" +
           "AgAAAAEACAAAAFRlc3REYXRlAQEoAQMAAAAAGwAAAFRoZSBkYXRlLXRpbWUgb2Ygd2VsbCB0ZXN0LgAu" +
           "AEQoAQAAAA3/////AQH/////AAAAADVgiQoCAAAAAQAKAAAAVGVzdFJlYXNvbgEBKQEDAAAAADoAAABU" +
           "aGUgcmVhc29uIGZvciB0aGUgd2VsbCB0ZXN0OiBpbml0aWFsLCBwZXJpb2RpYywgcmV2aXNpb24uAC4A" +
           "RCkBAAAADP////8BAf////8AAAAANWCJCgIAAAABAAwAAABUZXN0RHVyYXRpb24BASoBAwAAAAAsAAAA" +
           "VGhlIHRpbWUgbGVuZ3RoICh3aXRoIHVvbSkgb2YgdGhlIHdlbGwgdGVzdC4ALwEAQAkqAQAAAAv/////" +
           "AQH/////AgAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBMgEALgBEMgEAAAEAdAP/////AQH/////AAAA" +
           "ABVgiQoCAAAAAAAQAAAARW5naW5lZXJpbmdVbml0cwEBLQEALgBELQEAAAEAdwP/////AQH/////AAAA" +
           "ADVgiQoCAAAAAQANAAAASW5qZWN0ZWRGbHVpZAEBLgEDAAAAACMAAABUaGUgZmx1aWQgdGhhdCBpcyBi" +
           "ZWluZyBpbmplY3RlZC4gLgAuAEQuAQAAAAz/////AQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
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

        /// <remarks />
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
           "AQAAADUAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvSGlzdG9yaWNhbEV2ZW50" +
           "c/////8EYIACAQAAAAEAEAAAAFdlbGxUeXBlSW5zdGFuY2UBATQBAQE0ATQBAAD/////AAAAAA==";
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