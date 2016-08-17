//	WebHelp 5.10.001
var gfunLookUp;
var gbInputEnable;
var gfunInit;
var gstrFormName= "";
var gbWithButton = false;
var gsTitle="";
var gsHiliteSearchTitle="";
var gsMaxSearchTitle="";
var gsOverImage = "";
var gsOutImage = "";
var gsClickImage = "";
var gsText = "";
var gsBgColor = "#c0c0c0";
var gsBgImage = "";
var gbInImage = 0;
var gbInputEnable = 0;
var gbHighlightSearch = false;

var goTitleFont=null;
var goInputFont=null;
var goNormalFont=null;
var goHoverFont=null;
var gnType=-1;
var gbWhForm=false;
var gnMaxRslt = 10 ;

function setBackground(sBgImage)
{
	if (sBgImage != null && sBgImage.length > 0)
		gsBgImage = sBgImage;

	if  (gsBgImage  && gsBgImage .length > 0)
	{
		document.body.background = gsBgImage ;
	}
}

function setBackgroundcolor(sBgColor)
{
	if (sBgColor != null && sBgColor.length > 0)
		gsBgColor = sBgColor;

	if (gsBgColor&& gsBgColor.length > 0)
	{
		document.body.bgColor = gsBgColor;
	}
}

function setBtnType(sType)
{
	if (sType == "image")
	{
		gnType = 0;
	}
	else if (sType == "text")
	{
		gnType = 1;
	}
}

function setGoImage1(sImage1)
{
	gsOutImage = sImage1;
	if (gsOutImage && gsOutImage.length > 0)
		gbWithButton = true;
}

function setGoImage2(sImage2)
{
	gsOverImage = sImage2;
	if (gsOverImage && gsOverImage.length > 0)
		gbWithButton = true;
}

function setGoImage3(sImage3)
{
	gsClickImage = sImage3;
	if (gsClickImage && gsClickImage.length > 0)
		gbWithButton = true;
}

function setGoText(sText)
{
	gsText = sText;
	if (gsText.length > 0)
		gbWithButton = true;
}

function setFont(sType, sFontName, sFontSize, sFontColor, sFontStyle, sFontWeight, sFontDecoration)
{
	var vFont = new whFont(sFontName, sFontSize, sFontColor, sFontStyle, sFontWeight, sFontDecoration);
	if (sType == "Title")
	{
		goTitleFont = vFont;
		var vFont1 = new whFont(sFontName, sFontSize, "black", sFontStyle, sFontWeight, sFontDecoration);
		goInputFont=vFont1;		
	}
	else if (sType == "Normal")
		goNormalFont = vFont;
	else if (sType == "Hover")
		goHoverFont = vFont;
}

function setHighlightSearch(bEnable)
{
	gbHighlightSearch = bEnable;
}

function writeFormStyle()
{
	var sStyle = "<style type='text/css'>";
	sStyle += "p.title {" + getFontStyle(goTitleFont) + "margin-top:0;margin-bottom:0}\n";
	sStyle += ".inputfield {" + getFontStyle(goInputFont) +"width:100%; }\n";
	sStyle += ".maxfield {" + getFontStyle(goInputFont) +"width:12%; }\n";
	sStyle += ".hilite {" + getFontStyle(goTitleFont) + "margin-top:0;margin-bottom:0; }\n";
	sStyle+="A:link {"+getFontStyle(goNormalFont)+"}\n";
	sStyle+="A:visited {"+getFontStyle(goNormalFont)+"}\n";
	sStyle +="A:hover {"+getFontStyle(goHoverFont)+"}\n";
	sStyle+=".clsFormBackground{\n";
	if (gsBgImage)
		sStyle+="border-top:"+gsBgColor+" 1px solid;}\n";
	else
		sStyle+="border-top:black 1px solid;}\n";

	sStyle += "</style>";
	document.write(sStyle);
}

function lookupKeyDown(NSEvent)
{
	
	if (gbInputEnable)
	{
		if (gbIE4)
		{
			if (event.keyCode == 13)	//Enter key
				gfunLookUp(true);
			else
				gfunLookUp(false);
		}	
		else if (gbNav6 || gbSafari)		
		{
		    if(NSEvent)
		    {
		        if (NSEvent.which== 13)	//Enter key
				    gfunLookUp(true);
			    else
				    gfunLookUp(false);
		    }
		    else
				gfunLookUp(false);
		}
		else
			gfunLookUp(false);
	}
}

