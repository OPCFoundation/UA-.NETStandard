//	WebHelp 5.10.005
var gsPPath="";
var gaPaths=new Array();
var gaAvenues=new Array();
var gaSearchTerms = new Array();
var gaSearchTermType = new Array();
var gbPhraseTerm = false ;
var gChildPathInMain="";

var goFrame=null;
var gsStartPage="";
var gsRelCurPagePath="";
var gsSearchFormHref="";
var gnTopicOnly=-1;
var gnOutmostTopic=-1;
var gsFtsBreakChars="\t\r\n\"\\ .,!@#$%^&*()~'`:;<>?/{}[]|+-=\x85\x92\x93\x94\x95\x96\x97\x99\xA9\xAE\xB7";
var gsHiliteSearchSetting = "enable,#b2b4bf,black";
var gsQuote='\x22'; 
var gsBkgndColor="";
var gsTextColor="";
var BTN_TEXT=1;
var BTN_IMG=2;

var goSync=null;

var goShow=null;
var goHide=null;

var goPrev=null;
var goNext=null;
var gnForm=0;
var goShowNav=null;
var goHideNav=null;

var goWebSearch=null;

var gsBtnStyle="";
var gaButtons=new Array();
var gaTypes=new Array();
var whtopic_foldUnload=null;
var gbWhTopic=false;
var gbCheckSync=false;
var gbSyncEnabled=false;
var gaBreadcrumbsTrail = new Array();
var gnYPos = -1;
var gbBadUriError = false;

var	EST_TERM		= 1;
var	EST_PHRASE		= 2;
var	EST_STEM		= 3;

function AddMasterBreadcrumbs(relHomePage, styleInfo, separator, strHome, strHomePath)
{
	delete gaBreadcrumbsTrail;
	gaBreadcrumbsTrail = new Array();
	var sTopicFullPath = _getPath(document.location.href);
	var sXmlFullPath = _getFullPath(sTopicFullPath, relHomePage);
	var sXmlFolderPath = _getPath(sXmlFullPath);
	var sdocPath = _getFullPath(sXmlFolderPath, "MasterData.xml");

	try
	{
			GetMasterBreadcrumbs(sdocPath, styleInfo, separator);
	}
	catch(err)
	{
		//some error occurred while reading masterdata.xml
	}
	var i = gaBreadcrumbsTrail.length;
	if(i == 0)
	{
	    var strTrail;
	    if(styleInfo == "breadcrumbs")
		    strTrail = "<a class=\""+ styleInfo + "\"" + " href=\"" + strHomePath + "\">" + strHome + "</a> " + ((strHome == "")? "":separator) + " ";
	    else
	        strTrail = "<a style=\""+ styleInfo + "\"" + " href=\"" + strHomePath + "\">" + strHome + "</a> " + ((strHome == "")? "":separator) + " ";
		document.write(strTrail);
	}
	else
	{
		while(i > 0)
		{
			document.write(gaBreadcrumbsTrail[i-1]);
			i--;
		}
	}
	return;
}

var xmlHttp1;
var xmlDoc1;
function processReqChange1()
{
   // only if req shows "loaded"
    if (xmlHttp1.readyState == 4) 
		xmlDoc1 = xmlHttp1.responseXML;
}
function GetMasterBreadcrumbs(masterFullPath, styleInfo, separator)
{
	//dont read if not part of merged projects
	if(masterFullPath.indexOf("/mergedProjects/") == -1 &&
			masterFullPath.indexOf("\\mergedProjects\\") == -1)
			return;
				
	if(gbIE5)
	{
		xmlDoc1=new ActiveXObject("Microsoft.XMLDOM");
		xmlDoc1.async="false";
		xmlDoc1.load(masterFullPath);
	}
	else if(gbNav6)
	{
		var req=new XMLHttpRequest();
     		req.open("GET", masterFullPath, false);   
		req.send(null);   
		xmlDoc1 = req.responseXML;
	}
	else if(gbSafari3)
	{
        	if(window.XMLHttpRequest)
			xmlHttp1 = new XMLHttpRequest();
		if(xmlHttp1)
		{
			xmlHttp1.onreadystatechange=processReqChange1;
			xmlHttp1.open("GET", masterFullPath, false);
			xmlHttp1.send("");
		}
	}

	if(xmlDoc1 == null) throw "error";

	var root = xmlDoc1.documentElement;
	var masterProj = xmlDoc1.getElementsByTagName("MasterProject");
	var masterName="";
	var masterRelPath="";

	if(masterProj)
	{
		masterName = masterProj[0].getAttribute("name");
		masterRelPath = masterProj[0].getAttribute("url");		
	}

	var x = xmlDoc1.getElementsByTagName("item");
	var i = 0;
	var strTrail = "";

	for(i=0; i< x.length; i++)
	{
		var name= x[i].getAttribute("name");
		var path = x[i].getAttribute("url");

		name = name.replace(/\\\\/g, '\\'); 
		
		if(path == "")
		{
			strTrail += name + " " + separator + " ";
		}
		else
		{
			var sHrefRelPath = _getPath(masterFullPath) + masterRelPath;
			var sHrefFullPath = _getFullPath(sHrefRelPath, path); 
			if(styleInfo == "breadcrumbs")
 			    strTrail += "<a class=\""+ styleInfo + "\"" + " href=\"" + sHrefFullPath + "\">" + name + "</a> " + separator + " ";
 			else
 			    strTrail += "<a style=\""+ styleInfo + "\"" + " href=\"" + sHrefFullPath + "\">" + name + "</a> " + separator + " ";
		}

	}	

	gaBreadcrumbsTrail[gaBreadcrumbsTrail.length] = strTrail;

	// call for master breadcrumbs
	masterFullPath = _getPath(masterFullPath)
	masterFullPath = _getFullPath(masterFullPath, masterRelPath);
	masterFullPath = _getFullPath(masterFullPath, "MasterData.xml");

	GetMasterBreadcrumbs(masterFullPath, styleInfo, separator);
	
}


