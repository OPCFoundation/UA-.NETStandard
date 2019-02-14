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
using System.Data;
using Opc.Ua;

namespace Quickstarts.HistoricalEvents.Server
{
    public class ReportGenerator
    {
        public void Initialize()
        {
            m_dataset = new DataSet();

            m_dataset.Tables.Add("FluidLevelTests");
            m_dataset.Tables[0].Columns.Add(Opc.Ua.BrowseNames.EventId, typeof(string));
            m_dataset.Tables[0].Columns.Add(Opc.Ua.BrowseNames.Time, typeof(DateTime));
            m_dataset.Tables[0].Columns.Add(BrowseNames.NameWell, typeof(string));
            m_dataset.Tables[0].Columns.Add(BrowseNames.UidWell, typeof(string));
            m_dataset.Tables[0].Columns.Add(BrowseNames.TestDate, typeof(DateTime));
            m_dataset.Tables[0].Columns.Add(BrowseNames.TestReason, typeof(string));
            m_dataset.Tables[0].Columns.Add(BrowseNames.FluidLevel, typeof(double));
            m_dataset.Tables[0].Columns.Add(Opc.Ua.BrowseNames.EngineeringUnits, typeof(string));
            m_dataset.Tables[0].Columns.Add(BrowseNames.TestedBy, typeof(string));
            
            m_dataset.Tables.Add("InjectionTests");
            m_dataset.Tables[1].Columns.Add(Opc.Ua.BrowseNames.EventId, typeof(string));
            m_dataset.Tables[1].Columns.Add(Opc.Ua.BrowseNames.Time, typeof(DateTime));
            m_dataset.Tables[1].Columns.Add(BrowseNames.NameWell, typeof(string));
            m_dataset.Tables[1].Columns.Add(BrowseNames.UidWell, typeof(string));
            m_dataset.Tables[1].Columns.Add(BrowseNames.TestDate, typeof(DateTime));
            m_dataset.Tables[1].Columns.Add(BrowseNames.TestReason, typeof(string));
            m_dataset.Tables[1].Columns.Add(BrowseNames.TestDuration, typeof(double));
            m_dataset.Tables[1].Columns.Add(Opc.Ua.BrowseNames.EngineeringUnits, typeof(string));
            m_dataset.Tables[1].Columns.Add(BrowseNames.InjectedFluid, typeof(string));

            m_random = new Random();

            // look up the local timezone.
            TimeZone timeZone = TimeZone.CurrentTimeZone;
            m_timeZone = new TimeZoneDataType();
            m_timeZone.Offset = (short)timeZone.GetUtcOffset(DateTime.Now).TotalMinutes;
            m_timeZone.DaylightSavingInOffset = timeZone.IsDaylightSavingTime(DateTime.Now);
        }

        #region Hardcoded Source Data
        static readonly string[] s_WellNames = new string[]
        {
            "Area51/Jupiter",
            "Area51/Titan",
            "Area99/Saturn",
            "Area99/Mars"
        };
        
        static readonly string[] s_WellUIDs = new string[]
        {
            "Well_24412",
            "Well_48306",
            "Well_86234",
            "Well_91423"
        };
        
        static readonly string[] s_TestReasons = new string[]
        {
            "initial",
            "periodic",
            "revision",
            "unknown",
            "other"
        };
        
        static readonly string[] s_Testers = new string[]
        {
            "Anne",
            "Bob",
            "Charley",
            "Dawn"
        };
        
        static readonly string[] s_UnitLengths = new string[]
        {
            "m",
            "yd"
        };
        
        static readonly string[] s_UnitTimes = new string[]
        {
            "s",
            "min",
            "h"
        };

        static readonly string[] s_InjectionFluids = new string[]
        {
            "oil",
            "gas",
            "non HC gas",
            "CO2",
            "water",
            "brine",
            "fresh water",
            "oil-gas",
            "oil-water",
            "gas-water",
            "condensate",
            "steam",
            "air",
            "dry",
            "unknown",
            "other"
        };
        #endregion

