/* ========================================================================
 * Copyright (c) 2005-2022 The OPC Foundation, Inc. All rights reserved.
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


#pragma warning disable CS1591

namespace Alarms
{
    public class AlarmDefines
    {
        public const int MAX_VALUE = 100;
        public const int MIN_VALUE = 0;
        public const int NORMAL_START_VALUE = 50;

        public const int HIGHHIGH_ALARM = 90;
        public const int HIGH_ALARM = 70;
        public const int LOW_ALARM = 30;
        public const int LOWLOW_ALARM = 10;

        public const int BOOL_HIGH_ALARM = 80;
        public const int BOOL_LOW_ALARM = 20;

        public const int INACTIVE_SEVERITY = 100;

        public const int HIGHHIGH_SEVERITY = 850;
        public const int HIGH_SEVERITY = 450;
        public const int LOW_SEVERITY = 400;
        public const int LOWLOW_SEVERITY = 800;

        public const int BOOL_SEVERITY = 500;

        public const double NORMAL_MAX_TIME_SHELVED = 2000000;
        public const double SHORT_MAX_TIME_SHELVED = 30000;

        public const int MILLISECONDS_PER_SECOND = 1000;
        public const int MILLISECONDS_PER_MINUTE = 60 * MILLISECONDS_PER_SECOND;
        public const int MILLISECONDS_PER_HOUR = 60 * MILLISECONDS_PER_MINUTE;
        public const int MILLISECONDS_PER_DAY = 24 * MILLISECONDS_PER_HOUR;
        public const int MILLISECONDS_PER_WEEK = 7 * MILLISECONDS_PER_DAY;
        public const int MILLISECONDS_PER_TWO_WEEKS = 2 * MILLISECONDS_PER_WEEK;

        public const string TRIGGER_EXTENSION = ".Trigger";
        public const string ALARM_EXTENSION = ".Alarm";
        public const string DISCREPANCY_TARGET_NAME = "TargetValueNodeId";
    }
}