/////////highlight Search Routines /////////
function ClosedRange( a_nStart, a_nEnd )
{
	this.nStart = a_nStart;
	this.nEnd = a_nEnd;
}

////////generic functions //////////

var g_RunesWordBreaks=gsFtsBreakChars;
var g_RunesWhiteSpaces="\x20\x09\x0D\x0A\xA0";

function _isWordBreak( a_ch )
{
	return ( g_RunesWordBreaks.indexOf( a_ch ) >= 0 );
}

function _isWhiteSpace( a_ch )
{
	return ( g_RunesWhiteSpaces.indexOf( a_ch ) >= 0 );
}

function _getLengthOfWordBreak( a_str, a_nFrom )
{
	var i = a_nFrom, nLen = a_str.length;
	while ( i < nLen && _isWordBreak( a_str.charAt( i ) ) )
		++i;
	return i - a_nFrom;
}

function _getLengthOfWord( a_str, a_nFrom )
{
	var i = a_nFrom, nLen = a_str.length;
	while ( i < nLen &&	!_isWordBreak( a_str.charAt( i ) ) )
		++i;
	return i - a_nFrom;
}

function _getWord( a_str, a_nFrom )
{
	var nLen = _getLengthOfWord( a_str, a_nFrom );
	return a_str.substr( a_nFrom, nLen );
}

function _getPositionInc( a_str, a_nFrom )
{
	var i = a_nFrom, nLen = a_str.length, nInc = 1;
	while ( i < nLen && _isWordBreak( a_str.charAt( i ) ) )
	{
		if ( !_isWhiteSpace( a_str.charAt( i ) ) )
			nInc++;

		i++;
	}
	return nInc;
}

function _getNormalizedWord( a_strWord )
{
	var strLower = a_strWord.toLowerCase();
	
	return strLower;
}

function DolWord( a_strWord, a_nPosition, a_nCharLocation )
{
	this.strWord = a_strWord;
	this.nPosition = a_nPosition;
	this.nCharLocation = a_nCharLocation;
}

function dolSegment( a_strSrc )
{
	var nLen = a_strSrc.length;
	var nCur = 0;
	var nPosition = 1;
	var strWord = "";
	var aRslt = new Array();

	nCur += _getLengthOfWordBreak( a_strSrc, nCur );
	while ( nCur < nLen )
	{
		strWord = _getNormalizedWord( _getWord( a_strSrc, nCur ) );
		aRslt[aRslt.length] = new DolWord( strWord, nPosition, nCur );
		
		nCur += strWord.length;
		nPosition += _getPositionInc( a_strSrc, nCur );
		nCur += _getLengthOfWordBreak( a_strSrc, nCur );
	}
	return aRslt;
}

/////////// Dom Text node ///////////////
var s_strHlStart=null;
var s_strHlEnd =null;

function DomTextNode( a_Node, a_nFrom )
{
	this.node = a_Node;
	this.nFrom = a_nFrom;
	
	this.aClosedRanges = new Array();

	this.getClosedRanges = function( a_aRanges, a_nStart )
	{
		var nTo = this.nFrom + a_Node.data.length;			
		for ( var i = a_nStart; i < a_aRanges.length; i++ )
		{
			if ( a_aRanges[i].nStart <= nTo &&
				 a_aRanges[i].nEnd >= this.nFrom )
			{
				this.aClosedRanges[this.aClosedRanges.length] = new ClosedRange( a_aRanges[i].nStart > this.nFrom ? a_aRanges[i].nStart : this.nFrom,
																				 a_aRanges[i].nEnd < nTo ? a_aRanges[i].nEnd : nTo );
			}
			if ( a_aRanges[i].nEnd > nTo )
			{
				return i;
			}
		}
		return i;
	}

	this.doHighlight = function( a_aRanges, a_nStart )
	{
		s_strHlStart = "<font style='color:" + gsTextColor + "; background-color:" + gsBkgndColor + "'>";
		s_strHlEnd = "</font>";

		if ( a_nStart >= a_aRanges.length )
			return a_nStart;

		var nEnd = this.getClosedRanges( a_aRanges, a_nStart );
		if ( this.aClosedRanges.length == 0 )
			return nEnd;
			
		var strText = this.node.data;
		//replace newline, carriage return, tab characters with space
		strText = strText.replace(/[\n\r\t]/g," "); 
		
		var strHTML = "";
		var nLastStart = 0;
		for ( var i = 0; i < this.aClosedRanges.length; i++ )
		{
			strHTML += _textToHtml_nonbsp(strText.substring( nLastStart, this.aClosedRanges[i].nStart - this.nFrom ));
			strHTML += s_strHlStart;
			strHTML += _textToHtml_nonbsp(strText.substring( this.aClosedRanges[i].nStart - this.nFrom,
										  this.aClosedRanges[i].nEnd - this.nFrom ));
			strHTML += s_strHlEnd;

			nLastStart = this.aClosedRanges[i].nEnd - this.nFrom;
		}
		strHTML += _textToHtml_nonbsp(strText.substr( nLastStart ));
		
		var spanElement = document.createElement( "span" );
		spanElement.innerHTML = strHTML;
		if (gbIE)
		{
		    //for IE, when assigning string to innerHTML, leading whitespaces are dropped
		    if ((strHTML.length >0)&&(strHTML.charAt(0) == " "))
		        spanElement.innerHTML = "&nbsp;" + spanElement.innerHTML ;       
		}   
		
		this.node.parentNode.replaceChild( spanElement, this.node );
		if(gnYPos == -1)
		{
			var elemObj = spanElement;
			var curtop = 0;
    			if (elemObj.offsetParent)
    			{
        			while (elemObj.offsetParent)
        			{
            				curtop += elemObj.offsetTop
            				elemObj = elemObj.offsetParent;
        			}
    			}
    			else if (elemObj.y)
        			curtop += elemObj.y;
			
			gnYPos = curtop;
		}
		return nEnd;
	};
}