        private int GetRandom(int min, int max)
        {
            return (int)(Math.Truncate(m_random.NextDouble()*(max-min+1) + min));
        }

        private string GetRandom(string[] values)
        {
            return values[GetRandom(0, values.Length-1)];
        }
        
        public string[] GetAreas()
        {
            List<string> area = new List<string>();

            for (int ii = 0; ii < s_WellNames.Length; ii++)
            {
                int index = s_WellNames[ii].LastIndexOf('/');

                if (index >= 0)
                {
                    string areaName = s_WellNames[ii].Substring(0, index);
                    
                    if (!area.Contains(areaName))
                    {
                        area.Add(areaName);
                    }
                }
            }

            return area.ToArray();
        }

        public WellInfo[] GetWells(string areaName)
        {
            List<WellInfo> wells = new List<WellInfo>();

            for (int ii = 0; ii < s_WellUIDs.Length; ii++)
            {
                WellInfo well = new WellInfo();
                well.Id = s_WellUIDs[ii];
                well.Name = s_WellUIDs[ii];

                if (s_WellNames.Length > ii)
                {
                    int index = s_WellNames[ii].LastIndexOf('/');

                    if (index >= 0)
                    {
                        if (s_WellNames[ii].Substring(0, index) == areaName)
                        {
                            well.Name = s_WellNames[ii].Substring(index + 1);
                            wells.Add(well);
                        }
                    }
                }
            }

            return wells.ToArray();
        }

        public class WellInfo
        {
            public string Id;
            public string Name;
        }

        public DataRow GenerateFluidLevelTestReport()
        {
            DataRow row = m_dataset.Tables[0].NewRow();

            row[0] = Guid.NewGuid().ToString();
            row[1] = DateTime.UtcNow;

            int index = GetRandom(0, s_WellUIDs.Length - 1);
            row[2] = s_WellNames[index];
            row[3] = s_WellUIDs[index];

            row[4] = DateTime.UtcNow.AddHours(-GetRandom(0, 10));
            row[5] = GetRandom(s_TestReasons);
            row[6] = GetRandom(0, 1000);
            row[7] = GetRandom(s_UnitLengths);
            row[8] = GetRandom(s_Testers);
           
            m_dataset.Tables[0].Rows.Add(row);
            m_dataset.AcceptChanges();

            return row;
        }

