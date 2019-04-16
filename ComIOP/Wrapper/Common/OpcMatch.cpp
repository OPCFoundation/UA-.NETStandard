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
#include "OpcMatch.h"

//==============================================================================
// Local Functions

// ConvertCase
static inline int ConvertCase(int c, bool bCaseSensitive)
{
	return (bCaseSensitive)?c:toupper(c);
}

//==============================================================================
// OpcMatchPattern

bool OpcMatchPattern(
	LPCTSTR szString, 
	LPCTSTR szPattern, 
	bool bCaseSensitive
)
{
	// an empty pattern always matches.
	if (szPattern == NULL)
	{
		return true;
	}

	// an empty string never matches.
	if (szString == NULL)
	{
		return false;
	}

    TCHAR c, p, l;

    for (;;)
    {
        switch (p = ConvertCase(*szPattern++, bCaseSensitive))
        {
			// end of pattern.
			case 0:                            
			{
				return (*szString)?false:true; // if end of string true
			}

			// match zero or more char.
			case _T('*'):
			{
				while (*szString) 
				{   
					if (OpcMatchPattern(szString++, szPattern, bCaseSensitive))
					{
						return true;
					}
				}
			
				return OpcMatchPattern(szString, szPattern, bCaseSensitive);
			}

			// match any one char.
			case _T('?'):
			{
				if (*szString++ == 0) 
				{
					return false;  // not end of string 
				}

				break;
			}

			// match char set 
			case _T('['): 
			{
				if ((c = ConvertCase(*szString++, bCaseSensitive)) == 0)
				{
					return false; // syntax 
				}

				l = 0; 

				// match a char if NOT in set []
				if (*szPattern == _T('!')) 
				{
					++szPattern;

					while ((p = ConvertCase(*szPattern++, bCaseSensitive)) != _T('\0')) 
					{
						if (p == _T(']')) // if end of char set, then 
						{
							break; // no match found 
						}

						if (p == _T('-')) 
						{
							// check a range of chars? 
							p = ConvertCase( *szPattern, bCaseSensitive );

							// get high limit of range 
							if (p == 0  ||  p == _T(']'))
							{
								return false; // syntax 
							}

							if (c >= l  &&  c <= p) 
							{
								return false; // if in range, return false
							}
						} 

						l = p;
						
						if (c == p) // if char matches this element 
						{
							return false; // return false 
						}
					} 
				}

				// match if char is in set []
				else 
				{
					while ((p = ConvertCase(*szPattern++, bCaseSensitive)) != _T('\0')) 
					{
						if (p == _T(']')) // if end of char set, then no match found 
						{
							return false;
						}

						if (p == _T('-')) 
						{   
							// check a range of chars? 
							p = ConvertCase( *szPattern, bCaseSensitive );
							
							// get high limit of range 
							if (p == 0  ||  p == _T(']'))
							{
								return false; // syntax 
							}

							if (c >= l  &&  c <= p) 
							{
								break; // if in range, move on 
							}
						} 

						l = p;
						
						if (c == p) // if char matches this element move on 
						{
							break;           
						}
					} 

					while (p  &&  p != _T(']')) // got a match in char set skip to end of set
					{
						p = *szPattern++;             
					}
				}

				break; 
			}

			// match digit.
			case _T('#'):
			{
				c = *szString++; 

				if (!_istdigit(c))
				{
					return false; // not a digit
				}

				break;
			}

			// match exact char.
			default: 
			{
				c = ConvertCase(*szString++, bCaseSensitive); 
				
				if (c != p) // check for exact char
				{
					return false; // not a match
				}

				break;
			}
        } 
    } 

	return false;
}
