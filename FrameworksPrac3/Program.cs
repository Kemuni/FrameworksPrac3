using System.Threading.RateLimiting;
using FrameworksPrac3.Config;
using FrameworksPrac3.Domain;
using FrameworksPrac3.Middlewares;
using FrameworksPrac3.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var switchMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["--mode"] = "App:Mode",
    ["--origins"] = "App:TrustedOrigins",
    ["--readPerMinute"] = "App:RateLimits:ReadPerMinute",
    ["--writePerMinute"] = "App:RateLimits:WritePerMinute"
};

var builder = WebApplication.CreateBuilder(args);

// Явный порядок источников настроек
builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile("FrameworksPrac3/appsettings.json", optional: false, reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "PR3_")
    .AddCommandLine(args, switchMappings);

// Чтение и ранняя проверка настроек
var options = new AppOptions();
builder.Configuration.GetSection("App").Bind(options);

var errors = AppOptionsValidator.Validate(options);
if (errors.Count > 0)
{
    var text = string.Join(Environment.NewLine, errors.Select(e => "- " + e));
    Console.Error.WriteLine("Запуск остановлен из за некорректных настроек");
    Console.Error.WriteLine(text);
    throw new InvalidOperationException("Некорректные настройки приложения");
}

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<IProductRepository, InMemoryProductRepository>();

// Ограничение запросов из браузера только от доверенных источников
builder.Services.AddCors(cors =>
{
    cors.AddPolicy("TrustedOrigins", policy =>
    {
        policy.WithOrigins(options.TrustedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Ограничение частоты запросов
builder.Services.AddRateLimiter(limiter =>
{
    limiter.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsync("Слишком много запросов", token);
    };

    limiter.AddPolicy("read", httpContext =>
    {
        var key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.RateLimits.ReadPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }
        );
    });

    limiter.AddPolicy("write", httpContext =>
    {
        var key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.RateLimits.WritePerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }
        );
    });
});

var app = builder.Build();

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseCors("TrustedOrigins");
app.UseRateLimiter();
app.UseMiddleware<ErrorHandlingMiddleware>();

app.MapGet("/api/items", (IProductRepository repo, AppOptions appOptions) =>
{
    if (appOptions.Mode == AppMode.Учебный) Console.WriteLine($"{DateTime.Now:u} Были запрошены все элементы репозитория");
    return Results.Ok(repo.GetAll());
})
.RequireRateLimiting("read");

app.MapGet("/api/items/by-id/{id:guid}", (Guid id, IProductRepository repo, AppOptions appOptions) =>
{
    var item = repo.GetById(id);
    if (appOptions.Mode == AppMode.Учебный) Console.WriteLine($"{DateTime.Now:u} Были запрошен элемент с Id {id}");
    if (item is null)
    {
        if (appOptions.Mode == AppMode.Учебный) Console.WriteLine($"{DateTime.Now:u} Элемент с Id {id} не был найден");
        throw new ArgumentException("Элемент не найден");
    }

    return Results.Ok(item);
})
.RequireRateLimiting("read");

app.MapPost("/api/items", (HttpContext ctx, CreateItemRequest request, IProductRepository repo, AppOptions appOptions) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        if (appOptions.Mode == AppMode.Учебный) Console.WriteLine($"{DateTime.Now:u} ОШИБКА Пустой Name ({request.Name}) в запросе создания.");
        throw new ArgumentException("Поле name не должно быть пустым");
    }

    if (request.Price < 0)
    {
        if (appOptions.Mode == AppMode.Учебный) Console.WriteLine($"{DateTime.Now:u} ОШИБКА Price({request.Price}) < 0 в запросе создания.");
        throw new ArgumentException("Поле price не может быть отрицательным");
    }

    var created = repo.Create(request.Name.Trim(), request.Price);
    var location = $"/api/items/by-id/{created.Id}";
    ctx.Response.Headers.Location = location;

    return Results.Created(location, created);
})
.RequireRateLimiting("write");

app.MapGet("/api/mode", (AppOptions o) => Results.Ok(new { mode = o.Mode.ToString() }))
.RequireRateLimiting("read");

app.Run();

public partial class Program { }
