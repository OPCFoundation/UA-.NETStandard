/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua;
using Opc.Ua.Server;

namespace TestData
{
    /// <summary>
    /// A class used to read values from a history data source.
    /// </summary>
    public class HistoryDataReader : IDisposable
    {
        /// <summary>
        /// Constructs a reader for the source.
        /// </summary>
        /// <param name="source">The source of the history data.</param>
        public HistoryDataReader(NodeId variableId, IHistoryDataSource source)
        {
            Id = Guid.NewGuid();
            VariableId = variableId;
            m_source = source;
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            // nothing to do.
        }

        /// <summary>
        /// A globally unique identifier for the instance.
        /// </summary>
        public Guid Id { get; }

        /// <summary>
        /// The identifier for the variable being read.
        /// </summary>
        public NodeId VariableId { get; }

        /// <summary>
        /// Starts reading raw values.
        /// </summary>
        /// <param name="context">The context for the operation.</param>
        /// <param name="request">The request parameters.</param>
        /// <param name="timestampsToReturn">The timestamps to return with the value.</param>
        /// <param name="indexRange">The range to return for array values.</param>
        /// <param name="dataEncoding">The data encoding to use for structured values.</param>
        /// <param name="values">The values to return.</param>
        public void BeginReadRaw(
            ServerSystemContext context,
            ReadRawModifiedDetails request,
            TimestampsToReturn timestampsToReturn,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            DataValueCollection values)
        {
            m_request = request;

            bool startSpecified = m_request.StartTime != DateTime.MinValue;
            bool endSpecified = m_request.EndTime != DateTime.MinValue;

            // Part 11: a request with only StartTime is forward and a request
            // with only EndTime is reverse. DateTime.MinValue means that the
            // corresponding time was not specified.
            m_isForward = startSpecified &&
                (!endSpecified || m_request.StartTime <= m_request.EndTime);
            m_isOneSided = startSpecified != endSpecified;

            if (!startSpecified)
            {
                m_startTime = m_request.EndTime;
                m_endTime = DateTime.MinValue;
            }
            else if (!endSpecified)
            {
                m_startTime = m_request.StartTime;
                m_endTime = DateTime.MaxValue;
            }
            else
            {
                m_startTime = m_request.StartTime;
                m_endTime = m_request.EndTime;
            }

            m_position = -1;
            m_complete = false;
            m_pendingValue = null;

            // Position the cursor at the requested boundary. When bounds are
            // requested, first look on the opposite side of the boundary.
            DataValue value = m_source.FirstRaw(
                m_startTime,
                m_request.ReturnBounds ? !m_isForward : m_isForward,
                m_request.IsReadModified,
                out m_position);

            // A missing leading bound must not prevent values inside the
            // requested domain from being returned.
            if (value == null && m_request.ReturnBounds)
            {
                value = m_source.FirstRaw(
                    m_startTime,
                    m_isForward,
                    m_request.IsReadModified,
                    out m_position);
            }

            if (value == null)
            {
                m_complete = true;
                return;
            }

            m_complete = IsAtOrPastEnd(value.ServerTimestamp);
            if (!m_complete || m_request.ReturnBounds)
            {
                AddValue(timestampsToReturn, indexRange, dataEncoding, values, value);
            }
        }