function DomTexts()
{
	this.strText = "";
	this.aNodes = new Array();
	this.aRanges = new Array();

	this.addElementNode = function( a_Node )
	{
		if ( a_Node == null || a_Node.childNodes == null )
			return;

		var nLen = a_Node.childNodes.length;
		for ( var i = 0; i < nLen; i++ )
		{
			var node = a_Node.childNodes.item( i );
			if ( node != null )
			{
				if ( node.nodeType == 3 )
				{
					this.addTextNode( node );
				}
				else if ( node.nodeType == 1 )
				{
					this.addElementNode( node );
				}
			}
		}
	}

	this.addTextNode = function( a_Node )
	{
		if ( a_Node == null )
			return;

		var strInnerText = a_Node.data;
		
		//replace newline, carriage return, tab characters with space
		strInnerText = strInnerText.replace(/[\n\r\t]/g," "); 
		if ( strInnerText.length != 0 )
		{
			var nFrom = this.strText.length;
			this.strText += strInnerText;
			this.aNodes[this.aNodes.length] = new DomTextNode( a_Node, nFrom );
		}
	}

	this.isWordMatch = function( a_strHlWord, a_strTextWord )
	{
		return a_strTextWord.indexOf(a_strHlWord.toLowerCase()) != -1;
	}
					 
	this.makeHighlightRanges = function()
	{
		if(typeof(gaSearchTerms[0]) == "undefined")
			return;

		var str = escapeRegExp(gaSearchTerms[0].toLowerCase());
		for(var j = 1; j < gaSearchTerms.length; j++)
		{
			str += "|" + escapeRegExp(gaSearchTerms[j].toLowerCase());
			
		}

		var regexp = new RegExp(str, "i");

		var aWords ;
		if (!gbPhraseTerm)
			aWords = dolSegment( this.strText );
		else
		{
			aWords = new Array();
			aWords[0] = new DolWord( this.strText, 1, 0 );
		}
		
		for ( var i = 0; i < aWords.length; i++ )
		{
			var n = new Object;
			n.index = 0;
			var prevLen = 0;
			var tmpStr1 = aWords[i].strWord.toLowerCase();

			while(n != null && n.index > -1)
			{
				n = regexp.exec(tmpStr1);

				if (n != null &&  n.index > -1 )
				{
					var strWord = n[0];
					this.aRanges[this.aRanges.length] = new ClosedRange( aWords[i].nCharLocation + prevLen + n.index,
								aWords[i].nCharLocation + prevLen + n.index + strWord.length);
					prevLen = prevLen + n.index + strWord.length;							
					tmpStr1 = tmpStr1.substring(n.index + strWord.length, tmpStr1.length);
				}
			}
		}
	}
	
	this.highlightNodes = function()
	{
		var nFrom = 0;
		for ( var i = 0; i < this.aNodes.length; i++ )
			nFrom = this.aNodes[i].doHighlight( this.aRanges, nFrom );
	}

	this.jump2FirstHighlightedWord = function()
	{
		if (gnYPos > 51)
			window.scrollTo(0, gnYPos-50);
	}
}

function processSuspendNodes( a_aNodes )
{
	if ( a_aNodes.length == 0 )
		return false;

	var dt = new DomTexts();

	//build dom texts...
	for ( var i = 0; i < a_aNodes.length; i++ )
	{
		var node = a_aNodes[i];
		if ( node.nodeType == 1 )
		{
			dt.addElementNode( node );
		}
		else if ( node.nodeType == 3 )
		{
			dt.addTextNode( node );
		}
	}
	
	dt.makeHighlightRanges();
	dt.highlightNodes();
	dt.jump2FirstHighlightedWord();
}

var s_strRecursiveTags = "sub sup img applet object br iframe embed noembed param area input " +
						 "select textarea button option hr frame noframes marquee label p dl " +
						 "div center noscript blockquote form isindex table fieldset address layer " +
						 "dt dd caption thead tfoot tbody tr th td lengend h1 h2 h3 h4 h5 h6 " +
						 "ul ol dir menu li pre xmp listing plaintext ins del";

function doesTagRecursiveProcess( a_Node )
{
	if ( a_Node == null )
		return false;

	var strTagName = a_Node.tagName.toLowerCase();
	var rg = "\\b" + strTagName + "\\b";
	var ss = s_strRecursiveTags.match( rg );
	return ss != null;
}

function doHighLightDomElement( a_aSuspendedNodes, a_Node )
{
	var childNodes = a_Node.childNodes;
	
	if ( childNodes == null || childNodes.length == 0 )
		return;

	var nLen = childNodes.length;
	for ( var i = 0; i < nLen; i++ )
	{
		var node = childNodes.item( i );
		if ( node == null )
			continue;

		if ( node.nodeType == 1 )
		{	//element
			if ( doesTagRecursiveProcess( node ) )
			{
				if ( a_aSuspendedNodes.length > 0 )
				{
					processSuspendNodes( a_aSuspendedNodes );
					a_aSuspendedNodes.length = 0;
				}
			}
			doHighLightDomElement( a_aSuspendedNodes, node );
		}
		else if ( node.nodeType == 3 )
		{	//text
			a_aSuspendedNodes[a_aSuspendedNodes.length] = node;
		}
	}
}

function highlightDocument()
{
	if ( !document.body || document.body == null )
		return;
		
	var aSuspendedNodes = new Array();
	doHighLightDomElement( aSuspendedNodes, document.body );
	processSuspendNodes( aSuspendedNodes );
}

/////// start routine /////////
function IsHighLightRequired()
{
	var bRetVal = false;
	var searchSetting = gsHiliteSearchSetting.match( "^(.+),(.+),(.*)$" );

	if(searchSetting != null)
	{
		if(searchSetting[1] == "enable")
		{
			gsBkgndColor = searchSetting[2];
			gsTextColor = searchSetting[3];
			bRetVal = true;
		}
	}
	return bRetVal;
}

