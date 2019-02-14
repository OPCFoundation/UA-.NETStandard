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
using System.IO;
using System.Xml;
using System.Data;
using System.Reflection;
using Opc.Ua;

namespace Quickstarts.HistoricalAccessServer
{
    /// <summary>
    /// Reads an item history from a file.
    /// </summary>
    public class DataFileReader
    {
        /// <summary>
        /// Creates a new data set.
        /// </summary>
        private DataSet CreateDataSet()
        {
            DataSet dataset = new DataSet();

            dataset.Tables.Add("CurrentData");

            dataset.Tables[0].Columns.Add("SourceTimestamp", typeof(DateTime));
            dataset.Tables[0].Columns.Add("ServerTimestamp", typeof(DateTime));
            dataset.Tables[0].Columns.Add("Value", typeof(DataValue));
            dataset.Tables[0].Columns.Add("DataType", typeof(BuiltInType));
            dataset.Tables[0].Columns.Add("ValueRank", typeof(int));

            dataset.Tables[0].DefaultView.Sort = "SourceTimestamp";

            dataset.Tables.Add("ModifiedData");

            dataset.Tables[1].Columns.Add("SourceTimestamp", typeof(DateTime));
            dataset.Tables[1].Columns.Add("ServerTimestamp", typeof(DateTime));
            dataset.Tables[1].Columns.Add("Value", typeof(DataValue));
            dataset.Tables[1].Columns.Add("DataType", typeof(BuiltInType));
            dataset.Tables[1].Columns.Add("ValueRank", typeof(int));
            dataset.Tables[1].Columns.Add("UpdateType", typeof(int));
            dataset.Tables[1].Columns.Add("ModificationInfo", typeof(ModificationInfo));

            dataset.Tables[1].DefaultView.Sort = "SourceTimestamp";

            dataset.Tables.Add("AnnotationData");

            dataset.Tables[2].Columns.Add("SourceTimestamp", typeof(DateTime));
            dataset.Tables[2].Columns.Add("ServerTimestamp", typeof(DateTime));
            dataset.Tables[2].Columns.Add("Value", typeof(DataValue));
            dataset.Tables[2].Columns.Add("DataType", typeof(BuiltInType));
            dataset.Tables[2].Columns.Add("ValueRank", typeof(int));
            dataset.Tables[2].Columns.Add("Annotation", typeof(Annotation));

            dataset.Tables[2].DefaultView.Sort = "SourceTimestamp";
            
            return dataset;
        }

        /// <summary>
        /// Loads the item configuaration.
        /// </summary>
        public bool LoadConfiguration(ISystemContext context, ArchiveItem item)
        {
            using (StreamReader reader = item.OpenArchive())
            {
                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();

                    // check for end or error.
                    if (line == null)
                    {
                        break;
                    }

                    // ignore blank lines.
                    line = line.Trim();

                    if (String.IsNullOrEmpty(line))
                    {
                        continue;
                    }

                    // ignore commented out lines.
                    if (line.StartsWith("//"))
                    {
                        continue;
                    }

                    BuiltInType dataType = BuiltInType.String;
                    int valueRank = ValueRanks.Scalar;
                    int samplingInterval = 0;
                    int simulationType = 0;
                    int amplitude = 0;
                    int period = 0;
                    int archiving = 0;
                    int stepped = 0;
                    int useSlopedExtrapolation = 0;
                    int treatUncertainAsBad = 0;
                    int percentDataBad = 0;
                    int percentDataGood = 0;

                    // get data type.
                    if (!ExtractField(1, ref line, out dataType))
                    {
                        return false;
                    }

                    // get value rank.
                    if (!ExtractField(1, ref line, out valueRank))
                    {
                        return false;
                    }

                    // get sampling interval.
                    if (!ExtractField(1, ref line, out samplingInterval))
                    {
                        return false;
                    }

                    // get simulation type.
                    if (!ExtractField(1, ref line, out simulationType))
                    {
                        return false;
                    }

                    // get simulation amplitude.
                    if (!ExtractField(1, ref line, out amplitude))
                    {
                        return false;
                    }

                    // get simulation period.
                    if (!ExtractField(1, ref line, out period))
                    {
                        return false;
                    }

                    // get flag indicating whether new data is generated.
                    if (!ExtractField(1, ref line, out archiving))
                    {
                        return false;
                    }

                    // get flag indicating whether stepped interpolation is used.
                    if (!ExtractField(1, ref line, out stepped))
                    {
                        return false;
                    }

                    // get flag indicating whether sloped interpolation should be used.
                    if (!ExtractField(1, ref line, out useSlopedExtrapolation))
                    {
                        return false;
                    }

                    // get flag indicating whether sloped interpolation should be used.
                    if (!ExtractField(1, ref line, out treatUncertainAsBad))
                    {
                        return false;
                    }

                    // get the maximum permitted of bad data in an interval.
                    if (!ExtractField(1, ref line, out percentDataBad))
                    {
                        return false;
                    }

                    // get the minimum amount of good data in an interval.
                    if (!ExtractField(1, ref line, out percentDataGood))
                    {
                        return false;
                    }
                    
                    // update the item.
                    item.DataType = dataType;
                    item.ValueRank = valueRank;
                    item.SimulationType = simulationType;
                    item.Amplitude = amplitude;
                    item.Period = period;
                    item.SamplingInterval = samplingInterval;
                    item.Archiving = archiving != 0;
                    item.Stepped = stepped != 0;
                    item.AggregateConfiguration = new AggregateConfiguration();
                    item.AggregateConfiguration.UseServerCapabilitiesDefaults = false;
                    item.AggregateConfiguration.UseSlopedExtrapolation = useSlopedExtrapolation != 0;
                    item.AggregateConfiguration.TreatUncertainAsBad = treatUncertainAsBad != 0;
                    item.AggregateConfiguration.PercentDataBad = (byte)percentDataBad;
                    item.AggregateConfiguration.PercentDataGood = (byte)percentDataGood;
                    break;
                }
            }

