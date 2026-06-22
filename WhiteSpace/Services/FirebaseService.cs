using Firebase.Database;
using Firebase.Database.Query;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using WhiteSpace.Models;

namespace WhiteSpace
{
    /// <summary>Realtime-слой доски через Firebase: фигуры, чат, курсоры, участники.</summary>
    public class FirebaseService : IDisposable
    {
        private readonly FirebaseClient _client;
        private readonly string _databaseUrl = "https://whitespace-af424-default-rtdb.europe-west1.firebasedatabase.app/";

        private const string SHAPES_PATH = "shapes";
        private const string MEMBERS_PATH = "members";
        private const string CURSORS_PATH = "cursors";
        private const string CHAT_MESSAGES_PATH = "chat_messages";
        private const string PRESENTATION_PATH = "presentation";
        private const string BOARD_VERSIONS_PATH = "board_versions";

        public FirebaseService()
        {
            _client = new FirebaseClient(_databaseUrl);
        }

        #region Shapes

        /// <summary>Поток изменений отдельных фигур (добавление, правка, удаление).</summary>
        public IObservable<BoardShapeChange> GetShapesObservable(string boardId)
        {
            return _client
                .Child(SHAPES_PATH)
                .Child(boardId)
                .AsObservable<BoardShape>()
                .Select(dbevent =>
                {
                    if (!int.TryParse(dbevent.Key, out var id))
                    {
                        id = 0;
                    }

                    if (dbevent.Object == null)
                    {
                        return new BoardShapeChange { ShapeId = id };
                    }

                    if (int.TryParse(dbevent.Key, out int shapeId))
                    {
                        dbevent.Object.Id = shapeId;
                    }
                    else
                    {
                        dbevent.Object.Id = 0;
                    }

                    if (Guid.TryParse(boardId, out Guid guid))
                    {
                        dbevent.Object.BoardId = guid;
                    }

                    return new BoardShapeChange { ShapeId = dbevent.Object.Id, Shape = dbevent.Object };
                });
        }

        /// <summary>Полный снимок фигур доски при каждом изменении в ветке shapes.</summary>
        public IObservable<FirebaseShapesSnapshot> GetBoardShapesObservable(string boardId)
        {
            return _client
                .Child(SHAPES_PATH)
                .Child(boardId)
                .AsObservable<object>()
                .SelectMany(_ => Observable.FromAsync(async () =>
                {
                    try
                    {
                        var snapshot = await _client
                            .Child(SHAPES_PATH)
                            .Child(boardId)
                            .OnceAsync<BoardShape>();

                        var shapes = new List<BoardShape>();
                        if (snapshot == null || snapshot.Count == 0)
                        {
                            return FirebaseShapesSnapshot.Success(shapes);
                        }

                        foreach (var item in snapshot)
                        {
                            var shape = item.Object;
                            if (shape == null || !int.TryParse(item.Key, out var id))
                            {
                                continue;
                            }

                            shape.Id = id;
                            if (Guid.TryParse(boardId, out var boardGuid))
                            {
                                shape.BoardId = boardGuid;
                            }

                            shapes.Add(shape);
                        }

                        return FirebaseShapesSnapshot.Success(shapes);
                    }
                    catch
                    {
                        return FirebaseShapesSnapshot.Failed();
                    }
                }));
        }

        /// <summary>Создаёт или обновляет фигуру; для Id=0 выдаёт следующий свободный id.</summary>
        public async Task PushShapeAsync(string boardId, BoardShape shape)
        {
            if (Guid.TryParse(boardId, out Guid guid))
            {
                shape.BoardId = guid;
            }

            // Явный payload — так в RTDB всегда уходит Text с видом фигуры (поле sk в JSON).
            var payload = new
            {
                shape.BoardId,
                shape.Type,
                shape.X,
                shape.Y,
                shape.Width,
                shape.Height,
                shape.Color,
                shape.Text,
                shape.Points
            };

            if (shape.Id > 0)
            {
                await _client
                    .Child(SHAPES_PATH)
                    .Child(boardId)
                    .Child(shape.Id.ToString())
                    .PutAsync(payload);
            }
            else
            {
                var shapesResponse = await _client
                    .Child(SHAPES_PATH)
                    .Child(boardId)
                    .OnceAsync<BoardShape>();

                int maxId = 0;
                foreach (var item in shapesResponse)
                {
                    if (item.Object.Id > maxId)
                    {
                        maxId = item.Object.Id;
                    }
                }

                shape.Id = maxId + 1;

                await _client
                    .Child(SHAPES_PATH)
                    .Child(boardId)
                    .Child(shape.Id.ToString())
                    .PutAsync(shape);
            }
        }

