using Firebase.Database;
using Firebase.Database.Query;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace WhiteSpace
{
    public class FirebaseService : IDisposable
    {
        private readonly FirebaseClient _client;
        private readonly string _databaseUrl = "https://whitespace-af424-default-rtdb.europe-west1.firebasedatabase.app/";

        private const string SHAPES_PATH = "shapes";

        public FirebaseService()
        {
            _client = new FirebaseClient(_databaseUrl);
        }

        #region Shapes

        // Подписка на обновления фигур в реальном времени
        public IObservable<BoardShape> GetShapesObservable(string boardId)
        {
            return _client
                .Child(SHAPES_PATH)
                .Child(boardId)
                .AsObservable<BoardShape>()
                .Select(dbevent =>
                {
                    if (dbevent.Object != null)
                    {
                        // Преобразуем ключ из строки в int для Id
                        if (int.TryParse(dbevent.Key, out int id))
                        {
                            dbevent.Object.Id = id;  // Присваиваем Id как int
                        }
                        else
                        {
                            // Если не удалось преобразовать строку в int, присваиваем 0 или другой дефолтный ID
                            dbevent.Object.Id = 0;
                        }

                        // Преобразуем boardId в Guid
                        if (Guid.TryParse(boardId, out Guid guid))
                        {
                            dbevent.Object.BoardId = guid;  // Присваиваем BoardId как Guid
                        }
                        else
                        {
                            // Если boardId не является допустимым Guid, можно присвоить значение по умолчанию
                            dbevent.Object.BoardId = Guid.Empty;
                        }
                    }
                    return dbevent.Object;
                });
        }



        // Добавление или обновление фигуры
        public async Task PushShapeAsync(string boardId, BoardShape shape)
        {
            shape.BoardId = Guid.TryParse(boardId, out Guid guid) ? guid : Guid.Empty;

            if (shape.Id > 0)
            {
                // Обновление существующей фигуры
                await _client
                    .Child(SHAPES_PATH)
                    .Child(boardId)
                    .Child(shape.Id.ToString())
                    .PutAsync(shape);
            }
            else
            {
                // Получаем все фигуры для этой доски
                var shapesResponse = await _client
                    .Child(SHAPES_PATH)
                    .Child(boardId)
                    .OnceAsync<BoardShape>();

                // Находим максимальный ID
                int maxId = 0;
                foreach (var item in shapesResponse)
                {
                    if (item.Object.Id > maxId)
                    {
                        maxId = item.Object.Id;
                    }
                }

                // Новый ID = максимальный + 1
                shape.Id = maxId + 1;

                // Сохраняем новую фигуру
                await _client
                    .Child(SHAPES_PATH)
                    .Child(boardId)
                    .Child(shape.Id.ToString())
                    .PutAsync(shape);
            }
        }


        // Удаление фигуры
        public async Task DeleteShapeAsync(string boardId, string shapeId)
        {
            await _client
                .Child(SHAPES_PATH)
                .Child(boardId)
                .Child(shapeId)
                .DeleteAsync();  // Удаляем фигуру по ID
        }

        // Получение всех фигур доски
        public async Task<List<BoardShape>> GetAllShapesAsync(string boardId)
        {
            // Получаем все фигуры доски
            var shapes = await _client
                .Child(SHAPES_PATH)
                .Child(boardId)
                .OnceAsync<BoardShape>();

            return shapes.Select(s =>
            {
                // Преобразуем ключ Firebase (string) в int для Id
                if (int.TryParse(s.Key, out int id))
                {
                    s.Object.Id = id;
                }
                else
                {
                    s.Object.Id = 0; // Присваиваем значение по умолчанию, если не удалось преобразовать
                }

                // Преобразуем boardId из строки в Guid
                if (Guid.TryParse(boardId, out Guid boardGuid))
                {
                    s.Object.BoardId = boardGuid;
                }
                else
                {
                    s.Object.BoardId = Guid.Empty; // Присваиваем пустой Guid, если не удалось преобразовать
                }

                return s.Object;
            }).ToList();
        }


        #endregion

        public void Dispose()
        {
            // Очистка ресурсов при необходимости
        }
    }

}
