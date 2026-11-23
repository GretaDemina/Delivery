using DeliveryApi.Models;
using DeliveryApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace DeliveryApi.Controllers;

/// <summary>Интеграция сервиса доставки и внешнего источника курсов валют.</summary>
[ApiController]
[Route("api/integration")]
public class IntegrationController : ControllerBase
{
    private readonly DeliveryCalculator _calculator;
    private readonly CurrencyConverterClient _currencies;
    private readonly HistoryStorage _history;
    public IntegrationController(DeliveryCalculator calculator, CurrencyConverterClient currencies, HistoryStorage history)
    { _calculator = calculator; _currencies = currencies; _history = history; }

    /// <summary>Получить курс к рублю из ЦБ (USD, EUR, CNY).</summary>
    [HttpGet("rate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Rate([FromQuery] string currency = "USD", CancellationToken ct = default)
    {
        if (currency.ToUpperInvariant() is not ("USD" or "EUR" or "CNY"))
            return BadRequest(new { error = "Выберите USD, EUR или CNY." });
        try { return Ok(await _currencies.GetRateAsync(currency, ct)); }
        catch (Exception) { return StatusCode(503, new { error = "Курсы валют временно недоступны." }); }
    }

    /// <summary>Рассчитать доставку и запросить курс ЦБ; ошибка ЦБ не блокирует рублёвый расчёт.</summary>
    [HttpPost("quote")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Quote([FromBody] DeliveryRequest request, CancellationToken ct)
    {
        DeliveryResult delivery;
        try { delivery = _calculator.Calculate(request); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        var currency = (request.Currency ?? "RUB").Trim().ToUpperInvariant();
        if (currency is not ("RUB" or "USD" or "EUR" or "CNY"))
            return BadRequest(new { error = "Поддерживаемые валюты: RUB, USD, EUR, CNY." });
        object conversion;
        if (currency == "RUB")
        {
            conversion = new { status = "not_requested", amount = (decimal?)delivery.Cost, currency = "RUB", rate = (decimal?)1m, rateDate = "", source = "Локальный расчёт", sourceUrl = "", warning = (string?)null };
        }
        else
        {
            try
            {
                var info = await _currencies.GetRateAsync(currency, ct);
                var converted = Math.Round(delivery.Cost / info.RubPerUnit, 2, MidpointRounding.AwayFromZero);
                delivery.ConvertedCost = converted;
                delivery.ConvertedCurrency = currency;
                conversion = new { status = "ok", amount = (decimal?)converted, currency, rate = (decimal?)info.RubPerUnit, rateDate = info.RateDate, source = info.Source, sourceUrl = info.SourceUrl, warning = (string?)null };
            }
            catch (Exception)
            {
                delivery.Warning = "Сервис курсов валют недоступен. Стоимость показана только в рублях.";
                conversion = new { status = "unavailable", amount = (decimal?)null, currency, rate = (decimal?)null, rateDate = "", source = "Банк России", sourceUrl = "", warning = delivery.Warning };
            }
        }
        _history.Add(delivery);
        return Ok(new { delivery, conversion, integration = new { stage1 = "DeliveryApi: расчёт расстояния и тарифа", stage2 = currency == "RUB" ? "Конвертация не требуется" : "HTTP GET: API курса ЦБ РФ", stage3 = "Объединение результатов в JSON" } });
    }
}
