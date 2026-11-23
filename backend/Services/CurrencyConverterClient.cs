using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace DeliveryApi.Services;

/// <summary>Курсы ЦБ РФ: основной источник — официальный XML, резерв — JSON-зеркало.</summary>
public class CurrencyConverterClient
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<CurrencyConverterClient> _logger;
    private const string OfficialUrl = "https://www.cbr.ru/scripts/XML_daily.asp";
    private const string MirrorUrl = "https://www.cbr-xml-daily.ru/daily_json.js";

    public CurrencyConverterClient(HttpClient http, IConfiguration config, ILogger<CurrencyConverterClient> logger)
    { _http = http; _config = config; _logger = logger; }

    public record RateInfo(string Currency, decimal RubPerUnit, string RateDate, string Source, string SourceUrl);

    public async Task<decimal> ConvertAsync(decimal amountRub, string currency, CancellationToken ct = default)
    {
        var rate = await GetRateAsync(currency, ct);
        return Math.Round(amountRub / rate.RubPerUnit, 2, MidpointRounding.AwayFromZero);
    }

    public async Task<RateInfo> GetRateAsync(string currency, CancellationToken ct = default)
    {
        currency = currency.Trim().ToUpperInvariant();
        if (currency is not ("USD" or "EUR" or "CNY"))
            throw new ArgumentException("Поддерживаются только USD, EUR и CNY.");
        try
        {
            var url = _config["CurrencyApi:OfficialUrl"] ?? OfficialUrl;
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            // XmlReader reads encoding from XML header (CBR can use windows-1251).
            using var reader = System.Xml.XmlReader.Create(stream, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit });
            var xml = XDocument.Load(reader);
            var element = xml.Descendants("Valute").FirstOrDefault(x => (string?)x.Element("CharCode") == currency)
                ?? throw new InvalidOperationException("Валюта отсутствует в ответе ЦБ.");
            var nominal = decimal.Parse((string?)element.Element("Nominal") ?? "1", CultureInfo.InvariantCulture);
            var value = decimal.Parse(((string?)element.Element("Value") ?? "0").Replace(',', '.'), CultureInfo.InvariantCulture);
            if (nominal <= 0 || value <= 0) throw new InvalidOperationException("Некорректный курс валюты.");
            return new RateInfo(currency, value / nominal, (string?)xml.Root?.Attribute("Date") ?? "", "Банк России (официальный XML)", url);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Официальный API ЦБ недоступен; пробуем JSON-зеркало");
            var url = _config["CurrencyApi:MirrorUrl"] ?? MirrorUrl;
            using var response = await _http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var item = json.RootElement.GetProperty("Valute").GetProperty(currency);
            var rate = item.GetProperty("Value").GetDecimal() / item.GetProperty("Nominal").GetDecimal();
            if (rate <= 0) throw new InvalidOperationException("Некорректный курс валюты.");
            var date = json.RootElement.TryGetProperty("Date", out var d) ? d.GetString() ?? "" : "";
            return new RateInfo(currency, rate, date, "Резервное JSON-зеркало данных ЦБ", url);
        }
    }
}
