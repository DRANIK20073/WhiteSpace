using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Supabase;
using Supabase.Gotrue;
using Supabase.Gotrue.Exceptions;
using Supabase.Interfaces;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Shapes;
using WhiteSpace.Pages;
using WhiteSpace.Services;
using static Supabase.Gotrue.Constants;
using static Supabase.Realtime.PostgresChanges.PostgresChangesOptions;

public class SupabaseService
{
    private static Supabase.Client _client; // для работы с основной клиентской логикой
    private static Supabase.Gotrue.Client _authClient; // для работы с аутентификацией
    private static readonly object _localServerLock = new();
    private static HttpListener? _localServer;
    private static TaskCompletionSource<Dictionary<string, object>?>? _googleAuthCompletionSource;
    private const string LocalServerBaseUrl = "http://127.0.0.1:54322";
    private const string GoogleCallbackPageUrl = $"{LocalServerBaseUrl}/oauth-callback.html";
    private const string PasswordResetPageUrl = $"{LocalServerBaseUrl}/password-reset.html";

    public static Supabase.Client Client => _client;

    // Добавляем публичные статические свойства для URL и ключа
    public static string SupabaseUrl { get; private set; }
    public static string SupabaseKey { get; private set; }

    public static async Task InitAsync()
    {
        var url = "https://ceqnfiznaanuzojjgdcs.supabase.co";
        var key = "sb_publishable_GpGetyC36F_fZ2rLWEgSBg_UJ7ptd9G";

        SupabaseUrl = url;
        SupabaseKey = key;

        // Правильная инициализация клиента
        var options = new SupabaseOptions
        {
            AutoConnectRealtime = true,
            AutoRefreshToken = true
        };

        _client = new Supabase.Client(url, key, options);
        _authClient = (Supabase.Gotrue.Client)_client.Auth;
        await _client.InitializeAsync();
        EnsureLocalServerStarted();
    }

    //Регистрация
    public async Task<bool> SignUpAsync(string email, string password)
    {
        try
        {
            if (_client == null)
            {
                AppDialogService.ShowError("Клиент Supabase не был инициализирован.", "Supabase");
                return false;
            }

            var response = await _client.Auth.SignUp(email, password);

            if (response.User != null)
            {
                var userId = response.User.Id;
                AppDialogService.ShowSuccess("Регистрация успешна.", "Регистрация");
                return true;
            }
            else
            {
                AppDialogService.ShowError("Ошибка регистрации.", "Регистрация");
                return false;
            }
        }
        catch (Supabase.Gotrue.Exceptions.GotrueException ex)
        {
            if (ex.Message.Contains("user_already_exists"))
            {
                AppDialogService.ShowWarning("Пользователь с таким email уже зарегистрирован.", "Регистрация");
            }
            else if (ex.Message.Contains("validation_failed") && ex.Message.Contains("invalid format"))
            {
                AppDialogService.ShowWarning("Неправильный формат email. Пожалуйста, проверьте введенный адрес.", "Регистрация");
            }
            else if (ex.Message.Contains("password"))
            {
                AppDialogService.ShowWarning("Пароль должен содержать минимум 6 символов.", "Регистрация");
            }
            else
            {
                AppDialogService.ShowError($"Ошибка при регистрации: {ex.Message}", "Регистрация");
            }
            return false;
        }
        catch (Exception ex)
        {
            AppDialogService.ShowError($"Неизвестная ошибка: {ex.Message}", "Регистрация");
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
                AppDialogService.ShowWarning("Пользователь не авторизован.", "Профиль");
                return false;
            }

            if (string.IsNullOrWhiteSpace(newUsername) || newUsername.Length < 3)
            {
                AppDialogService.ShowWarning("Имя пользователя должно содержать минимум 3 символа.", "Профиль");
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
                    AppDialogService.ShowSuccess($"Имя пользователя успешно обновлено на: {newUsername}", "Профиль");
                    return true;
                }
                else
                {
                    AppDialogService.ShowError("Не удалось обновить имя пользователя.", "Профиль");
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
                    AppDialogService.ShowSuccess($"Профиль создан с именем пользователя: {newUsername}", "Профиль");
                    return true;
                }
                else
                {
                    AppDialogService.ShowError("Не удалось создать профиль.", "Профиль");
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            AppDialogService.ShowError($"Ошибка при смене имени пользователя: {ex.Message}", "Профиль");
            return false;
        }
    }

