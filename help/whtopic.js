//	WebHelp 5.10.005
var gsPPath="";
var gaPaths=new Array();
var gaAvenues=new Array();
var gaSearchTerms = new Array();

var goFrame=null;
var gsStartPage="";
var gsRelCurPagePath="";
var gsSearchFormHref="";
var gnTopicOnly=-1;
var gnOutmostTopic=-1;
var gsHiliteSearchSetting = "/*% WH_USER_OPTIONS.HiliteSearchSetting;%*/";
var g_RunesVowels="%%%OdinRunesVowels.js%%%";
var g_RunesWordBreaks="%%%OdinRunesWordBreaks.js%%%";
var g_RunesWhiteSpaces="%%%OdinRunesWhiteSpaces.js%%%";
var g_RunesSpecialBreaks = ",!@#$%^&*()~'`:;<>?/{}[]|+-=" ;
var g_RunesQuote='%%%OdinRunesQuote.js%%%';
var g_RunesHelSuffixes=new Array(%%%OdinRunesHelSuffixes.js%%%);
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
var gbSyncEnabled=false;
var gBCId = 0;
var gnYPos = -1;
var gbBadUriError = false;
var gFrameId = null;
var gsPageDir="%%% WH_PROJECT_LNG_DIR %%%"

var gBreadCrumbInfo = new Array;

function BreadCrumbInfo(relHomePage, styleInfo, separator, strHome, strHomePath) {
    this.relHomePage = relHomePage;
    this.styleInfo = styleInfo;
    this.separator = separator;
    this.strHome = strHome;
    this.strHomePath = strHomePath;
    this.bcLinks = [];
}

function AddMasterBreadcrumbs(relHomePage, styleInfo, separator, strHome, strHomePath) {
    document.write("<span id=\"brseq" + gBCId + "\" ></span>");
    gBreadCrumbInfo[gBCId] = new BreadCrumbInfo(relHomePage, styleInfo, separator, strHome, strHomePath);
    gBCId++;
}

function UpdateBreadCrumbsMarker() {  
	if(gBreadCrumbInfo.length > 0)
	{
        	var bcInfo = gBreadCrumbInfo[0];
       		var sCurTopicPath = _getPath(decodeURI(document.location.href));
		var sHomePath = _getFullPath(sCurTopicPath, bcInfo.relHomePage);
		var sHomeFolder = _getPath(sHomePath);
		var sMasterDataPath = _getFullPath(sHomeFolder, "MasterData.xml");
        	GetMasterBreadcrumbs(sMasterDataPath);
	}
}

function GetMasterBreadcrumbs(sMasterDataPath) {
    //dont read if not part of merged projects
	if(sMasterDataPath.indexOf("/mergedProjects/") == -1 &&
			sMasterDataPath.indexOf("\\mergedProjects\\") == -1)
    {
            writeBreadCrumbs();
			return;
    }

    var sCurrentDocPath = _getPath(decodeURI(document.location.href));
    var fileName = _getRelativeFileName(sCurrentDocPath, sMasterDataPath);

    xmlJsReader.loadFile(fileName, function (a_xmlDoc, args) {
        onLoadingMasterDataXML(a_xmlDoc, args);
    }, sMasterDataPath);
}


function onLoadingMasterDataXML(xmlDoc1, sMasterDataPath)
{
    if(xmlDoc1 == null)
    {
        writeBreadCrumbs();
        return;
    }
    var root = xmlDoc1.documentElement;
	var masterProj = xmlDoc1.getElementsByTagName("MasterProject");
	var masterName="";
	var masterRelPath="";

	if(masterProj.length > 0)
	{
		masterName = masterProj[0].getAttribute("name");
		masterRelPath = masterProj[0].getAttribute("url");		
	}

	var x = xmlDoc1.getElementsByTagName("item");
	var i = 0;
	var strTrail = "";

	for(i=x.length-1; i>=0; i--)
	{
		var bcName= x[i].getAttribute("name");
		var path = x[i].getAttribute("url");

		bcName = bcName.replace(/\\\\/g, '\\'); 

        var strLink = "";
        var sMasterPath = _getFullPath(_getPath(sMasterDataPath), masterRelPath);
        if(path != "")
        {
           strLink = _getFullPath(sMasterPath, path); 
        }
        for(var j=0;j<gBCId;j++) 
        {
            var bclink = new Object();
            bclink.name = bcName;
            bclink.strLink = strLink;
			bclink.firstEntry = (i==0?true:false);
            gBreadCrumbInfo[j].bcLinks.push(bclink);
        }
	}	

	// call for master breadcrumbs
	sMasterDataPath = _getFullPath(sMasterPath, "MasterData.xml");
	GetMasterBreadcrumbs(sMasterDataPath);
}

