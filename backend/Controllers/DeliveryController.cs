using DeliveryApi.Models;
using DeliveryApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DeliveryApi.Controllers;

[ApiController]
[Route("api/delivery")]
public class DeliveryController : ControllerBase
{
    private readonly DeliveryCalculator _calculator;
    private readonly CityService _cities;
    private readonly DistanceCalculator _distance;
    private readonly HistoryStorage _history;
    private readonly CurrencyConverterClient _converter;
    private readonly TariffSettings _tariff;

    public DeliveryController(
        DeliveryCalculator calculator,
        CityService cities,
        DistanceCalculator distance,
        HistoryStorage history,
        CurrencyConverterClient converter,
        IOptions<TariffSettings> tariff)
    {
        _calculator = calculator;
        _cities = cities;
        _distance = distance;
        _history = history;
        _converter = converter;
        _tariff = tariff.Value;
    }

    /// <summary>Рассчитать стоимость и срок доставки.</summary>
    /// <remarks>Принимает города, вес, габариты, тип доставки и страховку; по желанию выполняет пересчёт валюты. При недоступности курса возвращает рублёвый расчёт с предупреждением.</remarks>
    /// <response code="200">Расчёт успешно выполнен.</response>
    /// <response code="400">Некорректные входные данные или неизвестный город.</response>
    [HttpPost("calculate")]
    [ProducesResponseType(typeof(DeliveryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Calculate([FromBody] DeliveryRequest request, CancellationToken ct)
    {
        DeliveryResult result;
        try
        {
            result = _calculator.Calculate(request);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        await ApplyCurrencyAsync(result, request.Currency, ct);

        _history.Add(result);
        return Ok(result);
    }

    /// <summary>Получить расстояние между двумя городами.</summary>
    /// <param name="fromCity">Город отправления (параметр URL from).</param>
    /// <param name="toCity">Город назначения (параметр URL to).</param>
    /// <response code="200">Расстояние найдено.</response>
    /// <response code="404">Один или оба города отсутствуют в справочнике.</response>
    [HttpGet("distance")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetDistance([FromQuery(Name = "from")] string fromCity, [FromQuery(Name = "to")] string toCity)
    {
        var a = _cities.Find(fromCity);
        var b = _cities.Find(toCity);

        if (a is null || b is null)
        {
            return NotFound(new { error = "Один из городов не найден в справочнике" });
        }

        double straight = _distance.Haversine(a, b);
        return Ok(new
        {
            fromCity = a.Name,
            toCity = b.Name,
            straightKm = Math.Round(straight, 1),
            distanceKm = _distance.ToRoadDistance(straight)
        });
    }

    /// <summary>Получить список доступных городов с координатами.</summary>
    [HttpGet("cities")]
    public ActionResult<IEnumerable<City>> GetCities()
    {
        return Ok(_cities.GetAll());
    }

    /// <summary>Получить действующие тарифы расчёта доставки.</summary>
    [HttpGet("tariffs")]
    public ActionResult<TariffSettings> GetTariffs()
    {
        return Ok(_tariff);
    }

    /// <summary>Получить историю успешно выполненных расчётов (в памяти приложения).</summary>
    [HttpGet("history")]
    public ActionResult<IEnumerable<DeliveryResult>> GetHistory()
    {
        return Ok(_history.GetAll());
    }

    /// <summary>Пересчитывает стоимость в выбранную валюту. При ошибке добавляет предупреждение.</summary>
    private async Task ApplyCurrencyAsync(DeliveryResult result, string? currency, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Equals("RUB", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            result.ConvertedCost = await _converter.ConvertAsync(result.Cost, currency, ct);
            result.ConvertedCurrency = currency.Trim().ToUpperInvariant();
        }
        catch (Exception)
        {
            result.Warning = "Сервис конвертации валют недоступен. Стоимость показана только в рублях.";
        }
    }
}
