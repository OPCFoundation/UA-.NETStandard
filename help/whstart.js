//	WebHelp 5.10.003
registerListener2(this, WH_MSG_GETSTARTFRAME);
registerListener2(this, WH_MSG_GETDEFAULTTOPIC);
registerListener2(this, WH_MSG_MINIBARORDER);
registerListener2(this, WH_MSG_TOOLBARORDER);
registerListener2(this, WH_MSG_ISSEARCHSUPPORT);
registerListener2(this, WH_MSG_ISSYNCSSUPPORT);
registerListener2(this, WH_MSG_ISAVENUESUPPORT);
registerListener2(this, WH_MSG_GETPANETYPE);
registerListener2(this, WH_MSG_GETPANES);
registerListener2(this, WH_MSG_RELOADNS6);
registerListener2(this, WH_MSG_GETCMD);
registerListener2(this, WH_MSG_GETPANE);
registerListener2(this, WH_MSG_GETDEFPANE);

if (gbNav4 && !gbNav6)
{
	var gnReload=0;
	setTimeout("delayReload();",5000);
}

function delayReload()
{
	if (!(this.cMRServer && cMRServer.m_strVersion))
	{
		if(gnReload!=2)
		{
			if(typeof(nViewFrameType) != "undefined" && nViewFrameType==1)
				document.location=decodeURI(document.location);
		}
	}
}

var gsToolbarOrder = "";
var gsMinibarOrder = "";

var gsTopic = "welcome.htm";
var PANE_OPT_SEARCH = 1;
var PANE_OPT_BROWSESEQ = 2;
var gnOpts=-1;
var gnCmd=-1;
var gnPans=2;
var gsBtns="invalid";
var gsDefaultBtn="invalid";
var gbHasTitle=false;

if (location.hash.length > 1)
{
	var sParam = decodeURIComponent(location.hash);
	sParam = PatchParametersForEscapeChar(sParam);
	if (sParam.indexOf("#<") == 0)
	{
		document.location = "whcsh_home.htm#" + sParam.substring(2);
	}
	else if (sParam.indexOf("#>>") == 0)
	{
		parseParam(sParam.substring(3));
		sParam = "#" + gsTopic + sParam.substring(1);
	}
	else
	{
		var nPos = sParam.indexOf(">>");
		if (nPos>1)
		{
			if(IsInternal(sParam.substring(1, nPos)))
				gsTopic = sParam.substring(1, nPos);
			parseParam(sParam.substring(nPos+2));
		}
		else
		{
			if(IsInternal(sParam.substring(1)))
				gsTopic = sParam.substring(1);
		}
	}
	if (gnPans == 1 && gsTopic)
	{
		var strURL=decodeURI(location.href);
		if (location.hash)
		{
			var nPos=location.href.indexOf(location.hash);
			strURL=strURL.substring(0, nPos);
		}
		if (gbHasTitle)
			document.location=_getPath(strURL)+ "/*% SF_TOPIC_WINDOW %*/" + sParam;
		else
			document.location=_getPath(strURL)+ gsTopic;
	}
}

function parseParam(sParam)
{
	if (sParam)
	{
		var nBPos=0;
		do 
		{
			var nPos=sParam.indexOf(">>", nBPos);
			if (nPos!=-1)
			{
				if (nPos>0)
				{
					var sPart=sParam.substring(nBPos, nPos);
					parsePart(sPart);
				}
				nBPos = nPos + 2;
			}
			else
			{
				var sPart=sParam.substring(nBPos);
				parsePart(sPart);
				break;
			}
		} while(nBPos < sParam.length);
	}	
}

function parsePart(sPart)
{
	if(sPart.toLowerCase().indexOf("cmd=")==0)
	{
		gnCmd=parseInt(sPart.substring(4));
	}
	else if(sPart.toLowerCase().indexOf("cap=")==0)
	{
		document.title=_browserStringToText(sPart.substring(4));
		gbHasTitle=true;
	}
	else if(sPart.toLowerCase().indexOf("pan=")==0)
	{
		gnPans=parseInt(sPart.substring(4));
	}
	else if(sPart.toLowerCase().indexOf("pot=")==0)
	{
		gnOpts=parseInt(sPart.substring(4));
	}
	else if(sPart.toLowerCase().indexOf("pbs=")==0)
	{
		var sRawBtns = sPart.substring(4);
		var aBtns = sRawBtns.split("|");
		for (var i=0;i<aBtns.length;i++)
		{
			aBtns[i] = transferAgentNameToPaneName(aBtns[i]);
		}
		gsRawBtns = aBtns.join("|");
	}
	else if(sPart.toLowerCase().indexOf("pdb=")==0)
	{
		gsDefaultBtn=transferAgentNameToPaneName(sPart.substring(4));
	}
}