        /// <summary>Заменяет весь набор фигур доски без предварительной очистки.</summary>
        public async Task ReplaceBoardShapesAsync(string boardId, IEnumerable<BoardShape> shapes)
        {
            var payload = new Dictionary<string, BoardShape>();

            foreach (var shape in shapes ?? Enumerable.Empty<BoardShape>())
            {
                if (shape == null || shape.Id <= 0)
                {
                    continue;
                }

                if (Guid.TryParse(boardId, out Guid boardGuid))
                {
                    shape.BoardId = boardGuid;
                }

                payload[shape.Id.ToString()] = shape;
            }

            await _client
                .Child(SHAPES_PATH)
                .Child(boardId)
                .PutAsync(payload);
        }

        /// <summary>Полностью заменяет фигуры доски (удаляет лишние у всех клиентов).</summary>
        public async Task ClearAndReplaceBoardShapesAsync(string boardId, IEnumerable<BoardShape> shapes)
        {
            await _client
                .Child(SHAPES_PATH)
                .Child(boardId)
                .DeleteAsync();

            await ReplaceBoardShapesAsync(boardId, shapes);
        }

        /// <summary>Удаляет одну фигуру по id.</summary>
        public async Task DeleteShapeAsync(string boardId, string shapeId)
        {
            await _client
                .Child(SHAPES_PATH)
                .Child(boardId)
                .Child(shapeId)
                .DeleteAsync();
        }

        /// <summary>Разовая загрузка всех фигур доски.</summary>
        public async Task<List<BoardShape>> GetAllShapesAsync(string boardId)
        {
            var shapes = await _client
                .Child(SHAPES_PATH)
                .Child(boardId)
                .OnceAsync<BoardShape>();

            return shapes.Select(s =>
            {
                if (int.TryParse(s.Key, out int id))
                {
                    s.Object.Id = id;
                }

                if (Guid.TryParse(boardId, out Guid boardGuid))
                {
                    s.Object.BoardId = boardGuid;
                }

                return s.Object;
            }).ToList();
        }

        #endregion

        #region Members

        /// <summary>Список участников доски в realtime (онлайн-статус и роли).</summary>
        public IObservable<List<FirebaseBoardMember>> GetBoardMembersObservable(string boardId)
        {
            return _client
                .Child(MEMBERS_PATH)
                .Child(boardId)
                .AsObservable<object>()
                .SelectMany(_ => Observable.FromAsync(async () =>
                {
                    try
                    {
                        var snapshot = await _client
                            .Child(MEMBERS_PATH)
                            .Child(boardId)
                            .OnceSingleAsync<Dictionary<string, FirebaseBoardMember>>();

                        return snapshot?.Values.ToList() ?? new List<FirebaseBoardMember>();
                    }
                    catch
                    {
                        return new List<FirebaseBoardMember>();
                    }
                }));
        }

        /// <summary>Полная синхронизация участников с сохранением IsOnline из текущего снимка.</summary>
        public async Task PushBoardMembersAsync(string boardId, List<FirebaseBoardMember> members)
        {
            try
            {
                var currentSnapshot = await _client
                    .Child(MEMBERS_PATH)
                    .Child(boardId)
                    .OnceSingleAsync<Dictionary<string, FirebaseBoardMember>>()
                    ?? new Dictionary<string, FirebaseBoardMember>();

                var membersDict = new Dictionary<string, object>();

                foreach (var member in members)
                {
                    var hasCurrent = currentSnapshot.TryGetValue(member.UserId, out var currentMember);
                    membersDict[member.UserId] = new
                    {
                        member.UserId,
                        member.Role,
                        member.JoinedAt,
                        IsOnline = hasCurrent && currentMember?.IsOnline == true,
                        LastSeenUtc = hasCurrent && currentMember != null ? currentMember.LastSeenUtc : DateTime.MinValue
                    };
                }

                await _client
                    .Child(MEMBERS_PATH)
                    .Child(boardId)
                    .PutAsync(membersDict);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка отправки участников в Firebase: {ex.Message}");
            }
        }

        /// <summary>Обновляет или добавляет одного участника.</summary>
        public async Task PushBoardMemberAsync(string boardId, FirebaseBoardMember member)
        {
            try
            {
                await _client
                    .Child(MEMBERS_PATH)
                    .Child(boardId)
                    .Child(member.UserId)
                    .PutAsync(new
                    {
                        member.UserId,
                        member.Role,
                        member.JoinedAt,
                        member.IsOnline,
                        member.LastSeenUtc
                    });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка отправки участника в Firebase: {ex.Message}");
            }
        }

        /// <summary>Убирает участника из realtime-списка доски.</summary>
        public async Task DeleteBoardMemberAsync(string boardId, string userId)
        {
            try
            {
                await _client
                    .Child(MEMBERS_PATH)
                    .Child(boardId)
                    .Child(userId)
                    .DeleteAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка удаления участника из Firebase: {ex.Message}");
            }
        }