            return true;
        }

        /// <summary>
        /// Creates new data.
        /// </summary>
        public void CreateData(ArchiveItem item)
        {
            // get the data set to use.
            DataSet dataset = item.DataSet;

            if (dataset == null)
            {
                dataset = CreateDataSet();
            }

            // generate one hour worth of data by default.
            DateTime startTime = DateTime.UtcNow.AddHours(-1);
            startTime = new DateTime(startTime.Year, startTime.Month, startTime.Day, startTime.Hour, 0, 0, DateTimeKind.Utc);

            // check for existing data.
            if (dataset.Tables[0].Rows.Count > 0)
            {
                int index = dataset.Tables[0].DefaultView.Count;
                DateTime endTime = (DateTime)dataset.Tables[0].DefaultView[index-1].Row[0];
                endTime = startTime.AddMilliseconds(item.SamplingInterval);
            }

            DateTime currentTime = startTime;
            Opc.Ua.Test.DataGenerator generator = new Opc.Ua.Test.DataGenerator(null);

            while (currentTime < DateTime.UtcNow)
            {
                DataValue dataValue = new DataValue();
                dataValue.SourceTimestamp = currentTime;
                dataValue.ServerTimestamp = currentTime.AddSeconds(generator.GetRandomByte());
                dataValue.StatusCode = StatusCodes.Good;

                // generate random value.
                if (item.ValueRank < 0)
                {
                    dataValue.Value = generator.GetRandom(item.DataType);
                }
                else
                {
                    dataValue.Value = generator.GetRandomArray(item.DataType, false, 10, false);
                }
                
                // add record to table.
                DataRow row = dataset.Tables[0].NewRow();

                row[0] = dataValue.SourceTimestamp;
                row[1] = dataValue.ServerTimestamp;
                row[2] = dataValue;
                row[3] = dataValue.WrappedValue.TypeInfo.BuiltInType;
                row[4] = dataValue.WrappedValue.TypeInfo.ValueRank;

                dataset.Tables[0].Rows.Add(row);

                // increment timestamp.
                currentTime = currentTime.AddMilliseconds(item.SamplingInterval);
            }

            dataset.AcceptChanges();
            item.DataSet = dataset;
        }

