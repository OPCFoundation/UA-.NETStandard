/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.IO;
using System.Runtime.Serialization;
using System.Reflection;
using System.Threading;

#pragma warning disable 0618

namespace Opc.Ua.Server
{       
#if LEGACY_CORENODEMANAGER
    /// <summary> 
    /// The base class for custom VariableT nodes.
    /// </summary>
    [Obsolete("The VariableSource class is obsolete and is not supported. See Opc.Ua.BaseVariableState for a replacement.")]
    public abstract class VariableSource : BaseInstanceSource, IVariable
    {
        #region Constructors
        /// <summary>
        /// Adds the source to the type table.
        /// </summary>
        protected VariableSource(
            IServerInternal server,
            NodeSource      parent)
        : 
            base(server, parent)
        {
            m_datatype = DefaultDataType;
            m_valueRank = ValueRanks.Scalar;
            m_arrayDimensions = null;
            m_accessLevel = AccessLevels.CurrentReadOrWrite;
            m_userAccessLevel = AccessLevels.CurrentReadOrWrite;
            m_minimumSamplingInterval = MinimumSamplingIntervals.Continuous;
            m_historizing = false;
            m_copyPolicy = VariableCopyPolicy.CopyOnWrite;
        }

        // minimize overhead by re-using a static object for the default value.
        private static readonly NodeId DefaultDataType = new NodeId(DataTypes.BaseDataType);
        #endregion

        #region Public Members
        /// <summary>
        /// Gets or sets the copy policy to use for the variable.
        /// </summary>
        public VariableCopyPolicy CopyPolicy
        {
            get
            {
                lock (DataLock)
                {
                    return m_copyPolicy;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_copyPolicy = value;
                }
            }
        }
        
		/// <summary>
		/// The timestamp associated with the value.
		/// </summary>
		public abstract DateTime Timestamp { get; }

		/// <summary>
		/// The current status code for the value.
		/// </summary>
		public abstract StatusCode StatusCode { get; }

		/// <summary>
		/// The current status of the value.
		/// </summary>
		public abstract ServiceResult Status { get; }

		/// <summary>
		/// Returns the current value/status/timestamp.
		/// </summary>
		public abstract DataValue GetDataValue(IOperationContext context, int maxAge);
        
        /// <summary>
        /// Converts the value to an instance which can be stored in the variable.
        /// </summary>
        public abstract object Convert(object value, bool throwOnError);

        /// <summary>
        /// Updates the value.
        /// </summary>
        public abstract void UpdateValue(object value);
        
        /// <summary>
        /// Updates the value and status.
        /// </summary>
        public abstract void UpdateValue(object value, ServiceResult status);
        
        /// <summary>
        /// Updates the value, status and timestamp.
        /// </summary>
        public abstract void UpdateValue(object value, ServiceResult status, DateTime timestamp);

        /// <summary>
        /// Updates the timestamp and status.
        /// </summary>
        public abstract void UpdateStatus(ServiceResult status);

        /// <summary>
        /// Updates the timestamp and status.
        /// </summary>
        public abstract void UpdateStatus(ServiceResult status, DateTime timestamp);

        /// <summary>
        /// Sets the callback to use when the value is read.
        /// </summary>
        public abstract void SetCallback(VariableValueReadHandler callback, object callbackData);

        /// <summary>
        /// Sets the callback to use when the value changes.
        /// </summary>
        public abstract void SetCallback(VariableValueChangedHandler callback, object callbackData);

        /// <summary>
        /// Updates a datatype with the value from a child.
        /// </summary>
        public virtual void UpdateValueFromChild(VariableSource child)
        {
            // defined by the subclass.
        }

        /// <summary>
        /// Updates a datatype with the values from the variable children.
        /// </summary>
        public virtual void UpdateChildrenFromValue()
        {
            // defined by the subclass.
        }  
        #endregion
                
        #region INode Members
        /// <summary cref="INode.NodeClass" />
        public override NodeClass NodeClass
        {
            get
            {
                return NodeClass.Variable;
            }
        }
        #endregion
        
        #region ILocalNode Members
        /// <summary cref="ILocalNode.SupportsAttribute" />
        public override bool SupportsAttribute(uint attributeId)
        {
            lock (DataLock)
            {
                switch (attributeId)
                {
                    case Attributes.Value:
                    case Attributes.DataType:
                    case Attributes.ValueRank:
                    case Attributes.ArrayDimensions:                    
                    case Attributes.AccessLevel:                   
                    case Attributes.UserAccessLevel:          
                    case Attributes.MinimumSamplingInterval:                   
                    case Attributes.Historizing:
                    {
                        return true;
                    }

                    default:
                    {
                        return base.SupportsAttribute(attributeId);
                    }
                }
            }
        }
        #endregion

        #region IVariableBase Members
        /// <summary cref="IVariableBase.Value" />
        public object Value
        {
            get 
            { 
                return GetValue();  
            }
            
            set 
            {
                UpdateValue(value);
            }
        }

        /// <summary cref="IVariableBase.DataType" />
        public NodeId DataType
        {
            get
            {
                lock (DataLock)
                {
                    return m_datatype;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_datatype = value;
                }
            }
        }

