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
#include "COpcTextReader.h"

//==============================================================================
// Local Functions

// IsEqual
static bool IsEqual(COpcText& cToken, WCHAR zValue1, WCHAR zValue2)
{
    if (cToken.GetIgnoreCase())
    {
        WCHAR z1 = (iswlower(zValue1))?towupper(zValue1):zValue1;
        WCHAR z2 = (iswlower(zValue2))?towupper(zValue2):zValue2;

        return (z1 == z2);
    }

    return (zValue1 == zValue2);
}

// IsEqual
static bool IsEqual(COpcText& cToken, WCHAR zValue, LPCWSTR szValue)
{
    if (szValue == NULL)
    {
        return false;
    }

    return IsEqual(cToken, zValue, *szValue);
}

// IsEqual
static bool IsEqual(COpcText& cToken, LPCWSTR szValue1, LPCWSTR szValue2, UINT uSize = -1)
{
    if (szValue1 == NULL || szValue2 == NULL)
    {
        return (szValue1 == szValue2);
    }

    else if (uSize == -1 && cToken.GetIgnoreCase())
    {
        return (wcsicmp(szValue1, szValue2) == 0);
    }

    else if (uSize == -1)
    {
        return (wcscmp(szValue1, szValue2) == 0);
    }

    else if (cToken.GetIgnoreCase())
    {
        return (wcsnicmp(szValue1, szValue2, uSize) == 0);
    }
    
    return (wcsncmp(szValue1, szValue2, uSize) == 0);
}

//==============================================================================
// COpcTextReader

// Constructor
COpcTextReader::COpcTextReader(LPCSTR szBuf, UINT uLength)
:
    m_szBuf(NULL),
    m_uLength(0),
    m_uEndOfData(0)
{
    if (szBuf != NULL)
    {
        m_uLength = (uLength == -1)?strlen(szBuf):uLength;
        m_szBuf   = (WCHAR*)OpcAlloc((m_uLength+1)*sizeof(WCHAR));

        int iLength = MultiByteToWideChar(
           CP_ACP,
           0,
           szBuf,
           m_uLength,
           m_szBuf,
           m_uLength+1
        );

        if (iLength == 0)
        {
            OpcFree(m_szBuf);
            m_uLength = 0;
            m_szBuf   = NULL;
        }

        m_uEndOfData = m_uLength;
        m_szBuf[m_uEndOfData] = L'\0';
    }
}

COpcTextReader::COpcTextReader(const COpcString& cBuffer)
:
    m_szBuf(NULL),
    m_uLength(0),
    m_uEndOfData(0)
{
    if (!cBuffer.IsEmpty())
    {
        LPCWSTR szBuf = (COpcString&)cBuffer;

        m_uLength = (szBuf != NULL)?wcslen(szBuf):0;
        m_szBuf   = (WCHAR*)OpcAlloc((m_uLength+1)*sizeof(WCHAR));

        wcsncpy(m_szBuf, szBuf, m_uLength);

        m_uEndOfData = m_uLength;
        m_szBuf[m_uEndOfData] = L'\0';
    }
} 

// Constructor
COpcTextReader::COpcTextReader(LPCWSTR szBuf, UINT uLength)
:
    m_szBuf(NULL),
    m_uLength(0),
    m_uEndOfData(0)
{
    if (szBuf != NULL)
    {
        m_uLength = (uLength == -1)?wcslen(szBuf):uLength;
        m_szBuf   = (WCHAR*)OpcAlloc((m_uLength+1)*sizeof(WCHAR));

        wcsncpy(m_szBuf, szBuf, m_uLength);

        m_uEndOfData = m_uLength;
    }
}

// Destructor
COpcTextReader::~COpcTextReader()
{
    OpcFree(m_szBuf);
}

// CheckForHalt
bool COpcTextReader::CheckForHalt(COpcText& cToken, UINT uPosition)
{
    // check if max chars exceeded.
    if (cToken.GetMaxChars() > 0)
    {
        if (cToken.GetMaxChars() <= uPosition)
        {
            return false;
        }
    }

    // check for end of data - halts if EOF is not a delim.
    if (uPosition >= m_uEndOfData)
    {
        cToken.SetEof();
        return !cToken.GetEofDelim();
    }

    // check for one of halt characters.
    LPCWSTR szHaltChars = cToken.GetHaltChars();

    if (szHaltChars == NULL)
    {
        return false;
    }

    UINT uCount = wcslen(szHaltChars);

    for (UINT ii = 0; ii < uCount; ii++)
    {
        if (IsEqual(cToken, m_szBuf[uPosition], szHaltChars[ii]))
        {
            cToken.SetHaltChar(szHaltChars[ii]);
            return true;
        }
    }

    return false;
}