        /// <summary>Разовый снимок участников без подписки.</summary>
        public async Task<List<FirebaseBoardMember>> GetBoardMembersSnapshotAsync(string boardId)
        {
            try
            {
                var snapshot = await _client
                    .Child(MEMBERS_PATH)
                    .Child(boardId)
                    .OnceSingleAsync<Dictionary<string, FirebaseBoardMember>>();

                return snapshot?.Values.ToList() ?? new List<FirebaseBoardMember>();
            }
            catch
            {
                return new List<FirebaseBoardMember>();
            }
        }

        #endregion

        #region Chat

        /// <summary>Нормализует словарь сообщений: подставляет id из ключа Firebase.</summary>
        private static List<FirebaseChatMessage> NormalizeChatMessages(Dictionary<string, FirebaseChatMessage>? snapshot)
        {
            if (snapshot == null || snapshot.Count == 0)
            {
                return new List<FirebaseChatMessage>();
            }

            var list = new List<FirebaseChatMessage>();
            foreach (var kv in snapshot)
            {
                var message = kv.Value;
                if (message == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(message.Id))
                {
                    message.Id = kv.Key;
                }

                list.Add(message);
            }

            return list;
        }

        /// <summary>Поток сообщений чата доски.</summary>
        public IObservable<List<FirebaseChatMessage>> GetBoardChatMessagesObservable(string boardId)
        {
            return _client
                .Child(CHAT_MESSAGES_PATH)
                .Child(boardId)
                .AsObservable<object>()
                .SelectMany(_ => Observable.FromAsync(async () =>
                {
                    try
                    {
                        var snapshot = await _client
                            .Child(CHAT_MESSAGES_PATH)
                            .Child(boardId)
                            .OnceSingleAsync<Dictionary<string, FirebaseChatMessage>>();

                        return NormalizeChatMessages(snapshot);
                    }
                    catch
                    {
                        return new List<FirebaseChatMessage>();
                    }
                }));
        }

        /// <summary>Отправляет новое сообщение в чат.</summary>
        public async Task PushChatMessageAsync(string boardId, FirebaseChatMessage message)
        {
            try
            {
                await _client
                    .Child(CHAT_MESSAGES_PATH)
                    .Child(boardId)
                    .Child(message.Id)
                    .PutAsync(message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка отправки сообщения в Firebase: {ex.Message}");
            }
        }

        /// <summary>Редактирует существующее сообщение по id.</summary>
        public async Task UpdateChatMessageAsync(string boardId, FirebaseChatMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.Id))
            {
                return;
            }

            try
            {
                await _client
                    .Child(CHAT_MESSAGES_PATH)
                    .Child(boardId)
                    .Child(message.Id)
                    .PutAsync(message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка обновления сообщения в Firebase: {ex.Message}");
            }
        }

        /// <summary>Удаляет сообщение из чата.</summary>
        public async Task DeleteChatMessageAsync(string boardId, string messageId)
        {
            if (string.IsNullOrWhiteSpace(messageId))
            {
                return;
            }

            try
            {
                await _client
                    .Child(CHAT_MESSAGES_PATH)
                    .Child(boardId)
                    .Child(messageId)
                    .DeleteAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка удаления сообщения из Firebase: {ex.Message}");
            }
        }

        #endregion

        #region Cursors

        /// <summary>Позиции курсоров всех участников на доске.</summary>
        public IObservable<List<FirebaseCursorState>> GetBoardCursorsObservable(string boardId)
        {
            return _client
                .Child(CURSORS_PATH)
                .Child(boardId)
                .AsObservable<object>()
                .SelectMany(_ => Observable.FromAsync(async () =>
                {
                    try
                    {
                        var snapshot = await _client
                            .Child(CURSORS_PATH)
                            .Child(boardId)
                            .OnceSingleAsync<Dictionary<string, FirebaseCursorState>>();

                        return snapshot?.Values.ToList() ?? new List<FirebaseCursorState>();
                    }
                    catch
                    {
                        return new List<FirebaseCursorState>();
                    }
                }));
        }

        /// <summary>Обновляет положение курсора текущего пользователя.</summary>
        public async Task UpsertCursorAsync(string boardId, FirebaseCursorState cursorState)
        {
            try
            {
                await _client
                    .Child(CURSORS_PATH)
                    .Child(boardId)
                    .Child(cursorState.UserId)
                    .PutAsync(new
                    {
                        cursorState.UserId,
                        cursorState.DisplayName,
                        cursorState.X,
                        cursorState.Y,
                        cursorState.IsVisible,
                        cursorState.UpdatedAtUtc
                    });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка отправки курсора в Firebase: {ex.Message}");
            }
        }