function highlightSearch()
{
	if(!IsHighLightRequired())	return;

	//check pane in focus is Search pane.
	var oMsg=new whMessage(WH_MSG_GETPANEINFO,this,1,null);
	if(SendMessage(oMsg)) {
		if (oMsg.oParam != "fts") 
			return;
	}

	//check highlight result is enabled.
	var oMsg=new whMessage(WH_MSG_HILITESEARCH,this,1,null);
	if(SendMessage(oMsg))
	{
		if(oMsg.oParam == false)
			return;
	}
	
	//check num of results greater than 0
	var oMsg=new whMessage(WH_MSG_GETNUMRSLT,this,1,null);
	if(SendMessage(oMsg))
	{
		if(oMsg.oParam <= 0)
			return;
	}
	
	//get string in search box.
	var oMsg = new whMessage(WH_MSG_GETSEARCHSTR, this, 1, null);
	var strTerms = "";
	if (SendMessage(oMsg))
	{
		strTerms = oMsg.oParam;		
	}
	
	StartHighLightSearch(strTerms);	

}

function StartHighLightSearch(strTerms)
{
	if(!IsHighLightRequired())	return;

	findSearchTerms(strTerms, false);
	
	highlightDocument();
}

//////// common with FTS routines to identify stop word etc. ////////////

function findSearchTerms(searchTerms, bSkip)
{
	if(searchTerms != "")
	{
		var sInput=searchTerms;
		var bPhrase = false ;
		var sCW="";
		var nS=-1;
		var nSep=-1;
		for(var nChar=0;nChar<gsFtsBreakChars.length;nChar++){
			var nFound=sInput.indexOf(gsFtsBreakChars.charAt(nChar));
			if((nFound!=-1)&&((nS==-1)||(nFound<nS))){
				nS=nFound;
				nSep=nChar;
			}
		}
		
		if(nS==-1){
			sCW=sInput;
			sInput="";
		}
		else
		{
			if (isQuote(gsFtsBreakChars.charAt(nSep)))
			{
				if (nS == 0)
				{
					//it could be a phrase
					sInput = sInput.substring(nS+1) ;
					var phrLen = getLengthOfPhrase(sInput ) ;
					if (phrLen <= 0 )
					{
						//invalid expression
						return ;
					}
					else
					{					
						//phrase begins here
						bPhrase = true ; 
						//get the phrase							
						sCW=sInput.substring(0,phrLen);					
						sInput=sInput.substring(phrLen + 1);						
					}
				}
				else
				{
					//get the token preceeding phrase
					sCW=sInput.substring(0,nS);
					
					//keep the starting quote for next parse so next parse would know it's a phrase
					sInput=sInput.substring(nS);
				}				
			}
			else
			{
				sCW=sInput.substring(0,nS);
				sInput=sInput.substring(nS+1);
			}
		}

		searchTerms=sInput;
		
		var bAdd = true;
		if((sCW=="or")||(sCW=="|")||(sCW=="OR"))
		{
			bSkip = false;
			bAdd = false;
		}
		else if((sCW=="and")||(sCW=="&")||(sCW=="AND"))
		{
			bSkip = false;
			bAdd = false;
		}
		else if((sCW=="not")||(sCW=="~")||(sCW=="NOT"))
		{
			bSkip = true;
			bAdd = false;
		}

		if(bAdd && !bSkip && sCW!="" && sCW!=" " && !IsStopWord(sCW,gaFtsStop)){
			gaSearchTerms[gaSearchTerms.length] = sCW;
			if (bPhrase)
			{
				gaSearchTermType[gaSearchTermType.length] = EST_PHRASE ;
				gbPhraseTerm = true ;
			}
			else
			{
				gaSearchTermType[gaSearchTermType.length] = EST_TERM ;
			}
			
			if (!bPhrase)
			{
				var stemWord = GetStem(sCW);
				if(stemWord != sCW)
				{
					gaSearchTerms[gaSearchTerms.length] = stemWord;
					gaSearchTermType[gaSearchTermType.length] = EST_STEM ;
				}
			}
		}
		findSearchTerms(searchTerms, bSkip);
	}
	
}


function getLengthOfPhrase( a_str )
{
	var i = 0 ;
	var nLen = a_str.length;
	while ( i < nLen )
	{
		if ( isQuote( a_str.charAt( i ) ) )
			return i ;
		++i;
	}
	return -1;
}

function GetStem(szWord)
{
	if(gaFtsStem==null||gaFtsStem.length==0)return szWord;
	if(IsNonAscii(szWord))             return szWord;
	var aStems=gaFtsStem;

	var nStemPos=0;
	var csStem="";
	for(var iStem=0;iStem<aStems.length;iStem++){

		if(aStems[iStem].length>=szWord.length-1)	continue;
		nStemPos=szWord.lastIndexOf(aStems[iStem]);
		if(nStemPos>0){
			var cssub=szWord.substring(nStemPos);
			if(cssub==aStems[iStem]){
				csStem=szWord;
				if(szWord.charAt(nStemPos-2)==szWord.charAt(nStemPos-1)){
					csStem=csStem.substring(0,nStemPos-1);
				}else{
					csStem=csStem.substring(0,nStemPos);
				}
				return csStem;
			}
		}
	}
	return szWord;
}

function IsStopWord(sCW,aFtsStopArray)
{
	var nStopArrayLen=aFtsStopArray.length;
	var nB=0;
	var nE=nStopArrayLen-1;
	var nM=0;
	var bFound=false;
	var sStopWord="";
	while(nB<=nE){
		nM=(nB+nE);
		nM>>=1;
		sStopWord=aFtsStopArray[nM];
		if(compare(sCW,sStopWord)>0){
			nB=(nB==nM)?nM+1:nM;
		}else{
			if(compare(sCW,sStopWord)<0){
				nE=(nE==nM)?nM-1:nM;
			}else{
				bFound=true;
				break;
			}
		}
	}
	return bFound;
}

/////// end highlight search rountines /////////////

function setButtonFont(sType,sFontName,sFontSize,sFontColor,sFontStyle,sFontWeight,sFontDecoration)
{
	var vFont=new whFont(sFontName,sFontSize,sFontColor,sFontStyle,sFontWeight,sFontDecoration);
	gsBtnStyle+=".whtbtn"+sType+"{"+getFontStyle(vFont)+"}";
}