// CheckForDelim
bool COpcTextReader::CheckForDelim(COpcText& cToken, UINT uPosition)
{
    // check for new line delim.
    if (cToken.GetNewLineDelim())
    {
        if (m_szBuf[uPosition] == L'\n' || m_szBuf[uPosition] == L'\r')
        {
            cToken.SetNewLine();
            cToken.SetDelimChar(m_szBuf[uPosition]);
            return true;
        }
    }

    // check for one of the delim chars.
    LPCWSTR szDelims = cToken.GetDelims();
    UINT    uCount   = (szDelims != NULL)?wcslen(szDelims):0;

    for (UINT ii = 0; ii < uCount; ii++)
    {
        if (IsEqual(cToken, m_szBuf[uPosition], szDelims[ii]))
        {
            cToken.SetDelimChar(szDelims[ii]);
            return true;
        }
    }

    return false;
}

// SkipWhitespace 
UINT COpcTextReader::SkipWhitespace(COpcText& cToken)
{
    if (!cToken.GetSkipWhitespace())
    {
        return 0;
    }

    for (UINT ii = 0; ii < m_uEndOfData; ii++)
    {
        if (CheckForHalt(cToken, ii) || !iswspace(m_szBuf[ii]))
        {
            return ii;
        }
    }

    return m_uEndOfData;
}

// CopyData
void COpcTextReader::CopyData(COpcText& cToken, UINT uStart, UINT uEnd)
{
    cToken.CopyData(m_szBuf+uStart, uEnd-uStart);
    cToken.SetStart(uStart);
    cToken.SetEnd(uEnd-1);
}

// FindLiteral
bool COpcTextReader::FindLiteral(COpcText& cToken)
{
    OPC_ASSERT(m_szBuf != NULL);
    OPC_ASSERT(m_uLength != 0);

    LPCWSTR szText  = cToken.GetText();
    UINT    uLength = (szText != NULL)?wcslen(szText):0; 

    // check for trivial case
    if (uLength == 0)
    {
        return false;
    }

    UINT uPosition = SkipWhitespace(cToken);

    // check if there is enough data.
    if (uLength > (m_uEndOfData - uPosition))
    {
        return false;
    }

    for (UINT ii = uPosition; ii < m_uEndOfData-uLength+1; ii++)
    {
        // check if search halted.
        if (CheckForHalt(cToken, ii))
        {
            return false;
        }

        // compare text at current position.
        if (IsEqual(cToken, m_szBuf+ii, szText, uLength))
        {
            CopyData(cToken, ii, ii+uLength);
            return true;
        }

        // stop search if leading unmatching characters are not ignored.
        if (!cToken.GetSkipLeading())
        {
            break;
        }
    }

    return false;
}

// FindWhitespace
bool COpcTextReader::FindWhitespace(COpcText& cToken)
{
    OPC_ASSERT(m_szBuf != NULL);
    OPC_ASSERT(m_uLength != 0);

    UINT uPosition = 0;

    // skip leading non-whitespace
    if (cToken.GetSkipLeading())
    {
        for (UINT ii = 0; ii < m_uEndOfData; ii++)
        {
            if (CheckForHalt(cToken, ii))
            {
               return false;
            }

            if (iswspace(m_szBuf[ii]))
            {
               uPosition = ii;
               break;
            }
        }
    }

    // check if there is still data left to read.
    if (uPosition >= m_uEndOfData)
    {
        return false;
    } 

    // read until a non-whitespace.
    for (UINT ii = uPosition; ii < m_uEndOfData; ii++)
    {
        if (CheckForHalt(cToken, ii))
        {
            break;
        }

        if (!iswspace(m_szBuf[ii]))
        {
            break;
        }
    }

    // check for empty token
    if (ii == uPosition)
    {
        return false;
    }

    // copy token.
    CopyData(cToken, uPosition, ii);
    return true;
}

// FindNonWhitespace
bool COpcTextReader::FindNonWhitespace(COpcText& cToken)
{
    OPC_ASSERT(m_szBuf != NULL);
    OPC_ASSERT(m_uLength != 0);

    // skip leading whitespace
    UINT uPosition = SkipWhitespace(cToken);

    // check if there is still data left to read.
    if (uPosition >= m_uEndOfData)
    {
        return false;
    } 

    // read until a whitespace.
    for (UINT ii = uPosition; ii < m_uEndOfData; ii++)
    {
        if (CheckForHalt(cToken, ii))
        {
            break;
        }

        if (iswspace(m_szBuf[ii]))
        {
            break;
        }
    }

    // check for empty token
    if (ii == uPosition)
    {
        return false;
    }

    // copy token.
    CopyData(cToken, uPosition, ii);
    return true;
}