        /// <summary>
        /// Deletes the event with the specified event id.
        /// </summary>
        public bool DeleteEvent(string eventId)
        {
            StringBuilder filter = new StringBuilder();

            filter.Append('(');
            filter.Append(Opc.Ua.BrowseNames.EventId);
            filter.Append('=');
            filter.Append('\'');
            filter.Append(eventId);
            filter.Append('\'');
            filter.Append(')');

            for (int ii = 0; ii < m_dataset.Tables.Count; ii++)
            {
                DataView view = new DataView(m_dataset.Tables[ii], filter.ToString(), null, DataViewRowState.CurrentRows);

                if (view.Count > 0)
                {
                    view[0].Delete();
                    m_dataset.AcceptChanges();
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reads the report history for the specified time range.
        /// </summary>
        public DataView ReadHistoryForWellId(ReportType reportType, string uidWell, DateTime startTime, DateTime endTime)
        {
            StringBuilder filter = new StringBuilder();

            filter.Append('(');
            filter.Append(BrowseNames.UidWell);
            filter.Append('=');
            filter.Append('\'');
            filter.Append(uidWell);
            filter.Append('\'');
            filter.Append(')');

            return ReadHistory(reportType, filter, startTime, endTime);
        }

        /// <summary>
        /// Reads the report history for the specified time range.
        /// </summary>
        public DataView ReadHistoryForArea(ReportType reportType, string areaName, DateTime startTime, DateTime endTime)
        {
            StringBuilder filter = new StringBuilder();

            if (!String.IsNullOrEmpty(areaName))
            {
                filter.Append('(');
                filter.Append(BrowseNames.NameWell);
                filter.Append(" LIKE ");
                filter.Append('\'');
                filter.Append(areaName);
                filter.Append('*');
                filter.Append('\'');
                filter.Append(')');
            }

            return ReadHistory(reportType, filter, startTime, endTime);
        }

        /// <summary>
        /// Reads the history for the specified time range.
        /// </summary>
        private DataView ReadHistory(ReportType reportType, StringBuilder filter, DateTime startTime, DateTime endTime)
        {
            DateTime earlyTime = startTime;
            DateTime lateTime = endTime;

            if (endTime < startTime && endTime != DateTime.MinValue)
            {
                earlyTime = endTime;
                lateTime = startTime;
            }

            if (earlyTime != DateTime.MinValue)
            {
                if (filter.Length > 0)
                {
                    filter.Append(" AND ");
                }

                filter.Append('(');
                filter.Append(Opc.Ua.BrowseNames.Time);
                filter.Append(">=");
                filter.Append('#');
                filter.Append(earlyTime);
                filter.Append('#');
                filter.Append(')');
            }

            if (lateTime != DateTime.MinValue)
            {
                if (filter.Length > 0)
                {
                    filter.Append(" AND ");
                }

                filter.Append('(');
                filter.Append(Opc.Ua.BrowseNames.Time);
                filter.Append('<');
                filter.Append('#');
                filter.Append(lateTime);
                filter.Append('#');
                filter.Append(')');
            }

            DataView view = new DataView(
                m_dataset.Tables[(int)reportType],
                filter.ToString(),
                Opc.Ua.BrowseNames.Time,
                DataViewRowState.CurrentRows);

            return view;
        }

        /// <summary>
        /// Converts the DB row to a UA event,
        /// </summary>
        /// <param name="context">The UA context to use for the conversion.</param>
        /// <param name="namespaceIndex">The index assigned to the type model namespace.</param>
        /// <param name="reportType">The type of report.</param>
        /// <param name="row">The source for the report.</param>
        /// <returns>The new report.</returns>
        public Opc.Ua.BaseEventState GetReport(ISystemContext context, ushort namespaceIndex, ReportType reportType, DataRow row)
        {
            switch (reportType)
            {
                case ReportType.FluidLevelTest: return GetFluidLevelTestReport(context, namespaceIndex, row);
                case ReportType.InjectionTest: return GetInjectionTestReport(context, namespaceIndex, row);
            }

            return null;
        }

        public Opc.Ua.BaseEventState GetFluidLevelTestReport(ISystemContext SystemContext, ushort namespaceIndex, DataRow row)
        {
            // construct translation object with default text.
            TranslationInfo info = new TranslationInfo(
                "FluidLevelTestReport",
                "en-US",
                "A fluid level test report is available.");

            // construct the event.
            FluidLevelTestReportState e = new FluidLevelTestReportState(null);

            e.Initialize(
                SystemContext,
                null,
                EventSeverity.Medium,
                new LocalizedText(info));

            // override event id and time.                
            e.EventId.Value = new Guid((string)row[Opc.Ua.BrowseNames.EventId]).ToByteArray();
            e.Time.Value = (DateTime)row[Opc.Ua.BrowseNames.Time];

            
            string nameWell = (string)row[BrowseNames.NameWell];
            string uidWell = (string)row[BrowseNames.UidWell];

            e.SetChildValue(SystemContext, Opc.Ua.BrowseNames.SourceName, nameWell, false);
            e.SetChildValue(SystemContext, Opc.Ua.BrowseNames.SourceNode, new NodeId(uidWell, namespaceIndex), false);
            e.SetChildValue(SystemContext, Opc.Ua.BrowseNames.LocalTime, m_timeZone, false);

            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.NameWell, namespaceIndex), nameWell, false);
            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.UidWell, namespaceIndex), uidWell, false);
            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.TestDate, namespaceIndex), row[BrowseNames.TestDate], false);
            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.TestReason, namespaceIndex), row[BrowseNames.TestReason], false);
            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.TestedBy, namespaceIndex), row[BrowseNames.TestedBy], false);
            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.FluidLevel, namespaceIndex), row[BrowseNames.FluidLevel], false);
            e.FluidLevel.SetChildValue(SystemContext, Opc.Ua.BrowseNames.EngineeringUnits, new EUInformation((string)row[Opc.Ua.BrowseNames.EngineeringUnits], Namespaces.HistoricalEvents), false);

            return e;
        }

        public DataRow GenerateInjectionTestReport()
        {
            DataRow row = m_dataset.Tables[1].NewRow();

            row[0] = Guid.NewGuid().ToString();
            row[1] = DateTime.UtcNow;

            int index = GetRandom(0, s_WellUIDs.Length - 1);
            row[2] = s_WellNames[index];
            row[3] = s_WellUIDs[index];

            row[4] = DateTime.UtcNow.AddHours(-GetRandom(0, 10));
            row[5] = GetRandom(s_TestReasons);
            row[6] = GetRandom(0, 1000);
            row[7] = GetRandom(s_UnitTimes);
            row[8] = GetRandom(s_InjectionFluids);
           
            m_dataset.Tables[1].Rows.Add(row);
            m_dataset.AcceptChanges();

            return row;
        }

        public DataRow UpdateeInjectionTestReport(DataRow row, IList<SimpleAttributeOperand> fields, IList<Variant> values)
        {
            return row;
        }

        public Opc.Ua.BaseEventState GetInjectionTestReport(ISystemContext SystemContext, ushort namespaceIndex, DataRow row)
        {
            // construct translation object with default text.
            TranslationInfo info = new TranslationInfo(
                "InjectionTestReport",
                "en-US",
                "An injection test report is available.");

            // construct the event.
            InjectionTestReportState e = new InjectionTestReportState(null);

            e.Initialize(
                SystemContext,
                null,
                EventSeverity.Medium,
                new LocalizedText(info));

            // override event id and time.                
            e.EventId.Value = new Guid((string)row[Opc.Ua.BrowseNames.EventId]).ToByteArray();
            e.Time.Value = (DateTime)row[Opc.Ua.BrowseNames.Time];

            string nameWell = (string)row[BrowseNames.NameWell];
            string uidWell = (string)row[BrowseNames.UidWell];

            e.SetChildValue(SystemContext, Opc.Ua.BrowseNames.SourceName, nameWell, false);
            e.SetChildValue(SystemContext, Opc.Ua.BrowseNames.SourceNode, new NodeId(uidWell, namespaceIndex), false);
            e.SetChildValue(SystemContext, Opc.Ua.BrowseNames.LocalTime, m_timeZone, false);

            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.NameWell, namespaceIndex), nameWell, false);
            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.UidWell, namespaceIndex), uidWell, false);
            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.TestDate, namespaceIndex), row[BrowseNames.TestDate], false);
            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.TestReason, namespaceIndex), row[BrowseNames.TestReason], false);
            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.InjectedFluid, namespaceIndex), row[BrowseNames.InjectedFluid], false);
            e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.TestDuration, namespaceIndex), row[BrowseNames.TestDuration], false);
            e.TestDuration.SetChildValue(SystemContext, Opc.Ua.BrowseNames.EngineeringUnits, new EUInformation((string)row[Opc.Ua.BrowseNames.EngineeringUnits], Namespaces.HistoricalEvents), false);

            return e;
        }

        private DataSet m_dataset;
        private Random m_random;
        private TimeZoneDataType m_timeZone;
    }

    public enum ReportType
    {
        FluidLevelTest,
        InjectionTest
    }
}