        /// <summary cref="IVariableBase.ValueRank" />
        public int ValueRank
        {
            get
            {
                lock (DataLock)
                {
                    return m_valueRank;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_valueRank = value;
                }
            }
        }

        /// <summary cref="IVariableBase.ArrayDimensions" />
        public IList<uint> ArrayDimensions
        {
            get
            {
                lock (DataLock)
                {
                    return m_arrayDimensions;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_arrayDimensions = value;
                }
            }
        }
        #endregion

        #region IVariable Members
        /// <summary cref="IVariable.AccessLevel" />
        public byte AccessLevel
        {
            get
            {
                lock (DataLock)
                {
                    return m_accessLevel;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_accessLevel = value;
                }
            }
        }

        /// <summary cref="IVariable.UserAccessLevel" />
        public byte UserAccessLevel
        {
            get
            {
                lock (DataLock)
                {
                    return m_userAccessLevel;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_userAccessLevel = value;
                }
            }
        }

        /// <summary cref="IVariable.MinimumSamplingInterval" />
        public double MinimumSamplingInterval
        {
            get
            {
                lock (DataLock)
                {
                    return m_minimumSamplingInterval;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_minimumSamplingInterval = value;
                }
            }
        }

        /// <summary cref="IVariable.Historizing" />
        public bool Historizing
        {
            get
            {
                lock (DataLock)
                {
                    return m_historizing;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_historizing = value;
                }
            }
        }
        #endregion
         
        #region Protected Members
		/// <summary>
		/// Returns the current value.
		/// </summary>
		protected abstract object GetValue();
        #endregion

        #region Overridden Methods
        /// <summary cref="NodeSource.Initialize(NodeSource)" />
        public override void Initialize(NodeSource source)
        {
            lock (DataLock)
            {
                base.Initialize(source);
                
                VariableSource instance = (VariableSource)source;

                m_datatype = instance.m_datatype;
                m_valueRank = instance.m_valueRank;
                m_arrayDimensions = (IList<uint>)Utils.Clone(instance.m_arrayDimensions);
                m_accessLevel = instance.m_accessLevel;
                m_userAccessLevel = instance.m_accessLevel;
                m_minimumSamplingInterval = instance.m_minimumSamplingInterval;
                m_historizing = instance.m_historizing;
                m_copyPolicy = instance.m_copyPolicy;
            }
        }

        /// <summary cref="NodeSource.CreateNode" />
        protected override void CreateNode(NodeId parentId, NodeId referenceTypeId)
        {
            CheckNodeManagerState();

            VariableAttributes attributes = new VariableAttributes();

            attributes.SpecifiedAttributes = (uint)NodeAttributesMask.DisplayName;
            attributes.DisplayName         = DisplayName;

            NodeManager.CreateVariable(
                parentId,
                referenceTypeId,
                NodeId,
                BrowseName,
                attributes,
                TypeDefinitionId);
        }

        /// <summary cref="NodeSource.UpdateAttributes" />
        protected override void UpdateAttributes(ILocalNode source)
        {
            base.UpdateAttributes(source);

            IVariableType type = source as IVariableType;

            if (type != null)
            {
                DataType        = type.DataType;
                ValueRank       = type.ValueRank;
                ArrayDimensions = (IList<uint>)Utils.Clone(type.ArrayDimensions);
            }

            IVariable instance = source as IVariable;

            if (instance != null)
            {
                DataType                = instance.DataType;
                ValueRank               = instance.ValueRank;
                ArrayDimensions         = null;
                AccessLevel             = instance.AccessLevel;
                UserAccessLevel         = instance.UserAccessLevel;
                MinimumSamplingInterval = instance.MinimumSamplingInterval;
                Historizing             = instance.Historizing;

                if (instance.ArrayDimensions != null && instance.ArrayDimensions.Count > 0)
                {
                    ArrayDimensions = (IList<uint>)Utils.Clone(instance.ArrayDimensions);
                }
            }
        }

        /// <summary cref="NodeSource.ReadAttribute" />
        protected override ServiceResult ReadAttribute(IOperationContext context, uint attributeId, DataValue value)
        {
            // handle reads for other attributes.
            switch (attributeId)
            {
                case Attributes.DataType:
                {
                    value.Value = DataType;
                    break;
                }

                case Attributes.ValueRank:
                {
                    value.Value = ValueRank;
                    break;
                }

                case Attributes.ArrayDimensions:
                {
                    if (ArrayDimensions == null)
                    {
                        return StatusCodes.BadAttributeIdInvalid;
                    }

                    value.Value = ArrayDimensions;
                    break;
                }

                case Attributes.AccessLevel:
                {
                    value.Value = AccessLevel;
                    break;
                }

                case Attributes.UserAccessLevel:
                {
                    value.Value = UserAccessLevel;
                    break;
                }

                case Attributes.MinimumSamplingInterval:
                {
                    value.Value = MinimumSamplingInterval;
                    break;
                }

                case Attributes.Historizing:
                {
                    value.Value = Historizing;
                    break;
                }

                default:
                {
                    return base.ReadAttribute(context, attributeId, value);
                }
            }

            return ServiceResult.Good;
        }

