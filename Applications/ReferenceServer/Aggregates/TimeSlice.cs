/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Aggregates
{
    /// <summary>
    /// Represents one aggregation interval within an aggregation query.
    /// </summary>
    public abstract class TimeSlice
    {
        /// <summary>
        /// Start date of the time slice, which may be later than the end date.
        /// </summary>
        public DateTime From { get; set; }

        /// <summary>
        /// End date of the time slice, which may be earlier than the start date.
        /// </summary>
        public DateTime To { get; set; }

        /// <summary>
        /// Indicates that the timeslice is not as long as requested because the
        /// EndTime of the aggregation query occurred.
        /// </summary>
        public bool Incomplete { get; set; }

        /// <summary>
        /// Enumerator over the raw points that fall in this TimeSlice.
        /// </summary>
        public IEnumerable<DataValue> Values
        {
            get { return Accumulator; }
        }
        
        /// <summary>
        /// Collection of raw points that fall in this TimeSlice.
        /// </summary>
        protected List<DataValue> Accumulator = new List<DataValue>();

        /// <summary>
        /// Value and provenance of a value at the earliest time of this slice
        /// </summary>
        public BoundingValue EarlyBound { get; set; }

        /// <summary>
        /// Value and provenance of a value at the latest time of this slice
        /// </summary>
        public BoundingValue LateBound { get; set; }

        /// <summary>
        /// Converts the floating-point representation of the millisecond interval into a count
        /// of 100-nanosecond ticks.
        /// </summary>
        /// <param name="millis"></param>
        /// <returns></returns>
        protected static long MillisecondsToTicks(double millis)
        {
            return (long) Math.Round(millis * 10000);
        }

        /// <summary>
        /// Create the first (or only) TimeSlice in a sequence.
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <param name="inc"></param>
        /// <returns></returns>
        public static TimeSlice CreateInitial(DateTime from, DateTime to, double inc)
        {
            TimeSlice retval = null;
            bool incomplete = false;
            DateTime later;
            if (from > to)
            {
                later = from;
                if (inc > 0.0)
                {
                    long intMillis = MillisecondsToTicks(inc);
                    long totMillis = MillisecondsToTicks((from - to).TotalMilliseconds);
                    long remMillis = totMillis % intMillis;
                    later = (remMillis > 0) ? to + new TimeSpan(remMillis) : to + new TimeSpan(intMillis);
                    incomplete = (remMillis > 0);
                }
                retval = new BackwardTimeSlice
                {
                    From = later,
                    To = to,
                    Incomplete = incomplete,
                    EarlyBound = new BoundingValue { Timestamp = to },
                    LateBound = new BoundingValue { Timestamp = later }
                };
            }
            else if (to > from)
            {
                later = to;
                if (inc > 0.0)
                {
                    later = from + new TimeSpan(MillisecondsToTicks(inc));
                    if (later > to)
                    {
                        later = to;
                        incomplete = true;
                    }
                }
                retval = new ForwardTimeSlice
                {
                    From = from,
                    To = later,
                    Incomplete = incomplete,
                    EarlyBound = new BoundingValue { Timestamp = from },
                    LateBound = new BoundingValue { Timestamp = later }
                };
            }
            //deliberately ignore (if from == to)
            return retval;
        }
        
        /// <summary>
        /// For the given predecessor, create the next TimeSlice in the sequence.
        /// </summary>
        /// <param name="latest"></param>
        /// <param name="inc"></param>
        /// <param name="predecessor"></param>
        /// <returns></returns>
        public static TimeSlice CreateNext(DateTime latest, double inc, TimeSlice predecessor)
        {
            return predecessor.CreateSuccessor(latest, inc);
        }
        
        /// <summary>
        /// Used to determine whether there is a raw data point whose time stamp exactly matches
        /// the TimeSlice. If so, the processed data point may need to indicate that it is
        /// a raw value.
        /// </summary>
        /// <param name="timestamp"></param>
        /// <returns></returns>
        public bool ExactMatch(DateTime timestamp)
        {
            return timestamp.Equals(From);
        }

        /// <summary>
        /// Used to determine whether there is a raw data point whose time stamp exactly matches
        /// the end of the TimeSlice.
        /// </summary>
        /// <param name="timestamp"></param>
        /// <returns></returns>
        public bool EndMatch(DateTime timestamp)
        {
            return timestamp.Equals(To);
        }

        /// <summary>
        /// Tests to see whether a raw value can be added to this TimeSlice and adds it if so.
        /// </summary>
        /// <param name="rawValue"></param>
        /// <returns>true if the data value was added</returns>
        public bool AcceptValue(DataValue rawValue)
        {
            if (ContainsTime(rawValue.SourceTimestamp))
            {
                Accumulator.Add(rawValue);
                if (rawValue.StatusCode.Equals(StatusCodes.BadNoData))
                    this.Incomplete = true;
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Create the TimeSlice that immediately follows this one in time.
        /// </summary>
        /// <param name="latest"></param>
        /// <param name="inc"></param>
        /// <returns></returns>
        protected abstract TimeSlice CreateSuccessor(DateTime latest, double inc);

        /// <summary>
        /// Tests whether a DateTime falls within the TimeSlice
        /// </summary>
        /// <param name="time"></param>
        /// <returns>true if the DateTime is within the TimeSlice</returns>
        public abstract bool ContainsTime(DateTime time);

        /// <summary>
        /// Used to determine whether we might be able to release a processed data point for
        /// this TimeSlice, given the timestamp from the most recent raw data point.
        /// </summary>
        /// <param name="timestamp"></param>
        /// <returns></returns>
        public abstract bool Releasable(DateTime timestamp);
    }

    /// <summary>
    /// A TimeSlice for forward motion through time.
    /// </summary>
    public class ForwardTimeSlice : TimeSlice
    {
        /// <summary>
        /// Create the TimeSlice that immediately follows this one in time.
        /// </summary>
        /// <param name="latest"></param>
        /// <param name="inc"></param>
        /// <returns></returns>
        protected override TimeSlice CreateSuccessor(DateTime latest, double inc)
        {
            TimeSlice retval = null;
            if (To < latest)
            {
                bool incomplete = false;
                DateTime target = To + new TimeSpan(MillisecondsToTicks(inc));
                if (target > latest)
                {
                    target = latest;
                    incomplete = true;
                }
                retval = new ForwardTimeSlice
                {
                    From = this.To,
                    To = target,
                    Incomplete = incomplete,
                    EarlyBound = this.LateBound,
                    LateBound = new BoundingValue { Timestamp = target }
                };
            }
            return retval;
        }

        /// <summary>
        /// Tests whether a DateTime falls within the TimeSlice
        /// </summary>
        /// <param name="time"></param>
        /// <returns>true if the DateTime is within the TimeSlice</returns>
        public override bool ContainsTime(DateTime time)
        {
            return ((time >= From) && (time < To));
        }

        /// <summary>
        /// Used to determine whether we might be able to release a processed data point for
        /// this TimeSlice, given the timestamp from the most recent raw data point.
        /// </summary>
        /// <param name="timestamp"></param>
        /// <returns></returns>
        public override bool Releasable(DateTime timestamp)
        {
            return timestamp >= To;
        }
    }

    /// <summary>
    /// A TimeSlice for backward motion through time.
    /// </summary>
    public class BackwardTimeSlice : TimeSlice
    {
        /// <summary>
        /// Create the TimeSlice that immediately follows this one in time.
        /// </summary>
        /// <param name="latest"></param>
        /// <param name="inc"></param>
        /// <returns></returns>
        protected override TimeSlice CreateSuccessor(DateTime latest, double inc)
        {
            TimeSlice retval = null;
            if (From < latest)
            {
                DateTime target = From + new TimeSpan(MillisecondsToTicks(inc));
                retval = new BackwardTimeSlice
                {
                    From = target,
                    To = this.From,
                    Incomplete = false,
                    EarlyBound = this.LateBound,
                    LateBound = new BoundingValue { Timestamp = target }
                };
            }
            return retval;
        }

        /// <summary>
        /// Tests whether a DateTime falls within the TimeSlice
        /// </summary>
        /// <param name="time"></param>
        /// <returns>true if the DateTime is within the TimeSlice</returns>
        public override bool ContainsTime(DateTime time)
        {
            return ((time <= From) && (time > To));
        }

        /// <summary>
        /// Used to determine whether we might be able to release a processed data point for
        /// this TimeSlice, given the timestamp from the most recent raw data point.
        /// </summary>
        /// <param name="timestamp"></param>
        /// <returns></returns>
        public override bool Releasable(DateTime timestamp)
        {
            return timestamp > From;
        }
    }
}
