# Практическая работа №6 — DeliveryFlow

Интеграционная веб-платформа: **Netlify (HTML/CSS/JavaScript)** → **DeliveryApi на Amvera (ASP.NET Core 8)** → **курсы валют Банка России (HTTP/XML)**. Вместо API одногруппника используется сервис курсов ЦБ РФ по условию данного варианта.

## Адреса

- Backend Swagger: https://delivery-api-gusanova.msk1.amvera.tech/swagger/index.html
- GitHub: https://github.com/GretaDemina/DeliveryApi
- Frontend Netlify: **указать после публикации** (адрес пока не получен)
- Официальная документация ЦБ РФ: https://www.cbr.ru/development/SXML

## Что делает система

1. Пользователь выбирает маршрут, вес, габариты, тариф, страхование и валюту.
2. Браузер получает справочник городов GET `/api/delivery/cities`.
3. Браузер отправляет POST `/api/integration/quote` в DeliveryApi.
4. ASP.NET Core рассчитывает расстояние, расчётный вес, стоимость и сроки.
5. Backend делает **серверный HTTP GET** к `https://www.cbr.ru/scripts/XML_daily.asp`, читает XML с курсами и пересчитывает рубли в USD/EUR/CNY. Если официальный сервис не отвечает, используется резервное JSON-зеркало `https://www.cbr-xml-daily.ru/daily_json.js`. Источник возвращается в поле `conversion.source`.
6. Backend возвращает единый JSON с объектами `delivery`, `conversion`, `integration`. Интерфейс показывает обе суммы и путь обмена данными.
7. При недоступности обоих источников API возвращает основную стоимость в рублях, `conversion.status = unavailable` и предупреждение вместо вымышленного курса.

## Новые маршруты

### POST /api/integration/quote

Запрос:
```json
{"fromCity":"Москва","toCity":"Нижний Новгород","weight":10,"length":40,"width":30,"height":20,"type":"express","insurance":false,"currency":"USD"}
```

Успешный ответ — HTTP `200`, схематичный фрагмент (курс и результат конвертации **не фиксированы**):
```json
{
  "delivery": {"cost": 737,"days":1,"distanceKm":457,"currency":"RUB","convertedCost":null},
  "conversion": {"status":"ok","amount":null,"currency":"USD","rate":null,"rateDate":"...","source":"Банк России (официальный XML)","sourceUrl":"https://www.cbr.ru/scripts/XML_daily.asp","warning":null},
  "integration":{"stage1":"DeliveryApi: расчёт расстояния и тарифа","stage2":"HTTP GET: API курса ЦБ РФ","stage3":"Объединение результатов в JSON"}
}
```
`null` здесь — только **схематичные места**, при реальной успешной конвертации `amount`, `rate` и `convertedCost` будут числами. Если курс недоступен, `status = "unavailable"`, `amount = null`, `warning` содержит сообщение; рублёвая стоимость остаётся доступной.

Ошибки: `400` при неверном маршруте/весе/валюте; при недоступности курсов — `200` с предупреждением, так как доставка рассчитана.

### GET /api/integration/rate?currency=USD

Возвращает курс для одной единицы валюты в рублях, дату и название источника. `200` при успехе, `400` для неправильной валюты, `503` если оба источника недоступны.

Все ранее реализованные маршруты (`/api/delivery/*`, `/api/orders*`) сохранены.

## Развертывание

### 1. Обновить GitHub

Скопируйте **содержимое** папки `backend` в корень существующего репозитория `GretaDemina/DeliveryApi`, включая новые `Controllers/IntegrationController.cs`, `Services/CurrencyConverterClient.cs` и `appsettings.json`. **Не удаляйте прежние контроллеры и модели.** Для простого обновления через интерфейс GitHub редактируйте нужные файлы и создайте новый файл в `Controllers/`. Дополнительно загрузите папку `frontend` и этот README. Не загружайте `bin`, `obj`, токены и секреты. Проект уже содержит `Dockerfile` и `amvera.yml`.

### 2. Обновить Amvera

В приложении `delivery-api` проверьте источник кода и сделайте пересборку из **нового commit GitHub**. В конфигурации Docker путь `Dockerfile`, порт `8080`; после сборки проверьте Swagger `https://delivery-api-gusanova.msk1.amvera.tech/swagger/index.html` и наличие `/api/integration/quote` и `/api/integration/rate`. Если Amvera загружает код напрямую, загрузите обновлённые файлы backend и запустите повторную сборку.

### 3. Развернуть Netlify

На https://app.netlify.com/drop перетащите **папку `frontend`**, внутри которой напрямую находится `index.html` (не весь проект). Netlify выдаст URL вида `https://...netlify.app`. Внесите реальную ссылку в отчёт. Интерфейс уже настроен на публичный адрес Amvera, поэтому править адрес не требуется, пока он не меняется.

### 4. Проверить

- Откройте Netlify URL, дождитесь загрузки городов.
- Рассчитайте Москва → Нижний Новгород, вес 10, тип express, валюта RUB: ожидается **737 ₽, 1 день**.
- Выберите USD: при доступности ЦБ — сумма по курсу и источник; при сбое — предупреждение и цена RUB.
- Введите отрицательный вес: форма не должна отправлять запрос, API отдельно должен отвечать `400`.
- Проверьте неизвестный город напрямую через Swagger/Postman (`400`).
- Временно отключите сеть в браузере и нажмите «Рассчитать»: должно появиться сообщение о невозможности соединения.
- Нажмите «Обновить» в истории: должен появиться предыдущий расчёт.

**Важно:** сервисы ранее тестировались в практической №4, но **новые интеграционные маршруты пока не подтверждены удалёнными тестами**: их необходимо проверить после новой публикации. История и заказы сохраняются только в оперативной памяти backend и теряются при перезапуске.

## Структура

```
backend/ — ASP.NET Core, контроллеры, сервисы, Dockerfile и Amvera
frontend/ — статический интерфейс для Netlify (index.html, styles.css, app.js)
api_tests/ — коллекция Postman для интеграции
README.md — инструкция
```
