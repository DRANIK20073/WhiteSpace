using Microsoft.AspNetCore.SignalR;
using BoardRealtimeServer;
using Supabase;
using Newtonsoft.Json;

var builder = WebApplication.CreateBuilder(args);

// Регистрация Supabase клиентa
builder.Services.AddSingleton<Supabase.Client>(sp =>
{
    var url = "https://ceqnfiznaanuzojjgdcs.supabase.co";
    var key = "sb_publishable_GpGetyC36F_fZ2rLWEgSBg_UJ7ptd9G";
    return new Supabase.Client(url, key); // Инициализация клиента Supabase
});

// Настройка сериализации
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });


// Добавляем SignalR
builder.Services.AddSignalR();

// Добавляем сервисы для API
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Настройка для Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Настройка HTTPS
app.UseHttpsRedirection();

// Авторизация
app.UseAuthorization();

// Настройка маршрутов для SignalR
app.MapHub<BoardHub>("/boardhub");

// Настройка маршрутов для контроллеров
app.MapControllers();

app.Run();
