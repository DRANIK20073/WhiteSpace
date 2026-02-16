using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using Supabase;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace BoardRealtimeServer // Это пространство имен, которое вы хотите добавить
{
    public class BoardHub : Hub
    {
        private readonly Supabase.Client _supabaseClient;

        // Конструктор для внедрения Supabase клиента
        public BoardHub(Supabase.Client supabaseClient)
        {
            _supabaseClient = supabaseClient;
        }

        // Метод для обновления фигуры на доске
        public async Task SendShapeUpdate(BoardShape shape)
        {
            try
            {
                // Проверяем, что клиент Supabase инициализирован
                if (_supabaseClient == null)
                {
                    Console.WriteLine("Supabase client is not initialized.");
                    return;
                }

                // Создаем новый объект BoardShape для обновления или вставки в базу
                var updatedShape = new BoardShape
                {
                    Id = shape.Id,
                    Type = shape.Type,
                    X = shape.X,
                    Y = shape.Y,
                    Width = shape.Width,
                    Height = shape.Height,
                    Color = shape.Color,
                    Text = shape.Text
                };

                // Выполнение операции Upsert для обновления или вставки в базу данных
                var result = await _supabaseClient
                    .From<BoardShape>()
                    .Upsert(new[] { updatedShape });

                // Проверка результата
                if (result.ResponseMessage == null && result.Models?.Any() == true)
                {
                    // Отправляем обновление всем подключенным клиентам через SignalR
                    await Clients.All.SendAsync("ReceiveShapeUpdate", shape);
                    Console.WriteLine("Shape updated successfully.");
                }
                else
                {
                    Console.WriteLine($"Error upserting shape to Supabase: {result.ResponseMessage}");
                }
            }
            catch (Exception ex)
            {
                // Логируем исключение для отладки
                Console.WriteLine($"Error in SendShapeUpdate: {ex.Message}");
            }
        }
    }
}