function writeBtnStyle()
{
	if(gaButtons.length>0)
	{
		if(gsBtnStyle.length>0)
		{
			var sStyle="<style type='text/css'>";
			sStyle+=gsBtnStyle+"</style>";
			document.write(sStyle);
		}
	}
}

function button(sText,nWidth,nHeight)
{
	this.sText=sText;
	this.nWidth=nWidth;
	this.nHeight=nHeight;
	
	this.aImgs=new Array();
	var i=0;
	while(button.arguments.length>i+3)
	{
		this.aImgs[i]=button.arguments[3+i];
		i++;
	}
}


//recursively finds the parent project StartPage path if exists 
//also computes the child toc path in the parent toc recursively until 
//main proj

var xmlhttp=null;
var xmlDoc = null;
function processReqChange()
{
   // only if req shows "loaded"
    if (xmlhttp.readyState == 4) 
		xmlDoc = xmlhttp.responseXML;
}

function getPPStartPagePath(sPath)
{
	if(sPath.length != 0)
	{
		var sXmlFolderPath = _getPath(sPath);
		if(sXmlFolderPath.indexOf("/mergedProjects/") == -1 &&
			sXmlFolderPath.indexOf("\\mergedProjects\\") == -1)
			return sPath;
		
		var sdocPath = _getFullPath(sXmlFolderPath, "MasterData.xml");
		try
		{
			if(gbIE5) //Internet Explorer
			{
				xmlDoc=new ActiveXObject("Microsoft.XMLDOM");
				xmlDoc.async=false;
  				xmlDoc.load(sdocPath);
			}
			else if(gbNav6) //Firefox, Mozilla, Opera etc.
			{
				var req=new XMLHttpRequest();
 		        req.open("GET", sdocPath, false);   
	            req.send(null); 
	            xmlDoc = req.responseXML;
			}
			else if(gbSafari3) //Safari
			{ 
				if(window.XMLHttpRequest)
					xmlhttp = new XMLHttpRequest();
				if(xmlhttp)
				{
					xmlhttp.onreadystatechange=processReqChange;
					xmlhttp.open("GET", sdocPath, false);
					xmlhttp.send("");
				}
			}
		}
		catch(e){
			gbBadUriError=true;
			return sPath;
		}

		if(xmlDoc == null) return sPath;			
		var root = xmlDoc.documentElement;
		if(root == null) return sPath;
		var masterProj = null;
		try
		{
			masterProj = xmlDoc.getElementsByTagName("syncinfo");	
			var childTocPosInParent = null;
			if(masterProj)
			{
				var startpage = xmlDoc.getElementsByTagName("startpage");	
				masterStartPageName = startpage[0].getAttribute("name");
				masterStartPageRelPath = startpage[0].getAttribute("url");
				var tocpos = xmlDoc.getElementsByTagName("tocpos");
				childTocPosInParent = tocpos[0].getAttribute("path");
						
			}
		}
		catch(e){return sPath;}
		if(childTocPosInParent)
		{
			childTocPosInParent = childTocPosInParent.replace(/\\n/g, "\n");
			gChildPathInMain = childTocPosInParent +  gChildPathInMain;
		}
		sXmlFolderPath = _getFullPath(sXmlFolderPath, masterStartPageRelPath+masterStartPageName);
		sXmlFolderPath = getPPStartPagePath(sXmlFolderPath);
		return sXmlFolderPath;
	}
}

//project info
function setRelStartPage(sPath)
{
	if(gsPPath.length==0)
	{
		gsPPath=_getFullPath(_getPath(document.location.href),_getPath(sPath));
		gsStartPage=_getFullPath(_getPath(document.location.href),sPath);
		try{
			gsStartPage = getPPStartPagePath(gsStartPage);
		}
		catch(e)
		{
			alert("Error reading masterData.xml");
		}
		gsRelCurPagePath=_getRelativeFileName(gsStartPage,document.location.href);
		for(var i=0; i< gaPaths.length; i++)
			gaPaths[i] = gChildPathInMain + gaPaths[i];
	}
}

function getImage(oImage,sType)
{
	var sImg="";
	if(oImage&&oImage.aImgs&&(oImage.aImgs.length>0))
	{
		sImg+="<img alt=\""+sType+"\" src=\""+oImage.aImgs[0]+"\"";
		if(oImage.nWidth>0)
			sImg+=" width="+oImage.nWidth;
		if(oImage.nHeight>0)
			sImg+=" height="+oImage.nHeight;
		sImg+=" border=0>";
	}
	return sImg;
}

function addTocInfo(sTocPath)
{
	gaPaths[gaPaths.length]=sTocPath;
}


var flex_nextLocation;
var flex_previousLocation;

function addAvenueInfo(sName,sPrev,sNext)
{
	gaAvenues[gaAvenues.length]=new avenueInfo(sName,sPrev,sNext);
	flex_previousLocation = sPrev;
	flex_nextLocation = sNext;
}

