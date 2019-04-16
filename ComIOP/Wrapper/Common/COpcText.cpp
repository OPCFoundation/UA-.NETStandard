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
#include "COpcText.h"

//==============================================================================
// COpcText

// Constructor
COpcText::COpcText()
{
   Reset();
}

// Reset
void COpcText::Reset()
{
   m_cData.Empty();

   // Search Crtieria
   m_eType = COpcText::NonWhitespace;
   m_cHaltChars.Empty();
   m_uMaxChars = 0;
   m_bNoExtract = false;
   m_cText.Empty();
   m_bSkipLeading = false;
   m_bSkipWhitespace = false;
   m_bIgnoreCase = false;
   m_bEofDelim = false;
   m_bNewLineDelim = false;
   m_cDelims.Empty();
   m_bLeaveDelim = true;
   m_zStart = L'"';
   m_zEnd = L'"';
   m_bAllowEscape = true;

   // Search Results
   m_uStart = 0;
   m_uEnd = 0;
   m_zHaltChar = 0;
   m_uHaltPos = 0;
   m_zDelimChar = 0;
   m_bEof = false;
   m_bNewLine = false;
}

// CopyData
void COpcText::CopyData(LPCWSTR szData, UINT uLength)
{
    m_cData.Empty();

    if (uLength > 0 && szData != NULL)
    {
        LPWSTR wszData = OpcArrayAlloc(WCHAR, uLength+1);
        wcsncpy(wszData, szData, uLength);
        wszData[uLength] = L'\0';
        
        m_cData = wszData;
        OpcFree(wszData);
    }
}

// SetType
void COpcText::SetType(COpcText::Type eType)
{
   Reset();

   m_eType = eType;

   switch (eType)
   {
      case Literal:
      {
         m_cText.Empty();
         m_bSkipLeading = false;
         m_bSkipWhitespace = true;
         m_bIgnoreCase = false;
         break;
      }

      case Whitespace:
      {
         m_bSkipLeading = false;
         m_bEofDelim = true;
         break;
      }

      case NonWhitespace:
      {
         m_bSkipWhitespace = true;
         m_bEofDelim = true;
         break;
      }

      case Delimited:
      {
         m_cDelims.Empty();
         m_bSkipWhitespace = false;
         m_bIgnoreCase = false;
         m_bEofDelim = false;
         m_bNewLineDelim = false;
         m_bLeaveDelim = false;
         break;
      }

      default:
      {
         break;
      }
   }
}
