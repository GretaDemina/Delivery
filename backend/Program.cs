using System.Text.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using DeliveryApi.Models;
using DeliveryApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Контроллеры; тип доставки передаётся строкой: "standard" / "express"
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

// Тарифы читаются из раздела "Tariff" файла appsettings.json
builder.Services.Configure<TariffSettings>(builder.Configuration.GetSection("Tariff"));

// Сервисы бизнес-логики и хранилища (хранятся в памяти, без базы данных)
builder.Services.AddSingleton<CityService>();
builder.Services.AddSingleton<DistanceCalculator>();
builder.Services.AddSingleton<DeliveryCalculator>();
builder.Services.AddSingleton<HistoryStorage>();
builder.Services.AddSingleton<OrderRepository>();

// Клиент внешнего API конвертации валют
var timeoutSeconds = builder.Configuration.GetValue<int>("CurrencyApi:TimeoutSeconds", 5);
builder.Services.AddHttpClient<CurrencyConverterClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
});

// Разрешаем запросы с фронтенда (Netlify), он находится на другом домене
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

// Swagger для тестирования и документации API
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "DeliveryApi — сервис расчёта доставки",
        Version = "v1",
        Description = "Расчёт стоимости и срока доставки, справочник городов, тарифы, история расчётов и заказы."
    });
    var xmlFile = $"{typeof(Program).Assembly.GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    options.IncludeXmlComments(xmlPath);
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors();

app.MapControllers();

// Корневой адрес перенаправляет на Swagger
app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();
