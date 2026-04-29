using Firebase.Database;
using Firebase.Database.Query;
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
        private const string MEMBERS_PATH = "members";
        private const string CURSORS_PATH = "cursors";
        private const string CHAT_MESSAGES_PATH = "chat_messages";

        public FirebaseService()
        {
            _client = new FirebaseClient(_databaseUrl);
        }

        #region Shapes

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
                        if (int.TryParse(dbevent.Key, out int id))
                        {
                            dbevent.Object.Id = id;
                        }
                        else
                        {
                            dbevent.Object.Id = 0;
                        }

                        if (Guid.TryParse(boardId, out Guid guid))
                        {
                            dbevent.Object.BoardId = guid;
                        }
                    }
                    return dbevent.Object;
                });
        }

        public async Task PushShapeAsync(string boardId, BoardShape shape)
        {
            if (Guid.TryParse(boardId, out Guid guid))
            {
                shape.BoardId = guid;
            }

            if (shape.Id > 0)
            {
                await _client
                    .Child(SHAPES_PATH)
                    .Child(boardId)
                    .Child(shape.Id.ToString())
                    .PutAsync(shape);
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

        public async Task DeleteShapeAsync(string boardId, string shapeId)
        {
            await _client
                .Child(SHAPES_PATH)
                .Child(boardId)
                .Child(shapeId)
                .DeleteAsync();
        }

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

                        return snapshot?.Values.ToList() ?? new List<FirebaseChatMessage>();
                    }
                    catch
                    {
                        return new List<FirebaseChatMessage>();
                    }
                }));
        }

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

        #endregion

        #region Cursors

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

        public void Dispose()
        {
        }
    }

    // Класс для Firebase (с UserId как string)
    public class FirebaseBoardMember
    {
        public string UserId { get; set; }
        public string Role { get; set; }
        public DateTime JoinedAt { get; set; }
        public bool IsOnline { get; set; }
        public DateTime LastSeenUtc { get; set; }
    }

    public class FirebaseCursorState
    {
        public string UserId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public bool IsVisible { get; set; } = true;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public class FirebaseChatMessage
    {
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
    }
}