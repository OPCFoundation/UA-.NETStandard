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

#include "StdAfx.h"
#include "OpcDefs.h"
#include "COpcArray.h"
#include "COpcList.h"
#include "COpcMap.h"

#include <limits.h>

//==============================================================================
// OpcAlloc

void* OpcAlloc(size_t tSize)
{
	return CoTaskMemAlloc(tSize);
}

//==============================================================================
// OpcFree

void OpcFree(void* pBlock)
{
    if (pBlock != NULL) 
	{
		CoTaskMemFree(pBlock);
	}
}

//==============================================================================
// OpcStrDup

CHAR* OpcStrDup(LPCSTR szValue)
{
    CHAR* pCopy = NULL;

    if (szValue != NULL)
    {
        pCopy = OpcArrayAlloc(CHAR, strlen(szValue)+1);

		if (pCopy == NULL)
		{
			return NULL;
		}

        strcpy(pCopy, szValue);
    }

    return pCopy;
}

//==============================================================================
// OpcStrDup

WCHAR* OpcStrDup(LPCWSTR szValue)
{
    WCHAR* pCopy = NULL;

    if (szValue != NULL)
    {
        pCopy = OpcArrayAlloc(WCHAR, wcslen(szValue)+1);

		if (pCopy == NULL)
		{
			return NULL;
		}

        wcscpy(pCopy, szValue);
    }

    return pCopy;
}

//==============================================================================
// OpcUtcNow

FILETIME OpcUtcNow()
{
    FILETIME ft;
    GetSystemTimeAsFileTime(&ft);
	return ft;
}

//==============================================================================
// OpcMinDate

FILETIME OpcMinDate()
{
    FILETIME ft;
    memset(&ft, 0, sizeof(ft));
    return ft;
}

//==============================================================================
// OpcToInt64

LONGLONG OpcToInt64(FILETIME ftTime)
{
	LONGLONG llBuffer = (LONGLONG)ftTime.dwHighDateTime;

	if (llBuffer < 0) 
	{ 
		llBuffer += (((LONGLONG)_UI32_MAX)+1); 
	} 

	LONGLONG llTime = (llBuffer<<32);

	llBuffer = (LONGLONG)ftTime.dwLowDateTime;

	if (llBuffer < 0)
	{		
		llBuffer += (((LONGLONG)_UI32_MAX)+1); 
	}

	llTime += llBuffer;

	return llTime;
}

//==============================================================================
// OpcToFILETIME

FILETIME OpcToFILETIME(LONGLONG llTime)
{
	FILETIME ftTime;

	ftTime.dwLowDateTime  = (DWORD)(0x00000000FFFFFFFF & llTime);
	ftTime.dwHighDateTime = (DWORD)((0xFFFFFFFF00000000 & llTime) >> 32);

	return ftTime;
}


//============================================================================
// OpcGetDaysInMonth