    //Логин
    public async Task<bool> SignInAsync(string email, string password, bool rememberMe)
    {
        try
        {
            var session = await _client.Auth.SignIn(email, password);

            if (session == null)
            {
                AppDialogService.ShowError("Не удалось войти. Проверьте введенные данные.", "Вход");
                return false;
            }

            if (rememberMe)
            {
                SessionStorage.SaveSession(session);
            }

            AppDialogService.ShowSuccess("Вход выполнен успешно!", "Вход");

            return true;
        }
        catch (Supabase.Gotrue.Exceptions.GotrueException ex)
        {
            if (ex.Message.Contains("missing email or phone"))
            {
                AppDialogService.ShowWarning("Ошибка входа: Не указан email.", "Вход");
            }
            else if (ex.Message.Contains("invalid_credentials"))
            {
                AppDialogService.ShowWarning("Ошибка входа: Неверные учетные данные.", "Вход");
            }
            else
            {
                AppDialogService.ShowError($"Ошибка входа: {ex.Message}", "Вход");
            }

            return false;
        }
        catch (Exception ex)
        {
            AppDialogService.ShowError($"Неизвестная ошибка: {ex.Message}", "Вход");
            return false;
        }
    }

    public async Task<bool> SendPasswordResetEmailAsync(string email)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                AppDialogService.ShowWarning("Введите email для восстановления пароля.", "Восстановление пароля");
                return false;
            }

            EnsureLocalServerStarted();

            var options = new ResetPasswordForEmailOptions(email.Trim())
            {
                RedirectTo = PasswordResetPageUrl
            };

            await _client.Auth.ResetPasswordForEmail(options);

            AppDialogService.ShowInfo(
                $"Если аккаунт с адресом {email.Trim()} существует, письмо со ссылкой для смены пароля уже отправлено.",
                "Восстановление пароля");

            return true;
        }
        catch (GotrueException ex)
        {
            AppDialogService.ShowError($"Не удалось отправить письмо для восстановления пароля: {ex.Message}", "Восстановление пароля");
            return false;
        }
        catch (Exception ex)
        {
            AppDialogService.ShowError($"Неизвестная ошибка при восстановлении пароля: {ex.Message}", "Восстановление пароля");
            return false;
        }
    }

    public async Task<bool> GoogleSignInAsync(Page currentPage)
    {
        try
        {
            // Создаем URL для OAuth авторизации через Supabase
            string oauthUrl = $"https://ceqnfiznaanuzojjgdcs.supabase.co/auth/v1/authorize" +
                              $"?provider=google" +
                              $"&redirect_to={Uri.EscapeDataString(GoogleCallbackPageUrl)}";

            AppDialogService.ShowInfo(
                "Сейчас откроется браузер для входа через Google.\n" +
                "После авторизации данные будут автоматически отправлены в приложение.",
                "Вход через Google");

            EnsureLocalServerStarted();

            var completionSource = new TaskCompletionSource<Dictionary<string, object>?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _googleAuthCompletionSource = completionSource;

            Process.Start(new ProcessStartInfo
            {
                FileName = oauthUrl,
                UseShellExecute = true
            });

            var completedTask = await Task.WhenAny(completionSource.Task, Task.Delay(120000));

            if (completedTask != completionSource.Task)
            {
                AppDialogService.ShowWarning("Время ожидания авторизации истекло.", "Вход через Google");
                _googleAuthCompletionSource = null;
                return false;
            }

            var userData = await completionSource.Task;
            _googleAuthCompletionSource = null;

            if (userData != null)
            {
                string accessToken = userData.ContainsKey("accessToken") ? userData["accessToken"]?.ToString() : null;
                string refreshToken = userData.ContainsKey("refreshToken") ? userData["refreshToken"]?.ToString() : null;

                if (!string.IsNullOrEmpty(accessToken))
                {
                    await _client.Auth.SetSession(accessToken, refreshToken);
                    await Task.Delay(1000);

                    if (_client.Auth.CurrentUser != null)
                    {
                        if (_client.Auth.CurrentSession != null)
                        {
                            SessionStorage.SaveSession(_client.Auth.CurrentSession);
                        }

                        var profile = await GetProfileByActorIdAsync(_client.Auth.CurrentUser.Id);

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (profile != null && !string.IsNullOrEmpty(profile.Username))
                            {
                                AppDialogService.ShowSuccess($"Вход через Google выполнен успешно! Добро пожаловать, {profile.Username}", "Вход через Google");
                            }
                            else
                            {
                                AppDialogService.ShowSuccess($"Вход через Google выполнен успешно! Добро пожаловать, {_client.Auth.CurrentUser.Email}", "Вход через Google");
                            }

                            currentPage.NavigationService.Navigate(new UserHomePage());
                        });

                        return true;
                    }
                }
            }

            AppDialogService.ShowError("Не удалось получить данные авторизации.", "Вход через Google");
            return false;
        }
        catch (Exception ex)
        {
            AppDialogService.ShowError($"Ошибка при входе через Google: {ex.Message}", "Вход через Google");
            return false;
        }
    }

    // ДОБАВЬТЕ ЭТОТ МЕТОД ЗДЕСЬ
    public async Task<Profile?> GetProfileByActorIdAsync(string actorId)
    {
        try
        {
            if (string.IsNullOrEmpty(actorId)) return null;

            var userId = Guid.Parse(actorId);

            var result = await _client
                .From<Profile>()
                .Where(p => p.Id == userId)
                .Single();

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при получении профиля: {ex.Message}");
            return null;
        }
    }

    public async Task<Profile?> GetProfileByUserIdAsync(Guid userId)
    {
        try
        {
            var result = await _client
                .From<Profile>()
                .Where(p => p.Id == userId)
                .Single();

            return result;
        }
        catch
        {
            return null;
        }
    }

    public async Task<Board?> GetBoardByIdAsync(Guid boardId)
    {
        try
        {
            return await _client
                .From<Board>()
                .Where(b => b.Id == boardId)
                .Single();
        }
        catch
        {
            return null;
        }
    }

    public static void EnsureLocalServerStarted()
    {
        if (_localServer != null && _localServer.IsListening)
        {
            return;
        }

        lock (_localServerLock)
        {
            if (_localServer != null && _localServer.IsListening)
            {
                return;
            }

            _localServer = new HttpListener();
            _localServer.Prefixes.Add($"{LocalServerBaseUrl}/");
        }

        try
        {
            try
            {
                _localServer!.Start();
            }
            catch (HttpListenerException)
            {
                return;
            }

            Console.WriteLine($"Локальный сервер запущен на {LocalServerBaseUrl}");

            Task.Run(async () =>
            {
                while (_localServer != null && _localServer.IsListening)
                {
                    try
                    {
                        var context = await _localServer.GetContextAsync();
                        await HandleLocalServerRequestAsync(context);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка в локальном сервере: {ex.Message}");
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка запуска сервера: {ex.Message}");
        }
    }

    private static async Task HandleLocalServerRequestAsync(HttpListenerContext context)
    {
        try
        {
            if (context.Request.HttpMethod != "GET")
            {
                context.Response.StatusCode = 405;
                context.Response.Close();
                return;
            }

            var path = context.Request.Url?.AbsolutePath?.ToLowerInvariant();

            switch (path)
            {
                case "/oauth-callback.html":
                    await ServeLocalHtmlAsync(context, "oauth-callback.html");
                    break;
                case "/password-reset.html":
                    await ServeLocalHtmlAsync(context, "password-reset.html");
                    break;
                case "/auth/callback/":
                case "/auth/callback":
                    await HandleGoogleCallbackAsync(context);
                    break;
                case "/auth/reset-password/":
                case "/auth/reset-password":
                    await HandlePasswordResetCallbackAsync(context);
                    break;
                default:
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка обработки локального запроса: {ex.Message}");

            if (context.Response.OutputStream.CanWrite)
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
        }
    }

    private static async Task ServeLocalHtmlAsync(HttpListenerContext context, string fileName)
    {
        var htmlPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);

        if (!File.Exists(htmlPath))
        {
            context.Response.StatusCode = 404;
            await WriteResponseAsync(
                context.Response,
                $"<html><body><h2>Ошибка: файл {fileName} не найден</h2><p>Путь: {htmlPath}</p></body></html>",
                "text/html; charset=utf-8");
            return;
        }

        var html = File.ReadAllText(htmlPath);
        await WriteResponseAsync(context.Response, html, "text/html; charset=utf-8");
    }

    private static async Task HandleGoogleCallbackAsync(HttpListenerContext context)
    {
        string data = context.Request.QueryString["data"];
        Dictionary<string, object>? userData = null;

        if (!string.IsNullOrWhiteSpace(data))
        {
            userData = JsonConvert.DeserializeObject<Dictionary<string, object>>(data);
        }

        _googleAuthCompletionSource?.TrySetResult(userData);

        await WriteResponseAsync(
            context.Response,
            "<html><body style='font-family: Arial; text-align: center; margin-top: 50px;'><h2 style='color: #4CAF50;'>✅ Данные получены!</h2><p>Окно можно закрыть.</p><script>setTimeout(() => window.close(), 1500);</script></body></html>",
            "text/html; charset=utf-8");
    }

    private static async Task HandlePasswordResetCallbackAsync(HttpListenerContext context)
    {
        string data = context.Request.QueryString["data"];

        if (string.IsNullOrWhiteSpace(data))
        {
            context.Response.StatusCode = 400;
            await WriteResponseAsync(
                context.Response,
                JsonConvert.SerializeObject(new { success = false, message = "Данные для смены пароля не получены." }),
                "application/json; charset=utf-8");
            return;
        }

        var payload = JsonConvert.DeserializeObject<PasswordResetPayload>(data);
        var result = await CompletePasswordResetAsync(payload);

        context.Response.StatusCode = result.Success ? 200 : 400;
        await WriteResponseAsync(
            context.Response,
            JsonConvert.SerializeObject(new { success = result.Success, message = result.Message }),
            "application/json; charset=utf-8");
    }

    private static async Task<(bool Success, string Message)> CompletePasswordResetAsync(PasswordResetPayload? payload)
    {
        if (payload == null)
        {
            return (false, "Не удалось прочитать данные для смены пароля.");
        }

        if (string.IsNullOrWhiteSpace(payload.NewPassword) || payload.NewPassword.Length < 6)
        {
            return (false, "Пароль должен содержать минимум 6 символов.");
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(payload.AccessToken))
            {
                await _client.Auth.SetSession(payload.AccessToken, payload.RefreshToken, false);
            }
            else if (!string.IsNullOrWhiteSpace(payload.TokenHash))
            {
                await _client.Auth.VerifyTokenHash(payload.TokenHash, EmailOtpType.Recovery);
            }
            else
            {
                return (false, "Ссылка для восстановления не содержит токенов авторизации.");
            }

            var attributes = new UserAttributes
            {
                Password = payload.NewPassword
            };

            await _client.Auth.Update(attributes);

            SessionStorage.ClearSession();
            await _client.Auth.SignOut();

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Application.Current.MainWindow is WhiteSpace.MainWindow window)
                {
                    window.MainFrame.Navigate(new LoginPage());
                }
            });

            return (true, "Пароль успешно обновлен. Теперь вы можете войти с новым паролем.");
        }
        catch (Exception ex)
        {
            return (false, $"Не удалось обновить пароль: {ex.Message}");
        }
    }

    private static async Task WriteResponseAsync(HttpListenerResponse response, string content, string contentType)
    {
        var buffer = Encoding.UTF8.GetBytes(content);
        response.ContentType = contentType;
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        response.Close();
    }

    private sealed class PasswordResetPayload
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public string? TokenHash { get; set; }
        public string? NewPassword { get; set; }
    }


    // МЕТОД ДЛЯ ПРОВЕРКИ АВТОРИЗАЦИИ
    public bool IsUserAuthenticated()
    {
        return _client.Auth.CurrentUser != null;
    }

    //Получение токена сессии пользователя
    public static string GetSessionToken()
    {
        var session = _client.Auth.CurrentSession;
        if (session == null)
        {
            AppDialogService.ShowWarning("Пользователь не авторизован.", "Сессия");
            return null;
        }
        return session.AccessToken;
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
                    AppDialogService.ShowInfo($"Имя пользователя: {profile.Username}", "Профиль");
                }
                else if (profile != null && string.IsNullOrEmpty(profile.Username))
                {
                    AppDialogService.ShowInfo("Имя пользователя не установлено", "Профиль");
                }
                else
                {
                    AppDialogService.ShowWarning("Профиль пользователя не найден в базе данных", "Профиль");
                }
            }
            else
            {
                AppDialogService.ShowWarning("Пользователь не авторизован. Пожалуйста, выполните вход.", "Сессия");
            }
        }
        catch (Exception ex)
        {
            AppDialogService.ShowError($"Ошибка при получении имени пользователя: {ex.Message}", "Профиль");
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

            var memberships = await _client.From<BoardMember>()
                .Where(m => m.BoardId == boardId && m.UserId == userId)
                .Get();

            var membership = memberships.Models?.FirstOrDefault();
            return membership?.Role;
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
                AppDialogService.ShowWarning("Пользователь не авторизован", "Профиль");
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
            AppDialogService.ShowError($"Ошибка в GetMyProfileAsync: {ex.Message}", "Профиль");
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
                AppDialogService.ShowWarning("Пользователь не авторизован", "Создание доски");
                return null;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                AppDialogService.ShowWarning("Название доски не может быть пустым", "Создание доски");
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
                var boardId = result.Models.First().Id;
                var newBoardMember = new BoardMember
                {
                    BoardId = boardId,
                    UserId = Guid.Parse(user.Id),
                    Role = "owner",
                    JoinedAt = DateTime.UtcNow
                };

                await _client.From<BoardMember>().Insert(newBoardMember);

                AppDialogService.ShowSuccess("Доска успешно создана.", "Создание доски");
                return result.Models.First();
            }

            AppDialogService.ShowError("Не удалось создать доску", "Создание доски");
            return null;
        }
        catch (Exception ex)
        {
            AppDialogService.ShowError($"Ошибка создания доски: {ex.Message}", "Создание доски");
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

            var ownedBoards = await _client.From<Board>()
                .Where(b => b.OwnerId == userId)
                .Get();
            if (ownedBoards.Models != null)
            {
                foreach (var board in ownedBoards.Models)
                    result.Add((board, "owner"));
            }

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
                    if (board != null && board.OwnerId != userId)
                    {
                        result.Add((board, member.Role));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppDialogService.ShowError($"Ошибка получения досок: {ex.Message}", "Доски");
        }
        return result;
    }

    //Сохранить изменения на доске
    public async Task<bool> SaveShapeAsync(BoardShape shape)
    {
        try
        {
            string pointsJson = null;
            if (shape.DeserializedPoints != null && shape.DeserializedPoints.Count > 0)
            {
                pointsJson = JsonConvert.SerializeObject(shape.DeserializedPoints);
            }

            var result = await _client.From<BoardShape>().Upsert(new BoardShape
            {
                Id = shape.Id,
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
                return true;
            }
            else
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            AppDialogService.ShowError($"Ошибка при сохранении фигуры: {ex.Message}", "Доска");
            return false;
        }
    }

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
                    if (!string.IsNullOrEmpty(model.Points))
                    {
                        model.DeserializedPoints = JsonConvert.DeserializeObject<List<Point>>(model.Points);
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

    public async Task<bool> ClearBoardShapesAsync(Guid boardId)
    {
        try
        {
            await _client
                .From<BoardShape>()
                .Where(shape => shape.BoardId == boardId)
                .Delete();

            return true;
        }
        catch (Exception ex)
        {
            AppDialogService.ShowError($"Ошибка при очистке доски: {ex.Message}", "Доска");
            return false;
        }
    }

    public async Task<bool> ReplaceBoardShapesAsync(Guid boardId, IEnumerable<BoardShape> shapes)
    {
        if (!await ClearBoardShapesAsync(boardId))
        {
            return false;
        }

        foreach (var shape in shapes)
        {
            if (!await SaveShapeAsync(shape))
            {
                return false;
            }
        }

        return true;
    }

    public async Task<int> GenerateUniqueIdAsync(Guid boardId)
    {
        int newId;
        bool idExists;

        do
        {
            newId = Guid.NewGuid().GetHashCode();
            var existingShape = await _client
                .From<BoardShape>()
                .Where(s => s.BoardId == boardId && s.Id == newId)
                .Get();

            idExists = existingShape.Models?.Count > 0;
        }
        while (idExists);

        return newId;
    }

    public async Task<Board> JoinBoardAsync(string accessCode)
    {
        try
        {
            var user = _client.Auth.CurrentUser;
            if (user == null)
            {
                AppDialogService.ShowWarning("Пользователь не авторизован.", "Подключение к доске");
                return null;
            }

            var boardResult = await _client.From<Board>()
                .Where(b => b.AccessCode == accessCode)
                .Single();

            if (boardResult == null)
            {
                AppDialogService.ShowWarning("Доска с таким кодом не найдена.", "Подключение к доске");
                return null;
            }

            var boardId = boardResult.Id;
            var userId = Guid.Parse(user.Id);

            if (boardResult.OwnerId == userId)
            {
                AppDialogService.ShowInfo("Вы владелец этой доски.", "Подключение к доске");
                return boardResult;
            }

            var existingMember = await _client.From<BoardMember>()
                .Where(m => m.BoardId == boardId && m.UserId == userId)
                .Single();

            if (existingMember != null)
            {
                AppDialogService.ShowInfo("Вы уже присоединились к этой доске.", "Подключение к доске");
                return boardResult;
            }

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
                AppDialogService.ShowSuccess($"Вы успешно присоединились к доске \"{boardResult.Title}\" (режим просмотра).", "Подключение к доске");
                return boardResult;
            }
            else
            {
                AppDialogService.ShowError("Не удалось добавить запись в таблицу участников. Проверьте политики RLS.", "Подключение к доске");
                return null;
            }
        }
        catch (Exception ex)
        {
            AppDialogService.ShowError($"Ошибка присоединения к доске: {ex.Message}", "Подключение к доске");
            return null;
        }
    }

    public async Task<bool> CanEditBoardAsync(Guid boardId)
    {
        try
        {
            var user = _client.Auth.CurrentUser;
            if (user == null) return false;

            var userId = Guid.Parse(user.Id);

            var board = await _client.From<Board>()
                .Where(b => b.Id == boardId)
                .Single();
            if (board != null && board.OwnerId == userId)
                return true;

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

    public async Task<bool> UpdateBoardMemberRoleAsync(Guid boardId, Guid userId, string newRole)
    {
        try
        {
            var member = await _client.From<BoardMember>()
                .Where(m => m.BoardId == boardId && m.UserId == userId)
                .Single();

            if (member == null)
            {
                AppDialogService.ShowWarning("Пользователь не найден.", "Изменение роли");
                return false;
            }

            member.Role = newRole;
            var result = await _client.From<BoardMember>().Update(member);

            return result.Models?.Any() == true;
        }
        catch (Exception ex)
        {
            AppDialogService.ShowError($"Ошибка при изменении роли: {ex.Message}", "Изменение роли");
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
            AppDialogService.ShowError($"Ошибка при получении списка участников: {ex.Message}", "Участники доски");
            return new List<BoardMember>();
        }
    }

    public async Task<bool> DeleteBoardAsync(Guid boardId)
    {
        try
        {
            var user = _client.Auth.CurrentUser;
            if (user == null)
            {
                AppDialogService.ShowWarning("Пользователь не авторизован.", "Удаление доски");
                return false;
            }

            // Проверяем, является ли пользователь владельцем доски
            var board = await _client.From<Board>()
                .Where(b => b.Id == boardId)
                .Get();

            if (board.Models == null || !board.Models.Any())
            {
                AppDialogService.ShowWarning("Доска не найдена.", "Удаление доски");
                return false;
            }

            var boardToDelete = board.Models.First();

            if (boardToDelete.OwnerId != Guid.Parse(user.Id))
            {
                AppDialogService.ShowWarning("Только владелец может удалить доску.", "Удаление доски");
                return false;
            }

            // Подтверждение удаления
            var result = AppDialogService.ShowConfirmation(
                $"Вы уверены, что хотите удалить доску \"{boardToDelete.Title}\"?\nЭто действие нельзя отменить.",
                "Подтверждение удаления",
                "Удалить",
                "Отмена");

            if (!result)
                return false;

            // Удаляем всех участников доски
            var members = await _client.From<BoardMember>()
                .Where(m => m.BoardId == boardId)
                .Get();

            if (members.Models != null && members.Models.Any())
            {
                foreach (var member in members.Models)
                {
                    await _client.From<BoardMember>()
                        .Where(m => m.BoardId == member.BoardId && m.UserId == member.UserId)
                        .Delete();
                }
            }

            // Удаляем все фигуры с доски
            var shapes = await _client.From<BoardShape>()
                .Where(s => s.BoardId == boardId)
                .Get();

            if (shapes.Models != null && shapes.Models.Any())
            {
                foreach (var shape in shapes.Models)
                {
                    await _client.From<BoardShape>()
                        .Where(s => s.BoardId == shape.BoardId && s.Id == shape.Id)
                        .Delete();
                }
            }

            // Удаляем саму доску
            await _client.From<Board>()
                .Where(b => b.Id == boardId)
                .Delete();

            AppDialogService.ShowSuccess($"Доска \"{boardToDelete.Title}\" успешно удалена.", "Удаление доски");
            return true;
        }
        catch (Exception ex)
        {
            AppDialogService.ShowError($"Ошибка при удалении доски: {ex.Message}", "Удаление доски");
            return false;
        }
    }

}