        /// <summary>
        /// Loads the history for the item.
        /// </summary>
        public void LoadHistoryData(ISystemContext context, ArchiveItem item)
        {
            // use the beginning of the current hour for the baseline.
            DateTime baseline = DateTime.UtcNow;
            baseline = new DateTime(baseline.Year, baseline.Month, baseline.Day, baseline.Hour, 0, 0, DateTimeKind.Utc);

            using (StreamReader reader = item.OpenArchive())
            {
                // skip configuration line.
                reader.ReadLine();
                item.DataSet = LoadData(context, baseline, reader);
            }

            // create a random dataset if nothing found in the archive,
            if (item.DataSet == null || item.DataSet.Tables[0].Rows.Count == 0)
            {
                CreateData(item);
            }

            // update the timestamp.
            item.LastLoadTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Loads the history data from a stream.
        /// </summary>
        private DataSet LoadData(ISystemContext context, DateTime baseline, StreamReader reader)
        {
            DataSet dataset = CreateDataSet();
         
            ServiceMessageContext messageContext = new ServiceMessageContext();

            if (context != null)
            {
                messageContext.NamespaceUris = context.NamespaceUris;
                messageContext.ServerUris = context.ServerUris;
                messageContext.Factory = context.EncodeableFactory;
            }
            else
            {
                messageContext.NamespaceUris = ServiceMessageContext.GlobalContext.NamespaceUris;
                messageContext.ServerUris = ServiceMessageContext.GlobalContext.ServerUris;
                messageContext.Factory = ServiceMessageContext.GlobalContext.Factory;
            }

            int sourceTimeOffset = 0;
            int serverTimeOffset = 0;
            StatusCode status = StatusCodes.Good;
            int recordType = 0;
            int modificationTimeOffet = 0;
            string modificationUser = String.Empty;
            BuiltInType valueType = BuiltInType.String;
            Variant value = Variant.Null;
            int annotationTimeOffet = 0;
            string annotationUser = String.Empty;
            string annotationMessage = String.Empty;
            int lineCount = 0;

            while (!reader.EndOfStream)
            {
                string line = reader.ReadLine();

                // check for end or error.
                if (line == null)
                {
                    break;
                }

                // ignore blank lines.
                line = line.Trim();
                lineCount++;
                
                if (String.IsNullOrEmpty(line))
                {
                    continue;
                }

                // ignore commented out lines.
                if (line.StartsWith("//"))
                {
                    continue;
                }

                // get source time.
                if (!ExtractField(lineCount, ref line, out sourceTimeOffset))
                {
                    continue;
                }

                // get server time.
                if (!ExtractField(lineCount, ref line, out serverTimeOffset))
                {
                    continue;
                }

                // get status code.
                if (!ExtractField(lineCount, ref line, out status))
                {
                    continue;
                }
                
                // get modification type.
                if (!ExtractField(lineCount, ref line, out recordType))
                {
                    continue;
                }

                // get modification time.
                if (!ExtractField(lineCount, ref line, out modificationTimeOffet))
                {
                    continue;
                }

                // get modification user.
                if (!ExtractField(lineCount, ref line, out modificationUser))
                {
                    continue;
                }

                if (recordType >= 0)
                {
                    // get value type.
                    if (!ExtractField(lineCount, ref line, out valueType))
                    {
                        continue;
                    }

                    // get value.
                    if (!ExtractField(lineCount, ref line, messageContext, valueType, out value))
                    {
                        continue;
                    }
                }
                else
                {
                    // get annotation time.
                    if (!ExtractField(lineCount, ref line, out annotationTimeOffet))
                    {
                        continue;
                    }

                    // get annotation user.
                    if (!ExtractField(lineCount, ref line, out annotationUser))
                    {
                        continue;
                    }

                    // get annotation message.
                    if (!ExtractField(lineCount, ref line, out annotationMessage))
                    {
                        continue;
                    }
                }

                // add values to data table.
                DataValue dataValue = new DataValue();
                dataValue.WrappedValue = value;
                dataValue.SourceTimestamp = baseline.AddMilliseconds(sourceTimeOffset);
                dataValue.ServerTimestamp = baseline.AddMilliseconds(serverTimeOffset);
                dataValue.StatusCode = status;

                DataRow row = null;

                if (recordType == 0)
                {
                    row = dataset.Tables[0].NewRow();

                    row[0] = dataValue.SourceTimestamp;
                    row[1] = dataValue.ServerTimestamp;
                    row[2] = dataValue;
                    row[3] = valueType;
                    row[4] = (value.TypeInfo != null) ? value.TypeInfo.ValueRank : ValueRanks.Any;

                    dataset.Tables[0].Rows.Add(row);
                }

                else if (recordType > 0)
                {
                    row = dataset.Tables[1].NewRow();

                    row[0] = dataValue.SourceTimestamp;
                    row[1] = dataValue.ServerTimestamp;
                    row[2] = dataValue;
                    row[3] = valueType;
                    row[4] = (value.TypeInfo != null) ? value.TypeInfo.ValueRank : ValueRanks.Any;
                    row[5] = recordType;

                    ModificationInfo info = new ModificationInfo();
                    info.UpdateType = (HistoryUpdateType)recordType;
                    info.ModificationTime = baseline.AddMilliseconds(modificationTimeOffet);
                    info.UserName = modificationUser;
                    row[6] = info;

                    dataset.Tables[1].Rows.Add(row);
                }

                else if (recordType < 0)
                {
                    row = dataset.Tables[2].NewRow();

                    Annotation annotation = new Annotation();
                    annotation.AnnotationTime = baseline.AddMilliseconds(annotationTimeOffet);
                    annotation.UserName = annotationUser;
                    annotation.Message = annotationMessage;
                    dataValue.WrappedValue = new ExtensionObject(annotation);

                    row[0] = dataValue.SourceTimestamp;
                    row[1] = dataValue.ServerTimestamp;
                    row[2] = dataValue;
                    row[3] = valueType;
                    row[4] = (value.TypeInfo != null) ? value.TypeInfo.ValueRank : ValueRanks.Any;
                    row[5] = annotation;

                    dataset.Tables[2].Rows.Add(row);
                }

                dataset.AcceptChanges();
            }

            return dataset;
        }

        #region Parsing Functions
        /// <summary>
        /// Extracts the next comma seperated field from the line.
        /// </summary>
        private string ExtractField(ref string line)
        {
            string field = line;
            int index = field.IndexOf(',');

            if (index >= 0)
            {
                field = field.Substring(0, index);
                line = line.Substring(index + 1);
            }

            field = field.Trim();

            if (String.IsNullOrEmpty(field))
            {
                return null;
            }

            return field;
        }

        /// <summary>
        /// Extracts an integer value from the line.
        /// </summary>
        private bool ExtractField(int lineCount, ref string line, out string value)
        {
            value = String.Empty;
            string field = ExtractField(ref line);

            if (field == null)
            {
                return true;
            }

            value = field;
            return true;
        }
        
        /// <summary>
        /// Extracts an integer value from the line.
        /// </summary>
        private bool ExtractField(int lineCount, ref string line, out int value)
        {
            value = 0;
            string field = ExtractField(ref line);

            if (field == null)
            {
                return true;
            }

            try
            {
                value = System.Convert.ToInt32(field);
            }
            catch (Exception e)
            {
                Utils.Trace("PARSE ERROR [Line:{0}] - '{1}': {2}", lineCount, field, e.Message);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Extracts a StatusCode value from the line.
        /// </summary>
        private bool ExtractField(int lineCount, ref string line, out StatusCode value)
        {
            value = 0;
            string field = ExtractField(ref line);

            if (field == null)
            {
                return true;
            }

            if (field.StartsWith("0x"))
            {
                field = field.Substring(2);
            }

            try
            {
                uint code = System.Convert.ToUInt32(field, 16);
                value = new StatusCode(code);
            }
            catch (Exception e)
            {
                Utils.Trace("PARSE ERROR [Line:{0}] - '{1}': {2}", lineCount, field, e.Message);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Extracts a BuiltInType value from the line.
        /// </summary>
        private bool ExtractField(int lineCount, ref string line, out BuiltInType value)
        {
            value = BuiltInType.String;
            string field = ExtractField(ref line);

            if (field == null)
            {
                return true;
            }

            try
            {
                value = (BuiltInType)Enum.Parse(typeof(BuiltInType), field);
            }
            catch (Exception e)
            {
                Utils.Trace("PARSE ERROR [Line:{0}] - '{1}': {2}", lineCount, field, e.Message);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Extracts a BuiltInType value from the line.
        /// </summary>
        private bool ExtractField(int lineCount, ref string line, ServiceMessageContext context, BuiltInType valueType, out Variant value)
        {
            value = Variant.Null;
            string field = line;

            if (field == null)
            {
                return true;
            }

            if (valueType == BuiltInType.Null)
            {
                return true;
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendFormat("<Value xmlns=\"{0}\">", Opc.Ua.Namespaces.OpcUaXsd);
            builder.AppendFormat("<{0}>", valueType);
            builder.Append(line);
            builder.AppendFormat("</{0}>", valueType);
            builder.Append("</Value>");

            XmlDocument document = new XmlDocument();
            document.InnerXml = builder.ToString();

            try
            {
                XmlDecoder decoder = new XmlDecoder(document.DocumentElement, context);
                value = decoder.ReadVariant(null);
            }
            catch (Exception e)
            {
                Utils.Trace("PARSE ERROR [Line:{0}] - '{1}': {2}", lineCount, field, e.Message);
                return false;
            }

            return true;
        }
        #endregion
    }
}