UINT OpcGetDaysInMonth(UINT uYear, UINT uMonth)
{
	// array of days in month.
	static const int pDaysInMonth[] = {	31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

	// adjust for number of months greater than 12.
	uYear  += (uMonth-1)/12;
	uMonth  = (uMonth-1)%12;

	// lookup number of days in static array.
	UINT uDays = pDaysInMonth[uMonth];

	// adjust for leap year.
	if (uMonth == 1)
	{
		if (uYear%4 != 0 || uYear%400 == 0)
		{
			uDays--;
		}
	}

	// return number of days.
	return uDays;
}

//============================================================================
// OpcIsDaylightSaving

static bool OpcIsDaylightTime(TIME_ZONE_INFORMATION& cTimeZone, SYSTEMTIME& stLocalTime)
{

	// check if daylight time not used.
	if (cTimeZone.DaylightDate.wMonth == cTimeZone.StandardDate.wMonth)
	{
		return false;
	}

	bool bNorthern = (cTimeZone.DaylightDate.wMonth < cTimeZone.StandardDate.wMonth);

	if (bNorthern)
	{
		// check if month definitely falls within daylight time.
		if (stLocalTime.wMonth > cTimeZone.DaylightDate.wMonth && stLocalTime.wMonth < cTimeZone.StandardDate.wMonth)
		{
			return true;
		}

		// check if month definitely falls in standard time.
		if (stLocalTime.wMonth < cTimeZone.DaylightDate.wMonth || stLocalTime.wMonth > cTimeZone.StandardDate.wMonth)
		{
			return false;
		}
	}
	else
	{
		// check if month definitely falls within standard time.
		if (stLocalTime.wMonth > cTimeZone.StandardDate.wMonth && stLocalTime.wMonth < cTimeZone.DaylightDate.wMonth)
		{
			return false;
		}

		// check if month definitely falls in daylight time.
		if (stLocalTime.wMonth < cTimeZone.StandardDate.wMonth || stLocalTime.wMonth > cTimeZone.DaylightDate.wMonth)
		{
			return true;
		}
	}

	bool bGoingToDaylight = (stLocalTime.wMonth == cTimeZone.DaylightDate.wMonth);

	// get the transition information.
	SYSTEMTIME& stTransition = (bGoingToDaylight)?cTimeZone.DaylightDate:cTimeZone.StandardDate;

	// get start of month.
	SYSTEMTIME stStart;
	memset(&stStart, 0, sizeof(SYSTEMTIME));

	stStart.wYear  = stLocalTime.wYear;
	stStart.wMonth = stLocalTime.wMonth;
	stStart.wDay   = 1;

	// convert to FILETIME and back again to get day of week.
	FILETIME ftStart;
	SystemTimeToFileTime(&stStart, &ftStart);	
	FileTimeToSystemTime(&ftStart, &stStart);

	// get start of next month.
	SYSTEMTIME stEnd;
	memset(&stEnd, 0, sizeof(SYSTEMTIME));

	stEnd.wYear  = stLocalTime.wYear;
	stEnd.wMonth = stLocalTime.wMonth;
	stEnd.wDay   = OpcGetDaysInMonth(stLocalTime.wYear, stLocalTime.wMonth);

	// convert to FILETIME and back again to get day of week.
	FILETIME ftEnd;
	SystemTimeToFileTime(&stEnd, &ftEnd);	
	FileTimeToSystemTime(&ftEnd, &stEnd);

	WORD wDay       = 1;
	WORD wWeek      = 0;
	WORD wDayOfWeek = stTransition.wDayOfWeek;

	// find the nth day of the month that falls on the change over day.
	if (wWeek < 5)
	{
		while (wDay <= stEnd.wDay)
		{
			if (wDayOfWeek == stTransition.wDayOfWeek)
			{
				wWeek++;

				if (wWeek == stTransition.wDay)
				{
					break;
				}
			}

			wDay++;
			wDayOfWeek = (wDayOfWeek+1)%7;
		}
	}

	// find the last day of the month that falls on the change over day.
	else
	{
		wDay = stEnd.wDay;

		while (wDay > 0)
		{
			if (wDayOfWeek == stTransition.wDayOfWeek)
			{
				break;
			}

			wDay--;
			wDayOfWeek = (wDayOfWeek > 0)?wDayOfWeek-1:6;
		}
	}

	// check if it definately before the transition day.
	if (stLocalTime.wDay < wDay)
	{
		return !bGoingToDaylight;
	}

	// check if it definately fater the transition day.
	if (stLocalTime.wDay > wDay)
	{
		return bGoingToDaylight;
	}
	
	// check if it definately before the transition hour.
	if (stLocalTime.wHour < stTransition.wHour)
	{
		return !bGoingToDaylight;
	}
	
	// must be after the transition hour.
	return bGoingToDaylight;
}


//============================================================================
// OpcLocalTimeToUtcTime

bool OpcLocalTimeToUtcTime(FILETIME& ftLocalTime, FILETIME& ftUtcTime)
{
	SYSTEMTIME stLocalTime;
	
	if (!FileTimeToSystemTime(&ftLocalTime, &stLocalTime))
	{
		return false;
	}

	return OpcLocalTimeToUtcTime(stLocalTime, ftUtcTime);
}

bool OpcLocalTimeToUtcTime(SYSTEMTIME& stLocalTime, FILETIME& ftUtcTime)
{
	// get the timezone information for the machine.
	TIME_ZONE_INFORMATION cTimeZone;

	DWORD dwResult = GetTimeZoneInformation(&cTimeZone);

	// convert to file time.
	FILETIME ftLocalTime;

	if (!SystemTimeToFileTime(&stLocalTime, &ftLocalTime))
	{
		return false;
	}

	LONGLONG llLocalTime = OpcToInt64(ftLocalTime);

	// calculate offset from UTC.
	LONGLONG llBias = cTimeZone.Bias;

	if (dwResult != TIME_ZONE_ID_UNKNOWN)
	{
		if (OpcIsDaylightTime(cTimeZone, stLocalTime))
		{
			llBias += cTimeZone.DaylightBias;
		}
		else
		{
			llBias += cTimeZone.StandardBias;
		}
	}

	// apply offset from UTC.
	llLocalTime += llBias*60*10000000;

	// convert back FILETIME.
	ftUtcTime = OpcToFILETIME(llLocalTime);
	return true;
}
