//	WebHelp 5.10.006
var gbNav=false;
var gbNav6=false;
var gbNav61=false;
var gbNav7=false;
var gbNav4=false;
var gbIE4=false;
var gbIE=false;
var gbIE5=false;
var gbIE55=false;
var gbOpera6=false;
var gbOpera7=false;
var gbKonqueror3=false;
var gbSafari3=false;

var gAgent=navigator.userAgent.toLowerCase();
var gbMac=(gAgent.indexOf("mac")!=-1);
var gbSunOS=(gAgent.indexOf("sunos")!=-1);
var gbUnixOS=(gAgent.indexOf("linux")!=-1) || (gAgent.indexOf("unix")!=-1);
var gbOpera=(gAgent.indexOf("opera")!=-1);
var gbKonqueror=(gAgent.indexOf("konqueror")!= -1);
var gbSafari=(gAgent.indexOf("safari")!= -1);
var gbWindows=((gAgent.indexOf('win')!= -1)||(gAgent.indexOf('16bit')!= -1));
var gbMozilla=((gAgent.indexOf('gecko')!=-1) && (gAgent.indexOf('netscape')==-1));
var gbAIR=(gAgent.indexOf('adobeair')!=-1);
var gbChrome = (gAgent.indexOf('chrome')!=-1);
var gbAIRSSL= false ;

var gVersion=navigator.appVersion.toLowerCase();

var gnVerMajor=parseInt(gVersion);
var gnVerMinor=parseFloat(gVersion);

if(!gbOpera&&!gbKonqueror&&!gbSafari) // opera can mimic IE or Netscape by settings.
{
	gbIE=(navigator.appName.indexOf("Microsoft")!=-1);
	gbNav=(gAgent.indexOf('mozilla')!=-1) && ((gAgent.indexOf('spoofer')==-1) && (gAgent.indexOf('compatible')==-1));
	if(gnVerMajor>=4)
	{
		if(navigator.appName=="Netscape")
		{
			gbNav4=true;
			if(gnVerMajor>=5)
				gbNav6=true;
		}
		gbIE4=(navigator.appName.indexOf("Microsoft")!=-1);
	}
	if(gbNav6)
	{
		var nPos=gAgent.indexOf("gecko");
		if(nPos!=-1)
		{
			var nPos2=gAgent.indexOf("/", nPos);
			if(nPos2!=-1)
			{
				var nVersion=parseFloat(gAgent.substring(nPos2+1));
				if(nVersion>=20010726)
				{
					gbNav61=true;
					if (nVersion>=20020823)
						gbNav7=true;
				}
			}
		}
	}else if(gbIE4)
	{
		var nPos=gAgent.indexOf("msie");
		if(nPos!=-1)
		{
			var nVersion=parseFloat(gAgent.substring(nPos+5));
			if(nVersion>=5)
			{
				gbIE5=true;
				if(nVersion>=5.5)
					gbIE55=true;
			}
		}
	}
}
else if (gbOpera)
{
	var nPos = gAgent.indexOf("opera");
	if(nPos!=-1)
	{
		var nVersion=parseFloat(gAgent.substring(nPos+6));
		if(nVersion>=6)
		{
			gbOpera6=true;
			if(nVersion>=7)
				gbOpera7=true;
		}
	}
}
else if (gbKonqueror)
{
	var nPos = gAgent.indexOf("konqueror");
	if(nPos!=-1)
	{
		var nVersion = parseFloat(gAgent.substring(nPos+10));
		if (nVersion >= 3)
		{
			gbKonqueror3=true;
		}
	}
}
if(gbSafari)
{
	var nPos = gAgent.indexOf("version/");
	if(nPos!=-1)
	{
		var nVersion = parseFloat(gAgent.substring(nPos+8,nPos+9));
		if (nVersion >= 3)
		{
			gbSafari3=true;
		}
	}
}
if(gbChrome)
{
	//for the time being use same tests as safari
	gbSafari = true ;
	gbSafari3=true;
}
var gbWhVer=true;