// FindDelimited
bool COpcTextReader::FindDelimited(COpcText& cToken)
{
    OPC_ASSERT(m_szBuf != NULL);
    OPC_ASSERT(m_uLength != 0);

    // skip leading whitespace
    UINT uPosition = SkipWhitespace(cToken);

    // check if there is still data left to read.
    if (uPosition >= m_uEndOfData)
    {
        return false;
    } 

    // read until a delimiter.
    for (UINT ii = uPosition; ii < m_uEndOfData; ii++)
    {
        // check if search halted.
        if (CheckForHalt(cToken, ii))
        {
            return false;
        }

        // check if delimiter found.
        if (CheckForDelim(cToken, ii))
        {
            // copy token - empty tokens are valid.
            CopyData(cToken, uPosition, ii);
            return true;
        }
    }

    // check for end of data - true if EOF is a delim.
    if (ii >= m_uEndOfData)
    {
        cToken.SetEof();
        
        if (cToken.GetEofDelim())
        {
            CopyData(cToken, uPosition, ii);
            return true;
        }
    }

    return false;
}


// FindEnclosed
bool COpcTextReader::FindEnclosed(COpcText& cToken)
{
    OPC_ASSERT(m_szBuf != NULL);
    OPC_ASSERT(m_uLength != 0);
      
    WCHAR zStart = 0;
    WCHAR zEnd   = 0;
    cToken.GetBounds(zStart, zEnd);

    // skip leading whitespace
    UINT uPosition = SkipWhitespace(cToken);

    // check if there is still data left to read.
    if (uPosition >= m_uEndOfData)
    {
        return false;
    } 

    // read until finding the start delimiter,
    for (UINT ii = uPosition; ii < m_uEndOfData; ii++)
    {
        // check if search halted.
        if (CheckForHalt(cToken, ii))
        {
            return false;
        }

        // check for start character.
        if (IsEqual(cToken, m_szBuf[ii], zStart)) 
        {
            uPosition = ii;
            break;
        }
    }
    
    // check if there is still data left to read.
    if (ii >= m_uEndOfData)
    {
        return false;
    } 

    // read until finding the end delimiter,
    for (ii = uPosition+1; ii < m_uEndOfData; ii++)
    {
        // check if search halted.
        if (CheckForHalt(cToken, ii))
        {
            return false;
        }

        // check for end character.
        if (IsEqual(cToken, m_szBuf[ii], zStart)) 
        {
            // ignore if character is escaped.
            if (cToken.GetAllowEscape() && (uPosition < ii-1))
            {
                if (m_szBuf[ii-1] == L'\\')
                {
                    continue;
                }
            }

            // copy token - empty tokens are valid.
            CopyData(cToken, uPosition+1, ii);
            return true;
        }
    }

    return false;
}

// FindToken
bool COpcTextReader::FindToken(COpcText& cToken)
{
    OPC_ASSERT(m_szBuf != NULL);
    OPC_ASSERT(m_uLength != 0);

    switch (cToken.GetType())
    {
        case COpcText::Literal:       return FindLiteral(cToken);
        case COpcText::NonWhitespace: return FindNonWhitespace(cToken);
        case COpcText::Whitespace:    return FindWhitespace(cToken);
        case COpcText::Delimited:     return FindDelimited(cToken);
    }

    return false;
}

// GetNext
bool COpcTextReader::GetNext(COpcText& cToken)
{
    // no more data to get - give up
    if (m_uEndOfData == 0)
    {
        return false;
    }

    // find the token
    if (!FindToken(cToken))
    {
        return false;
    }     

    // all done if token is not being extracted.
    if (cToken.GetNoExtract())
    {
        return true;
    }

    UINT uEndOfToken = cToken.GetEnd() + 1;
    UINT uDataLeft   = m_uEndOfData - uEndOfToken;
  
    // extract the delimiter if extracting token.

    // new line delimiter found.
    if (cToken.GetNewLine())
    {
        if (cToken.GetDelimChar() == _T('\r'))
        {
            uEndOfToken += 2;
            uDataLeft   -= 2;
        }

        else
        {
            uEndOfToken += 1;
            uDataLeft   -= 1;
        }
    }

    // specific delimiter found.
    else if (cToken.GetDelimChar() > 0 && !cToken.GetEof())
    {
        uEndOfToken++;
        uDataLeft--;
    }

    // move leftover data to the start of the buffer
    for (UINT ii = 0; ii < uDataLeft; ii++)
    {
        m_szBuf[ii] = m_szBuf[uEndOfToken+ii];
    }

    m_szBuf[ii]  = L'\0';
    m_uEndOfData = uDataLeft;

	// set EOF flag if no data left in buffer.
	if (m_uEndOfData == 0)
	{
		cToken.SetEof();
	}
    
    return true;
}