        /// <summary cref="NodeSource.WriteAttribute" />
        protected override ServiceResult WriteAttribute(uint attributeId, DataValue value)
        {
            // handle writes to other attributes.
            switch (attributeId)
            {
                case Attributes.DataType:
                {
                    DataType = (NodeId)value.Value;
                    break;
                }

                case Attributes.ValueRank:
                {
                    ValueRank = (int)value.Value;
                    break;
                }

                case Attributes.ArrayDimensions:
                {
                    ArrayDimensions = (IList<uint>)value.Value;
                    break;
                }

                case Attributes.AccessLevel:
                {
                    AccessLevel = (byte)value.Value;
                    break;
                }

                case Attributes.UserAccessLevel:
                {
                    UserAccessLevel = (byte)value.Value;
                    break;
                }

                case Attributes.MinimumSamplingInterval:
                {
                    MinimumSamplingInterval = (double)value.Value;
                    break;
                }

                case Attributes.Historizing:
                {
                    Historizing = (bool)value.Value;
                    break;
                }

                default:
                {
                    return  base.WriteAttribute(attributeId, value);
                }
            }

            return ServiceResult.Good;
        }  

        /// <summary>
        /// Returns true if the node is being updated.
        /// </summary>
        internal bool Updating
        {
            get
            {
                return m_updating;
            }

            set
            {
                m_updating = value;
            }
        }
        #endregion

        #region Private Fields
        private NodeId m_datatype;
        private int m_valueRank;
        private IList<uint> m_arrayDimensions;
        private byte m_accessLevel;
        private byte m_userAccessLevel;
        private double m_minimumSamplingInterval;
        private bool m_historizing;
        private VariableCopyPolicy m_copyPolicy;
        private bool m_updating;
        #endregion
    }
        
    /// <summary> 
    /// The base class for custom VariableT nodes.
    /// </summary>
    [Obsolete("The VariableSource<T> class is obsolete and is not supported. See Opc.Ua.BaseVariableState<T> for a replacement.")]
    public class VariableSource<T> : VariableSource
    {  
        #region Constructors
        /// <summary>
        /// Adds the source to the type table.
        /// </summary>
        public VariableSource(
            IServerInternal server,
            NodeSource      parent)
        : 
            base(server, parent)
        {
            // set the datatype.
            NodeId datatypeId = TypeInfo.GetDataTypeId(typeof(T));

            if (datatypeId != null)
            {
                DataType = datatypeId;
            }

            // set the value rank.
            ValueRank = TypeInfo.GetValueRank(typeof(T));

            // set the copy policy.
            if (typeof(T).IsValueType)
            {
                CopyPolicy = VariableCopyPolicy.Never;
            }
            else
            {
                CopyPolicy = VariableCopyPolicy.CopyOnWrite;
            }
        }

        /// <summary>
        /// Creates a new instance of the node.
        /// </summary>
        public static VariableSource<T> Construct(
            IServerInternal server, 
            NodeSource      parent, 
            NodeId          referenceTypeId,
            NodeId          nodeId,
            QualifiedName   browseName,
            uint            numericId)
        {
            VariableSource<T> instance = new VariableSource<T>(server, parent);
            instance.Initialize(referenceTypeId, nodeId, browseName, numericId, VariableTypes.BaseDataVariableType);
            return instance;
        }
        #endregion
        
        #region ICloneable Members
        /// <summary cref="NodeSource.Clone(NodeSource)" />
        public override NodeSource Clone(NodeSource parent)
        {
            lock (DataLock)
            {
                VariableSource<T> clone = new VariableSource<T>(Server, parent);
                clone.Initialize(this);
                return clone;
            }
        }
        #endregion
        
		#region IDisposable Members
        /// <summary>
        /// Frees resources held by the object.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (Disposed)
            {
                if (disposing)
                {
                    if (typeof(IDisposable).IsInstanceOfType(m_value))
                    {
                        ((IDisposable)m_value).Dispose();
                    }
                }

                m_value = default(T);
            }

            base.Dispose(disposing);
        }
        #endregion

        #region Public Members
        /// <summary>
        /// The default value for instances of the VariableType.
        /// </summary>
        /// <remarks>
        /// This property can return a reference to the value stored inside the object. 
        /// Updating this object without acquiring a lock will create problems.
        /// 
        /// This property will return a copy of the data if the CopyPolicy is set to Read.
        /// This means updates to the value returned do not affect the object.
        /// 
        /// The same problem exists if the variable has children which contain the real 
        /// data. Updates to the contents of structured will not affect the children. However,
        /// setting the Value property will update the children.
        /// </remarks>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
        public new T Value
        {
            get
            {
                lock (DataLock)
                {
                    return GetValueToRead();
                }
            }

            set
            {
                lock (DataLock)
                {
                    // ensure all event occur when a write occurs.
                    if (Created)
                    {
                        UpdateValue(value);
                    }

                    // suppress events during initialization.
                    else
                    {
                        m_value = GetValueToWrite(value);
                    }
                }
            }
        }   

        /// <summary>
        /// Returns the value of the variable.
        /// </summary>
        public static implicit operator T(VariableSource<T> value)
        {
            if (value != null)
            {
                return value.Value;
            }

            return default(T);
        }

