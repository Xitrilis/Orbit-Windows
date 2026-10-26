using System;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Orbit;

static class WalletTests
{
    static int count;
    static void Check(bool b, string label) { if (!b) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
    static void Reject(Action a, string label) { bool threw = false; try { a(); } catch { threw = true; } Check(threw, label); }
    const string Html = "<div class='e-payment-method' data-value='SBP' data-fee='1'></div><div class='e-payment-method' data-value='CARD' data-fee='6'></div><script>window.serviceFee = 0.02; window.steamRate = 80; window.steamRateKzt = 450; window.steamCurrency = 'USD'; window.min_amount = '100'; window.max_amount = '14000';</script>";
    // Formula signatures observed in the provider's public calculator; not executable code.
    const string Script = "Math.ceil(e+s) o=e*(window.serviceFee||0),i=e*window.payFee,s=o+i Math.ceil(o/window.steamRate*100)/100 Math.round(i*window.steamRateKzt) Math.round(i*window.steamRate*100)/100";
    static PriceFx Fx() => new PriceFx { date = DateTime.UtcNow.Date, rubPerUnit = { ["KZT"] = .2m } };
    sealed class Mock : HttpMessageHandler
    {
        readonly bool noProvider, noFx;
        public int calls;
        public Mock(bool noProvider=false, bool noFx=false) { this.noProvider=noProvider; this.noFx=noFx; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage m, CancellationToken ct)
        {
            calls++; if (m.Method != HttpMethod.Get || m.Content != null || m.Headers.Contains("Cookie")) throw new Exception("Unexpected payment/auth request");
            string body;
            if (m.RequestUri.AbsoluteUri == WalletService.ProviderUrl) { if(noProvider)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)); body=Html; }
            else if(m.RequestUri.AbsoluteUri == WalletService.ScriptUrl)body=Script;
            else if(m.RequestUri.AbsoluteUri == WalletService.CbrUrl) { if(noFx)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)); body="<ValCurs Date='"+DateTime.UtcNow.ToString("dd.MM.yyyy")+"'><Valute><CharCode>KZT</CharCode><Nominal>100</Nominal><Value>20</Value></Valute></ValCurs>"; }
            else throw new Exception("Unexpected host");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)});
        }
    }
    static async Task<int> Main(string[] args)
    {
        var rates=WalletService.ParseRates(Html, Script);var fx=Fx();
        Check(rates.serviceFee==.02m && rates.sbpFee==.01m && rates.cardFee==.06m,"both service fee and payment fee parsed");
        var sbp=WalletService.Calculate(rates,1000,"RU","sbp",fx);var card=WalletService.Calculate(rates,1000,"RU","card",fx);
        Check(sbp.payRub==1030 && card.payRub==1080 && sbp.receive==1000 && sbp.extraPercent==3,"3% SBP vs 8% card, full cost");
        Check(WalletService.Calculate(rates,100.01m,"RU","sbp",fx).payRub==104,"round payment upward to whole ruble");
        var kz=WalletService.Calculate(rates,1000,"KZ","sbp",fx);
        Check(kz.receive==5625 && kz.currency=="KZT" && kz.receiveRub==1125 && kz.extraPercent==-8.44m,"KZT conversion uses nominal-adjusted CBR reference; negative spread retained");
        Check(WalletService.Calculate(rates,1000,"KZ","card",null).extraPercent==null && WalletService.Calculate(rates,1000,"RU","sbp",null).extraPercent==3,"missing FX does not invent KZT cost or erase RUB calculation");
        Reject(()=>WalletService.Calculate(rates,99,"RU","sbp",fx),"provider minimum enforced");
        Reject(()=>WalletService.Calculate(rates,14001,"RU","sbp",fx),"provider maximum enforced");
        Reject(()=>WalletService.Calculate(rates,0,"RU","sbp",fx),"zero refused");
        Reject(()=>WalletService.Calculate(rates,100.001m,"RU","sbp",fx),"fractional kopecks refused");
        Reject(()=>WalletService.Calculate(rates,1000,"US","sbp",fx),"unsupported region refused");
        Reject(()=>WalletService.Calculate(rates,1000,"RU","crypto",fx),"unknown method refused");
        Reject(()=>WalletService.ParseRates(Html.Replace("data-fee='1'","data-fee='999'"),Script),"unreasonable fee refused");
        Reject(()=>WalletService.ParseRates(Html.Replace("window.serviceFee = 0.02;",""),Script),"missing fee is not zero");
        Reject(()=>WalletService.ParseRates(Html+"window.serviceFee = 0.1;",Script),"ambiguous fee refused");
        Reject(()=>WalletService.ParseRates(Html,"new formula"),"changed provider formula disables estimate");
        Reject(()=>WalletService.ParseRates(Html.Replace("'USD'","'EUR'"),Script),"changed provider currency refused");
        Check(!WalletService.Links.ContainsKey("https://gamemoney.ru.evil.test/") && WalletService.Links.Values.All(u=>new Uri(u).Scheme=="https" && !u.Contains("?")),"only fixed external destinations, no account data in URLs");
        using(var service=new WalletService(new Mock())){var r=await service.Load(1000,"KZ",CancellationToken.None);Check(r.quotes.Count==2 && r.fxDate!=null && r.expiresAt>r.checkedAt,"public data load and quote lifetime");}
        using(var service=new WalletService(new Mock(noProvider:true))){var r=await service.Load(1000,"RU",CancellationToken.None);Check(r.quotes.Count==0 && r.providerError!=null && r.fxDate!=null,"provider failure isolated, no dummy quote");}
        using(var service=new WalletService(new Mock(noFx:true))){var r=await service.Load(1000,"KZ",CancellationToken.None);Check(r.quotes.Count==2 && r.quotes[0].receiveRub==null && r.fxError!=null,"FX failure keeps native quote only");}
        Console.WriteLine(count+" checks passed");
        if(args.Contains("--live")) using(var service=new WalletService()) {
            var r=await service.Load(1000,"KZ",CancellationToken.None);
            Console.WriteLine(JsonConvert.SerializeObject(r,Formatting.Indented));
            if(r.quotes.Count!=2) return 2;
        }
        return 0;
    }
}
