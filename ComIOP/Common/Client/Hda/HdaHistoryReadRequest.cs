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
using System.Runtime.InteropServices;
using OpcRcw.Comn;
using OpcRcw.Hda;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Com;
using Opc.Ua.Com.Client;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// Stores a read history request.
    /// </summary>
    public class HdaHistoryBaseRequest
    {
        /// <summary>
        /// Gets the attribute value.
        /// </summary>
        protected ServiceResult GetAttributeValue(
            ISystemContext context,
            NumericRange indexRange,
            QualifiedName dataEncoding, 
            uint attributeId, 
            DataValue value, 
            params HdaAttributeValue[] sources)
        {
            if (sources == null || sources.Length <= 0)
            {
                value.StatusCode = StatusCodes.BadNoData;
                return value.StatusCode;
            }

            switch (attributeId)
            {
                case Constants.OPCHDA_NORMAL_MAXIMUM:
                {
                    double high = this.GetAttributeValue<double>(value, sources[0]);

                    if (StatusCode.IsBad(value.StatusCode))
                    {
                        return value.StatusCode;
                    }

                    if (sources.Length <= 1)
                    {
                        value.StatusCode = StatusCodes.BadNoData;
                        return value.StatusCode;
                    }

                    double low = this.GetAttributeValue<double>(value, sources[1]);

                    if (StatusCode.IsBad(value.StatusCode))
                    {
                        return value.StatusCode;
                    }

                    // take the latest timestamp.
                    if (sources[0].Timestamp > sources[1].Timestamp)
                    {
                        value.SourceTimestamp = sources[0].Timestamp;
                    }

                    value.Value = new Range(high, low);
                    break;
                }

                case Constants.OPCHDA_HIGH_ENTRY_LIMIT:
                {
                    double high = this.GetAttributeValue<double>(value, sources[0]);

                    if (StatusCode.IsBad(value.StatusCode))
                    {
                        return value.StatusCode;
                    }

                    if (sources.Length <= 1)
                    {
                        value.StatusCode = StatusCodes.BadNoData;
                        return value.StatusCode;
                    }

                    double low = this.GetAttributeValue<double>(value, sources[1]);

                    if (StatusCode.IsBad(value.StatusCode))
                    {
                        return value.StatusCode;
                    }

                    // take the latest timestamp.
                    if (sources[0].Timestamp > sources[1].Timestamp)
                    {
                        value.SourceTimestamp = sources[0].Timestamp;
                    }

                    value.Value = new Range(high, low);
                    break;
                }

                case Constants.OPCHDA_ENG_UNITS:
                {
                    string units = this.GetAttributeValue<string>(value, sources[0]);

                    if (StatusCode.IsBad(value.StatusCode))
                    {
                        return value.StatusCode;
                    }

                    value.Value = new EUInformation(units, Namespaces.ComInterop);
                    break;
                }

                case Constants.OPCHDA_MAX_TIME_INT:
                case Constants.OPCHDA_MIN_TIME_INT:
                {
                    string number = this.GetAttributeValue<string>(value, sources[0]);

                    if (StatusCode.IsBad(value.StatusCode))
                    {
                        return value.StatusCode;
                    }

                    try
                    {
                        value.Value = Convert.ToDouble(number);
                    }
                    catch (Exception)
                    {
                        value.StatusCode = StatusCodes.BadTypeMismatch;
                        return value.StatusCode;
                    }

                    break;
                }

                case Constants.OPCHDA_EXCEPTION_DEV_TYPE:
                {
                    short number = this.GetAttributeValue<short>(value, sources[0]);

                    if (StatusCode.IsBad(value.StatusCode))
                    {
                        return value.StatusCode;
                    }

                    value.Value = (int)number;
                    break;
                }

                default:
                {
                    value.Value = this.GetAttributeValue<object>(value, sources[0]);
                    break;
                }
            }

            return ApplyIndexRangeAndDataEncoding(context, indexRange, dataEncoding, value);
        }

        /// <summary>
        /// Converts the attribute value to the specified type.
        /// </summary>
        protected T GetAttributeValue<T>(DataValue value, HdaAttributeValue source)
        {
            value.Value = null;

            // find the attribute value.
            if (source == null)
            {
                value.StatusCode = StatusCodes.BadOutOfService;
                return default(T);
            }

            // set the appropriate error code if no value found.
            if (source == null || source.Error < 0 || source.Error == ResultIds.S_NODATA)
            {
                value.StatusCode = StatusCodes.BadNoData;
                return default(T);
            }

            // update the source timestamp if a value is being read.
            value.SourceTimestamp = source.Timestamp;

            // check type conversion error.
            if (!typeof(T).IsInstanceOfType(source.Value))
            {
                value.StatusCode = StatusCodes.BadTypeMismatch;
                return default(T);
            }

            // save the value and return the result.
            value.Value = (T)source.Value;
            return (T)source.Value;
        }

        /// <summary>
        /// Applies the index range and data encoding to the value.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="indexRange">The index range.</param>
        /// <param name="dataEncoding">The data encoding.</param>
        /// <param name="value">The value.</param>
        /// <returns></returns>
        protected ServiceResult ApplyIndexRangeAndDataEncoding(
            ISystemContext context,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            DataValue value)
        {
            if (StatusCode.IsBad(value.StatusCode))
            {
                return value.StatusCode;
            }

            if (indexRange != NumericRange.Empty || !QualifiedName.IsNull(dataEncoding))
            {
                object valueToUpdate = value.Value;

                ServiceResult result = BaseVariableState.ApplyIndexRangeAndDataEncoding(
                    context,
                    indexRange,
                    dataEncoding,
                    ref valueToUpdate);

                if (ServiceResult.IsBad(result))
                {
                    value.Value = null;
                    value.StatusCode = result.StatusCode;
                    return result;
                }

                value.Value = valueToUpdate;
            }

            return value.StatusCode;
        }
    }

    /// <summary>
    /// Stores a read history request.
    /// </summary>
    public class HdaHistoryReadRequest : HdaHistoryBaseRequest
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="HdaHistoryReadRequest"/> class.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="details">The details.</param>
        /// <param name="nodeToRead">The node to read.</param>
        public HdaHistoryReadRequest(string itemId, HistoryReadDetails details, HistoryReadValueId nodeToRead)
        {
            ItemId = itemId;
            IndexRange = nodeToRead.ParsedIndexRange;
            DataEncoding = nodeToRead.DataEncoding;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets or sets the continuation point.
        /// </summary>
        /// <value>The continuation point.</value>
        public byte[] ContinuationPoint
        {
            get { return m_continuationPoint; }
            set { m_continuationPoint = value; }
        }

        /// <summary>
        /// Gets or sets the item id.
        /// </summary>
        /// <value>The item id.</value>
        public string ItemId
        {
            get { return m_itemId; }
            set { m_itemId = value; }
        }

        /// <summary>
        /// Gets or sets the server handle.
        /// </summary>
        /// <value>The server handle.</value>
        public int ServerHandle
        {
            get { return m_serverHandle; }
            set { m_serverHandle = value; }
        }

        /// <summary>
        /// Gets or sets the index range.
        /// </summary>
        /// <value>The index range.</value>
        public NumericRange IndexRange
        {
            get { return m_indexRange; }
            set { m_indexRange = value; }
        }

        /// <summary>
        /// Gets or sets the data encoding.
        /// </summary>
        /// <value>The data encoding.</value>
        public QualifiedName DataEncoding
        {
            get { return m_dataEncoding; }
            set { m_dataEncoding = value; }
        }

        /// <summary>
        /// Gets or sets the start time.
        /// </summary>
        /// <value>The start time.</value>
        public DateTime StartTime
        {
            get { return m_startTime; }
            set { m_startTime = value; }
        }

        /// <summary>
        /// Gets or sets the end time.
        /// </summary>
        /// <value>The end time.</value>
        public DateTime EndTime
        {
            get { return m_endTime; }
            set { m_endTime = value; }
        }

        /// <summary>
        /// Gets or sets the max return values.
        /// </summary>
        /// <value>The max return values.</value>
        public int MaxReturnValues
        {
            get { return m_maxReturnValues; }
            set { m_maxReturnValues = value; }
        }

        /// <summary>
        /// Gets or sets the error.
        /// </summary>
        /// <value>The error.</value>
        public StatusCode Error
        {
            get { return m_error; }
            set { m_error = value; }
        }

        /// <summary>
        /// Gets or sets the results.
        /// </summary>
        /// <value>The results.</value>
        public DataValueCollection Results
        {
            get { return m_results; }
            set { m_results = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="HdaHistoryReadRequest"/> is completed.
        /// </summary>
        /// <value><c>true</c> if completed; otherwise, <c>false</c>.</value>
        public bool Completed
        {
            get { return m_completed; }
            set { m_completed = value; }
        }

        /// <summary>
        /// Gets or sets the total values returned.
        /// </summary>
        /// <value>The total values returned.</value>
        public int TotalValuesReturned
        {
            get { return m_totalValuesReturned; }
            set { m_totalValuesReturned = value; }
        }
        #endregion

        #region Private Fields
        private byte[] m_continuationPoint;
        private string m_itemId;
        private int m_serverHandle;
        private NumericRange m_indexRange;
        private QualifiedName m_dataEncoding;
        private DateTime m_startTime;
        private DateTime m_endTime;
        private int m_maxReturnValues;
        private StatusCode m_error;
        private DataValueCollection m_results;
        private bool m_completed;
        private int m_totalValuesReturned;
        #endregion
    }

    /// <summary>
    /// A request to read raw or modified history data.
    /// </summary>
    public class HdaHistoryReadRawModifiedRequest : HdaHistoryReadRequest
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="HdaHistoryReadRawModifiedRequest"/> class.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="details">The details.</param>
        /// <param name="nodeToRead">The node to read.</param>
        public HdaHistoryReadRawModifiedRequest(string itemId, ReadRawModifiedDetails details, HistoryReadValueId nodeToRead)
        :
            base(itemId, details, nodeToRead)
        {
            StartTime = details.StartTime;
            EndTime = details.EndTime;
            MaxReturnValues = (int)details.NumValuesPerNode;
            ReturnBounds = details.ReturnBounds;
            IsReadModified = details.IsReadModified;
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Gets or sets a value indicating whether [return bounds].
        /// </summary>
        /// <value><c>true</c> if the bounds should be returned; otherwise, <c>false</c>.</value>
        public bool ReturnBounds
        {
            get { return m_returnBounds; }
            set { m_returnBounds = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is read modified.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this instance is read modified; otherwise, <c>false</c>.
        /// </value>
        public bool IsReadModified
        {
            get { return m_isReadModified; }
            set { m_isReadModified = value; }
        }

        /// <summary>
        /// Gets or sets the modification infos.
        /// </summary>
        /// <value>The modification infos.</value>
        public ModificationInfoCollection ModificationInfos
        {
            get { return m_modificationInfos; }
            set { m_modificationInfos = value; }
        }
        #endregion

        #region Private Fields
        private bool m_returnBounds;
        private bool m_isReadModified;
        private ModificationInfoCollection m_modificationInfos;
        #endregion
    }

    /// <summary>
    /// A request to read raw history data at specific times.
    /// </summary>
    public class HdaHistoryReadAtTimeRequest : HdaHistoryReadRequest
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="HdaHistoryReadRawModifiedRequest"/> class.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="details">The details.</param>
        /// <param name="nodeToRead">The node to read.</param>
        public HdaHistoryReadAtTimeRequest(string itemId, ReadAtTimeDetails details, HistoryReadValueId nodeToRead)
        :
            base(itemId, details, nodeToRead)
        {
            ReqTimes = details.ReqTimes;
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// Gets or sets the requested times.
        /// </summary>
        /// <value>The req times.</value>
        public DateTimeCollection ReqTimes
        {
            get { return m_reqTimes; }
            set { m_reqTimes = value; }
        }
        #endregion

        #region Private Fields
        private DateTimeCollection m_reqTimes;
        #endregion
    }

    /// <summary>
    /// A request to read raw history data at specific times.
    /// </summary>
    public class HdaHistoryReadAttributeRequest : HdaHistoryReadRawModifiedRequest
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="HdaHistoryReadRawModifiedRequest"/> class.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="details">The details.</param>
        /// <param name="nodeToRead">The node to read.</param>
        public HdaHistoryReadAttributeRequest(string itemId, uint attributeId, ReadRawModifiedDetails details, HistoryReadValueId nodeToRead)
        :
            base(itemId, details, nodeToRead)
        {
            MaxReturnValues = (int)details.NumValuesPerNode;
            m_attributeId = attributeId;
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Gets or sets the attribute id.
        /// </summary>
        /// <value>The attribute id.</value>
        public uint AttributeId
        {
            get { return m_attributeId; }
            set { m_attributeId = value; }
        }

        /// <summary>
        /// Gets the history for the attribute.
        /// </summary>
        public void GetHistoryResults(
            ISystemContext context,
            HistoryReadValueId nodeToRead,
            DataValueCollection values)
        {
           DataValue value = null;

           do
           {
               value = NextValue(
                   context,
                   nodeToRead.ParsedIndexRange,
                   nodeToRead.DataEncoding);

               if (value != null)
               {
                   values.Add(value);

                   if (MaxReturnValues > 0 && values.Count == MaxReturnValues)
                   {
                       break;
                   }
               }               
           }
           while (value != null);

           Completed = value == null;
        }

        /// <summary>
        /// Sets the history for the attribute.
        /// </summary>
        public void SetHistoryResults(
            int[] attributeIds,
            HdaAttributeValue[][] results)
        {
            m_historyData = null;

            // save the primary series.
            if (results.Length > 0)
            {
                m_historyData = results[0];
            }

            // create a merged series from multiple streams.
            if (results.Length > 1)
            {
                m_mergedSeries = new List<HdaAttributeValue[]>();

                // find the first set.
                HdaAttributeValue[] tuple = new HdaAttributeValue[attributeIds.Length];

                for (int ii = 0; ii < tuple.Length; ii++)
                {
                    if (results[ii] != null)
                    {
                        tuple[ii] = results[ii][0];
                    }
                }

                m_mergedSeries.Add(tuple);

                // add in additional sets.
                int[] indexes = new int[attributeIds.Length];

                while (tuple != null)
                {
                    tuple = NextTuple(results, indexes);

                    if (tuple != null)
                    {
                        m_mergedSeries.Add(tuple);
                    }
                }
            }
        }

        private HdaAttributeValue[] NextTuple(HdaAttributeValue[][] results, int[] indexes)
        {
            int soonest = -1;
            DateTime timestamp = DateTime.MaxValue;

            for (int ii = 0; ii < indexes.Length; ii++)
            {
                // get the next index for the series.
                int index = indexes[ii]+1;

                // check if at the end of the series.
                if (index >= results[ii].Length)
                {
                    continue;
                }
                
                // check the timestamp.
                HdaAttributeValue value = results[ii][index];

                if (value.Timestamp < timestamp)
                {
                    timestamp = value.Timestamp;
                    soonest = ii;
                }
            }

            // check if at end.
            if (soonest == -1)
            {
                return null;
            }

            // increment all series with a matching timestamp.
            for (int ii = 0; ii < indexes.Length; ii++)
            {
                // get the next index for the series.
                int index = indexes[ii]+1;

                // check if at the end of the series.
                if (index >= results[ii].Length)
                {
                    continue;
                }

                // check the timestamp.
                HdaAttributeValue value = results[ii][index];

                if (value.Timestamp == timestamp)
                {
                    indexes[ii]++;
                }
            }

            // build the tuple.
            HdaAttributeValue[] tuple = new HdaAttributeValue[indexes.Length];

            for (int ii = 0; ii < indexes.Length; ii++)
            {
                tuple[ii] = results[ii][indexes[ii]];
            }

            return tuple;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Nexts the value in the time series.
        /// </summary>
        private DataValue NextValue(
            ISystemContext context,
            NumericRange indexRange,
            QualifiedName dataEncoding)
        {
            DataValue value = new DataValue();

            if (m_mergedSeries == null)
            {
                // check for end of series.
                if (m_historyData == null || m_position >= m_historyData.Length)
                {
                    return GetLastBound(context, indexRange, dataEncoding);
                }

                // process next value.
                HdaAttributeValue source = m_historyData[m_position++];
                GetAttributeValue(context, indexRange, dataEncoding, m_attributeId, value, source);

                return value;
            }

            // check for end of series.
            if (m_mergedSeries == null || m_position >= m_mergedSeries.Count)
            {
                return GetLastBound(context, indexRange, dataEncoding);
            }

            // process next value.
            HdaAttributeValue[] sources = m_mergedSeries[m_position++];
            GetAttributeValue(context, indexRange, dataEncoding, m_attributeId, value, sources);
            return value;
        }

        /// <summary>
        /// Gets the last bound if requested.
        /// </summary>
        private DataValue GetLastBound(
            ISystemContext context,
            NumericRange indexRange,
            QualifiedName dataEncoding)
        {
            if (m_lastBoundReturned)
            {
                return null;
            }

            m_lastBoundReturned = true;

            if (!ReturnBounds)
            {
                return null;
            }

            if (m_mergedSeries == null && m_historyData == null)
            {
                return null;
            }

            if (EndTime == DateTime.MinValue)
            {
                return null;
            }

            m_position = ((m_mergedSeries != null)?m_mergedSeries.Count:m_historyData.Length)-1;

            DataValue lastValue = NextValue(context, indexRange, dataEncoding);

            if (lastValue == null)
            {
                return null;
            }

            if (lastValue.SourceTimestamp == EndTime)
            {
                return null;
            }

            return new DataValue(Variant.Null, StatusCodes.BadBoundNotSupported, EndTime, EndTime);
        }
        #endregion

        #region Private Fields
        private uint m_attributeId;
        private HdaAttributeValue[] m_historyData;
        private List<HdaAttributeValue[]> m_mergedSeries;
        private int m_position;
        private bool m_lastBoundReturned;
        #endregion
    }

    /// <summary>
    /// A request to read raw history data at specific times.
    /// </summary>
    public class HdaHistoryReadAnnotationRequest : HdaHistoryReadRawModifiedRequest
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="HdaHistoryReadRawModifiedRequest"/> class.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="details">The details.</param>
        /// <param name="nodeToRead">The node to read.</param>
        public HdaHistoryReadAnnotationRequest(string itemId, ReadRawModifiedDetails details, HistoryReadValueId nodeToRead)
        :
            base(itemId, details, nodeToRead)
        {
            MaxReturnValues = (int)details.NumValuesPerNode;
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Gets or sets the annotations.
        /// </summary>
        /// <value>The annotations.</value>
        public List<DataValue> Annotations
        {
            get { return m_annotations; }
            set { m_annotations = value; }
        }

        /// <summary>
        /// Gets the history for the attribute.
        /// </summary>
        public void GetHistoryResults(
            ISystemContext context,
            HistoryReadValueId nodeToRead,
            DataValueCollection values)
        {
            DataValue value = null;

            do
            {
                if (m_annotations == null || m_position >= m_annotations.Count)
                {
                    break;
                }

                value = m_annotations[m_position++];
                ApplyIndexRangeAndDataEncoding(context, nodeToRead.ParsedIndexRange, nodeToRead.DataEncoding, value);
                values.Add(value);

                if (values.Count == MaxReturnValues)
                {
                    break;
                }
            }
            while (value != null);

            Completed = m_annotations == null || m_position >= m_annotations.Count;

            if (m_annotations != null && m_position > 0)
            {
                m_annotations.RemoveRange(0, m_position);
                m_position = 0;
            }
        }
        #endregion

        #region Private Fields
        private List<DataValue> m_annotations;
        private int m_position;
        #endregion
    }

    /// <summary>
    /// A request to read raw or modified history data.
    /// </summary>
    public class HdaHistoryReadProcessedRequest : HdaHistoryReadRequest
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="HdaHistoryReadProcessedRequest"/> class.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="aggregateId">The aggregate id.</param>
        /// <param name="details">The details.</param>
        /// <param name="nodeToRead">The node to read.</param>
        public HdaHistoryReadProcessedRequest(string itemId, uint aggregateId, ReadProcessedDetails details, HistoryReadValueId nodeToRead)
            :
                base(itemId, details, nodeToRead)
        {
            StartTime = details.StartTime;
            EndTime = details.EndTime;
            ResampleInterval = (long)details.ProcessingInterval;

           m_aggregateId = aggregateId;
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Gets or sets the resample interval.
        /// </summary>
        /// <value>The resample interval.</value>
        public long ResampleInterval
        {
            get { return m_resampleInterval; }
            set { m_resampleInterval = value; }
        }

        /// <summary>
        /// Gets or sets the resample interval.
        /// </summary>
        /// <value>The resample interval.</value>
        public uint AggregateId
        {
            get { return m_aggregateId; }
            set { m_aggregateId = value; }
        }
        #endregion

        #region Private Fields
        private long m_resampleInterval;
        private uint m_aggregateId;
        #endregion
    }
}