        /// <summary>
        /// Returns the value of the variable.
        /// </summary>
        public static T FromVariableSource(VariableSource<T> value)
        {
            if (value != null)
            {
                return value.Value;
            }

            return default(T);
        }
        
		/// <summary cref="VariableSource.Timestamp" />
		public override DateTime Timestamp
		{
			get 
            { 
                lock (DataLock)
                {
                    return m_timestamp;
                }
            }
		}
                
		/// <summary cref="VariableSource.StatusCode" />
		public override StatusCode StatusCode
		{
			get 
            { 
                lock (DataLock)
                {
                    if (m_status == null)
                    {
                        return StatusCodes.Good;
                    }

                    return m_status.Code;
                }
            }
		}

		/// <summary cref="VariableSource.Status" />
		public override ServiceResult Status
		{
			get 
            { 
                lock (DataLock)
                {
                    return m_status;
                }
            }
		}        

		/// <summary cref="VariableSource.GetDataValue" />
		public override DataValue GetDataValue(IOperationContext context, int maxAge)
		{
            lock (DataLock)
            {
                DataValue value = new DataValue(new Variant(GetValueToRead()), StatusCode, m_timestamp, DateTime.UtcNow);

                value = UpdateValueBeforeRead(context, maxAge, value);

                return value;
            }
		}
        
        /// <summary cref="VariableSource.UpdateValue(object)" />
        public override void UpdateValue(object value)
        {
            UpdateValue(value, ServiceResult.Good, DateTime.UtcNow);      
        }

        /// <summary cref="VariableSource.UpdateValue(object,ServiceResult)" />
        public override void UpdateValue(object value, ServiceResult status)
        {
            UpdateValue(value, ServiceResult.Good, DateTime.UtcNow);
        }
        