function addButton(sType,nStyle,sText,sHref,sOnClick,sOnMouseOver,sOnLoad,nWidth,nHeight,sImg1,sImg2,sImg3)
{
	var sButton="";
	var nBtn=gaButtons.length;
	if(sType=="prev")
	{
		if(canGo(false))
		{
			var sTitle="Previous Topic";
			goPrev=new button(sText,nWidth,nHeight,sImg1,sImg2,sImg3);
			sButton="<a title=\""+sTitle+"\" class=\"whtbtnprev\" href=\"javascript:void(0);\" onclick=\"goAvenue(false);return false;\">";
			if(nStyle==BTN_TEXT)
				sButton+=goPrev.sText;
			else
				sButton+=getImage(goPrev,sTitle);
			sButton+="</a>";
		}
	}
	else if(sType=="next")
	{
		if(canGo(true))
		{
			var sTitle="Next Topic";
			goNext=new button(sText,nWidth,nHeight,sImg1,sImg2,sImg3);
			sButton="<a title=\""+sTitle+"\" class=\"whtbtnnext\" href=\"javascript:void(0);\" onclick=\"goAvenue(true);return false;\">";
			if(nStyle==BTN_TEXT)
				sButton+=goNext.sText;
			else
				sButton+=getImage(goNext,sTitle);
			sButton+="</a>";
		}
	}
	else if(sType=="show")
	{
		if(isTopicOnly()&&(!gbOpera6||gbOpera7))
		{
			var sTitle="Show Navigation Component";
			goShow=new button(sText,nWidth,nHeight,sImg1,sImg2,sImg3);
			sButton="<a title=\""+sTitle+"\" class=\"whtbtnshow\" href=\"javascript:void(0);\" onclick=\"show();return false;\">";
			if(nStyle==BTN_TEXT)
				sButton+=goShow.sText;
			else
				sButton+=getImage(goShow,sTitle);
			sButton+="</a>";
		}
	}
	else if(sType=="hide")
	{
		if(!isTopicOnly()&&!gbOpera6)
		{
			var sTitle="Hide Navigation Component";
			goHide=new button(sText,nWidth,nHeight,sImg1,sImg2,sImg3);
			sButton="<a title=\""+sTitle+"\" class=\"whtbtnhide\" href=\"javascript:void(0);\" onclick=\"hide();return false;\">";
			if(nStyle==BTN_TEXT)
				sButton+=goHide.sText;
			else
				sButton+=getImage(goHide,sTitle);
			sButton+="</a>";
		}
	}
	else if(sType=="shownav")
	{
		if(isShowHideEnable())
		{
			var sTitle="Show Navigation Component";
			goShowNav=new button(sText,nWidth,nHeight,sImg1,sImg2,sImg3);
			sButton="<a title=\""+sTitle+"\" class=\"whtbtnshownav\" href=\"javascript:void(0);\" onclick=\"showHidePane(true);return false;\">";
			if(nStyle==BTN_TEXT)
				sButton+=goShowNav.sText;
			else
				sButton+=getImage(goShowNav,sTitle);
			sButton+="</a>";
		}
	}
	else if(sType=="hidenav")
	{
		if(isShowHideEnable())
		{
			var sTitle="Hide Navigation Component";
			goHideNav=new button(sText,nWidth,nHeight,sImg1,sImg2,sImg3);
			sButton="<a title=\""+sTitle+"\" class=\"whtbtnhidenav\" href=\"javascript:void(0);\" onclick=\"showHidePane(false);return false;\">";
			if(nStyle==BTN_TEXT)
				sButton+=goHideNav.sText;
			else
				sButton+=getImage(goHideNav,sTitle);
			sButton+="</a>";
		}
	}
	else if(sType=="synctoc")
	{
		if(gaPaths.length>0)
		{
			var sTitle="Sync TOC";
			goSync=new button(sText,nWidth,nHeight,sImg1,sImg2,sImg3);
			sButton="<a title=\""+sTitle+"\" class=\"whtbtnsynctoc\" href=\"javascript:void(0);\" onclick=\"syncWithShow();return false;\">";
			if(nStyle==BTN_TEXT)
				sButton+=goSync.sText;
			else
				sButton+=getImage(goSync,sTitle);
			sButton+="</a>";
		}
	}
	else if(sType=="websearch")
	{
		if(gsSearchFormHref.length>0)
		{
			var sTitle="WebSearch";
			goWebSearch=new button(sText,nWidth,nHeight,sImg1,sImg2,sImg3);
			sButton="<a title=\""+sTitle+"\" class=\"whtbtnwebsearch\" href=\""+gsSearchFormHref+"\">";
			if(nStyle==BTN_TEXT)
				sButton+=goWebSearch.sText;
			else
				sButton+=getImage(goWebSearch,sTitle);
			sButton+="</a>";
		}
	}
	else if(sType=="searchform")
	{
		gaButtons[nBtn]="NeedSearchForm";
		gaTypes[nBtn]=sType;
	}
	if(sButton.length!=0)
	{
		if(nStyle==BTN_TEXT)
			sButton+="&nbsp;";
		gaButtons[nBtn]="<td>"+sButton+"</td>";
		gaTypes[nBtn]=sType;
	}
}

function isSyncEnabled()
{
	if(!gbCheckSync)
	{
		var oMsg=new whMessage(WH_MSG_ISSYNCSSUPPORT,this,1,null);
		if(SendMessage(oMsg))
		{
			gbSyncEnabled=oMsg.oParam;
		}
		gbCheckSync=true;
	}
	return gbSyncEnabled;
}

function isInPopup()
{
	return (window.name.indexOf("BSSCPopup")!=-1);
}

function getIntopicBar(sAlign)
{
	var sHTML="";
	if(gaButtons.length>0)
	{
		sHTML+="<div align="+sAlign+">";

		sHTML+="<table cellpadding=\"2\" cellspacing=\"0\" border=\"0\"><tr>";
		for(var i=0;i<gaButtons.length;i++)
		{
			if(gaTypes[i]!="synctoc"||isSyncEnabled())
			{
				if(gaButtons[i]=="NeedSearchForm")
					sHTML+=getSearchFormHTML();
				else
					sHTML+=gaButtons[i];
			}
		}
		sHTML+="</tr></table>";

		sHTML+="</div>";
	}
	return sHTML;
}


function writeIntopicBar(nAligns)
{
	if(isInPopup()) return;
	if(gaButtons.length>0)
	{
		var sHTML="";
		if(nAligns!=0)
		{
			sHTML+="<table width=100%><tr>"
			if(nAligns&1)
				sHTML+="<td width=33%>"+getIntopicBar("left")+"</td>";
			if(nAligns&2)
				sHTML+="<td width=34%>"+getIntopicBar("center")+"</td>";
			if(nAligns&4)
				sHTML+="<td width=33%>"+getIntopicBar("right")+"</td>";
			sHTML+="</tr></table>";
			document.write(sHTML);
		}
	}
}

function sendAveInfoOut()
{
	if(!isInPopup())
		setTimeout("sendAveInfo();",100);
}

