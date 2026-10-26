using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Parser.Html;

namespace Orbit
{
    public sealed class WalletQuote
    {
        public string provider = "gamemoney", method, currency, basis = "estimate";
        public decimal payRub, receive, serviceFee, paymentFee;
        public decimal? receiveRub, receiveKzt, extraRub, extraPercent;
    }
    public sealed class WalletRates
    {
        public decimal serviceFee, sbpFee, cardFee, rubPerUsd, kztPerUsd, min, max;
    }
    public sealed class WalletResult
    {
        public string region, fxDate, fxError, providerError;
        public decimal principal;
        public DateTime checkedAt, expiresAt;
        public List<WalletQuote> quotes = new List<WalletQuote>();
        public Dictionary<string, decimal> rubPerUnit;
    }
    // Read-only calculator. Never creates an order, submits account data, or charges a card.
    public sealed class WalletService : IDisposable
    {
        public const string ProviderUrl = "https://gamemoney.ru/popolnit-stim";
        public const string ScriptUrl = "https://gamemoney.ru/js/app.js";
        public const string CbrUrl = "https://www.cbr.ru/scripts/XML_daily.asp";
        public static readonly Dictionary<string, string> Links = new Dictionary<string, string> {
            { "gamemoney", ProviderUrl }, { "mts", "https://payment.mts.ru/cyber/steam" },
            { "steam", "https://store.steampowered.com/steamaccount/addfunds/" },
            { "steamAccount", "https://store.steampowered.com/account/" },
            { "gmReviews", "https://www.tbank.ru/reviews/company/gamemoney/243717/" },
            { "mtsReviews", "https://www.banki.ru/services/responses/bank/response/12081024/" },
            { "gmTerms", "https://gamemoney.ru/public-offer" },
            { "cbr", "https://www.cbr.ru/currency_base/daily/" }
        };
        readonly HttpClient http;
        public WalletService(HttpMessageHandler handler = null)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
            http.Timeout = TimeSpan.FromSeconds(25); http.MaxResponseContentBufferSize = 5 * 1024 * 1024;
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Orbit/0.4 (read-only wallet calculator)");
        }
        public static string Region(string value)
        {
            if (value != "RU" && value != "KZ") throw new ArgumentException("Выбери регион Steam: Россия или Казахстан.");
            return value;
        }
        public static decimal Amount(decimal amount)
        {
            if (amount < 1 || amount > 100000 || decimal.Round(amount, 2) != amount) throw new ArgumentException("Сумма должна быть от 1 до 100 000 ₽, не больше двух знаков после запятой.");
            return amount;
        }
        static decimal Variable(string html, string name)
        {
            var matches = Regex.Matches(html, @"window\." + Regex.Escape(name) + @"\s*=\s*['"" ]?([0-9]+(?:\.[0-9]+)?)['"" ]?\s*;", RegexOptions.CultureInvariant);
            if (matches.Count != 1) throw new InvalidOperationException("Параметры сервиса изменились. Проверь расчёт на его сайте.");
            return decimal.Parse(matches[0].Groups[1].Value, CultureInfo.InvariantCulture);
        }
        public static WalletRates ParseRates(string html, string script)
        {
            // Guard the observed public calculator formula. Changed scripts fail closed.
            var s = script ?? "";
            if (!Regex.IsMatch(s, @"Math\.ceil\(\s*e\s*\+\s*s\s*\)") ||
                !Regex.IsMatch(s, @"o\s*=\s*e\s*\*\s*\(window\.serviceFee\|\|0\)\s*,\s*i\s*=\s*e\s*\*\s*window\.payFee\s*,\s*s\s*=\s*o\s*\+\s*i") ||
                !Regex.IsMatch(s, @"Math\.ceil\(\s*o\s*/\s*window\.steamRate\s*\*\s*100\s*\)\s*/\s*100") ||
                !Regex.IsMatch(s, @"Math\.round\(\s*i\s*\*\s*window\.steamRateKzt\s*\)") ||
                !Regex.IsMatch(s, @"Math\.round\(\s*i\s*\*\s*window\.steamRate\s*\*\s*100\s*\)\s*/\s*100"))
                throw new InvalidOperationException("Калькулятор сервиса обновился. Открой сайт для точной суммы.");
            if (!Regex.IsMatch(html, @"window\.steamCurrency\s*=\s*['""]USD['""]\s*;")) throw new InvalidOperationException("Изменилась валюта расчёта сервиса.");
            var doc = new HtmlParser().Parse(html);
            Func<string, decimal> fee = method => {
                var nodes = doc.QuerySelectorAll(".e-payment-method[data-value='" + method + "'][data-fee]");
                decimal n;
                if (nodes.Length != 1 || !decimal.TryParse(nodes[0].GetAttribute("data-fee"), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out n) || n < 0 || n > 50)
                    throw new InvalidOperationException("Не удалось прочитать комиссию выбранного способа оплаты.");
                return n / 100;
            };
            var r = new WalletRates { serviceFee = Variable(html, "serviceFee"), sbpFee = fee("SBP"), cardFee = fee("CARD"), rubPerUsd = Variable(html, "steamRate"), kztPerUsd = Variable(html, "steamRateKzt"), min = Variable(html, "min_amount"), max = Variable(html, "max_amount") };
            if (r.serviceFee < 0 || r.serviceFee > .5m || r.rubPerUsd < 1 || r.rubPerUsd > 10000 || r.kztPerUsd < 1 || r.kztPerUsd > 100000 || r.min < 1 || r.max < r.min || r.max > 100000)
                throw new InvalidOperationException("Сервис вернул неожиданные параметры. Проверь сумму на сайте.");
            return r;
        }
        public static WalletQuote Calculate(WalletRates r, decimal amount, string region, string method, PriceFx fx)
        {
            Region(region); Amount(amount);
            if (method != "sbp" && method != "card") throw new ArgumentException("Неизвестный способ оплаты.");
            if (amount < r.min || amount > r.max) throw new ArgumentException("Для этого сервиса введи от " + r.min + " до " + r.max + " ₽.");
            var paymentFee = method == "sbp" ? r.sbpFee : r.cardFee;
            var usd = decimal.Ceiling(amount / r.rubPerUsd * 100) / 100;
            var q = new WalletQuote { method = method, currency = region == "KZ" ? "KZT" : "RUB", serviceFee = r.serviceFee * 100, paymentFee = paymentFee * 100,
                payRub = decimal.Ceiling(amount * (1 + r.serviceFee + paymentFee)),
                receive = decimal.Round(usd * (region == "KZ" ? r.kztPerUsd : r.rubPerUsd), region == "KZ" ? 0 : 2, MidpointRounding.AwayFromZero) };
            q.receiveRub = q.currency == "RUB" ? q.receive : fx?.Convert(q.receive, "KZT", "RUB");
            q.receiveKzt = q.currency == "KZT" ? q.receive : fx?.Convert(q.receive, "RUB", "KZT");
            if (q.receiveRub > 0) { q.extraRub = decimal.Round(q.payRub - q.receiveRub.Value, 2); q.extraPercent = decimal.Round((q.payRub / q.receiveRub.Value - 1) * 100, 2); }
            return q;
        }
        async Task<string> Get(string url, CancellationToken ct)
        {
            if (url != ProviderUrl && url != ScriptUrl && url != CbrUrl) throw new InvalidOperationException("Источник не разрешён.");
            using (var response = await http.GetAsync(url, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                return Encoding.UTF8.GetString(bytes);
            }
        }
        public async Task<WalletResult> Load(decimal amount, string region, CancellationToken ct)
        {
            Amount(amount); Region(region);
            var result = new WalletResult { principal = amount, region = region };
            PriceFx fx = null; WalletRates rates = null;
            var ratesTask = Task.Run(async () => {
                try { var html = Get(ProviderUrl, ct); var js = Get(ScriptUrl, ct); await Task.WhenAll(html, js).ConfigureAwait(false); rates = ParseRates(html.Result, js.Result); }
                catch (Exception e) { result.providerError = e is InvalidOperationException ? e.Message : "Сервис не вернул расчёт. Открой его сайт или повтори позже."; }
            }, ct);
            var fxTask = Task.Run(async () => {
                try { fx = PriceFx.Parse(await Get(CbrUrl, ct).ConfigureAwait(false), DateTime.UtcNow); result.fxDate = fx.date.ToString("yyyy-MM-dd"); result.rubPerUnit = fx.rubPerUnit; }
                catch { result.fxError = "Курс ЦБ недоступен. Пересчёт ₽ / ₸ и сравнение разных валют отключены."; }
            }, ct);
            await Task.WhenAll(ratesTask, fxTask).ConfigureAwait(false); ct.ThrowIfCancellationRequested();
            if (rates != null)
            {
                try { foreach (var m in new[] { "sbp", "card" }) result.quotes.Add(Calculate(rates, amount, region, m, fx)); }
                catch (ArgumentException e) { result.providerError = e.Message; }
            }
            result.checkedAt = DateTime.UtcNow; result.expiresAt = result.checkedAt.AddMinutes(5);
            return result;
        }
        public void Dispose() { http.Dispose(); }
    }
}