        /// <summary>Убирает курсор пользователя (уход с доски).</summary>
        public async Task DeleteCursorAsync(string boardId, string userId)
        {
            try
            {
                await _client
                    .Child(CURSORS_PATH)
                    .Child(boardId)
                    .Child(userId)
                    .DeleteAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка удаления курсора из Firebase: {ex.Message}");
            }
        }

        #endregion

        #region Presentation mode

        /// <summary>Включён ли режим презентации на доске.</summary>
        public IObservable<bool> GetBoardPresentationActiveObservable(string boardId)
        {
            return _client
                .Child(PRESENTATION_PATH)
                .Child(boardId)
                .AsObservable<object>()
                .SelectMany(_ => Observable.FromAsync(async () =>
                {
                    try
                    {
                        var snap = await _client
                            .Child(PRESENTATION_PATH)
                            .Child(boardId)
                            .OnceSingleAsync<FirebasePresentationState>();

                        return snap?.Active == true;
                    }
                    catch
                    {
                        return false;
                    }
                }));
        }

        /// <summary>Включает или выключает режим презентации для всех клиентов.</summary>
        public async Task SetBoardPresentationActiveAsync(string boardId, bool active)
        {
            await _client
                .Child(PRESENTATION_PATH)
                .Child(boardId)
                .PutAsync(new FirebasePresentationState
                {
                    Active = active,
                    UpdatedAtUtc = DateTime.UtcNow
                });
        }

        #endregion

        #region Board versions

        /// <summary>Сохраняет именованный снимок версии доски.</summary>
        public async Task PushBoardVersionSnapshotAsync(string boardId, string versionKey, BoardVersionSnapshot snapshot)
        {
            await _client
                .Child(BOARD_VERSIONS_PATH)
                .Child(boardId)
                .Child(versionKey)
                .PutAsync(snapshot);
        }

        /// <summary>Список сохранённых версий, от новых к старым.</summary>
        public async Task<List<(string Key, BoardVersionSnapshot Snapshot)>> GetBoardVersionSnapshotsAsync(string boardId)
        {
            try
            {
                var snap = await _client
                    .Child(BOARD_VERSIONS_PATH)
                    .Child(boardId)
                    .OnceAsync<BoardVersionSnapshot>();

                var list = new List<(string, BoardVersionSnapshot)>();
                if (snap == null)
                {
                    return list;
                }

                foreach (var item in snap)
                {
                    if (item.Object != null && !string.IsNullOrWhiteSpace(item.Key))
                    {
                        list.Add((item.Key, item.Object));
                    }
                }

                return list
                    .OrderByDescending(x => x.Item2?.SavedAtUtc ?? DateTime.MinValue)
                    .ToList();
            }
            catch
            {
                return new List<(string Key, BoardVersionSnapshot Snapshot)>();
            }
        }

        /// <summary>Удаляет сохранённую версию по ключу.</summary>
        public async Task DeleteBoardVersionAsync(string boardId, string versionKey)
        {
            try
            {
                await _client
                    .Child(BOARD_VERSIONS_PATH)
                    .Child(boardId)
                    .Child(versionKey)
                    .DeleteAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка удаления версии: {ex.Message}");
            }
        }

        #endregion

        public void Dispose()
        {
        }
    }

    /// <summary>Состояние режима презентации в Firebase.</summary>
    public sealed class FirebasePresentationState
    {
        public bool Active { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }

    /// <summary>Участник доски в Firebase (UserId — строка для совместимости с RTDB).</summary>
    public class FirebaseBoardMember
    {
        public string UserId { get; set; }
        public string Role { get; set; }
        public DateTime JoinedAt { get; set; }
        public bool IsOnline { get; set; }
        public DateTime LastSeenUtc { get; set; }
    }

    /// <summary>Результат загрузки полного снимка фигур (успех или ошибка сети).</summary>
    public sealed class FirebaseShapesSnapshot
    {
        public bool IsSuccess { get; init; }
        public List<BoardShape> Shapes { get; init; } = new();

        /// <summary>Успешная загрузка со списком фигур.</summary>
        public static FirebaseShapesSnapshot Success(List<BoardShape> shapes) =>
            new()
            {
                IsSuccess = true,
                Shapes = shapes ?? new List<BoardShape>()
            };

        /// <summary>Ошибка загрузки — Shapes пустой, IsSuccess=false.</summary>
        public static FirebaseShapesSnapshot Failed() =>
            new()
            {
                IsSuccess = false,
                Shapes = new List<BoardShape>()
            };
    }

    /// <summary>Позиция и видимость курсора участника на доске.</summary>
    public class FirebaseCursorState
    {
        public string UserId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public bool IsVisible { get; set; } = true;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Сообщение чата доски в Firebase.</summary>
    public class FirebaseChatMessage
    {
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Время последнего редактирования (UTC), если сообщение меняли.</summary>
        public DateTime? EditedAtUtc { get; set; }
    }
}