function sendAveInfo()
{
	var oMsg=new whMessage(WH_MSG_AVENUEINFO,this,1,gaAvenues);
	SendMessage(oMsg);
}


function onNext()
{
	var oMsg=new whMessage(WH_MSG_NEXT,this,1,null);
	SendMessage(oMsg);
}

function onPrev()
{
	var oMsg=new whMessage(WH_MSG_PREV,this,1,null);
	SendMessage(oMsg);
}

function createSyncInfo()
{
	var oParam=new Object();
	var sPath = null;
	if(gsStartPage.length != 0)
		sPath = _getPath(gsStartPage);
	else if(gsPPath.length==0)
		sPath =_getPath(document.location.href);
	else 
		sPath = gsPPath;
	oParam.sPPath=sPath;
	oParam.sTPath=document.location.href;
	oParam.aPaths=gaPaths;
	return oParam;
}

function syncWithShow()
{
	if(isTopicOnly())
		show();
	else
	{
		sync();
		showTocPane();
	}
}

function showTocPane()
{
	var oMsg=new whMessage(WH_MSG_SHOWTOC,this,1,null);
	SendMessage(oMsg);
}

function sendSyncInfo()
{
	if(!isInPopup())
	{
		var oParam=null;
		if(gaPaths.length>0)
		{
			oParam=createSyncInfo();
		}
		var oMsg=new whMessage(WH_MSG_SYNCINFO,this,1,oParam);
		SendMessage(oMsg);
	}
}

function sendInvalidSyncInfo()
{
	if(!isInPopup())
	{
		var oMsg=new whMessage(WH_MSG_SYNCINFO,this,1,null);
		SendMessage(oMsg);
	}
}

function enableWebSearch(bEnable)
{
	if(!isInPopup())
	{
		var oMsg=new whMessage(WH_MSG_ENABLEWEBSEARCH,this,1,bEnable);
		SendMessage(oMsg);
	}
}

function autoSync(nSync)
{
	if(nSync==0) return;
	if(isInPopup()) return;
	if(isOutMostTopic())
		sync();
}

function isOutMostTopic()
{
	if(gnOutmostTopic==-1)
	{
		var oMessage=new whMessage(WH_MSG_ISINFRAMESET,this,1,null);
		if(SendMessage(oMessage))
			gnOutmostTopic=0;
		else
			gnOutmostTopic=1;
	}
	return (gnOutmostTopic==1);
}

function sync()
{
	if(gaPaths.length>0)
	{
		var oParam=createSyncInfo();
		var oMessage=new whMessage(WH_MSG_SYNCTOC,this,1,oParam);
		SendMessage(oMessage);
	}
}


function avenueInfo(sName,sPrev,sNext)
{
	this.sName=sName;
	this.sPrev=sPrev;
	this.sNext=sNext;
}

function getCurrentAvenue()
{
	var oParam=new Object();
	oParam.sAvenue=null;
	var oMessage=new whMessage(WH_MSG_GETCURRENTAVENUE,this,1,oParam);
	SendMessage(oMessage);
	return oParam.sAvenue;
}

function unRegisterListener()
{
	if(gbAIRSSL)
		return;
	sendInvalidSyncInfo();
	enableWebSearch(false);
	if(whtopic_foldUnload)
		whtopic_foldUnload();
}

function onSendMessage(oMsg)
{
	var nMsgId=oMsg.nMessageId;
	if(nMsgId==WH_MSG_GETAVIAVENUES)
	{
		oMsg.oParam.aAvenues=gaAvenues;
		return false;
	}
	else if(nMsgId==WH_MSG_GETTOCPATHS)
	{
		if(isOutMostTopic())
		{
			oMsg.oParam.oTocInfo=createSyncInfo();
			return false;		
		}
		else
			return true;
	}
	else if(nMsgId==WH_MSG_NEXT)
	{
		goAvenue(true);
	}
	else if(nMsgId==WH_MSG_PREV)
	{
		goAvenue(false);
	}
	else if(nMsgId==WH_MSG_WEBSEARCH)
	{
		websearch();
	}
	return true;
}

function goAvenue(bNext)
{
	var sTopic=null;
	var sAvenue=getCurrentAvenue();
	var nAvenue=-1;
	if(sAvenue!=null&&sAvenue!="")
	{
		for(var i=0;i<gaAvenues.length;i++)
		{
			if(gaAvenues[i].sName==sAvenue)
			{
				nAvenue=i;
				break;
			}
		}
		if(nAvenue!=-1)
		{
			if(bNext)
				sTopic=gaAvenues[nAvenue].sNext;
			else
				sTopic=gaAvenues[nAvenue].sPrev;
		}
	}
	else
	{
		for(var i=0;i<gaAvenues.length;i++)
		{
			if(gaAvenues[i].sNext!=null&&gaAvenues[i].sNext.length>0&&bNext)
			{
				sTopic=gaAvenues[i].sNext;
				break;
			}
			else if(gaAvenues[i].sPrev!=null&&gaAvenues[i].sPrev.length>0&&!bNext)
			{
				sTopic=gaAvenues[i].sPrev;
				break;
			}
		}
	}
	
	if(sTopic!=null&&sTopic!="")
	{
		if(gsPPath!=null&&gsPPath!="")
		{
			sFullTopicPath=_getFullPath(gsPPath,sTopic);
			document.location=sFullTopicPath;
		}
	}
}

function canGo(bNext)
{
	for(var i=0;i<gaAvenues.length;i++)
	{
		if((gaAvenues[i].sNext!=null&&gaAvenues[i].sNext.length>0&&bNext)||
			(gaAvenues[i].sPrev!=null&&gaAvenues[i].sPrev.length>0&&!bNext))
			return true;
	}
	return false;
}

function show()
{
	if(gbBadUriError)
	{
		var strMainPage = document.location.href;
		var indx = strMainPage.toLowerCase().indexOf("/mergedprojects/");
		if(indx != -1)
			window.location = strMainPage.substring(0, indx+1) + "whcsh_home.htm#topicurl=" + strMainPage.substring(indx+1);
		else if(gsStartPage!="")
				window.location=gsStartPage+"#"+gsRelCurPagePath;
	}
	else if(gsStartPage!="")
			window.location=gsStartPage+"#"+gsRelCurPagePath;
}