function writeBreadCrumbs() {
    for(var i=0;i<gBCId;i++) {   
	var bHomeFound = false;
        var strTrail = "";
        if(gBreadCrumbInfo[i].bcLinks.length == 0)
        {   
	        if(gBreadCrumbInfo[i].styleInfo == "breadcrumbs")
		        strTrail = "<a class=\""+ gBreadCrumbInfo[i].styleInfo + "\"" + " href=\"" + gBreadCrumbInfo[i].strHomePath + "\">" + gBreadCrumbInfo[i].strHome + "</a> " + ((gBreadCrumbInfo[i].strHome == "")? "":gBreadCrumbInfo[i].separator) + " ";
	        else
	            strTrail = "<a style=\""+ gBreadCrumbInfo[i].styleInfo + "\"" + " href=\"" + gBreadCrumbInfo[i].strHomePath + "\">" + gBreadCrumbInfo[i].strHome + "</a> " + ((gBreadCrumbInfo[i].strHome == "")? "":gBreadCrumbInfo[i].separator) + " ";
        }
        else{
            var len = gBreadCrumbInfo[i].bcLinks.length;
            for(var j=len-1;j>=0;j--)
            { 
                if(gBreadCrumbInfo[i].bcLinks[j].firstEntry == true)
                {
                        if(bHomeFound)
                           continue;
                        else
                           bHomeFound = true;
                }
                if(gBreadCrumbInfo[i].bcLinks[j].strLink == "")
                {
                    strTrail += gBreadCrumbInfo[i].bcLinks[j].name + " " + gBreadCrumbInfo[i].separator + " ";
                }
                else{
                    if(gBreadCrumbInfo[i].styleInfo == "breadcrumbs")
 			            strTrail += "<a class=\""+ gBreadCrumbInfo[i].styleInfo + "\"" + " href=\"" + gBreadCrumbInfo[i].bcLinks[j].strLink + "\">" + gBreadCrumbInfo[i].bcLinks[j].name + "</a> " + gBreadCrumbInfo[i].separator + " ";
 			        else
 			            strTrail += "<a style=\""+ gBreadCrumbInfo[i].styleInfo + "\"" + " href=\"" + gBreadCrumbInfo[i].bcLinks[j].strLink + "\">" + gBreadCrumbInfo[i].bcLinks[j].name + "</a> " + gBreadCrumbInfo[i].separator + " ";
                }
            }
        }
        var brselem = document.getElementById("brseq"+i);
        if(typeof(brselem) != 'undefined' && brselem != null)
            brselem.innerHTML = strTrail;
        else
        {
            //used in air help
            RH_BreadCrumbDataStringVariable = RH_BreadCrumbDataStringVariable.replace(/__brseq__/g, strTrail);
			if(gbAIR)
			{
				try{
						UpdateBreadCrumbState();
				}catch(e) {}	
			}
			else if(gbAIRSSL)
				createFrameInfo();
        }
    }
}

/////////highlight Search Routines /////////
function ClosedRange( a_nStart, a_nEnd )
{
	this.nStart = a_nStart;
	this.nEnd = a_nEnd;
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
		showHighlightedElement(spanElement);
		return nEnd;
	};
}

function getDropSpotId(dropTextId)
{
	var src = null;	
	if(!src&&dropTextId)
	{
		for (var i=0;i<gPopupData.length;i++)
		{
			if(gPopupData[i].popupId == dropTextId)
			{
				src=gPopupData[i].el;
				break;
			}
			else if(gPopupData[i].popupId == "#" + dropTextId)
			{
				src=gPopupData[i].el;
				break;
			}
		}
	}
	return src;
}