function init()
{
	if (gfunInit)
		gfunInit();
	if (!window.Array)  return;
	if (window.captureEvents){
        window.captureEvents(Event.KEYUP);
        window.onkeyup=lookupKeyDown;
    }
    else
        document.onkeyup=lookupKeyDown;		
}

function inputSubmit()
{
	if ((gbInputEnable && !gbIE4)|| gbInImage)
		gfunLookUp(true);
}

function inputEnable(bEnable)
{
	gbInputEnable = bEnable;
}

function inImage(bImage)
{
	gbInImage = bImage;
}

function getFormHTML()
{
	var sForm = "";
	sForm += "<table class=\"clsFormBackground\" width=\"100%\" cellspacing=\"0\" cellpadding=\"5\" border=\"0\">";
	sForm += "<form name=\"" + gstrFormName + "\" method=\"POST\" action=\"javascript:inputSubmit()\" style=\"width:100%\">";
	sForm += "<tr>";
	sForm += "<td>";
	if(gbSafari3)
	    sForm += "<p class=title>" + gsTitle + "<br><table width=\"100%\"><tr valign=\"middle\"><td width=\"100%\"><input class=\"inputfield\" type=\"text\" name=\"keywordField\" onfocus=\"inputEnable(1);\"\"></td>";
	else
	    sForm += "<p class=title>" + gsTitle + "<br><table width=\"100%\"><tr valign=\"middle\"><td width=\"100%\"><input class=\"inputfield\" type=\"text\" name=\"keywordField\" onKeyPress=\"lookupKeyDown();\" onfocus=\"inputEnable(1);\" onblur=\"inputEnable(0);\"></td>";
	if (gbWithButton && gnType >= 0)
	{
		sForm += "<td><a title=\"submit button\" href=\"javascript:void(0);\" onclick=\"" + gstrFormName + ".submit(); return false;\" onfocus=\"inImage(1);\" onblur=\"inImage(0);\" onmouseup=\"onMouseUp();\" onmousedown=\"onMouseDown();\" onmouseover=\"onMouseOver();\" onmouseout=\"onMouseOut();\">"
		if (gnType == 0)
		{
			if (!gsText)
				gsText="Go";
			sForm += "<img alt=\""+gsText+"\" id=\"go\" border=\"0\" src=\"" + gsOutImage + "\">";
		}
		else
			sForm += gsText ;
		sForm += "</a></td>";
	}
	if(gbHighlightSearch == true)
	{
		sForm += "</tr><tr class=\"hilite\" valign=\"middle\"><td width=\"100%\"><input type=\"checkbox\" name=\"HiLite\" checked>" + gsHiliteSearchTitle + "<br></td>";
	}

	sForm += "</tr>" ;
	
    //check pane in focus is Search pane.
	var oMsg=new whMessage(WH_MSG_GETPANEINFO,this,1,null);
	if(SendMessage(oMsg)) {
		if (oMsg.oParam == "fts") 
			sForm += "<tr class=\"hilite\" valign=\"middle\"><td>" + gsMaxSearchTitle + "  <input class=\"maxfield\" type=\"text\" maxLength=\"2\" name=\"MaxResults\" onblur=\"javascript: this.value =input_filter(this.value)\" value=\"" + gnMaxRslt + "\" ></td></tr>" ;
	}
	sForm += "</table></p></td></tr></form></table>";
	return sForm;
}

function input_filter(a_sVal)
{
	var num = gnMaxRslt ;
	try 
	{
		num=parseInt(a_sVal);
		if (( num.toString() == "NaN")||(num <= 0))
		    num = gnMaxRslt ;
	}
	catch(er) 
	{
	    num = gnMaxRslt ;
	}
	return num ;
}

function onMouseOver()
{
	if (getElement("go") && gsOverImage)
		getElement("go").src = gsOverImage;
}

function onMouseDown()
{
	if (getElement("go") && gsClickImage)
		getElement("go").src = gsClickImage;
}

function onMouseUp()
{
	if (getElement("go") && gsOutImage)
		getElement("go").src = gsOutImage;
}

function onMouseOut()
{
	if (getElement("go") && gsOutImage)
		getElement("go").src = gsOutImage;
}

if (window.gbWhUtil&&window.gbWhVer&&window.gbWhProxy&&window.gbWhMsg)
{
	goTitleFont=new whFont("Arial", "9pt", "#000000", "normal", "normal", "none");
	goNormalFont=new whFont("Arial", "9pt", "#000000", "normal", "normal", "none");
	goHoverFont=new whFont("Arial", "9pt", "#000000", "normal", "normal", "underline");
	gbWhForm=true;
}
else
	document.location.reload();