function setToolbarOrder(sOrder)
{
	gsToolbarOrder = sOrder;
}

function setMinibarOrder(sOrder)
{
	gsMinibarOrder = sOrder;
}

function onReceiveRequest(oMsg) {
    var nMsgId = oMsg.msgId;
    if (nMsgId == WH_MSG_GETDEFAULTTOPIC)
	{
		if (this.cMRServer && cMRServer.m_strVersion)
		{
			if (cMRServer.m_strURLTopic);
			{
				oMsg.oParam.sTopic = cMRServer.m_strURLTopic;
				reply(oMsg);
				return false;
			}

		}
		oMsg.oParam.sTopic = gsTopic;
		reply(oMsg);
		return false;
    }
    else if (nMsgId == WH_MSG_TOOLBARORDER) {
        if (this.cMRServer && cMRServer.m_strVersion) {
            var oPanes = new Object();
            var aAgentNames = null;
            if (cMRServer.m_strDefAgent)
                oPanes.sDefault = transferAgentNameToPaneName(cMRServer.m_strDefAgent);

            aPanes = new Array();
            for (var i = 0; i < cMRServer.m_cAgents.length; i++) {
                var nCur = aPanes.length;
                aPanes[nCur] = new Object();
                aPanes[nCur].sPaneName = transferAgentNameToPaneName(cMRServer.m_cAgents[i].m_strID);
                aPanes[nCur].sPaneURL = cMRServer.m_cAgents[i].m_strURL;
            }
            oPanes.aPanes = aPanes;

            var aToolbarOrder = cMRServer.m_strAgentList.split(";");
            var i = 0;
            for (i = 0; i < aToolbarOrder.length; i++)
                aToolbarOrder[i] = transferANToPN2(aToolbarOrder[i]);
            aToolbarOrder[aToolbarOrder.length] = "blankblock";
            if (cMRServer.m_bShowSearchInput) {
                aToolbarOrder[aToolbarOrder.length] = "searchform";
            }
            aToolbarOrder[aToolbarOrder.length] = "banner";
            var aToolbarOrderNew = new Array();
            for (i = 0; i < aToolbarOrder.length; i++) {
                if (isAPane(aToolbarOrder[i])) {
                    if (oPanes.aPanes && oPanes.aPanes.length) {
                        for (var j = 0; j < oPanes.aPanes.length; j++) {
                            if (aToolbarOrder[i] == oPanes.aPanes[j].sPaneName) {
                                aToolbarOrderNew[aToolbarOrderNew.length] = aToolbarOrder[i];
                                break;
                            }
                        }
                    }
                }
                else
                    aToolbarOrderNew[aToolbarOrderNew.length] = aToolbarOrder[i];
            }
            oMsg.oParam = aToolbarOrderNew.join("|");
            reply(oMsg);
            return false;
        }
        
        var sParam = "";
        if (gsBtns != "invalid")
            sParam = gsBtns + "|blankblock|banner";
        else
            sParam = gsToolbarOrder;

        if (gnOpts != -1) {
            var nPosForm = sParam.indexOf("|searchform|");
            if (gnOpts & PANE_OPT_SEARCH) {
                if (nPosForm == -1 && sParam.indexOf("|fts|") != -1) {
                    var nPos = sParam.indexOf("banner");
                    if (nPos != -1) {
                        sParam = sParam.substring(0, nPos) + "searchform|" + sParam.substring(nPos);
                    }
                }
            }
            else {
                if (nPosForm != -1) {
                    sParam = sParam.substring(0, nPosForm) + sParam.substring(nPosForm + 11);
                }
            }
        }
        oMsg.oParam = sParam;
        reply(oMsg);
        return false;
    }
    else if (nMsgId == WH_MSG_MINIBARORDER) {
        var sMinParam = gsMinibarOrder;
        if (gnOpts != -1) {
            var nPosBro = gsMinibarOrder.indexOf("avprev|avnext");
            if (gnOpts & PANE_OPT_BROWSESEQ) {
                if (nPosBro == -1) {
                    sMinParam = "avprev|avnext|" + gsMinibarOrder;
                }
            }
            else {
                if (nPosBro != -1) {
                    if (nPosBro != 0)
                        sMinParam = gsMinibarOrder.substring(0, nPosBro) + gsMinibarOrder.substring(nPosBro + 14);
                    else
                        sMinParam = gsMinibarOrder.substring(14);
                }
            }
        }
        oMsg.oParam = sMinParam;
        reply(oMsg);
        return false;
    }
    else if (nMsgId == WH_MSG_ISSYNCSSUPPORT) {
        if (this.cMRServer && cMRServer.m_strVersion) {
            if (cMRServer.m_bShowSync)
                oMsg.oParam = true;
            else
                oMsg.oParam = false;
            reply(oMsg);
            return false;
        }
        else {
            if (typeof (nViewFrameType) != "undefined") {
                oMsg.oParam = (nViewFrameType < 3);
                reply(oMsg);
                return false;
            }
        }
    }
    else if (nMsgId == WH_MSG_ISAVENUESUPPORT) {
        if (this.cMRServer && cMRServer.m_strVersion) {
            if (cMRServer.m_bShowBrowseSequences)
                oMsg.oParam = true;
            else
                oMsg.oParam = false;
        }
        else {
            oMsg.oParam = true;
        }
        reply(oMsg);
        return false;
    }
    else if (nMsgId == WH_MSG_ISSEARCHSUPPORT) {
        if (typeof (nViewFrameType) != "undefined") {
            oMsg.oParam = (nViewFrameType < 3);
            reply(oMsg);
            return false;
        }
    }
    else if (nMsgId == WH_MSG_GETPANETYPE) {
        if (typeof (nViewFrameType) != "undefined") {
            var oPaneInfo = new Object();
            oPaneInfo.nType = nViewFrameType;
            oPaneInfo.sPaneURL = strPane;
            oMsg.oParam = oPaneInfo;
            reply(oMsg);
            return false;
        }
    }
    else if (nMsgId == WH_MSG_GETPANES) {
        if (this.cMRServer && cMRServer.m_strVersion) {
            var oPanes = new Object();
            var aAgentNames = null;
            if (cMRServer.m_strDefAgent)
                oPanes.sDefault = transferAgentNameToPaneName(cMRServer.m_strDefAgent);

            aPanes = new Array();
            for (var i = 0; i < cMRServer.m_cAgents.length; i++) {
                var nCur = aPanes.length;
                aPanes[nCur] = new Object();
                aPanes[nCur].sPaneName = transferAgentNameToPaneName(cMRServer.m_cAgents[i].m_strID);
                aPanes[nCur].sPaneURL = cMRServer.m_cAgents[i].m_strURL;
            }
            oPanes.aPanes = aPanes;
            oMsg.oParam = oPanes;
            reply(oMsg);
            return false;
        }
        else {
            oMsg.oParam = null;
            reply(oMsg);
            return false;
        }
    }
    else if (nMsgId == WH_MSG_GETCMD) {
        oMsg.oParam = gnCmd;
        reply(oMsg);
        return false;
    }
    else if (nMsgId == WH_MSG_GETPANE) {
        if (gsBtns != "invalid" && oMsg.iParam.sName) {
            if (gsBtns.indexOf(oMsg.iParam.sName) != -1)
                oMsg.oParam.bEnable = true;
            else
                oMsg.oParam.bEnable = false;
        }
        else
            oMsg.oParam.bEnable = true;
        reply(oMsg);
        return false;
    }
    else if (nMsgId == WH_MSG_GETDEFPANE) {
        if (gsDefaultBtn != "invalid") {
            oMsg.oParam = gsDefaultBtn;
        }
        reply(oMsg);
        return false;
    }
    else if (nMsgId == WH_MSG_GETSTARTFRAME) {
        oMsg.oParam.oFrameName = this.name;
        if(!isChromeLocal())
            oMsg.oParam.oFrame = this;
        reply(oMsg);
        return false;
    }
    return true;
}

function onReceiveNotification(oMsg)
{
	var nMsgId = oMsg.msgId;
	if(nMsgId==WH_MSG_RELOADNS6)
	{
		if(gbNav4 && !gbNav6)
			gnReload++;
		return false;
	}
	return true;
}

function transferANToPN2(sAN)
{
	if (sAN =="toc")
		return "toc";
	else if	(sAN =="ndx")
		return "idx";
	else if	(sAN =="nls")
		return "fts";
	else if	(sAN =="gls")
		return "glo";
	else if	(sAN =="WebSearch")
		return "websearch";
	else if (sAN.indexOf("custom_")==0);
		return "custom" + sAN.substring(7);
	return sAN;
}

function transferAgentNameToPaneName(sAgentName)
{
	if (sAgentName =="toc")
		return "toc";
	else if	(sAgentName =="ndx")
		return "idx";
	else if	(sAgentName =="nls")
		return "fts";
	else if	(sAgentName =="gls")
		return "glo";
	return "";
}

function isAPane(sPaneName)
{
	if (sPaneName == "toc" || sPaneName == "idx" || sPaneName == "fts" || sPaneName == "glo")
		return true;
	else
		return false;
}