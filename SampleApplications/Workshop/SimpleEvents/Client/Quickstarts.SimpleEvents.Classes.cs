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
    #region SystemCycleStatusEventState Class
    #if (!OPCUA_EXCLUDE_SystemCycleStatusEventState)
    /// <summary>
    /// Stores an instance of the SystemCycleStatusEventType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class SystemCycleStatusEventState : SystemEventState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public SystemCycleStatusEventState(NodeState parent) : base(parent)
        {
        }
        
        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.SimpleEvents.ObjectTypes.SystemCycleStatusEventType, Quickstarts.SimpleEvents.Namespaces.SimpleEvents, namespaceUris);
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
           "AQAAADEAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvU2ltcGxlRXZlbnRz////" +
           "/yRggAABAAAAAQAiAAAAU3lzdGVtQ3ljbGVTdGF0dXNFdmVudFR5cGVJbnN0YW5jZQEB6wADAAAAACsA" +
           "AABBbiBldmVudCByYWlzZWQgd2hlbiBhIHN5c3RlbSBjeWNsZSBzdGFydHMuAQHrAP////8LAAAANWCJ" +
           "CgIAAAAAAAcAAABFdmVudElkAQHsAAMAAAAAKwAAAEEgZ2xvYmFsbHkgdW5pcXVlIGlkZW50aWZpZXIg" +
           "Zm9yIHRoZSBldmVudC4ALgBE7AAAAAAP/////wEB/////wAAAAA1YIkKAgAAAAAACQAAAEV2ZW50VHlw" +
           "ZQEB7QADAAAAACIAAABUaGUgaWRlbnRpZmllciBmb3IgdGhlIGV2ZW50IHR5cGUuAC4ARO0AAAAAEf//" +
           "//8BAf////8AAAAANWCJCgIAAAAAAAoAAABTb3VyY2VOb2RlAQHuAAMAAAAAGAAAAFRoZSBzb3VyY2Ug" +
           "b2YgdGhlIGV2ZW50LgAuAETuAAAAABH/////AQH/////AAAAADVgiQoCAAAAAAAKAAAAU291cmNlTmFt" +
           "ZQEB7wADAAAAACkAAABBIGRlc2NyaXB0aW9uIG9mIHRoZSBzb3VyY2Ugb2YgdGhlIGV2ZW50LgAuAETv" +
           "AAAAAAz/////AQH/////AAAAADVgiQoCAAAAAAAEAAAAVGltZQEB8AADAAAAABgAAABXaGVuIHRoZSBl" +
           "dmVudCBvY2N1cnJlZC4ALgBE8AAAAAEAJgH/////AQH/////AAAAADVgiQoCAAAAAAALAAAAUmVjZWl2" +
           "ZVRpbWUBAfEAAwAAAAA+AAAAV2hlbiB0aGUgc2VydmVyIHJlY2VpdmVkIHRoZSBldmVudCBmcm9tIHRo" +
           "ZSB1bmRlcmx5aW5nIHN5c3RlbS4ALgBE8QAAAAEAJgH/////AQH/////AAAAADVgiQoCAAAAAAAJAAAA" +
           "TG9jYWxUaW1lAQHyAAMAAAAAPAAAAEluZm9ybWF0aW9uIGFib3V0IHRoZSBsb2NhbCB0aW1lIHdoZXJl" +
           "IHRoZSBldmVudCBvcmlnaW5hdGVkLgAuAETyAAAAAQDQIv////8BAf////8AAAAANWCJCgIAAAAAAAcA" +
           "AABNZXNzYWdlAQHzAAMAAAAAJQAAAEEgbG9jYWxpemVkIGRlc2NyaXB0aW9uIG9mIHRoZSBldmVudC4A" +
           "LgBE8wAAAAAV/////wEB/////wAAAAA1YIkKAgAAAAAACAAAAFNldmVyaXR5AQH0AAMAAAAAIQAAAElu" +
           "ZGljYXRlcyBob3cgdXJnZW50IGFuIGV2ZW50IGlzLgAuAET0AAAAAAX/////AQH/////AAAAABVgiQoC" +
           "AAAAAQAHAAAAQ3ljbGVJZAEB9QAALgBE9QAAAAAM/////wEB/////wAAAAAVYIkKAgAAAAEACwAAAEN1" +
           "cnJlbnRTdGVwAQH2AAAuAET2AAAAAQG3AP////8BAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the CycleId Property.
        /// </summary>
        public PropertyState<string> CycleId
        {
            get
            { 
                return m_cycleId;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_cycleId, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_cycleId = value;
            }
        }

        /// <summary>
        /// A description for the CurrentStep Property.
        /// </summary>
        public PropertyState<CycleStepDataType> CurrentStep
        {
            get
            { 
                return m_currentStep;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_currentStep, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_currentStep = value;
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
            if (m_cycleId != null)
            {
                children.Add(m_cycleId);
            }

            if (m_currentStep != null)
            {
                children.Add(m_currentStep);
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
                case Quickstarts.SimpleEvents.BrowseNames.CycleId:
                {
                    if (createOrReplace)
                    {
                        if (CycleId == null)
                        {
                            if (replacement == null)
                            {
                                CycleId = new PropertyState<string>(this);
                            }
                            else
                            {
                                CycleId = (PropertyState<string>)replacement;
                            }
                        }
                    }

                    instance = CycleId;
                    break;
                }

                case Quickstarts.SimpleEvents.BrowseNames.CurrentStep:
                {
                    if (createOrReplace)
                    {
                        if (CurrentStep == null)
                        {
                            if (replacement == null)
                            {
                                CurrentStep = new PropertyState<CycleStepDataType>(this);
                            }
                            else
                            {
                                CurrentStep = (PropertyState<CycleStepDataType>)replacement;
                            }
                        }
                    }

                    instance = CurrentStep;
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
        private PropertyState<string> m_cycleId;
        private PropertyState<CycleStepDataType> m_currentStep;
        #endregion
    }
    #endif
    #endregion

    #region SystemCycleStartedEventState Class
    #if (!OPCUA_EXCLUDE_SystemCycleStartedEventState)
    /// <summary>
    /// Stores an instance of the SystemCycleStartedEventType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class SystemCycleStartedEventState : SystemCycleStatusEventState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public SystemCycleStartedEventState(NodeState parent) : base(parent)
        {
        }
        
        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.SimpleEvents.ObjectTypes.SystemCycleStartedEventType, Quickstarts.SimpleEvents.Namespaces.SimpleEvents, namespaceUris);
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
           "AQAAADEAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvU2ltcGxlRXZlbnRz////" +
           "/yRggAABAAAAAQAjAAAAU3lzdGVtQ3ljbGVTdGFydGVkRXZlbnRUeXBlSW5zdGFuY2UBAbgAAwAAAAAr" +
           "AAAAQW4gZXZlbnQgcmFpc2VkIHdoZW4gYSBzeXN0ZW0gY3ljbGUgc3RhcnRzLgEBuAD/////DAAAADVg" +
           "iQoCAAAAAAAHAAAARXZlbnRJZAEBuQADAAAAACsAAABBIGdsb2JhbGx5IHVuaXF1ZSBpZGVudGlmaWVy" +
           "IGZvciB0aGUgZXZlbnQuAC4ARLkAAAAAD/////8BAf////8AAAAANWCJCgIAAAAAAAkAAABFdmVudFR5" +
           "cGUBAboAAwAAAAAiAAAAVGhlIGlkZW50aWZpZXIgZm9yIHRoZSBldmVudCB0eXBlLgAuAES6AAAAABH/" +
           "////AQH/////AAAAADVgiQoCAAAAAAAKAAAAU291cmNlTm9kZQEBuwADAAAAABgAAABUaGUgc291cmNl" +
           "IG9mIHRoZSBldmVudC4ALgBEuwAAAAAR/////wEB/////wAAAAA1YIkKAgAAAAAACgAAAFNvdXJjZU5h" +
           "bWUBAbwAAwAAAAApAAAAQSBkZXNjcmlwdGlvbiBvZiB0aGUgc291cmNlIG9mIHRoZSBldmVudC4ALgBE" +
           "vAAAAAAM/////wEB/////wAAAAA1YIkKAgAAAAAABAAAAFRpbWUBAb0AAwAAAAAYAAAAV2hlbiB0aGUg" +
           "ZXZlbnQgb2NjdXJyZWQuAC4ARL0AAAABACYB/////wEB/////wAAAAA1YIkKAgAAAAAACwAAAFJlY2Vp" +
           "dmVUaW1lAQG+AAMAAAAAPgAAAFdoZW4gdGhlIHNlcnZlciByZWNlaXZlZCB0aGUgZXZlbnQgZnJvbSB0" +
           "aGUgdW5kZXJseWluZyBzeXN0ZW0uAC4ARL4AAAABACYB/////wEB/////wAAAAA1YIkKAgAAAAAACQAA" +
           "AExvY2FsVGltZQEBvwADAAAAADwAAABJbmZvcm1hdGlvbiBhYm91dCB0aGUgbG9jYWwgdGltZSB3aGVy" +
           "ZSB0aGUgZXZlbnQgb3JpZ2luYXRlZC4ALgBEvwAAAAEA0CL/////AQH/////AAAAADVgiQoCAAAAAAAH" +
           "AAAATWVzc2FnZQEBwAADAAAAACUAAABBIGxvY2FsaXplZCBkZXNjcmlwdGlvbiBvZiB0aGUgZXZlbnQu" +
           "AC4ARMAAAAAAFf////8BAf////8AAAAANWCJCgIAAAAAAAgAAABTZXZlcml0eQEBwQADAAAAACEAAABJ" +
           "bmRpY2F0ZXMgaG93IHVyZ2VudCBhbiBldmVudCBpcy4ALgBEwQAAAAAF/////wEB/////wAAAAAVYIkK" +
           "AgAAAAEABwAAAEN5Y2xlSWQBAcIAAC4ARMIAAAAADP////8BAf////8AAAAAFWCJCgIAAAABAAsAAABD" +
           "dXJyZW50U3RlcAEB9wAALgBE9wAAAAEBtwD/////AQH/////AAAAABVgiQoCAAAAAQAFAAAAU3RlcHMB" +
           "AcQAAC4ARMQAAAABAbcAAQAAAAEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the Steps Property.
        /// </summary>
        public PropertyState<CycleStepDataType[]> Steps
        {
            get
            { 
                return m_steps;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_steps, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_steps = value;
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
            if (m_steps != null)
            {
                children.Add(m_steps);
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
                case Quickstarts.SimpleEvents.BrowseNames.Steps:
                {
                    if (createOrReplace)
                    {
                        if (Steps == null)
                        {
                            if (replacement == null)
                            {
                                Steps = new PropertyState<CycleStepDataType[]>(this);
                            }
                            else
                            {
                                Steps = (PropertyState<CycleStepDataType[]>)replacement;
                            }
                        }
                    }

                    instance = Steps;
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
        private PropertyState<CycleStepDataType[]> m_steps;
        #endregion
    }
    #endif
    #endregion

    #region SystemCycleAbortedEventState Class
    #if (!OPCUA_EXCLUDE_SystemCycleAbortedEventState)
    /// <summary>
    /// Stores an instance of the SystemCycleAbortedEventType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class SystemCycleAbortedEventState : SystemCycleStatusEventState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public SystemCycleAbortedEventState(NodeState parent) : base(parent)
        {
        }
        
        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.SimpleEvents.ObjectTypes.SystemCycleAbortedEventType, Quickstarts.SimpleEvents.Namespaces.SimpleEvents, namespaceUris);
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
           "AQAAADEAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvU2ltcGxlRXZlbnRz////" +
           "/yRggAABAAAAAQAjAAAAU3lzdGVtQ3ljbGVBYm9ydGVkRXZlbnRUeXBlSW5zdGFuY2UBAcUAAwAAAAAv" +
           "AAAAQW4gZXZlbnQgcmFpc2VkIHdoZW4gYSBzeXN0ZW0gY3ljbGUgaXMgYWJvcnRlZC4BAcUA/////wwA" +
           "AAA1YIkKAgAAAAAABwAAAEV2ZW50SWQBAcYAAwAAAAArAAAAQSBnbG9iYWxseSB1bmlxdWUgaWRlbnRp" +
           "ZmllciBmb3IgdGhlIGV2ZW50LgAuAETGAAAAAA//////AQH/////AAAAADVgiQoCAAAAAAAJAAAARXZl" +
           "bnRUeXBlAQHHAAMAAAAAIgAAAFRoZSBpZGVudGlmaWVyIGZvciB0aGUgZXZlbnQgdHlwZS4ALgBExwAA" +
           "AAAR/////wEB/////wAAAAA1YIkKAgAAAAAACgAAAFNvdXJjZU5vZGUBAcgAAwAAAAAYAAAAVGhlIHNv" +
           "dXJjZSBvZiB0aGUgZXZlbnQuAC4ARMgAAAAAEf////8BAf////8AAAAANWCJCgIAAAAAAAoAAABTb3Vy" +
           "Y2VOYW1lAQHJAAMAAAAAKQAAAEEgZGVzY3JpcHRpb24gb2YgdGhlIHNvdXJjZSBvZiB0aGUgZXZlbnQu" +
           "AC4ARMkAAAAADP////8BAf////8AAAAANWCJCgIAAAAAAAQAAABUaW1lAQHKAAMAAAAAGAAAAFdoZW4g" +
           "dGhlIGV2ZW50IG9jY3VycmVkLgAuAETKAAAAAQAmAf////8BAf////8AAAAANWCJCgIAAAAAAAsAAABS" +
           "ZWNlaXZlVGltZQEBywADAAAAAD4AAABXaGVuIHRoZSBzZXJ2ZXIgcmVjZWl2ZWQgdGhlIGV2ZW50IGZy" +
           "b20gdGhlIHVuZGVybHlpbmcgc3lzdGVtLgAuAETLAAAAAQAmAf////8BAf////8AAAAANWCJCgIAAAAA" +
           "AAkAAABMb2NhbFRpbWUBAcwAAwAAAAA8AAAASW5mb3JtYXRpb24gYWJvdXQgdGhlIGxvY2FsIHRpbWUg" +
           "d2hlcmUgdGhlIGV2ZW50IG9yaWdpbmF0ZWQuAC4ARMwAAAABANAi/////wEB/////wAAAAA1YIkKAgAA" +
           "AAAABwAAAE1lc3NhZ2UBAc0AAwAAAAAlAAAAQSBsb2NhbGl6ZWQgZGVzY3JpcHRpb24gb2YgdGhlIGV2" +
           "ZW50LgAuAETNAAAAABX/////AQH/////AAAAADVgiQoCAAAAAAAIAAAAU2V2ZXJpdHkBAc4AAwAAAAAh" +
           "AAAASW5kaWNhdGVzIGhvdyB1cmdlbnQgYW4gZXZlbnQgaXMuAC4ARM4AAAAABf////8BAf////8AAAAA" +
           "FWCJCgIAAAABAAcAAABDeWNsZUlkAQHPAAAuAETPAAAAAAz/////AQH/////AAAAABVgiQoCAAAAAQAL" +
           "AAAAQ3VycmVudFN0ZXABAfgAAC4ARPgAAAABAbcA/////wEB/////wAAAAAVYIkKAgAAAAEABQAAAEVy" +
           "cm9yAQH5AAAuAET5AAAAABP/////AQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the Error Property.
        /// </summary>
        public PropertyState<StatusCode> Error
        {
            get
            { 
                return m_error;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_error, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_error = value;
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
            if (m_error != null)
            {
                children.Add(m_error);
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
                case Quickstarts.SimpleEvents.BrowseNames.Error:
                {
                    if (createOrReplace)
                    {
                        if (Error == null)
                        {
                            if (replacement == null)
                            {
                                Error = new PropertyState<StatusCode>(this);
                            }
                            else
                            {
                                Error = (PropertyState<StatusCode>)replacement;
                            }
                        }
                    }

                    instance = Error;
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
        private PropertyState<StatusCode> m_error;
        #endregion
    }
    #endif
    #endregion

    #region SystemCycleFinishedEventState Class
    #if (!OPCUA_EXCLUDE_SystemCycleFinishedEventState)
    /// <summary>
    /// Stores an instance of the SystemCycleFinishedEventType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class SystemCycleFinishedEventState : SystemCycleStatusEventState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public SystemCycleFinishedEventState(NodeState parent) : base(parent)
        {
        }
        
        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.SimpleEvents.ObjectTypes.SystemCycleFinishedEventType, Quickstarts.SimpleEvents.Namespaces.SimpleEvents, namespaceUris);
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
           "AQAAADEAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvU2ltcGxlRXZlbnRz////" +
           "/yRggAABAAAAAQAkAAAAU3lzdGVtQ3ljbGVGaW5pc2hlZEV2ZW50VHlwZUluc3RhbmNlAQHSAAMAAAAA" +
           "LgAAAEFuIGV2ZW50IHJhaXNlZCB3aGVuIGEgc3lzdGVtIGN5Y2xlIGNvbXBsZXRlcy4BAdIA/////wsA" +
           "AAA1YIkKAgAAAAAABwAAAEV2ZW50SWQBAdMAAwAAAAArAAAAQSBnbG9iYWxseSB1bmlxdWUgaWRlbnRp" +
           "ZmllciBmb3IgdGhlIGV2ZW50LgAuAETTAAAAAA//////AQH/////AAAAADVgiQoCAAAAAAAJAAAARXZl" +
           "bnRUeXBlAQHUAAMAAAAAIgAAAFRoZSBpZGVudGlmaWVyIGZvciB0aGUgZXZlbnQgdHlwZS4ALgBE1AAA" +
           "AAAR/////wEB/////wAAAAA1YIkKAgAAAAAACgAAAFNvdXJjZU5vZGUBAdUAAwAAAAAYAAAAVGhlIHNv" +
           "dXJjZSBvZiB0aGUgZXZlbnQuAC4ARNUAAAAAEf////8BAf////8AAAAANWCJCgIAAAAAAAoAAABTb3Vy" +
           "Y2VOYW1lAQHWAAMAAAAAKQAAAEEgZGVzY3JpcHRpb24gb2YgdGhlIHNvdXJjZSBvZiB0aGUgZXZlbnQu" +
           "AC4ARNYAAAAADP////8BAf////8AAAAANWCJCgIAAAAAAAQAAABUaW1lAQHXAAMAAAAAGAAAAFdoZW4g" +
           "dGhlIGV2ZW50IG9jY3VycmVkLgAuAETXAAAAAQAmAf////8BAf////8AAAAANWCJCgIAAAAAAAsAAABS" +
           "ZWNlaXZlVGltZQEB2AADAAAAAD4AAABXaGVuIHRoZSBzZXJ2ZXIgcmVjZWl2ZWQgdGhlIGV2ZW50IGZy" +
           "b20gdGhlIHVuZGVybHlpbmcgc3lzdGVtLgAuAETYAAAAAQAmAf////8BAf////8AAAAANWCJCgIAAAAA" +
           "AAkAAABMb2NhbFRpbWUBAdkAAwAAAAA8AAAASW5mb3JtYXRpb24gYWJvdXQgdGhlIGxvY2FsIHRpbWUg" +
           "d2hlcmUgdGhlIGV2ZW50IG9yaWdpbmF0ZWQuAC4ARNkAAAABANAi/////wEB/////wAAAAA1YIkKAgAA" +
           "AAAABwAAAE1lc3NhZ2UBAdoAAwAAAAAlAAAAQSBsb2NhbGl6ZWQgZGVzY3JpcHRpb24gb2YgdGhlIGV2" +
           "ZW50LgAuAETaAAAAABX/////AQH/////AAAAADVgiQoCAAAAAAAIAAAAU2V2ZXJpdHkBAdsAAwAAAAAh" +
           "AAAASW5kaWNhdGVzIGhvdyB1cmdlbnQgYW4gZXZlbnQgaXMuAC4ARNsAAAAABf////8BAf////8AAAAA" +
           "FWCJCgIAAAABAAcAAABDeWNsZUlkAQHcAAAuAETcAAAAAAz/////AQH/////AAAAABVgiQoCAAAAAQAL" +
           "AAAAQ3VycmVudFN0ZXABAfoAAC4ARPoAAAABAbcA/////wEB/////wAAAAA=";
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
