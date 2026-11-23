using DeliveryApi.Models;
using DeliveryApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace DeliveryApi.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly DeliveryCalculator _calculator;
    private readonly OrderRepository _orders;

    public OrdersController(DeliveryCalculator calculator, OrderRepository orders)
    {
        _calculator = calculator;
        _orders = orders;
    }

    /// <summary>Создать заказ и рассчитать его стоимость.</summary>
    /// <remarks>Сохраняет заказ в памяти сервера; после перезапуска данные могут исчезнуть.</remarks>
    /// <response code="201">Заказ создан.</response>
    /// <response code="400">Некорректные сведения о клиенте или посылке.</response>
    [HttpPost]
    [ProducesResponseType(typeof(Order), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Create([FromBody] CreateOrderRequest request)
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

        var order = _orders.Add(request.CustomerName, result);
        return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
    }

    /// <summary>Получить список созданных заказов.</summary>
    [HttpGet]
    public ActionResult<IEnumerable<Order>> GetAll()
    {
        return Ok(_orders.GetAll());
    }

    /// <summary>Получить заказ по идентификатору.</summary>
    /// <param name="id">Номер заказа.</param>
    /// <response code="200">Заказ найден.</response>
    /// <response code="404">Заказ не найден.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(Order), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetById(int id)
    {
        var order = _orders.GetById(id);
        return order is null ? NotFound(new { error = $"Заказ №{id} не найден" }) : Ok(order);
    }
}
