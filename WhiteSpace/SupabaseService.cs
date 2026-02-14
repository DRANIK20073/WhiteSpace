using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Supabase;
using Supabase.Interfaces;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Shapes;
using WhiteSpace.Pages;
using static Supabase.Realtime.PostgresChanges.PostgresChangesOptions;

public class SupabaseService
{

    private static Client _client;
    public static Client Client => _client;

    // Добавляем публичные статические свойства для URL и ключа
    public static string SupabaseUrl { get; private set; }
    public static string SupabaseKey { get; private set; }

    public static async Task InitAsync()
    {
        var url = "https://ceqnfiznaanuzojjgdcs.supabase.co";
        var key = "sb_publishable_GpGetyC36F_fZ2rLWEgSBg_UJ7ptd9G";

        // Сохраняем их в свойствах
        SupabaseUrl = url;
        SupabaseKey = key;

        _client = new Client(url, key);
        await _client.InitializeAsync();
    }

    //Регистрация
    public async Task<bool> SignUpAsync(string email, string password)
    {
        try
        {
            if (_client == null)
            {
                MessageBox.Show("Клиент Supabase не был инициализирован.");
                return false;
            }

            var response = await _client.Auth.SignUp(email, password);

            if (response.User != null)
            {
                var userId = response.User.Id;
                MessageBox.Show("Регистрация успешна 🎉");

                return true;
            }
            else
            {
                MessageBox.Show("Ошибка регистрации");
                return false;
            }
        }
        catch (Supabase.Gotrue.Exceptions.GotrueException ex)
        {
            if (ex.Message.Contains("user_already_exists"))
            {
                MessageBox.Show("Пользователь с таким email уже зарегистрирован.");
            }
            else if (ex.Message.Contains("validation_failed") && ex.Message.Contains("invalid format"))
            {
                MessageBox.Show("Неправильный формат email. Пожалуйста, проверьте введенный адрес.");
            }
            else if (ex.Message.Contains("password"))
            {
                MessageBox.Show("Пароль должен содержать минимум 6 символов.");
            }
            else
            {
                MessageBox.Show($"Ошибка при регистрации: {ex.Message}");
            }
            return false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Неизвестная ошибка: {ex.Message}");
            return false;
        }
    }

