using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class SignalRService
{
    private HubConnection _connection;

    public async Task InitAsync(string serverUrl)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl($"{serverUrl}/boardhub")
            .Build();

        _connection.On<BoardShape>("ReceiveShapeUpdate", (shape) =>
        {
            // Обрабатываем получение обновлений о фигуре
            Console.WriteLine($"Received shape update: {shape.Type} at ({shape.X}, {shape.Y})");
            // Здесь можно обновить доску в UI
        });

        await _connection.StartAsync();
    }

    public async Task SendShapeUpdate(BoardShape shape)
    {
        // Отправка обновлений на сервер
        await _connection.SendAsync("SendShapeUpdate", shape);
    }
}