        /// <summary cref="VariableSource.Convert(object, bool)" />
        public override object Convert(object value, bool throwOnError)
        {
            // return default value for type.
            if (value == null)
            {
                return default(T);
            }

            // check for simple cast.
            if (typeof(T).IsInstanceOfType(value))
            {
                return (T)value;
            }

            // check if the value is an IEncodeable wrapped in an ExtensionObject.
            ExtensionObject extension = value as  ExtensionObject;
            
            if (extension != null)
            {
                IEncodeable encodeable = ExtensionObject.ToEncodeable(extension);

                if (typeof(T).IsInstanceOfType(encodeable))
                {
                    return encodeable;
                }
            }

            // check for an array of wrapped extension objects.
            ExtensionObject[] extensions = value as  ExtensionObject[];
            
            if (extensions != null)
            {
                Array encodeables = ExtensionObject.ToArray(extensions, typeof(T));

                if (typeof(T).IsInstanceOfType(encodeables))
                {
                    return encodeables;
                }
            }
            
            // check if the value is an IEncodeable wrapped in an ExtensionObject.
            Variant? variant = value as  Variant?;
            
            if (variant != null)
            {
                return Convert(variant.Value.Value, throwOnError);
            }

            // check for an array of wrapped extension objects.
            Variant[] variants = value as  Variant[];
            
            if (variants != null)
            {
                object[] array = new object[variants.Length];

                for (int ii = 0; ii < variants.Length; ii++)
                {
                    array[ii] = variants[ii].Value;
                }

                return array;
            }
            
            // check if the value is an IEncodeable wrapped in an ExtensionObject.
            Uuid? uuid = value as  Uuid?;
            
            if (uuid != null)
            {
                return (Guid)uuid.Value;
            }

            // check for an array of wrapped extension objects.
            Uuid[] uuids = value as  Uuid[];
            
            if (uuids != null)
            {
                Guid[] array = new Guid[uuids.Length];

                for (int ii = 0; ii < uuids.Length; ii++)
                {
                    array[ii] = (Guid)uuids[ii];
                }

                return array;
            }

            // cast not supported.
            if (throwOnError)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "Variable value must be a instance of a(n) {0}.",
                    typeof(T).Name);
            }

            return default(T);
        }

        /// <summary cref="VariableSource.UpdateValue(object,ServiceResult,DateTime)" />
        public override void UpdateValue(object value, ServiceResult status, DateTime timestamp)
        {
            lock (DataLock)
            {
                UpdateValue((T)Convert(value, true), status, timestamp);
            }            
        }

		/// <summary cref="VariableSource.UpdateStatus(ServiceResult)" />
        public override void UpdateStatus(ServiceResult status)
        {
            lock (DataLock)
            {
                UpdateStatus(status, DateTime.UtcNow);
            }            
        }
        
		/// <summary cref="VariableSource.UpdateStatus(ServiceResult, DateTime)" />
        public override void UpdateStatus(ServiceResult status, DateTime timestamp)
        {
            lock (DataLock)
            {
                ServiceResult result = OnBeforeValueChanged(null, timestamp, (status != null)?status.StatusCode:StatusCodes.Good);

                if (ServiceResult.IsBad(result))
                {
                    throw new ServiceResultException(result);
                }

                m_status = status;
                m_timestamp = timestamp;

                OnAfterValueChanged();
            }      
        }       

		/// <summary>
		/// Returns the current value.
		/// </summary>
		public T GetValue(IOperationContext context, int maxAge)
		{
            lock (DataLock)
            {
                T value = GetValueToRead();
                
                if (m_ValueReadCallback != null)
                {
                    DataValue dataValue = new DataValue(new Variant(value));
                    UpdateValueBeforeRead(context, maxAge, dataValue);
                    value = (T)dataValue.Value;
                }

                return value;
            }
		}

        /// <summary>
        /// Updates the value, timestamp and status.
        /// </summary>
        public void UpdateValue(T value)
        {
            lock (DataLock)
            {
                UpdateValue(value, ServiceResult.Good, DateTime.UtcNow);
            }            
        }

        /// <summary>
        /// Updates the value, timestamp and status.
        /// </summary>
        public void UpdateValue(T value, ServiceResult status)
        {
            lock (DataLock)
            {
                UpdateValue(value, status, DateTime.UtcNow);
            }            
        }

        /// <summary>
        /// Updates the value, timestamp and status.
        /// </summary>
        public void UpdateValue(T value, ServiceResult status, DateTime timestamp)
        {
            lock (DataLock)
            {
                if (timestamp == DateTime.MinValue)
                {
                    timestamp = DateTime.UtcNow;
                }
                ServiceResult result = OnBeforeValueChanged(value, timestamp, (status != null)?status.StatusCode:StatusCodes.Good);

                if (ServiceResult.IsBad(result))
                {
                    throw new ServiceResultException(result);
                }

                m_value = GetValueToWrite(value);
                m_status = status;
                m_timestamp = timestamp;

                OnAfterValueChanged();
            } 
        }
        
		/// <summary cref="VariableSource.SetCallback(VariableValueReadHandler, object)" />
        public override void SetCallback(VariableValueReadHandler callback, object callbackData)
        {
            lock (DataLock)
            {
                m_ValueReadCallback = callback;
                m_valueReadCallbackData = callbackData;
            }     
        } 

		/// <summary cref="VariableSource.SetCallback(VariableValueChangedHandler, object)" />
        public override void SetCallback(VariableValueChangedHandler callback, object callbackData)
        {
            lock (DataLock)
            {
                m_ValueChangedCallback = callback;
                m_valueChangedCallbackData = callbackData;
            }     
        }      
        #endregion 
        
        #region Protected Members
        /// <summary>
        /// Gets or sets the value without applying the copy policy.
        /// </summary>
        protected T RawValue
        {
            get
            {
                return m_value;
            }

            set
            {
                m_value = value;
            }
        }   

        /// <summary>
        /// Called when a value is returned to a client.
        /// </summary>
        protected virtual T GetValueToRead()
        {
            if (CopyPolicy == VariableCopyPolicy.CopyOnRead || CopyPolicy == VariableCopyPolicy.Always)
            {
                return (T)Utils.Clone(m_value);
            }

            return m_value;
        }

        /// <summary>
        /// Called when a value is written by a client.
        /// </summary>
        /// <remarks>
        /// This method enforces the copy policy for the node source.
        /// </remarks>
        protected virtual T GetValueToWrite(T value)
        {
            if (CopyPolicy == VariableCopyPolicy.CopyOnWrite || CopyPolicy == VariableCopyPolicy.Always)
            {
                return (T)Utils.Clone(value);
            }

            return value;
        }

        /// <summary>
        /// Called before the value changes.
        /// </summary>
        protected virtual ServiceResult OnBeforeValueChanged(object value, DateTime timestamp, StatusCode status)
        {
            return ServiceResult.Good;
        } 

        /// <summary>
        /// Updates the value before it is returned as part of a read.
        /// </summary>
		private DataValue UpdateValueBeforeRead(IOperationContext context, int maxAge, DataValue value)
		{
            if (StatusCode.IsBad(m_statusOverride))
            {
                return new DataValue(Variant.Null, m_statusOverride, m_timestamp, DateTime.UtcNow);
            }

            if (m_statusOverride != StatusCodes.Good)
            {
                value.StatusCode = m_statusOverride;
            }

            if (m_ValueReadCallback != null)
            {
                m_ValueReadCallback(this, context, maxAge, value, m_valueReadCallbackData);
            }

            return value;
		}

        /// <summary>
        /// Called after the value has changed.
        /// </summary>
        protected virtual void OnAfterValueChanged()
        {        
            if (!Updating)
            {
                try
                {
                    // set flag to stop infinite recursion.
                    Updating = true;            
                    UpdateChildrenFromValue();
                }
                finally
                {
                    Updating = false;
                }
            }

            if (MonitoredItems != null)
            {                
                Variant value = new Variant(m_value);
                
                foreach (IMonitoredItem monitoredItem in MonitoredItems)
                {
                    IDataChangeMonitoredItem datachangeItem = monitoredItem as IDataChangeMonitoredItem;

                    if (datachangeItem != null)
                    {
                        DataValue dataValue = new DataValue(value, StatusCode, m_timestamp, DateTime.UtcNow);
                        dataValue = UpdateValueBeforeRead(new OperationContext(datachangeItem), Int32.MaxValue, dataValue);
                        datachangeItem.QueueValue(dataValue, ServiceResult.Good);
                    }
                }
            }

            if (m_ValueChangedCallback != null)
            {
                m_ValueChangedCallback(this, null, m_value, m_timestamp, m_status, m_valueChangedCallbackData);
            }

            // update the parent.
            VariableSource parent = Parent as VariableSource;

            if (parent != null && !parent.Updating)
            {
                try
                {
                    // set flag to stop infinite recursion.
                    parent.Updating  = true;
                    parent.UpdateValueFromChild(this);
                }
                finally
                {
                    parent.Updating  = false;
                }
            }
        } 
        #endregion
           
        #region Overridden Members
        /// <summary cref="NodeSource.Initialize(NodeSource)" />
        public override void Initialize(NodeSource source)
        {
            lock (DataLock)
            {
                base.Initialize(source);
                
                VariableSource<T> instance = (VariableSource<T>)source;

                m_value = (T)Utils.Clone(instance.Value);
                m_timestamp = instance.m_timestamp;
                m_status = instance.m_status;
                m_statusOverride = instance.m_statusOverride;
            }
        }

        /// <summary cref="NodeSource.UpdateAttributes" />
        protected override void UpdateAttributes(ILocalNode source)
        {
            base.UpdateAttributes(source);

            IVariableType type = source as IVariableType;

            if (type != null)
            {
                m_value = (T)Utils.Clone(Convert(type.Value, false));
                m_timestamp = DateTime.UtcNow;
            }

            IVariable instance = source as IVariable;

            if (instance != null)
            {
                m_value = (T)Utils.Clone(Convert(instance.Value, false));
                m_timestamp = DateTime.UtcNow;
            }
        }

        /// <summary cref="VariableSource.GetValue" />
		protected override object GetValue()
		{
            lock (DataLock)
            {
                return GetValueToRead();
            }
		}

        /// <summary cref="NodeSource.ReadAttribute" />
        protected override ServiceResult ReadAttribute(IOperationContext context, uint attributeId, DataValue value)
        {
            // handle value read.
            if (attributeId == Attributes.Value)
            {                
                try
                {
                    DataValue currentValue = GetDataValue(context, 0);

                    value.Value           = currentValue.Value;
                    value.SourceTimestamp = currentValue.SourceTimestamp;
                    value.StatusCode      = currentValue.StatusCode;
                }
                catch (Exception e)
                {
                    return ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Error reading value from source");
                }
        
                return value.StatusCode;
            }            
                    
            return base.ReadAttribute(context, attributeId, value);
        }

        /// <summary cref="NodeSource.WriteAttribute" />
        protected override ServiceResult WriteAttribute(uint attributeId, DataValue value)
        {
            // handle value write.
            if (attributeId == Attributes.Value)
            {
                try
                {                    
                    UpdateValue((T)Convert(value.Value, true), value.StatusCode, value.SourceTimestamp);
                }
                catch (Exception e)
                {
                    return ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Error writing value to source");
                }

                return ServiceResult.Good;
            }

            // check for status/timestamp writes.
            if (value.StatusCode != StatusCodes.Good || value.ServerTimestamp != DateTime.MinValue || value.SourceTimestamp != DateTime.MinValue)
            {
                return StatusCodes.BadWriteNotSupported;
            }
                        
            return base.WriteAttribute(attributeId, value);
        }  
        #endregion

        #region Private Fields
        private T m_value;
        private DateTime m_timestamp;
        private ServiceResult m_status;
        private StatusCode m_statusOverride;
        private event VariableValueReadHandler m_ValueReadCallback;
        private object m_valueReadCallbackData;
        private event VariableValueChangedHandler m_ValueChangedCallback;
        private object m_valueChangedCallbackData;
        #endregion
    }

    /// <summary>
    /// Called before a variable value is read.
    /// </summary>
    public delegate void VariableValueReadHandler(
        VariableSource    variable,
        IOperationContext context,
        int               maxAge,
        DataValue         value,
        object            callbackData);

    /// <summary>
    /// Called after a variable value has changed.
    /// </summary>
    public delegate void VariableValueChangedHandler(
        VariableSource    variable,
        IOperationContext context,
        object            value,
        DateTime          timestamp,  
        ServiceResult     status,
        object            callbackData);
    
    /// <summary>
    /// Specifies the policies to use when handling reads and write to value.
    /// </summary>
    /// <remarks>
    /// This policy is not applied if the application directly accesses the Value property.
    /// </remarks>
    public enum VariableCopyPolicy
    {
        /// <summary>
        /// The value is copied when is is read.
        /// </summary>
        CopyOnRead,

        /// <summary>
        /// The value is copied before it is written.
        /// </summary>
        CopyOnWrite,

        /// <summary>
        /// The value is never copied (only useful for value types that do not contain reference types).
        /// </summary>
        Never,

        /// <summary>
        /// Data is copied when it is written and when it is read.
        /// </summary>
        Always
    }

    #region Property Class
    /// <summary>
    /// Represents an instance of the PropertyType VariableType in the address space.
    /// </summary>
    public partial class Property<T> : VariableSource<T>
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        protected Property(IServerInternal server, NodeSource parent) 
        : 
            base(server, parent)
        {
        }

        /// <summary>
        /// Creates a new instance of the node.
        /// </summary>
        public static new Property<T> Construct(
            IServerInternal server, 
            NodeSource      parent, 
            NodeId          referenceTypeId,
            NodeId          nodeId,
            QualifiedName   browseName,
            uint            numericId)
        {
            Property<T> instance = new Property<T>(server, parent);
            instance.Initialize(referenceTypeId, nodeId, browseName, numericId, VariableTypes.PropertyType);
            return instance;
        }

        /// <summary>
        /// Creates a new instance of the node.
        /// </summary>
        public static Property<T> Construct(IServerInternal server)
        {
            Property<T> instance = new Property<T>(server, null);
            instance.Initialize(null, null, null, 0, VariableTypes.PropertyType);
            return instance;
        }
        #endregion     

        #region ICloneable Members
        /// <summary cref="NodeSource.Clone(NodeSource)" />
        public override NodeSource Clone(NodeSource parent)
        {
            lock (DataLock)
            {
                Property<T> clone = new Property<T>(Server, parent);
                clone.Initialize(this);
                return clone;
            }
        }
        #endregion          
    }
    #endregion
    
    #region DataVariable Class
    /// <summary>
    /// Represents an instance of the DataVariableType ObjectType in the address space.
    /// </summary>
    public partial class DataVariable<T> : VariableSource<T>
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        protected DataVariable(IServerInternal server, NodeSource parent) 
        : 
            base(server, parent)
        {
        }

        /// <summary>
        /// Creates a new instance of the node.
        /// </summary>
        public static new DataVariable<T> Construct(
            IServerInternal server, 
            NodeSource      parent, 
            NodeId          referenceTypeId,
            NodeId          nodeId,
            QualifiedName   browseName,
            uint            numericId)
        {
            DataVariable<T> instance = new DataVariable<T>(server, parent);
            instance.Initialize(referenceTypeId, nodeId, browseName, numericId, VariableTypes.BaseDataVariableType);
            return instance;
        }

        /// <summary>
        /// Creates a new instance of the node.
        /// </summary>
        public static DataVariable<T> Construct(IServerInternal server)
        {
            DataVariable<T> instance = new DataVariable<T>(server, null);
            instance.Initialize(null, null, null, 0, VariableTypes.BaseDataVariableType);
            return instance;
        }
        #endregion
           
        #region ICloneable Members
        /// <summary cref="NodeSource.Clone(NodeSource)" />
        public override NodeSource Clone(NodeSource parent)
        {
            lock (DataLock)
            {
                DataVariable<T> clone = new DataVariable<T>(Server, parent);
                clone.Initialize(this);
                return clone;
            } 
        }
        #endregion

        #region Public Properties
        #region TimeZone
        /// <summary>
        /// The timezone offset in minutes where the value was produced.
        /// </summary>
        public Property<int> TimeZone
        {
        	get 
            {
                lock (DataLock)
                {      
                    return m_timeZone; 
                }
            }

            protected set
            {
                lock (DataLock)
                {      
                    if (m_timeZone != null)
                    {
                        RemoveChild(m_timeZone);
                    }

                    m_timeZone = value; 
                }
            }
        }

        /// <summary>
        /// Whether the TimeZone node is specified for the node.
        /// </summary>
        public bool TimeZoneSpecified
        {
        	get 
            { 
                lock (DataLock)
                {
                    return m_timeZone != null; 
                }
            }
        }

        /// <summary>
        /// Specifies the optional child.
        /// </summary>
        public void SpecifyTimeZone(IServerInternal server, Property<int> replacement)
        {
            lock (DataLock)
            {
                if (TimeZoneSpecified)
                {
                    TimeZone = (Property<int>)DeleteChild(m_timeZone);
                }

                if (replacement != null)
                {       
                    TimeZone = replacement;

                    TimeZone.Create(
                        this.NodeId, 
                        new NodeId(Opc.Ua.ReferenceTypes.HasProperty), 
                        null,
                        new QualifiedName(Opc.Ua.BrowseNames.TimeZone),
                        Opc.Ua.Variables.BaseDataVariableType_TimeZone,
                        null);
                }
            }
        }
        #endregion
              
        #region DaylightSavingTime
        /// <summary>
        /// Whether daylight saving time is in effect where the value as produced.
        /// </summary>
        public Property<bool> DaylightSavingTime
        {
        	get 
            {
                lock (DataLock)
                {      
                    return m_daylightSavingTime; 
                }
            }

            protected set
            {
                lock (DataLock)
                {      
                    if (m_daylightSavingTime != null)
                    {
                        RemoveChild(m_daylightSavingTime);
                    }

                    m_daylightSavingTime = value; 
                }
            }
        }

        /// <summary>
        /// Whether the DaylightSavingTime node is specified for the node.
        /// </summary>
        public bool DaylightSavingTimeSpecified
        {
        	get 
            { 
                lock (DataLock)
                {
                    return m_daylightSavingTime != null; 
                }
            }
        }

        /// <summary>
        /// Specifies the optional child.
        /// </summary>
        public void SpecifyDaylightSavingTime(IServerInternal server, Property<bool> replacement)
        {
            lock (DataLock)
            {
                if (DaylightSavingTimeSpecified)
                {
                    DaylightSavingTime = (Property<bool>)DeleteChild(m_daylightSavingTime);
                }

                if (replacement != null)
                {       
                    DaylightSavingTime = replacement;

                    DaylightSavingTime.Create(
                        this.NodeId, 
                        new NodeId(Opc.Ua.ReferenceTypes.HasProperty), 
                        null,
                        new QualifiedName(Opc.Ua.BrowseNames.DaylightSavingTime),
                        Opc.Ua.Variables.BaseDataVariableType_DaylightSavingTime,
                        null);
                }
            }
        }
        #endregion

        #region EnumStrings
        /// <summary>
        /// The localized names for an enumerated value.
        /// </summary>
        public Property<IList<LocalizedText>> EnumStrings
        {
        	get 
            {
                lock (DataLock)
                {      
                    return m_enumStrings; 
                }
            }

            protected set
            {
                lock (DataLock)
                {      
                    if (m_enumStrings != null)
                    {
                        RemoveChild(m_enumStrings);
                    }

                    m_enumStrings = value; 
                }
            }
        }

        /// <summary>
        /// Whether the EnumStrings node is specified for the node.
        /// </summary>
        public bool EnumStringsSpecified
        {
        	get 
            { 
                lock (DataLock)
                {
                    return m_enumStrings != null; 
                }
            }
        }

        /// <summary>
        /// Specifies the optional child.
        /// </summary>
        public void SpecifyEnumStrings(Property<IList<LocalizedText>> replacement)
        {
            CheckNodeManagerState();

            lock (DataLock)
            {
                if (EnumStringsSpecified)
                {
                    EnumStrings = (Property<IList<LocalizedText>>)DeleteChild(m_enumStrings);
                }

                if (replacement != null)
                {       
                    EnumStrings = replacement;

                    EnumStrings.Create(
                        this.NodeId, 
                        new NodeId(Opc.Ua.ReferenceTypes.HasProperty, GetNamespaceIndex(Opc.Ua.Namespaces.OpcUa)), 
                        null,
                        new QualifiedName(Opc.Ua.BrowseNames.EnumStrings, GetNamespaceIndex(Opc.Ua.Namespaces.OpcUa)),
                        Opc.Ua.Variables.BaseDataVariableType_EnumStrings,
                        null);
                }
            }
        }
        #endregion
        #endregion

        #region Overridden Methods
        /// <summary cref="NodeSource.Initialize(NodeSource)" />
        public override void Initialize(NodeSource source)
        {
            lock (DataLock)
            {            
                DataVariable<T> instance = source as DataVariable<T>;

                if (instance != null)
                {
                    base.Initialize(source);
                }

                if (instance != null && instance.TimeZoneSpecified)
                {
                    TimeZone = (Property<int>)instance.TimeZone.Clone(this);
                    TimeZone.Initialize(instance.TimeZone);
                }

                if (instance != null && instance.DaylightSavingTimeSpecified)
                {
                    DaylightSavingTime = (Property<bool>)instance.DaylightSavingTime.Clone(this);
                    DaylightSavingTime.Initialize(instance.DaylightSavingTime);
                }

                if (instance != null && instance.EnumStringsSpecified)
                {
                    EnumStrings = (Property<IList<LocalizedText>>)instance.EnumStrings.Clone(this);
                    EnumStrings.Initialize(instance.EnumStrings);
                }
            }
        }
        
        /// <summary cref="NodeSource.CreateChildren" />
        protected override void CreateChildren(object configuration)
        {
            base.CreateChildren(configuration);
                            
            TimeZone = (Property<int>)InitializeOptionalChild(
                new ConstructInstanceDelegate(Property<int>.Construct), 
                new NodeId(Opc.Ua.ReferenceTypes.HasProperty), 
                new QualifiedName(Opc.Ua.BrowseNames.TimeZone),
                Opc.Ua.Variables.BaseDataVariableType_TimeZone,
                configuration);

            DaylightSavingTime = (Property<bool>)InitializeOptionalChild(
                new ConstructInstanceDelegate(Property<bool>.Construct), 
                new NodeId(Opc.Ua.ReferenceTypes.HasProperty), 
                new QualifiedName(Opc.Ua.BrowseNames.DaylightSavingTime),
                Opc.Ua.Variables.BaseDataVariableType_DaylightSavingTime,
                configuration);

            EnumStrings = (Property<IList<LocalizedText>>)InitializeOptionalChild(
                new ConstructInstanceDelegate(Property<IList<LocalizedText>>.Construct), 
                new NodeId(Opc.Ua.ReferenceTypes.HasProperty), 
                new QualifiedName(Opc.Ua.BrowseNames.EnumStrings),
                Opc.Ua.Variables.BaseDataVariableType_EnumStrings,
                configuration);
        }
        #endregion

        #region Private Fields
        private Property<int> m_timeZone;
        private Property<bool> m_daylightSavingTime;
        private Property<IList<LocalizedText>> m_enumStrings;
        #endregion
    }
    #endregion
#endif
}
