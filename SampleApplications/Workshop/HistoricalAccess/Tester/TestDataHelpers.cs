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
using System.Linq;
using System.Text;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts
{
    public partial class TestData
    {
        #region Public Methods
        /// <summary>
        /// Finds the raw dataset with the specified name.
        /// </summary>
        public RawDataSetType FindRawDataSet(string dataSetName)
        {
            if (this.RawDataSets != null)
            {
                foreach (RawDataSetType dataset in this.RawDataSets)
                {
                    if (dataset.Name == dataSetName)
                    {
                        return dataset;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the processed dataset with the specified name.
        /// </summary>
        public ProcessedDataSetType FindProcessedDataSet(string dataSetName)
        {
            if (String.IsNullOrEmpty(dataSetName))
            {
                return null;
            }

            if (this.ProcessedDataSets != null)
            {
                foreach (ProcessedDataSetType dataset in this.ProcessedDataSets)
                {
                    if (dataset.Name == dataSetName)
                    {
                        return dataset;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the best match for the target.
        /// </summary>
        public ProcessedDataSetType FindBestMatch(string rawDataSetName, ProcessedDataSetType target)
        {
            if (target != null && this.ProcessedDataSets != null)
            {
                // try for match on name.
                foreach (ProcessedDataSetType dataset in this.ProcessedDataSets)
                {
                    if (dataset.DataSetName == rawDataSetName)
                    {
                        if (target.Name == dataset.Name)
                        {
                            return dataset;
                        }
                    }
                }

                // try for match on aggregate.
                foreach (ProcessedDataSetType dataset in this.ProcessedDataSets)
                {
                    if (dataset.DataSetName == rawDataSetName)
                    {
                        if (target.AggregateName == dataset.AggregateName)
                        {
                            return dataset;
                        }
                    }
                }
            }

            // nothing found.
            return null;
        }

        /// <summary>
        /// Finds the processed dataset for the specified raw data set.
        /// </summary>
        public ProcessedDataSetType[] GetProcessedDataSets(string rawDataSetName)
        {
            RawDataSetType rawDataset = FindRawDataSet(rawDataSetName);

            List<ProcessedDataSetType> datasets = new List<ProcessedDataSetType>();

            if (this.ProcessedDataSets != null)
            {
                foreach (ProcessedDataSetType dataset in this.ProcessedDataSets)
                {
                    if (dataset.DataSetName == rawDataSetName)
                    {
                        dataset.Name = GetName(dataset);
                        datasets.Add(dataset);
                    }
                }
            }

            return datasets.ToArray();
        }

        /// <summary>
        /// Constructs a name for the processed dataset.
        /// </summary>
        public static string GetName(ProcessedDataSetType dataset)
        {
            StringBuilder buffer = new StringBuilder();

            buffer.Append(dataset.AggregateName);
            buffer.Append(" [");
            buffer.Append(dataset.ProcessingInterval);
            buffer.Append(':');
            buffer.Append((dataset.Stepped) ? "Stepped" : "Sloped");
            buffer.Append('/');
            buffer.Append((dataset.UseSlopedExtrapolation) ? "Sloped" : "Stepped");
            buffer.Append(':');
            buffer.Append((dataset.TreatUncertainAsBad)?"AsBad":"AsUncertain");
            buffer.Append(':');
            buffer.Append(dataset.PercentBad);
            buffer.Append('/');
            buffer.Append(dataset.PercentGood);
            buffer.Append("]");

            return buffer.ToString();
        }

        /// <summary>
        /// Adds a new dataset.
        /// </summary>
        public void AddDataSet(ProcessedDataSetType dataset)
        {
            List<ProcessedDataSetType> datasets = new List<ProcessedDataSetType>();

            if (this.ProcessedDataSets != null)
            {
                datasets.AddRange(this.ProcessedDataSets);
            }

            // check for duplicates.
            if (datasets.Contains(dataset))
            {
                return;
            }

            dataset.Name = GetName(dataset);
            datasets.Add(dataset);

            this.ProcessedDataSets = datasets.ToArray();
        }

        /// <summary>
        /// Removes a dataset.
        /// </summary>
        /// <param name="dataset"></param>
        public void RemoveDataSet(ProcessedDataSetType dataset)
        {
            if (dataset != null)
            {
                List<ProcessedDataSetType> datasets = new List<ProcessedDataSetType>();

                if (this.ProcessedDataSets != null)
                {
                    datasets.AddRange(this.ProcessedDataSets);
                }

                int index = datasets.IndexOf(dataset);
                
                if (index >= 0)
                {
                    datasets.RemoveAt(index);
                    this.ProcessedDataSets = datasets.ToArray();
                }
            }
        }
                
        /// <summary>
        /// Returns the values in the specified raw dataset.
        /// </summary>
        public SortedDictionary<DateTime, DataValue> GetRawValues(string dataSetName)
        {
            RawDataSetType dataset = FindRawDataSet(dataSetName);

            if (dataset != null)
            {
                return ToDataValues(dataset.Values);
            }

            return new SortedDictionary<DateTime, DataValue>();
        }

        public class DataValue
        {
            public DataValue() { m_value = new Opc.Ua.DataValue();  }
            public DataValue(Opc.Ua.DataValue value) { m_value = value; }
            public string Comment { get; set; }
            public object Value { get { return m_value.Value; } set { m_value.Value = value; } }
            public Variant WrappedValue { get { return m_value.WrappedValue; } set { m_value.WrappedValue = value; } }
            public DateTime SourceTimestamp { get { return m_value.SourceTimestamp; } set { m_value.SourceTimestamp = value; } }
            public StatusCode StatusCode { get { return m_value.StatusCode; } set { m_value.StatusCode = value; } }

            public static explicit operator Opc.Ua.DataValue(DataValue value) { return value.m_value; }

            private Opc.Ua.DataValue m_value;
        }

        /// <summary>
        /// Returns the values in the specified processed dataset.
        /// </summary>
        public SortedDictionary<DateTime, DataValue> GetProcessedValues(ProcessedDataSetType dataset)
        {
            if (dataset != null)
            {
                return ToDataValues(dataset.Values);
            }

            return new SortedDictionary<DateTime,DataValue>();
        }

        /// <summary>
        /// Adds or updates a values in the specified processed dataset.
        /// </summary>
        public void UpdateProcessedValues(ProcessedDataSetType dataset, params DataValue[] newValues)
        {
            SortedDictionary<DateTime,DataValue> existingValues = ToDataValues(dataset.Values);

            if (newValues != null)
            {
                foreach (DataValue newValue in newValues)
                {
                    existingValues[newValue.SourceTimestamp] = newValue;
                }
            }

            dataset.Values = ToValueTypes(existingValues);
        }

        /// <summary>
        /// Sets the values for the specified processed dataset.
        /// </summary>
        public void SetProcessedValues(ProcessedDataSetType dataset, IList<DataValue> newValues)
        {
            SortedDictionary<DateTime, DataValue> values = new SortedDictionary<DateTime, DataValue>();

            if (newValues != null)
            {
                foreach (DataValue newValue in newValues)
                {
                    values[newValue.SourceTimestamp] = newValue;
                }
            }

            dataset.Values = ToValueTypes(values);
        }
        #endregion

        #region Static Methods
        /// <summary>
        /// Formats a value as string for serilization or display.
        /// </summary>
        public static string FormatValue(Variant value)
        {
            if (value == Variant.Null)
            {
                return String.Empty;
            }

            double? doubleValue = value.Value as double?;

            if (doubleValue != null)
            {
                if (doubleValue.Value != Math.Truncate(doubleValue.Value))
                {
                    return String.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F4}", doubleValue);
                }
            }
            
            return String.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", value);
        }

        /// <summary>
        /// Formats a timestamp as string for serilization or display.
        /// </summary>
        public static string FormatTimestamp(DateTime timestamp)
        {
            return String.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:HH:mm:ss.fff}", timestamp);
        }

        /// <summary>
        /// Formats a StatusCide as string for serilization or display.
        /// </summary>
        public static string FormatQuality(StatusCode statusCode)
        {
            StringBuilder buffer = new StringBuilder();
            buffer.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0}", new StatusCode(statusCode.CodeBits));

            if ((statusCode.AggregateBits & AggregateBits.Interpolated) != 0)
            {
                buffer.Append(", Interpolated");
            }

            if ((statusCode.AggregateBits & AggregateBits.Calculated) != 0)
            {
                buffer.Append(", Calculated");
            }

            if ((statusCode.AggregateBits & AggregateBits.Partial) != 0)
            {
                buffer.Append(", Partial");
            }

            if ((statusCode.AggregateBits & AggregateBits.MultipleValues) != 0)
            {
                buffer.Append(", MultipleValues");
            }

            return buffer.ToString();
        }

        /// <summary>
        /// Validates the formatted HA value and returns a Variant. 
        /// </summary>
        public static DateTime ValidateTimestamp(object formattedValue)
        {
            string value = formattedValue as string;

            if (String.IsNullOrEmpty(value))
            {
                throw new FormatException("Timestamp must not be null.");
            }

            DateTime timestamp = new DateTime(DateTime.Now.Year, 1, 1);

            string[] fields = value.Split(':');

            if (fields == null || fields.Length < 3)
            {
                throw new FormatException("Timestamp must have format 'HH:MM:SS'");
            }

            ushort hours  = Convert.ToUInt16(fields[0]);
            timestamp = timestamp.AddHours(hours);

            if (hours > 23)
            {
                throw new FormatException("The hour must be less than 24.'");
            }

            ushort minutes = Convert.ToUInt16(fields[1]);
            timestamp = timestamp.AddMinutes(minutes);

            if (minutes > 59)
            {
                throw new FormatException("The minute must be less than 60.'");
            }

            var secondsWithMs = fields[2].Split('.');

            double seconds = Convert.ToDouble(secondsWithMs[0]);
            timestamp = timestamp.AddSeconds(seconds);

            if (seconds > 60)
            {
              throw new FormatException("The second must be less than 60.'");
            }

            if (secondsWithMs.Length == 2)
            {
              double milliseconds = Convert.ToDouble(secondsWithMs[1]);
              timestamp.AddMilliseconds(milliseconds);
            }

            return timestamp;
        }

        /// <summary>
        /// Validates the formatted HA value and returns a Variant. 
        /// </summary>
        public static Variant ValidateValue(object formattedValue)
        {
            string value = formattedValue as string;

            if (String.IsNullOrEmpty(value))
            {
                return Variant.Null;
            }

            if (String.Compare(value, "true", StringComparison.InvariantCultureIgnoreCase) == 0)
            {
                return new Variant(true, TypeInfo.Scalars.Boolean);
            }

            if (String.Compare(value, "false", StringComparison.InvariantCultureIgnoreCase) == 0)
            {
                return new Variant(false, TypeInfo.Scalars.Boolean);
            }
            
            try
            {
                return new Variant(Convert.ToDouble(value), TypeInfo.Scalars.Double);
            }
            catch
            {
                try
                {
                    uint code = StatusCodes.GetIdentifier(value);
                    return new Variant(new StatusCode(code), TypeInfo.Scalars.StatusCode);
                }
                catch
                {
                    throw new FormatException("Could not parse field value.");
                }
            }
        }

        /// <summary>
        /// Validates the formatted HA quality code and returns a StatusCode. 
        /// </summary>
        public static StatusCode ValidateQuality(object formattedValue)
        {
            string value = formattedValue as string;

            if (String.IsNullOrEmpty(value))
            {
                return StatusCodes.Good;
            }

            string[] parts = value.Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

            StatusCode statusCode = StatusCodes.Good;

            if (parts.Length > 0)
            {
                switch (parts[0].Trim())
                {
                    case "G":
                    case "Good": { statusCode = StatusCodes.Good; break; }
                    case "B":
                    case "Bad": { statusCode = StatusCodes.Bad; break; }
                    case "U":
                    case "Uncertain": { statusCode = StatusCodes.Uncertain; break; }
                    case "USN":
                    case "UncertainDataSubNormal": { statusCode = StatusCodes.UncertainDataSubNormal; break; }
                    case "BND":
                    case "BadNoData": { statusCode = StatusCodes.BadNoData; break; }

                    default:
                    {
                        throw new FormatException("Quality must be a valid UA quality.");
                    }
                }
            }

            for (int ii = 1; ii < parts.Length; ii++)
            {
                switch (parts[ii].Trim())
                {
                    case "I":
                    case "Interpolated": { statusCode = statusCode.SetAggregateBits(statusCode.AggregateBits | AggregateBits.Interpolated); break; }
                    case "C": { statusCode = statusCode.SetAggregateBits(statusCode.AggregateBits | AggregateBits.Calculated); break; }
                    case "Calculated": { statusCode = statusCode.SetAggregateBits(statusCode.AggregateBits | AggregateBits.Calculated); break; }
                    case "P": { statusCode = statusCode.SetAggregateBits(statusCode.AggregateBits | AggregateBits.Partial); break; }
                    case "Partial": { statusCode = statusCode.SetAggregateBits(statusCode.AggregateBits | AggregateBits.Partial); break; }
                    case "M": { statusCode = statusCode.SetAggregateBits(statusCode.AggregateBits | AggregateBits.MultipleValues); break; }
                    case "MultipleValues": { statusCode = statusCode.SetAggregateBits(statusCode.AggregateBits | AggregateBits.MultipleValues); break; }
                    
                    default:
                    {
                        throw new FormatException("Aggregate bits are not valid.");
                    }
                }
            }

            return statusCode;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Converts a list of test values to a list of DataValues.
        /// </summary>
        private SortedDictionary<DateTime, DataValue> ToDataValues(ValueType[] values)
        {
            SortedDictionary<DateTime, DataValue> dvs = new SortedDictionary<DateTime, DataValue>();

            if (values != null)
            {
                foreach (ValueType value in values)
                {
                    DataValue dv = new DataValue();
                    dv.Value = ValidateValue(value.Value);
                    dv.StatusCode = ValidateQuality(value.Quality);
                    dv.SourceTimestamp = ValidateTimestamp(value.Timestamp);
                    dv.Comment = value.Comment;
                    dvs[dv.SourceTimestamp] = dv;
                }
            }

            return dvs;
        }

        /// <summary>
        /// Converts a list of DataValues to a list of test values.
        /// </summary>
        private ValueType[] ToValueTypes(SortedDictionary<DateTime, DataValue> values)
        {
            if (values == null || values.Count == 0)
            {
                return null;
            }

            List<ValueType> serializedValues = new List<ValueType>();

            foreach (DataValue dv in values.Values)
            {
                ValueType value = new ValueType();
                value.Timestamp = FormatTimestamp(dv.SourceTimestamp);
                value.Value = FormatValue(dv.WrappedValue);
                value.Quality = FormatQuality(dv.StatusCode);
                value.Comment = dv.Comment;
                serializedValues.Add(value);            
            }

            return serializedValues.ToArray();
        }
        #endregion
    }

    public partial class RawDataSetType
    {
        public override string ToString()
        {
            return this.Name;
        }
    }

    public partial class ProcessedDataSetType
    {
        public ProcessedDataSetType()
        {
        }

        public override string ToString()
        {
            if (Modified)
            {
                return Utils.Format("{0}*", this.Name);
            }

            return this.Name;
        }

        [System.Xml.Serialization.XmlIgnore()]
        public string Name { get; set; }

        [System.Xml.Serialization.XmlIgnore()]
        public bool Modified { get; set; }
    }
}
