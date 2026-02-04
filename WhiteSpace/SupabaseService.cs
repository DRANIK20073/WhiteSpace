using Supabase;
using System.Windows;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System.Text.RegularExpressions;

public class SupabaseService
{
    private static Client _client;

    public static Client Client => _client;

    public static async Task InitAsync()
    {
        var url = "https://ceqnfiznaanuzojjgdcs.supabase.co";  // Ваш URL Supabase
        var key = "sb_publishable_GpGetyC36F_fZ2rLWEgSBg_UJ7ptd9G";  // Ваш ключ

        _client = new Client(url, key);  // Здесь инициализируем Supabase клиент
        await _client.InitializeAsync();
    }

    public async Task SignUpAsync(string email, string password)
    {
        try
        {
            // Регистрация пользователя в Supabase Authentication
            var response = await _client.Auth.SignUp(email, password);

            if (response.User != null)
            {
                // Получаем ID пользователя из ответа
                var userId = response.User.Id;

                MessageBox.Show("Регистрация успешна 🎉");
            }
            else
            {
                MessageBox.Show("Ошибка регистрации");
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
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Неизвестная ошибка: {ex.Message}");
        }
    }


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

            // Валидация нового имени пользователя
            if (string.IsNullOrWhiteSpace(newUsername) || newUsername.Length < 3)
            {
                MessageBox.Show("Имя пользователя должно содержать минимум 3 символа.");
                return false;
            }

            var userId = Guid.Parse(user.Id);

            // Пробуем получить существующий профиль
            var existingProfile = await _client.From<Profile>()
                .Where(p => p.Id == userId)
                .Single();

            if (existingProfile != null)
            {
                // ОБНОВЛЯЕМ СУЩЕСТВУЮЩИЙ ПРОФИЛЬ
                // Создаем копию с обновленными данными
                var updatedProfile = new Profile
                {
                    Id = existingProfile.Id,
                    Email = existingProfile.Email,
                    Username = newUsername,
                    CreatedAt = existingProfile.CreatedAt
                };

                // Используем Upsert для обновления
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


    public async Task SignInAsync(string email, string password)
    {
        try
        {
            var session = await SupabaseService.Client.Auth.SignIn(email, password);
            if (session != null)
            {
                MessageBox.Show("Вход выполнен ✅");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка входа: {ex.Message}");
        }
    }

    public async Task GetCurrentUserAsync()
    {
        try
        {
            var user = _client.Auth.CurrentUser;

            if (user != null)
            {
                // Получаем профиль пользователя
                var profile = await GetMyProfileAsync();

                if (profile != null && !string.IsNullOrEmpty(profile.Username))
                {
                    // Выводим только username
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
}

