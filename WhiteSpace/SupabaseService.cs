using Supabase;
using System.Windows;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System.Text.RegularExpressions;
using System.Windows.Navigation;
using WhiteSpace.Pages;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Newtonsoft.Json;

public class SupabaseService
{
    private static Client _client;

    public static Client Client => _client;

    public static async Task InitAsync()
    {
        var url = "https://ceqnfiznaanuzojjgdcs.supabase.co";  //URL Supabase
        var key = "sb_publishable_GpGetyC36F_fZ2rLWEgSBg_UJ7ptd9G";  //ключ

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
                OwnerId = Guid.Parse(user.Id),
                AccessCode = Guid.NewGuid().ToString("N")[..6].ToUpper(),
                CreatedAt = DateTime.UtcNow
            };

            var result = await _client.From<Board>().Insert(board);

            if (result.Models?.Any() == true)
            {
                MessageBox.Show("Доска успешно создана 🎉");
                return result.Models.First(); // Возвращаем объект доски, который содержит ID
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
    public async Task<List<Board>> GetBoardsAsync()
    {
        try
        {
            var user = _client.Auth.CurrentUser;
            if (user == null)
            {
                MessageBox.Show("Пользователь не авторизован.");
                return new List<Board>();
            }

            var userId = Guid.Parse(user.Id);

            var result = await _client.From<Board>()
                .Where(b => b.OwnerId == userId)
                .Get();

            return result.Models?.ToList() ?? new List<Board>();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при получении досок: {ex.Message}");
            return new List<Board>();
        }
    }

    // Сохранение фигуры на доске
    public async Task<bool> SaveShapeAsync(Guid boardId, BoardShape shape)
    {
        try
        {
            var userId = _client.Auth.CurrentUser?.Id;

            if (string.IsNullOrEmpty(userId))
            {
                throw new Exception("Пользователь не авторизован");
            }

            // Получаем доску, принадлежащую пользователю
            var result = await _client
                .From<Board>()
                .Where(b => b.OwnerId == Guid.Parse(userId) && b.Id == boardId)
                .Single();  // Используем Single вместо Get для одной записи

            if (result == null)
            {
                throw new Exception("Доска не найдена");
            }

            // Устанавливаем BoardId для фигуры
            shape.BoardId = boardId;

            // Для всех типов фигур используем одинаковый метод сохранения
            var resultInsert = await _client
                .From<BoardShape>()
                .Insert(shape);

            return resultInsert.Model != null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при сохранении фигуры: {ex.Message}");
            return false;
        }
    }


    // Метод для загрузки фигур с базы данных
    public async Task<List<BoardShape>> LoadBoardShapesAsync(Guid boardId)
    {
        try
        {
            // Загружаем все фигуры, связанные с доской
            var result = await _client
                .From<BoardShape>()  // Указываем тип модели BoardShape
                .Where(s => s.BoardId == boardId)  // Фильтруем по ID доски
                .Get();

            // Возвращаем список фигур
            return result.Models?.ToList() ?? new List<BoardShape>();
        }
        catch (Exception ex)
        {
            // Обрабатываем ошибки при загрузке данных
            Console.WriteLine($"Ошибка при загрузке фигур: {ex.Message}");
            return new List<BoardShape>();
        }
    }

}