function showHighlightedElement(highlightElement) {
	//display a dropdown/expand text if highlighted element is inside it
	var parent = highlightElement.parentNode;
	while( (typeof parent != 'undefined') && parent != null )
	{
		var tagname = parent.tagName.toLowerCase();
		if( tagname == 'body')
			break;
		if( (tagname == "div" && parent.className == "droptext"))
		{
			if(parent.style.display == "none")
				parent.style.display = "";
			var dsId = getDropSpotId(parent.getAttribute("id"));
			if(dsId != null)
			{
				var dsElem = document.getElementById(dsId);
				showHighlightedElement(dsElem);
			}
		}
		else if((tagname == "span" && parent.className == "expandtext") ||
			(tagname == "span" && parent.className == "glosstext"))
		{
			if(parent.style.display == "none")
				parent.style.display = "";
		}
		parent = parent.parentNode;
	}
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

		var n = new Object;
		n.index = 0;
		var prevLen = 0;
		var tmpStr1 = this.strText.toLowerCase();

		while(n != null && n.index > -1)
		{
			n = regexp.exec(tmpStr1);

			if (n != null &&  n.index > -1 )
			{
				var strWord = n[0];
				this.aRanges[this.aRanges.length] = new ClosedRange(  prevLen + n.index, 	prevLen + n.index + strWord.length);
				prevLen = prevLen + n.index + strWord.length;							
				tmpStr1 = tmpStr1.substring(n.index + strWord.length, tmpStr1.length);
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

function onGetHighlightInfo(oMsg)
{
    if(oMsg && oMsg.oParam)
    {
        if(typeof(oMsg.oParam.bHighlight) == 'undefined' || oMsg.oParam.bHighlight == false)
            return;
        if(oMsg.oParam.nResults <= 0)
            return;
        var strTerms = "";
        strTerms = oMsg.oParam.strTerms;
        StartHighLightSearch(strTerms);	
    }
}

function onGetPaneInfoToHighlight(oMsg)
{
    if (oMsg)
    {
        if(oMsg.oParam != "fts") 
			return;
	    var oMsg = new whMessage(WH_MSG_GETHIGHLIGHTINFO, null, new Object());
        request(oMsg, onGetHighlightInfo);
	}
}

function highlightSearch()
{
	if(!IsHighLightRequired())	return;
	
	//check pane in focus is Search pane.
	var oMsg=new whMessage(WH_MSG_GETPANEINFO, null, null);
	request(oMsg, onGetPaneInfoToHighlight);
}

function StartHighLightSearch(strTerms)
{
	if(!IsHighLightRequired())	return;

	findSearchTerms(strTerms);
	
	highlightDocument();
}

//////// common with FTS routines to identify stop word etc. ////////////
// LanguageService.js---------------------------

function LanguageService()
{
	this.getNormalizedOrg = function( a_strOrg, a_Result )
	{
		var strUpper = a_strOrg.toUpperCase();
		var strLower = a_strOrg.toLowerCase();

		if ( utf8Compare(strUpper, strLower) == 0 || utf8Compare(strUpper, a_strOrg) != 0 )
		{
			a_Result.strNormalizedOrg = strLower;
			a_Result.bUpperCase = false;
		}
		else
		{
			a_Result.strNormalizedOrg = strUpper;
			a_Result.bUpperCase = true;
		}
	}
	this.stemWith = function( a_strWord, a_strSuffix )
	{
		var s = a_strSuffix.split( "," );
		var strSuffix = s[0];
		var bRemoveOnly = ( s[1] == '1' );
		
		var ss = a_strWord.match( "^..+" + strSuffix + "$" );
		if ( ss == null )
			return null;

		var nLenRest = a_strWord.length - strSuffix.length;
		var bAddE = false;
		if ( !bRemoveOnly )
		{
			if ( !this.isVowel( a_strWord.charAt( nLenRest - 1 ) ) )
			{
				if ( a_strWord.charAt( nLenRest - 1 ) == a_strWord.charAt( nLenRest - 2 ) )
					nLenRest--;
				else
					bAddE = true;
			}
		}
		
		var strStem = a_strWord.substr( 0, nLenRest );
		
		if ( strStem.length < 2 || (( strStem.length == 2) && !bAddE ) )
			return null;

		//if ( strStem.length <= 2 )
			//return null;
			
		return strStem;
	}
	this.helStem = function( a_Result )
	{
		var strWord = a_Result.strNormalizedOrg.toLowerCase();

		var nSuffixNum = g_RunesHelSuffixes.length;
		var nStemFound = 0;
		var strStem = null;
		for ( var i = 0; i < nSuffixNum; i++ )
		{
			strStem = this.stemWith( strWord, g_RunesHelSuffixes[i] );
			if ( strStem != null )
			{
				nStemFound = i + 1;
				break;
			}
		}
		if ( strStem == null )
			strStem = strWord;

		a_Result.strHelStem = strStem;
		a_Result.nHelWordShape = a_Result.bUpperCase ? nStemFound * 2 + 1 : nStemFound * 2;
	}
	this.isVowel = function( a_ch )
	{
		return g_RunesVowels.indexOf( a_ch ) >= 0;
	}
	this.isWordBreak = function( a_ch )
	{
		return ( !this.isQuote( a_ch ) && g_RunesWordBreaks.indexOf( a_ch ) >= 0 );
	}
	this.isWhiteSpace = function( a_ch )
	{
		return ( g_RunesWhiteSpaces.indexOf( a_ch ) >= 0 );
	}
	this.isSpecialBreak = function( a_ch )
	{
		return ( g_RunesSpecialBreaks.indexOf( a_ch ) >= 0 );
	}	
	this.isCJKCodePoint = function( a_ch )
	{
		//from http://en.wikipedia.org/wiki/Plane_%28Unicode%29
		if ( (typeof(a_ch) == "undefined" ) || (a_ch == "" ) )
			return false ;
		var val = a_ch.charCodeAt(0)	;
				
		return (  ((0x2E80 <= val) &&  ( val <= 0x9FFF)) //East Asian scripts and symbols
				 || ((0xF900 <= val) &&  ( val <= 0xFAFF))  //CJK Compatibility Ideographs
				 || ((0xFE30 <= val) &&  ( val <= 0xFE4F)) 	 //CJK Compatibility Forms 
				 || ((0xFF00 <= val) &&  ( val <= 0xFFEF)) ); //Halfwidth and Fullwidth Forms (FF00–FFEF)
	}
	this.isQuote = function( a_ch )
	{
		return ( a_ch == g_RunesQuote );
	}
	this.isAND = function( a_strOp )
	{	return ( a_strOp == "and" );	}
	this.isOR = function( a_strOp )
	{	return ( a_strOp == "or" );		}
	this.isNOT = function( a_strOp )
	{	return ( a_strOp == "not" );	}
	this.isOperator = function( strOp )
	{	if ( strOp == "and" ||
			 strOp == "or" ||
			 strOp == "not" )
			return true;
	}
}

// Runes.js----------------------------------

var	ESNT_AND		= 1;
var	ESNT_OR			= 2;
var	ESNT_NOT		= 3;
var	ESNT_DEFAULT	= 4;
var	ESNT_PHRASE		= 5;

function RunesContext( a_strSrc )
{
	this.strSrc = a_strSrc;
	this.nCur = 0;
	this.bFailed = false;
	this.bNot = false;
	this.nWordIndex = 0;
	
	this.getCurChar = function()
	{
		return this.strSrc.charAt( this.nCur );
	}
	this.getChar = function( i )
	{
		return this.strSrc.charAt( i );
	}
	this.reachEnd = function()
	{
		return this.nCur >= this.strSrc.length;
	}
}

function DolWord( a_strWord, a_nPosition )
{
	this.strWord		= a_strWord;
	this.nPosition		= a_nPosition;
}

function SolNode(){}

function RunesService()
{
	this.langSev = new LanguageService();
	
	this.isOperator = function( a_str, a_nFrom )
	{
		var strOp = this.getWord( a_str, a_nFrom ).toLowerCase();

		if ( this.langSev.isOperator( strOp ) )
			return true;

		return false;
	}
	
	this.getLengthOfWordBreak = function( a_str, a_nFrom )
	{
		var i = a_nFrom, nLen = a_str.length;
		while ( i < nLen && this.langSev.isWordBreak( a_str.charAt( i ) ) )
			i++;
		return i - a_nFrom;
	}
	
	this.getLengthOfCJKWordBreak = function( a_str, a_nFrom )
	{
		var i = a_nFrom, nLen = a_str.length;
		while ( i < nLen && (this.langSev.isWordBreak( a_str.charAt( i ) ) || this.langSev.isCJKCodePoint( a_str.charAt( i ) )))
			i++;
		return i - a_nFrom;
	}

	this.getLengthOfWord = function( a_str, a_nFrom )
	{
		var i = a_nFrom, nLen = a_str.length;
		while ( i < nLen &&
				!this.langSev.isWordBreak( a_str.charAt( i ) ) &&
				!this.langSev.isQuote( a_str.charAt( i ) ) )
			++i;
		return i - a_nFrom;
	}
	
	this.getNonCJKWord = function( a_str, a_nFrom )
	{
		var i = a_nFrom, nLen = a_str.length;
		while ( i < nLen &&
				!this.langSev.isWordBreak( a_str.charAt( i ) ) &&
				!this.langSev.isCJKCodePoint( a_str.charAt( i ) ) &&
				!this.langSev.isQuote( a_str.charAt( i ) ) )
			++i;
		var nLen =  i - a_nFrom;
		return a_str.substr( a_nFrom, nLen );
	}

	this.getWord = function( a_str, a_nFrom )
	{
		var nLen = this.getLengthOfWord( a_str, a_nFrom );
		return a_str.substr( a_nFrom, nLen );
	}
	
	this.getTerm = function( a_Context, a_Rslt )
	{
		if ( this.langSev.isQuote( a_Context.getCurChar() ) )
		{
			a_Context.nCur++;

			var nLen = this.getLengthOfPhrase( a_Context.strSrc, a_Context.nCur );
			if ( nLen <= 0 )
				return false;

			a_Rslt.eType = ESNT_PHRASE;
			a_Rslt.strTerm = a_Context.strSrc.substr( a_Context.nCur, nLen );
			a_Context.nCur += nLen + 1;
		}
		else
		{
			var nLen = this.getLengthOfDefault( a_Context.strSrc, a_Context.nCur );
			if ( nLen <= 0 )
				return false;

			a_Rslt.eType = ESNT_DEFAULT;
			a_Rslt.strTerm = a_Context.strSrc.substr( a_Context.nCur, nLen );
			a_Context.nCur += nLen;
		}

		return true;
	}

	this.getOperator = function( a_Context, a_Rslt )
	{
		if ( a_Context.reachEnd() )
			return false;

		var strOp = this.getWord( a_Context.strSrc, a_Context.nCur ).toLowerCase();

		if ( this.langSev.isAND( strOp ) )
		{
			a_Rslt.eType = ESNT_AND;
			a_Context.nCur += strOp.length;
		}
		else if ( this.langSev.isOR( strOp ) )
		{
			a_Rslt.eType = ESNT_OR;
			a_Context.nCur += strOp.length;
		}
		else if ( this.langSev.isNOT( strOp ) )
		{
			a_Rslt.eType = ESNT_NOT;
			a_Context.nCur += strOp.length;
		}
		else
		{
			a_Rslt.eType = ESNT_OR;
		}
		
		return true;
	}

	this.getLengthOfPhrase = function( a_str, a_nFrom )
	{
		var i = a_nFrom, nLen = a_str.length;
		while ( i < nLen )
		{
			if ( this.langSev.isQuote( a_str.charAt( i ) ) )
				return i - a_nFrom;
			++i;
		}
		return -1;
	}

	this.getLengthOfDefault = function( a_str, a_nFrom )
	{
		var i = a_nFrom, nLen = a_str.length;
		while ( i < nLen &&
				!this.isOperator( a_str, i ) &&
				!this.langSev.isQuote( a_str.charAt( i ) ) )
		{
			i += this.getLengthOfWord( a_str, i );
			i += this.getLengthOfWordBreak( a_str, i );
		}
		return i - a_nFrom;
	}

	this.parseOperator = function( a_Context, a_Result, a_bNotAllowed )
	{
		a_Context.nCur += this.getLengthOfWordBreak( a_Context.strSrc, a_Context.nCur );

		var rslt = new Object;
		if ( !this.getOperator( a_Context, rslt ) )
			return false;

		if ( rslt.eType == ESNT_NOT )
		{
			if (a_bNotAllowed)
			{
			    if ( a_Context.bNOT )
			    {
				    rslt.eType = ESNT_OR;
			    }
			    else
			    {
				    a_Context.bNOT = true;
			    }
			}
			else
			{
			    a_Context.bFailed = true;
			    return false ;			
			}
		}
		a_Result.eType = rslt.eType;
		a_Result.right = new SolNode();
		if ( !this.parseTerm( a_Context, a_Result.right ) )
			return false;

		return true;
	}

	/**
	Start parsing the search query from a_Context.nCur and check for presence of a phrase or normal term
	Or a term prefixed with NOT operator. In case a phrase or normal term is encountered, check for operators
	in the rest of the expression.	
	A term can contain many words for e.g. 
	Search query: hello world AND first topic
	This consist of two search terms: 
	1) hello world
	2) first topic
	And each of these terms have two words each.
	**/
	this.parseTerm = function( a_Context, a_Result )
	{
		a_Context.nCur += this.getLengthOfWordBreak( a_Context.strSrc, a_Context.nCur );

		var rslt = new Object;
		if ( !this.getTerm( a_Context, rslt ) )
		{
			if (( this.parseOperator( a_Context, rslt, true ) )&&(rslt.eType == ESNT_NOT))
		    {
		        a_Result.eType = rslt.eType;
		        if (rslt.right.eType == ESNT_DEFAULT)
		        {
			        a_Result.strTerm = rslt.right.strTerm;	
			        return true ;		    
			    }
			    else
			    {
			        a_Context.bFailed = true;
			        return false;
			    }
		    }
		    else
		    {
		        a_Context.bFailed = true;
			    return false;
		    }			
		}

		if ( this.parseOperator( a_Context, a_Result, false ) )
		{
			a_Result.left = new SolNode();
			a_Result.left.eType = rslt.eType;
			a_Result.left.strTerm = rslt.strTerm;
		}
		else
		{
			a_Result.eType = rslt.eType;
			a_Result.strTerm = rslt.strTerm;
		}
		
		return true;
	}

	this.extractTerm = function( a_Context, a_Term )
	{
		a_Term.aWords = new Array();
		this.dolSegment( a_Term );

		if ( a_Term.aWords.length == 0 )
			return false;

		if(a_Term.eType != ESNT_PHRASE)
		{
			//if search type is not phrase search, remove special break characters
			var j =0;
			for ( var i = 0; i < a_Term.aWords.length; i++ )
			{
				if(!this.langSev.isSpecialBreak( a_Term.aWords[i].strWord.charAt( 0 ) ))
				{
					a_Term.aWords[j] = a_Term.aWords[i];
					j++;
				}
			}
			a_Term.aWords.length = j;
		}
		
		for ( var i = 0; i < a_Term.aWords.length; i++ )
		{
			a_Term.aWords[i].nWordId = a_Term.aWords[i].nPosition + a_Context.nWordIndex;
		}
		a_Context.nWordIndex = a_Term.aWords[a_Term.aWords.length - 1].nWordId + 1;
		return true;
	}

	/**
	Check each term in the query and break up each term in individual words.
	**/
	this.parsePhraseAndDefault = function( a_Context, a_Node )
	{
		if ( a_Node.eType == ESNT_PHRASE || a_Node.eType == ESNT_DEFAULT || a_Node.eType == ESNT_NOT)
		{
			if ( !this.extractTerm( a_Context, a_Node ) &&
				 a_Node.eType == ESNT_PHRASE )
				a_Context.bFailed = true;
		}
		else
		{
			this.parsePhraseAndDefault( a_Context, a_Node.left );
			this.parsePhraseAndDefault( a_Context, a_Node.right );
		}
	}

	this.helStem = function( a_strOrg, a_Result )
	{
		this.langSev.getNormalizedOrg( a_strOrg, a_Result );
		this.langSev.helStem( a_Result );
	}
	
	/**
	 * Check presence of any break characters in given term, starting from "cur" position.
	 * If the break characters present are special break/CJK, include them in a_Result.
	 * Update position of next word and also character position of next non breaking
	 * character in the term. 
	 * Change the term type to phrase, if a CJK break is encountered.
	 */
	this.parseBreakCharacters  = function( a_Term , a_positions)
	{
		var a_strSrc = a_Term.strTerm;
		var a_Result = a_Term.aWords ;	
		var nLen = a_strSrc.length;
		var nCur = a_positions["cur"];
		var nPosition = a_positions["pos"] ;
		var bCJKTerm = false ;
		var bCJKBreak = false ;
		while ( nCur < nLen && (this.langSev.isWordBreak( a_strSrc.charAt( nCur )) || this.langSev.isCJKCodePoint( a_strSrc.charAt( nCur ))) )
		{
			
			if ( this.langSev.isSpecialBreak( a_strSrc.charAt( nCur ) ) || (bCJKBreak = this.langSev.isCJKCodePoint( a_strSrc.charAt( nCur ))) )
			{
				//it's a special word/CJK break, include it in search
				a_Result[a_Result.length] = new DolWord( a_strSrc.charAt( nCur ), nPosition );
				nPosition++;
				
				if (!bCJKTerm && bCJKBreak) //set the term as CJK term
					bCJKTerm  = true ;
			}
			nCur++;
		}
		a_positions["cur"] = nCur ;
		a_positions["pos"] = nPosition ;		
		
		if (bCJKTerm)
			a_Term.eType = ESNT_PHRASE ;
	}
	
	/**
	Break the current term in words.
	If the term contains CJK characters, treat each one of them
	as a seperate word.
	If any of the words is a CJK character, treat this term as a phrase term.
	**/
	this.dolSegment = function( a_Term )
	{
		var a_strSrc = a_Term.strTerm;
		var a_Result = a_Term.aWords ;		
		var nLen = a_strSrc.length;
		var strWord = "";		
		var positions = new Array();
		positions["cur"] = 0 ;
		positions["pos"] = 1 ;

		this.parseBreakCharacters( a_Term, positions );
		
		while ( positions["cur"] < nLen )
		{
			strWord = this.getNonCJKWord( a_strSrc, positions["cur"] );
			a_Result[a_Result.length] = new DolWord( strWord, positions["pos"] );
			
			positions["cur"] += strWord.length;
			positions["pos"]++ ;
			
			//check if we can find some special break/CJK characters in between this and next word		
			this.parseBreakCharacters( a_Term, positions  );
		}
	}
	
	this.solParse = function( a_strSrc, a_Result )
	{
		var context = new RunesContext( a_strSrc );
		this.parseTerm( context, a_Result );

		if ( context.bFailed )
			return false;
			
		this.parsePhraseAndDefault( context, a_Result );
		if ( context.bFailed )
			return false; 

		return true;
	}
}

function _helStemNode( a_Runes, a_Node )
{
	with ( a_Node )
	{
		if ( eType == ESNT_PHRASE || eType == ESNT_DEFAULT || eType == ESNT_NOT)
		{
			for ( var i = 0; i < aWords.length; i++ )
			{
				a_Runes.helStem( aWords[i].strWord, aWords[i] )
			}
		}
		else
		{
			_helStemNode( a_Runes, left );
			_helStemNode( a_Runes, right );
		}
	}
}

function findSearchTerms(searchTerms)
{
	var runes = new RunesService();
	var expression = new SolNode();
	if ( !runes.solParse( searchTerms, expression ) )
		return ;
		
	_helStemNode( runes, expression )
		
	buildSearchTerms(expression) ;	
}

function buildSearchTerms(a_Node)
{
	if (a_Node.eType == ESNT_NOT	)
		return ;
	else if ( a_Node.eType == ESNT_PHRASE )
	{
		gaSearchTerms[gaSearchTerms.length] = trim( a_Node.strTerm ) ;
	}
	else if ( a_Node.eType == ESNT_DEFAULT )
	{
		with ( a_Node )
		{
			for ( var i = 0; i < aWords.length; i++ )
			{
				gaSearchTerms[gaSearchTerms.length] = aWords[i].strHelStem ;
			}
		}
	}
	else
	{
		buildSearchTerms(  a_Node.left );
		buildSearchTerms(  a_Node.right );
	}
}

function trim(stringToTrim) {
	return stringToTrim.replace(/^\s+|\s+$/g,"");
}

function utf8Compare(strText1,strText2)
{
	var strt1=strText1;
	var strt2=strText2;

	try {
		strt1=strText1.toLowerCase();
	}
	catch(er) {
	}

	try {
		strt2=strText2.toLowerCase();
	}
	catch(er) {
	}

	if(strt1<strt2) return -1;
	if(strt1>strt2) return 1;
	return 0;
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

function onGetPPStartPage(xmlDoc, sPath)
{
    if(xmlDoc == null)
	    return;
        
	var root = xmlDoc.documentElement;
	var masterProj = null;
	try
	{
		masterProj = xmlDoc.getElementsByTagName("syncinfo");	
		if(masterProj.length > 0)
		{
			var startpage = xmlDoc.getElementsByTagName("startpage");	
			if(startpage.length>0)
			{
			    masterStartPageName = startpage[0].getAttribute("name");
			    masterStartPageRelPath = startpage[0].getAttribute("url");
			    var sXmlFolderPath = _getPath(sPath);
	            sXmlFolderPath = _getFullPath(sXmlFolderPath, masterStartPageRelPath+masterStartPageName);
                
                gsStartPage = sXmlFolderPath;
                gsRelCurPagePath=_getRelativeFileName(gsStartPage, decodeURI(document.location.href));
                
	            getPPStartPagePath(sXmlFolderPath);
			}
		}
	}
	catch(e)
	{
	    return;
	}
}

function getPPStartPagePath(sPath)
{
	if(sPath.length != 0)
	{
		var sXmlFolderPath = _getPath(sPath);
		if(sXmlFolderPath.indexOf("/mergedProjects/") == -1 &&
			sXmlFolderPath.indexOf("\\mergedProjects\\") == -1)
        {
			return;
        }
		
		var sdocPath = _getFullPath(sXmlFolderPath, "MasterData.xml");
        var sCurrentDocPath = _getPath(decodeURI(document.location.href));
        var fileName = _getRelativeFileName(sCurrentDocPath, sdocPath);

        xmlJsReader.loadFile(fileName, function (a_xmlDoc, args) {
            onGetPPStartPage(a_xmlDoc, args);
        }, sPath);
	}
}

//project info
function setRelStartPage(sPath)
{
	if(gsPPath.length==0)
	{
		gsPPath=_getFullPath(_getPath(decodeURI(document.location.href)), _getPath(sPath));
		onSetStartPage();
		
		gsStartPage=_getFullPath(_getPath(decodeURI(document.location.href)), sPath);
		gsRelCurPagePath=_getRelativeFileName(gsStartPage, decodeURI(document.location.href));
		
		try{
			getPPStartPagePath(gsStartPage);
		}
		catch(e)
		{
			alert("Error reading masterData.xml");
		}
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
			var sTitle="%%%WH_LNG_PreTooltip%%%";
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
			var sTitle="%%%WH_LNG_NextTooltip%%%";
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
			var sTitle="%%%WH_LNG_Show_Navigation_Component%%%";
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
			var sTitle="%%%WH_LNG_Hide_Navigation_Component%%%";
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
			var sTitle="%%%WH_LNG_Show_Navigation_Component%%%";
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
			var sTitle="%%%WH_LNG_Hide_Navigation_Component%%%";
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
			var sTitle="%%%WH_LNG_SyncTocTooltip%%%";
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
			var sTitle="%%%WH_LNG_WebSearch%%%";
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

function onGetSyncSupport(oMsg)
{
	if(oMsg && oMsg.oParam)
	{
		gbSyncEnabled=oMsg.oParam;
	}
}

function GetSyncEnabled()
{
	var oMsg=new whMessage(WH_MSG_ISSYNCSSUPPORT,null,null);
	request(oMsg, onGetSyncSupport);
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
			if(gaTypes[i]!="synctoc"||gbSyncEnabled)
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
		var alignment1 = (gsPageDir == "rtl")?"right":"left";
		var alignment2 = (gsPageDir == "ltr")?"right":"left";
		if(nAligns!=0)
		{
			sHTML+="<table width=100%><tr>"
			if(nAligns&1)
				sHTML+="<td width=33%>"+getIntopicBar(alignment1)+"</td>";
			if(nAligns&2)
				sHTML+="<td width=34%>"+getIntopicBar("center")+"</td>";
			if(nAligns&4)
				sHTML+="<td width=33%>"+getIntopicBar(alignment2)+"</td>";
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
	var oMsg=new whMessage(WH_MSG_AVENUEINFO,gaAvenues, null);
	notify(oMsg);
}


function onNext()
{
	var oMsg=new whMessage(WH_MSG_NEXT,null,null);
	notify(oMsg);
}

function onPrev()
{
	var oMsg=new whMessage(WH_MSG_PREV,null,null);
	notify(oMsg);
}

function createSyncInfo()
{
	var oParam=new Object();
	var sPath = null;
    if(gsPPath.length==0)
		sPath =_getPath(decodeURI(document.location.href));
	else 
		sPath = gsPPath;
	oParam.sPPath=sPath;
	oParam.sTPath=decodeURI(document.location.href);
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
	var oMsg=new whMessage(WH_MSG_SHOWTOC,null,null);
	notify(oMsg);
}

function sendSyncInfo()
{
	if(!isInPopup())
	{
		var iParam=null;
		if(gaPaths.length>0)
		{
			iParam=createSyncInfo();
		}
		var oMsg=new whMessage(WH_MSG_SYNCINFO, iParam, null);
		notify(oMsg);
	}
}

function sendInvalidSyncInfo()
{
	if(!isInPopup())
	{
		var oMsg=new whMessage(WH_MSG_SYNCINFO,null,null);
		notify(oMsg);
	}
}

function enableWebSearch(bEnable)
{
	if(!isInPopup())
	{
		var oMsg=new whMessage(WH_MSG_ENABLEWEBSEARCH, bEnable, null);
		notify(oMsg);
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
	if(gnOutmostTopic==-1) {
	    if (window.name == 'bsscright')
	        gnOutmostTopic = 1;
	    else
	        gnOutmostTopic = 0;
	 }
	return (gnOutmostTopic==1);
}

function sync()
{
	if(gaPaths.length>0)
	{
		var iParam=createSyncInfo();
		var oMessage=new whMessage(WH_MSG_SYNCTOC,iParam, null);
		notify(oMessage);
	}
}


function avenueInfo(sName,sPrev,sNext)
{
	this.sName=sName;
	this.sPrev=sPrev;
	this.sNext=sNext;
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

function onReceiveNotification(oMsg)
{
	var nMsgId=oMsg.msgId;
	if(nMsgId==WH_MSG_NEXT)
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
	else if(nMsgId==WH_MSG_PRINT)
	{
		window.print();
	}
	return true;
}

function onReceiveRequest(oMsg)
{
    var nMsgId=oMsg.msgId;
    if(nMsgId==WH_MSG_GETAVIAVENUES)
	{
		oMsg.oParam.aAvenues=gaAvenues;
		reply(oMsg);
		return false;
	}
	else if(nMsgId==WH_MSG_GETTOCPATHS)
	{
		if(isOutMostTopic())
		{
			oMsg.oParam.oTocInfo=createSyncInfo();
			reply(oMsg);
			return false;		
		}
		else
			return true;
	}
	return true;
}


function onGetCurrentAvenue(oMsg)
{
	if(oMsg && oMsg.oParam)
	{
	    var sTopic=null;
	    var sAvenue= oMsg.oParam.sAvenue;
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
			    if(oMsg.iParam.bNext)
				    sTopic=gaAvenues[nAvenue].sNext;
			    else
				    sTopic=gaAvenues[nAvenue].sPrev;
		    }
	    }
	    else
	    {
		    for(var i=0;i<gaAvenues.length;i++)
		    {
			    if(gaAvenues[i].sNext!=null&&gaAvenues[i].sNext.length>0&&oMsg.iParam.bNext)
			    {
				    sTopic=gaAvenues[i].sNext;
				    break;
			    }
			    else if(gaAvenues[i].sPrev!=null&&gaAvenues[i].sPrev.length>0&&!oMsg.iParam.bNext)
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
}

function goAvenue(bNext)
{
    var iParam = new Object();
    iParam.bNext = bNext;
    var oParam=new Object();
	oParam.sAvenue=null;
	var oMsg=new whMessage(WH_MSG_GETCURRENTAVENUE, iParam, oParam);
	if(isChromeLocal())
	{
	    //browse sequence selection is not yet implemented fully in webhelp, 
	    //so just ignoring current avenue
	    onGetCurrentAvenue(oMsg);
	}
	else    
        request(oMsg, onGetCurrentAvenue);
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
	var strUrl = '';
	if(gbBadUriError)
	{
		var strMainPage = decodeURI(document.location.href);
		var indx = strMainPage.toLowerCase().indexOf("/mergedprojects/");
		if(indx != -1)
			strUrl = strMainPage.substring(0, indx+1) + "whcsh_home.htm#topicurl=" + strMainPage.substring(indx+1);
		else if(gsStartPage!="")
			strUrl =gsStartPage+"#"+gsRelCurPagePath;
	}
	else if(gsStartPage!="")
			strUrl =gsStartPage+"#"+gsRelCurPagePath;
	window.location = strUrl;
}

function hide()
{
	if(goFrame!=null)
	{
		goFrame.location=decodeURI(window.location);
	}
	else
	{
		var oParam=new Object();
        oParam.oFrameName = "";
		oParam.oFrame=null;
		var oMsg=new whMessage(WH_MSG_GETSTARTFRAME,null,oParam);
		request(oMsg, function(oMsg){
            if(oMsg && oMsg.oParam)
            {
                if(oMsg.oParam.oFrame != null)
                {
                    goFrame= oMsg.oParam.oFrame;
                }
                else if(typeof(oMsg.oParam.oFrameName) != undefined && oMsg.oParam.oFrameName != "")
                {
                    try{
                        var oWnd = top.frames[oMsg.oParam.oFrameName];
                        if (typeof (oWnd) != 'undefined' && oWnd != null)
                        {
                            goFrame = oWnd;
                        }
                    }catch(e) {}
                }
                else if(isChromeLocal())
                    goFrame = top;
                if(goFrame != null)
                        goFrame.location=decodeURI(window.location);
            }
        });
	}	
}

function isTopicOnly()
{
	if(gnTopicOnly==-1)
	{
	    if(isChromeLocal())
	    {
	        if(window.name == 'bsscmain' || //for webhelp pro outputs one pane view topic opens in a frame with name 'bsscmain'
		window.name == 'topicwindow' || //'topicwindow' is for opening CSH with window
		window.name == 'ContentFrame')  //Topic can be directly open in 'ContentFrame' for DUCC 508 compliant output, when toc pane is hidden
	            gnTopicOnly=1; 
	        else if (window.name == 'bsscright' || window != top)
	            gnTopicOnly=0;
	        else
	            gnTopicOnly=1;
	    }
	    else
	    {
		     var mainWnd = getStubPage();
		     if(mainWnd != null)
			     gnTopicOnly = 0;
		     else
			     gnTopicOnly = 1;
	    }
	}
	return (gnTopicOnly==1);
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
	var oMsg=new whMessage(WH_MSG_SEARCHTHIS, sValue, null);
	notify(oMsg);
}


function getSearchFormHTML()
{
	var sHTML="";
	gnForm++;
	var sFormName="searchForm"+gnForm;
	var sButton="<form name=\""+sFormName+"\" method=\"POST\" action=\"javascript:searchB("+gnForm+")\">"
	sButton+="<input type=\"text\" name=\"searchString\" value=\"- Full Text search -\" size=\"20\"/>";
	if("%%%WH_WEBSKIN.topic.btn.searchform.form.mode%%%"=="text")
	{
		sButton+="<a class=\"searchbtn\" href=\"javascript:void(0);\" onclick=\""+sFormName+".submit();return false;\">%%%WH_WEBSKIN.topic.btn.searchform.form.text.face%%%</a>";
	}
	else if("%%%WH_WEBSKIN.topic.btn.searchform.form.mode%%%"=="image")
	{
		sButton+="<a class=\"searchbtn\" href=\"javascript:void(0);\" onclick=\""+sFormName+".submit();return false;\">"
		sButton+="<img src=\"%%%WH_WEBSKIN.topic.btn.searchform.form.btn.image%%%\" border=0></a>";
	}
	sButton+="</form>";
	sHTML="<td align=\"center\">"+sButton+"</td>";
	return sHTML;
}




function showHidePane(bShow)
{
	var oMsg=null;
	if(bShow)
		oMsg=new whMessage(WH_MSG_SHOWPANE,null,null);
	else
		oMsg=new whMessage(WH_MSG_HIDEPANE,null,null);
	notify(oMsg);
}

function isShowHideEnable()
{
	if(gbIE4)
		return true;
	else
		return false;
}

var gMsgSeparator = "::";
function installListerner_air()
{
    /* this function will install msg listener for browser based air help */
    if (isChromeLocal()) {
        window.addEventListener("message", onReceiveMsg_air, false);
    }
}

function createFrameInfo()
{
        var frameInfo = new Object();
        frameInfo.frameID = gFrameId;
        frameInfo.title = escape(document.title);
        if(typeof(RH_BreadCrumbDataStringVariable) != 'undefined')
            frameInfo.breadCrumbString = escape(RH_BreadCrumbDataStringVariable);
        else
            frameInfo.breadCrumbString = "";
         
        if(typeof(flex_previousLocation) != 'undefined')
            frameInfo.previous = escape(flex_previousLocation);
        else 
            frameInfo.previous = "";
            
        if(typeof(flex_nextLocation) != 'undefined')
            frameInfo.next = escape(flex_nextLocation);
        else 
            frameInfo.next = "";
        frameInfo.relativepath = escape(gsRelCurPagePath);
        frameInfo.newLocation = escape(window.location.href);
        if(isChromeLocal())
        	this.parent.postMessage("getFrameInfo" + gMsgSeparator + JSON.stringify(frameInfo), "*");
		else if(typeof(this.parent.updateFrameInfo) == 'function')
			this.parent.updateFrameInfo(frameInfo);
}

function onReceiveMsg_air(event)
{
    var msg = event.data.split(gMsgSeparator);
    var msgType = msg[0];
    var msgData = msg[1];
    switch(msgType)
    {
    case "startHightlight":
        StartHighLightSearch(msgData);
        break;
    case "getFrameInfo":
		gFrameId = msgData;
        break;
      case "print":
            window.print()
            break;
    }
}

if(window.gbWhUtil && window.gbWhVer && (gbAIRSSL ||(window.gbWhMsg&&window.gbWhProxy)))
{
	if(!gbAIRSSL)
	{
		registerListener("bsscright",WH_MSG_GETAVIAVENUES);
		registerListener("bsscright",WH_MSG_GETTOCPATHS);
		registerListener("bsscright",WH_MSG_NEXT);
		registerListener("bsscright",WH_MSG_PREV);
		registerListener("bsscright",WH_MSG_WEBSEARCH);
		registerListener("bsscright",WH_MSG_PRINT);
	}
	else
	{
	    installListerner_air();   
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
	%%% SCRIPT_OutputTopicBtnFont(); %%%
	if(!gbAIRSSL)
	    GetSyncEnabled();
	gbWhTopic=true;
}
else
	document.location.reload();

function PickupDialog_Invoke()
{
	if(gbIE4 || gbNav6)
	{
		if(PickupDialog_Invoke.arguments.length>2)
		{
			var sPickup="%%%SF_PICKUP_HTM%%%";
			var sPickupPath=gsPPath+sPickup;
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
					{
						var vRet=window.showModalDialog(sPickupPath,aTopics,"center:yes;dialogHeight:"+nHeight+"px;dialogWidth:"+nWidth+"px;resizable:yes;status:no;scroll:no;help:no;");
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
	else
	{
		if(typeof(_PopupMenu_Invoke)=="function")
			return _PopupMenu_Invoke(PickupDialog_Invoke.arguments);
	}
}

function escapeRegExp(str)
{
	var specials = new RegExp("[.*+?|()\\^\\$\\[\\]{}\\\\]", "g"); // .*+?|()^$[]{}\
	return str.replace(specials, "\\$&");
}