function hide()
{
	if(goFrame!=null)
	{
		goFrame.location=window.location;
	}
}

function isTopicOnly()
{
	if(gnTopicOnly==-1)
	{
		var oParam=new Object();
		oParam.oFrame=null;
		var oMsg=new whMessage(WH_MSG_GETSTARTFRAME,this,1,oParam);
		if(SendMessage(oMsg))
		{
			goFrame=oParam.oFrame;
			gnTopicOnly=0;
		}
		else
			gnTopicOnly=1;
	}
	if(gnTopicOnly==1)
		return true;
	else
		return false;
}

function websearch()
{
	if(gbNav4)
	{
		if(document.ehelpform)
			document.ehelpform.submit();
	}
	else
	{
		if(window.ehelpform)
			window.ehelpform.submit();
	}
}

function addSearchFormHref(sHref)
{
	gsSearchFormHref=sHref;
	enableWebSearch(true);
}

function searchB(nForm)
{
	var sValue=eval("document.searchForm"+nForm+".searchString.value");
	var oMsg=new whMessage(WH_MSG_SEARCHTHIS,this,1,sValue);
	SendMessage(oMsg);
}


function getSearchFormHTML()
{
	var sHTML="";
	gnForm++;
	var sFormName="searchForm"+gnForm;
	var sButton="<form name=\""+sFormName+"\" method=\"POST\" action=\"javascript:searchB("+gnForm+")\">"
	sButton+="<input type=\"text\" name=\"searchString\" value=\"- Full Text search -\" size=\"20\"/>";
	if(""=="text")
	{
		sButton+="<a class=\"searchbtn\" href=\"javascript:void(0);\" onclick=\""+sFormName+".submit();return false;\"></a>";
	}
	else if(""=="image")
	{
		sButton+="<a class=\"searchbtn\" href=\"javascript:void(0);\" onclick=\""+sFormName+".submit();return false;\">"
		sButton+="<img src=\"\" border=0></a>";
	}
	sButton+="</form>";
	sHTML="<td align=\"center\">"+sButton+"</td>";
	return sHTML;
}




function showHidePane(bShow)
{
	var oMsg=null;
	if(bShow)
		oMsg=new whMessage(WH_MSG_SHOWPANE,this,1,null);
	else
		oMsg=new whMessage(WH_MSG_HIDEPANE,this,1,null);
	SendMessage(oMsg);
}

function isShowHideEnable()
{
	if(gbIE4)
		return true;
	else
		return false;
}




if(window.gbWhUtil && window.gbWhVer && (gbAIRSSL ||(window.gbWhMsg&&window.gbWhProxy)))
{
	if(!gbAIRSSL)
	{
		RegisterListener("bsscright",WH_MSG_GETAVIAVENUES);
		RegisterListener("bsscright",WH_MSG_GETTOCPATHS);
		RegisterListener("bsscright",WH_MSG_NEXT);
		RegisterListener("bsscright",WH_MSG_PREV);
		RegisterListener("bsscright",WH_MSG_WEBSEARCH);
	}
	if(gbMac&&gbIE4)
	{
		if(typeof(window.onunload)!="unknown")
			if(window.onunload.toString!=unRegisterListener.toString)
				whtopic_foldUnload=window.onunload;
	}
	else
	{
		if(window.onunload)
			if(window.onunload.toString!=unRegisterListener.toString)
				whtopic_foldUnload=window.onunload;
	}
	window.onunload=unRegisterListener;
	setButtonFont("show","","","","","","");
setButtonFont("hide","","","","","","");
setButtonFont("prev","","","","","","");
setButtonFont("next","","","","","","");

	gbWhTopic=true;
}
else
	document.location.reload();

function PickupDialog_Invoke()
{
	if(!gbIE4||gbMac||gbAIRSSL)
	{
		if(typeof(_PopupMenu_Invoke)=="function")
			return _PopupMenu_Invoke(PickupDialog_Invoke.arguments);
	}
	else
	{
		if(PickupDialog_Invoke.arguments.length>2)
		{
			var sPickup="whskin_pickup.htm";
			var sPickupPath=gsPPath+sPickup;
			if(gbIE4)
			{
				var sFrame=PickupDialog_Invoke.arguments[1];
				var aTopics=new Array();
				for(var i=2;i<PickupDialog_Invoke.arguments.length;i+=2)
				{
					var j=aTopics.length;
					aTopics[j]=new Object();
					aTopics[j].m_sName=PickupDialog_Invoke.arguments[i];
					aTopics[j].m_sURL=PickupDialog_Invoke.arguments[i+1];
				}

				if(aTopics.length>1)
				{
					var nWidth=300;
					var nHeight=180;
					var	nScreenWidth=screen.width;
					var	nScreenHeight=screen.height;
					var nLeft=(nScreenWidth-nWidth)/2;
					var nTop=(nScreenHeight-nHeight)/2;
					if(gbIE4)
					{
						var vRet=window.showModalDialog(sPickupPath,aTopics,"dialogHeight:"+nHeight+"px;dialogWidth:"+nWidth+"px;resizable:yes;status:no;scroll:no;help:no;center:yes;");
						if(vRet)
						{
							var sURL=vRet.m_url;
							if(sFrame)
								window.open(sURL,sFrame);
							else
								window.open(sURL,"_self");
						}
					}
				}
				else if(aTopics.length==1)
				{
					var sURL=aTopics[0].m_sURL
					if(sFrame)
						window.open(sURL,sFrame);
					else
						window.open(sURL,"_self");
				}
			}
		}
	}
}

function isQuote( a_ch )
{
	return ( a_ch == gsQuote );
} 

function escapeRegExp(str)
{
	var specials = new RegExp("[.*+?|()\\^\\$\\[\\]{}\\\\]", "g"); // .*+?|()^$[]{}\
	return str.replace(specials, "\\$&");
}