    //Обновить имя пользователя
    public async Task<bool> UpdateUsernameAsync(string newUsername)
    {
        try
        {
            var user = _client.Auth.CurrentUser;

            if (user == null)
            {
                MessageBox.Show("Пользователь не авторизован.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(newUsername) || newUsername.Length < 3)
            {
                MessageBox.Show("Имя пользователя должно содержать минимум 3 символа.");
                return false;
            }

            var userId = Guid.Parse(user.Id);

            var existingProfile = await _client.From<Profile>()
                .Where(p => p.Id == userId)
                .Single();

            if (existingProfile != null)
            {
                var updatedProfile = new Profile
                {
                    Id = existingProfile.Id,
                    Email = existingProfile.Email,
                    Username = newUsername,
                    CreatedAt = existingProfile.CreatedAt
                };

                var result = await _client.From<Profile>().Upsert(updatedProfile);

                if (result.Models?.Any() == true)
                {
                    MessageBox.Show($"Имя пользователя успешно обновлено на: {newUsername}");
                    return true;
                }
                else
                {
                    MessageBox.Show("Не удалось обновить имя пользователя.");
                    return false;
                }
            }
            else
            {
                // СОЗДАЕМ НОВЫЙ ПРОФИЛЬ
                var newProfile = new Profile
                {
                    Id = userId,
                    Email = user.Email ?? "",
                    Username = newUsername,
                    CreatedAt = DateTime.UtcNow
                };

                var result = await _client.From<Profile>().Insert(newProfile);

                if (result.Models?.Any() == true)
                {
                    MessageBox.Show($"Профиль создан с именем пользователя: {newUsername}");
                    return true;
                }
                else
                {
                    MessageBox.Show("Не удалось создать профиль.");
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при смене имени пользователя: {ex.Message}");
            return false;
        }
    }

    //Логин
    public async Task<bool> SignInAsync(string email, string password, bool rememberMe)
    {
        try
        {
            var session = await Client.Auth.SignIn(email, password);

            if (session == null)
            {
                MessageBox.Show("Не удалось войти. Проверьте введенные данные.");
                return false;
            }

            if (rememberMe)
            {
                SessionStorage.SaveSession(session);
            }

            MessageBox.Show("Вход выполнен успешно!");

            return true;
        }
        catch (Supabase.Gotrue.Exceptions.GotrueException ex)
        {
            // Перевод ошибок на русский
            if (ex.Message.Contains("missing email or phone"))
            {
                MessageBox.Show("Ошибка входа: Не указан email.");
            }
            else if (ex.Message.Contains("invalid_credentials"))
            {
                MessageBox.Show("Ошибка входа: Неверные учетные данные.");
            }
            else
            {
                MessageBox.Show($"Ошибка входа: {ex.Message}");
            }

            return false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Неизвестная ошибка: {ex.Message}");
            return false;
        }
    }

    //Получение токена сессии пользователя
    public static string GetSessionToken()
    {
        var session = _client.Auth.CurrentSession;
        if (session == null)
        {
            MessageBox.Show("Пользователь не авторизован.");
            return null;
        }
        return session.AccessToken;  // Токен сессии
    }

    //Получить текущего пользователя (отобразить имя)
    public async Task GetCurrentUserAsync()
    {
        try
        {
            var user = _client.Auth.CurrentUser;

            if (user != null)
            {

                var profile = await GetMyProfileAsync();

                if (profile != null && !string.IsNullOrEmpty(profile.Username))
                {
                    MessageBox.Show($"Имя пользователя: {profile.Username}");
                }
                else if (profile != null && string.IsNullOrEmpty(profile.Username))
                {
                    MessageBox.Show("Имя пользователя не установлено");
                }
                else
                {
                    MessageBox.Show("Профиль пользователя не найден в базе данных");
                }
            }
            else
            {
                MessageBox.Show("Пользователь не авторизован. Пожалуйста, выполните вход.");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при получении имени пользователя: {ex.Message}");
        }
    }

    // Метод для получения роли пользователя на доске
    public async Task<string> GetUserRoleForBoardAsync(Guid boardId)
    {
        try
        {
            var user = _client.Auth.CurrentUser;
            if (user == null) return null;

            var userId = Guid.Parse(user.Id);

            // Получаем роль пользователя для доски, используя метод Get()
            var memberships = await _client.From<BoardMember>()
                .Where(m => m.BoardId == boardId && m.UserId == userId)
                .Get();

            var membership = memberships.Models?.FirstOrDefault();  // Получаем первый элемент, если он есть
            return membership?.Role;  // Возвращаем роль пользователя (например, "viewer", "editor")
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при получении роли пользователя: {ex.Message}");
            return null;
        }
    }

    //Профиль текущего пользователя
    public async Task<Profile?> GetMyProfileAsync()
    {
        try
        {
            var user = _client.Auth.CurrentUser;
            if (user == null)
            {
                MessageBox.Show("Пользователь не авторизован");
                return null;
            }

            var userId = Guid.Parse(user.Id);

            var result = await _client
                .From<Profile>()
                .Where(p => p.Id == userId)
                .Single();

            return result;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка в GetMyProfileAsync: {ex.Message}");
            return null;
        }
    }

    //Создать доску
    public async Task<Board> CreateBoardAsync(string title)
    {
        try
        {
            var user = _client.Auth.CurrentUser;
            if (user == null)
            {
                MessageBox.Show("Пользователь не авторизован");
                return null;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                MessageBox.Show("Название доски не может быть пустым");
                return null;
            }

            var board = new Board
            {
                Title = title,
                OwnerId = Guid.Parse(user.Id),  // Устанавливаем владельца доски
                AccessCode = Guid.NewGuid().ToString("N")[..6].ToUpper(),
                CreatedAt = DateTime.UtcNow
            };

            var result = await _client.From<Board>().Insert(board);

            if (result.Models?.Any() == true)
            {
                // Добавляем владельца в таблицу board_members с ролью "owner"
                var boardId = result.Models.First().Id;
                var newBoardMember = new BoardMember
                {
                    BoardId = boardId,
                    UserId = Guid.Parse(user.Id),
                    Role = "owner",  // Роль владельца
                    JoinedAt = DateTime.UtcNow
                };

                // Вставляем владельца в таблицу board_members
                await _client.From<BoardMember>().Insert(newBoardMember);

                MessageBox.Show("Доска успешно создана 🎉");
                return result.Models.First(); // Возвращаем объект доски с ID
            }

            MessageBox.Show("Не удалось создать доску");
            return null;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка создания доски: {ex.Message}");
            return null;
        }
    }

    //Получить список досок
    public async Task<List<(Board Board, string Role)>> GetAllAccessibleBoardsWithRoleAsync()
    {
        var result = new List<(Board, string)>();
        try
        {
            var user = _client.Auth.CurrentUser;
            if (user == null) return result;

            var userId = Guid.Parse(user.Id);

            // 1. Свои доски (владелец)
            var ownedBoards = await _client.From<Board>()
                .Where(b => b.OwnerId == userId)
                .Get();
            if (ownedBoards.Models != null)
            {
                foreach (var board in ownedBoards.Models)
                    result.Add((board, "owner"));
            }

            // 2. Доски, где пользователь участник
            var memberships = await _client.From<BoardMember>()
                .Where(m => m.UserId == userId)
                .Get();

            if (memberships.Models != null)
            {
                foreach (var member in memberships.Models)
                {
                    var board = await _client.From<Board>()
                        .Where(b => b.Id == member.BoardId)
                        .Single();
                    if (board != null)
                    {
                        // Добавляем только тех, кто не является владельцем
                        if (board.OwnerId != userId) // Проверка: если это не доска владельца, добавляем
                        {
                            result.Add((board, member.Role));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка получения досок: {ex.Message}");
        }
        return result;
    }

    //Сохранить изменения на доске
    public async Task<bool> SaveShapeAsync(BoardShape shape)
    {
        try
        {
            // Сериализация списка точек в строку JSON
            string pointsJson = null;
            if (shape.DeserializedPoints != null && shape.DeserializedPoints.Count > 0)
            {
                pointsJson = JsonConvert.SerializeObject(shape.DeserializedPoints); // Преобразуем List<Point> в JSON строку
            }

            // Используем Upsert для добавления или обновления фигуры
            var result = await _client.From<BoardShape>().Upsert(new BoardShape
            {
                // Передаем все данные, включая Id
                Id = shape.Id,  // Это важно для обновления существующей фигуры
                BoardId = shape.BoardId,
                Type = shape.Type,
                X = shape.X,
                Y = shape.Y,
                Width = shape.Width,
                Height = shape.Height,
                Color = shape.Color,
                Text = shape.Text,
                Points = pointsJson
            });

            if (result.Models?.Any() == true)
            {
                MessageBox.Show("Фигура успешно сохранена.");
                return true;
            }
            else
            {
                MessageBox.Show("Не удалось сохранить фигуру.");
                return false;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при сохранении фигуры: {ex.Message}");
            return false;
        }
    }

    // Метод для загрузки фигур с базы данных
    public async Task<List<BoardShape>> LoadBoardShapesAsync(Guid boardId)
    {
        try
        {
            var result = await _client
                .From<BoardShape>()
                .Where(s => s.BoardId == boardId)
                .Get();

            var shapes = new List<BoardShape>();

            if (result.Models != null)
            {
                foreach (var model in result.Models)
                {
                    // Десериализация строки JSON в List<Point> и сохранение в коллекцию
                    if (!string.IsNullOrEmpty(model.Points))
                    {
                        model.DeserializedPoints = JsonConvert.DeserializeObject<List<Point>>(model.Points); // Десериализуем строку JSON в List<Point>
                    }

                    shapes.Add(model);
                }
            }

            return shapes;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при загрузке фигур: {ex.Message}");
            return new List<BoardShape>();
        }
    }

    // Метод для генерации уникального Id
    public async Task<int> GenerateUniqueIdAsync(Guid boardId)
    {
        int newId;
        bool idExists;

        do
        {
            // Генерируем новый уникальный Id
            newId = Guid.NewGuid().GetHashCode(); // Генерация случайного int

            // Проверяем, существует ли уже такой id
            var existingShape = await _client
                .From<BoardShape>()
                .Where(s => s.BoardId == boardId && s.Id == newId)
                .Get();

            idExists = existingShape.Models?.Count > 0; // Если фигура с таким id существует, генерируем новый
        }
        while (idExists);

        return newId;
    }

    // Присоединение к доске по коду доступа (роль по умолчанию "viewer")
    public async Task<Board> JoinBoardAsync(string accessCode)
    {
        try
        {
            var user = _client.Auth.CurrentUser;
            if (user == null)
            {
                MessageBox.Show("Пользователь не авторизован.");
                return null;
            }

            var boardResult = await _client.From<Board>()
                .Where(b => b.AccessCode == accessCode)
                .Single();

            if (boardResult == null)
            {
                MessageBox.Show("Доска с таким кодом не найдена.");
                return null;
            }

            var boardId = boardResult.Id;
            var userId = Guid.Parse(user.Id);

            // Владелец уже имеет доступ
            if (boardResult.OwnerId == userId)
            {
                MessageBox.Show("Вы владелец этой доски.");
                return boardResult;
            }

            // Проверяем, не участник ли уже (без исключения!)
            var existingMember = await _client.From<BoardMember>()
                .Where(m => m.BoardId == boardId && m.UserId == userId)
                .Single();

            if (existingMember != null)
            {
                MessageBox.Show("Вы уже присоединились к этой доске.");
                return boardResult;
            }

            // Добавляем с ролью "viewer"
            var newMember = new BoardMember
            {
                BoardId = boardId,
                UserId = userId,
                Role = "viewer",
                JoinedAt = DateTime.UtcNow
            };

            var insertResult = await _client.From<BoardMember>().Insert(newMember);

            if (insertResult.Models?.Count > 0)
            {
                MessageBox.Show($"✅ Вы успешно присоединились к доске \"{boardResult.Title}\" (режим просмотра).");
                return boardResult;
            }
            else
            {
                MessageBox.Show("❌ Не удалось добавить запись в таблицу участников. Проверьте политики RLS.");
                return null;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"❌ Ошибка присоединения к доске: {ex.Message}");
            return null;
        }
    }

    // Проверка прав редактирования доски для текущего пользователя
    public async Task<bool> CanEditBoardAsync(Guid boardId)
    {
        try
        {
            var user = _client.Auth.CurrentUser;
            if (user == null) return false;

            var userId = Guid.Parse(user.Id);

            // Владелец может редактировать
            var board = await _client.From<Board>()
                .Where(b => b.Id == boardId)
                .Single();
            if (board != null && board.OwnerId == userId)
                return true;

            // Проверяем роль в board_members
            var member = await _client.From<BoardMember>()
                .Where(m => m.BoardId == boardId && m.UserId == userId)
                .Single();

            return member != null && member.Role == "editor";
        }
        catch
        {
            return false;
        }
    }

    // (Опционально) Изменение роли участника (только для владельца)
    public async Task<bool> UpdateBoardMemberRoleAsync(Guid boardId, Guid userId, string newRole)
    {
        try
        {
            // Получаем текущего члена доски
            var member = await _client.From<BoardMember>()
                .Where(m => m.BoardId == boardId && m.UserId == userId)
                .Single();

            if (member == null)
            {
                MessageBox.Show("Пользователь не найден.");
                return false;
            }

            // Обновляем роль
            member.Role = newRole;
            var result = await _client.From<BoardMember>().Update(member);

            return result.Models?.Any() == true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при изменении роли: {ex.Message}");
            return false;
        }
    }


    public async Task<List<BoardMember>> GetBoardMembersAsync(Guid boardId)
    {
        try
        {
            var result = await _client
                .From<BoardMember>()
                .Where(m => m.BoardId == boardId)
                .Get();

            return result.Models ?? new List<BoardMember>();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при получении списка участников: {ex.Message}");
            return new List<BoardMember>();
        }
    }

}