        /// <summary>
        /// Continues a read raw operation.
        /// </summary>
        /// <param name="context">The context for the operation.</param>
        /// <param name="timestampsToReturn">The timestamps to return with the value.</param>
        /// <param name="indexRange">The range to return for array values.</param>
        /// <param name="dataEncoding">The data encoding to use for structured values.</param>
        /// <param name="values">The values to return.</param>
        /// <returns>False if the operation halted because the maximum number of values was discovered.</returns>
        public bool NextReadRaw(
            ServerSystemContext context,
            TimestampsToReturn timestampsToReturn,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            DataValueCollection values)
        {
            if (m_complete)
            {
                return true;
            }

            while (true)
            {
                // check for limit.
                if (m_request.NumValuesPerNode > 0 && values.Count >= m_request.NumValuesPerNode)
                {
                    // For a one-sided request the count defines the end of the
                    // time domain, so no continuation point is required.
                    if (m_isOneSided)
                    {
                        return true;
                    }

                    // Look ahead before returning a continuation point. Without
                    // this check an exact page boundary produces a spurious empty
                    // final page. The source position is advanced by NextRaw, so
                    // retain a qualifying value for the next page.
                    DataValue nextValue = m_source.NextRaw(
                        m_lastTime,
                        m_isForward,
                        m_request.IsReadModified,
                        ref m_position);

                    if (nextValue == null)
                    {
                        m_complete = true;
                        return true;
                    }

                    if (IsAtOrPastEnd(nextValue.ServerTimestamp) && !m_request.ReturnBounds)
                    {
                        m_complete = true;
                        return true;
                    }

                    m_pendingValue = nextValue;
                    return false;
                }

                DataValue value;

                if (m_pendingValue != null)
                {
                    value = m_pendingValue;
                    m_pendingValue = null;
                }
                else
                {
                    value = m_source.NextRaw(
                        m_lastTime,
                        m_isForward,
                        m_request.IsReadModified,
                        ref m_position);
                }

                // no more data.
                if (value == null)
                {
                    m_complete = true;
                    return true;
                }

                // check for bound.
                if (IsAtOrPastEnd(value.ServerTimestamp))
                {
                    if (m_request.ReturnBounds)
                    {
                        AddValue(timestampsToReturn, indexRange, dataEncoding, values, value);
                    }
                    m_complete = true;
                    return true;
                }

                // add value.
                AddValue(timestampsToReturn, indexRange, dataEncoding, values, value);
            }
        }

        private bool IsAtOrPastEnd(DateTime timestamp)
        {
            return (m_isForward && timestamp >= m_endTime) ||
                (!m_isForward && timestamp <= m_endTime);
        }

        /// <summary>
        /// Adds a DataValue to a list of values to return.
        /// </summary>
        private void AddValue(
            TimestampsToReturn timestampsToReturn,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            DataValueCollection values,
            DataValue value)
        {
            // ignore invalid case.
            if (value == null)
            {
                return;
            }

            // save the last timestamp returned.
            m_lastTime = value.ServerTimestamp;

            // check if the index range or data encoding can be applied.
            if (StatusCode.IsGood(value.StatusCode))
            {
                object valueToReturn = value.Value;

                // apply the index range.
                if (indexRange != NumericRange.Empty)
                {
                    StatusCode error = indexRange.ApplyRange(ref valueToReturn);

                    if (StatusCode.IsBad(error))
                    {
                        value.Value = null;
                        value.StatusCode = error;
                    }
                    else
                    {
                        value.Value = valueToReturn;
                    }
                }

                // apply the data encoding.
                if (!QualifiedName.IsNull(dataEncoding))
                {
                    value.Value = null;
                    value.StatusCode = StatusCodes.BadDataEncodingUnsupported;
                }
            }

            // apply the timestamps filter.
            if (timestampsToReturn is TimestampsToReturn.Neither or TimestampsToReturn.Server)
            {
                value.SourceTimestamp = DateTime.MinValue;
            }

            if (timestampsToReturn is TimestampsToReturn.Neither or TimestampsToReturn.Source)
            {
                value.ServerTimestamp = DateTime.MinValue;
            }

            // add result.
            values.Add(value);
        }

        private readonly IHistoryDataSource m_source;
        private ReadRawModifiedDetails m_request;
        private DateTime m_startTime;
        private DateTime m_endTime;
        private bool m_isForward;
        private bool m_isOneSided;
        private bool m_complete;
        private int m_position;
        private DateTime m_lastTime;
        private DataValue m_pendingValue;
    }
}
