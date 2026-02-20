using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Supabase;
using Supabase.Gotrue;
using Supabase.Interfaces;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System.Diagnostics;
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
using static Supabase.Gotrue.Constants;
using static Supabase.Realtime.PostgresChanges.PostgresChangesOptions;

public class SupabaseService
{
    private static Supabase.Client _client; // для работы с основной клиентской логикой
    private static Supabase.Gotrue.Client _authClient; // для работы с аутентификацией

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
            var session = await _client.Auth.SignIn(email, password);

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

    public async Task<bool> GoogleSignInAsync(Page currentPage)
    {
        HttpListener listener = null;

        try
        {
            // Запоминаем текущего пользователя
            var previousUser = _client.Auth.CurrentUser;

            // URL для нашего HTML файла (локальный сервер)
            string callbackPageUrl = "http://127.0.0.1:54322/oauth-callback.html";

            // Создаем URL для OAuth авторизации через Supabase
            string oauthUrl = $"https://ceqnfiznaanuzojjgdcs.supabase.co/auth/v1/authorize" +
                              $"?provider=google" +
                              $"&redirect_to={Uri.EscapeDataString(callbackPageUrl)}";

            // Информируем пользователя
            MessageBox.Show(
                "Сейчас откроется браузер для входа через Google.\n" +
                "После авторизации данные будут автоматически отправлены в приложение.",
                "Вход через Google",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // Запускаем локальный сервер для HTML страницы
            StartLocalServer();

            // Создаем HTTP слушатель для получения данных от HTML страницы
            listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:54322/auth/callback/");
            listener.Start();

            // Открываем браузер
            Process.Start(new ProcessStartInfo
            {
                FileName = oauthUrl,
                UseShellExecute = true
            });

            // Ждем данные от HTML страницы (таймаут 120 секунд)
            var timeoutTask = Task.Delay(120000);
            var getContextTask = listener.GetContextAsync();
            var completedTask = await Task.WhenAny(getContextTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                MessageBox.Show("Время ожидания авторизации истекло.");
                listener.Stop();
                return false;
            }

            var context = await getContextTask;

            // Получаем данные из запроса
            string data = context.Request.QueryString["data"];

            // Отправляем ответ
            string responseHtml = "<html><body style='font-family: Arial; text-align: center; margin-top: 50px;'><h2 style='color: #4CAF50;'>✅ Данные получены!</h2><p>Окно можно закрыть.</p><script>setTimeout(() => window.close(), 1500);</script></body></html>";
            byte[] buffer = Encoding.UTF8.GetBytes(responseHtml);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            context.Response.Close();

            listener.Stop();

            // Парсим полученные данные
            if (!string.IsNullOrEmpty(data))
            {
                var userData = JsonConvert.DeserializeObject<Dictionary<string, object>>(data);

                if (userData != null)
                {
                    string actorId = userData.ContainsKey("actorId") ? userData["actorId"]?.ToString() : null;
                    string accessToken = userData.ContainsKey("accessToken") ? userData["accessToken"]?.ToString() : null;
                    string refreshToken = userData.ContainsKey("refreshToken") ? userData["refreshToken"]?.ToString() : null;
                    string email = userData.ContainsKey("email") ? userData["email"]?.ToString() : null;

                    // Устанавливаем сессию
                    if (!string.IsNullOrEmpty(accessToken))
                    {
                        await _client.Auth.SetSession(accessToken, refreshToken);

                        // Даем время на установку сессии
                        await Task.Delay(1000);

                        if (_client.Auth.CurrentUser != null)
                        {
                            // Сохраняем сессию
                            if (_client.Auth.CurrentSession != null)
                            {
                                SessionStorage.SaveSession(_client.Auth.CurrentSession);
                            }

                            // Получаем профиль
                            var profile = await GetProfileByActorIdAsync(_client.Auth.CurrentUser.Id);

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                if (profile != null && !string.IsNullOrEmpty(profile.Username))
                                {
                                    MessageBox.Show($"Вход через Google выполнен успешно! Добро пожаловать, {profile.Username}");
                                }
                                else
                                {
                                    MessageBox.Show($"Вход через Google выполнен успешно! Добро пожаловать, {_client.Auth.CurrentUser.Email}");
                                }

                                currentPage.NavigationService.Navigate(new UserHomePage());
                            });

                            return true;
                        }
                    }
                }
            }

            MessageBox.Show("Не удалось получить данные авторизации.");
            return false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при входе через Google: {ex.Message}");
            return false;
        }
        finally
        {
            try
            {
                listener?.Stop();
                listener?.Close();
            }
            catch { }
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

    // Добавьте этот метод в класс SupabaseService
    private void StartLocalServer()
    {
        try
        {
            // Создаем HTTP слушатель для HTML страницы
            var htmlListener = new HttpListener();
            htmlListener.Prefixes.Add("http://127.0.0.1:54322/");

            try
            {
                htmlListener.Start();
            }
            catch (HttpListenerException)
            {
                // Сервер уже запущен, игнорируем
                return;
            }

            Console.WriteLine("Локальный сервер запущен на http://127.0.0.1:54322");

            // Запускаем обработку запросов в фоновом потоке
            Task.Run(async () =>
            {
                while (htmlListener.IsListening)
                {
                    try
                    {
                        var context = await htmlListener.GetContextAsync();

                        // Обрабатываем только GET запросы
                        if (context.Request.HttpMethod != "GET")
                        {
                            context.Response.StatusCode = 405;
                            context.Response.Close();
                            continue;
                        }

                        // Если запрашивают наш HTML файл
                        if (context.Request.Url.AbsolutePath == "/oauth-callback.html")
                        {
                            try
                            {
                                // Получаем путь к HTML файлу (рядом с EXE)
                                string htmlPath = System.IO.Path.Combine(
                                    AppDomain.CurrentDomain.BaseDirectory,
                                    "oauth-callback.html");

                                // Проверяем, существует ли файл
                                if (System.IO.File.Exists(htmlPath))
                                {
                                    string html = System.IO.File.ReadAllText(htmlPath);
                                    byte[] buffer = Encoding.UTF8.GetBytes(html);

                                    context.Response.ContentType = "text/html; charset=utf-8";
                                    context.Response.ContentLength64 = buffer.Length;
                                    await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                                }
                                else
                                {
                                    // Файл не найден, отправляем ошибку
                                    string errorHtml = $"<html><body><h2>Ошибка: файл oauth-callback.html не найден</h2><p>Путь: {htmlPath}</p></body></html>";
                                    byte[] buffer = Encoding.UTF8.GetBytes(errorHtml);
                                    context.Response.ContentType = "text/html; charset=utf-8";
                                    context.Response.ContentLength64 = buffer.Length;
                                    await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Ошибка при чтении HTML файла: {ex.Message}");
                                context.Response.StatusCode = 500;
                            }
                        }
                        else
                        {
                            // Для всех других запросов отправляем 404
                            context.Response.StatusCode = 404;
                        }

                        context.Response.Close();
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
            MessageBox.Show("Пользователь не авторизован.");
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
                var boardId = result.Models.First().Id;
                var newBoardMember = new BoardMember
                {
                    BoardId = boardId,
                    UserId = Guid.Parse(user.Id),
                    Role = "owner",
                    JoinedAt = DateTime.UtcNow
                };

                await _client.From<BoardMember>().Insert(newBoardMember);

                MessageBox.Show("Доска успешно создана 🎉");
                return result.Models.First();
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
            MessageBox.Show($"Ошибка получения досок: {ex.Message}");
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

            if (boardResult.OwnerId == userId)
            {
                MessageBox.Show("Вы владелец этой доски.");
                return boardResult;
            }

            var existingMember = await _client.From<BoardMember>()
                .Where(m => m.BoardId == boardId && m.UserId == userId)
                .Single();

            if (existingMember != null)
            {
                MessageBox.Show("Вы уже присоединились к этой доске.");
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
                MessageBox.Show("Пользователь не найден.");
                return false;
            }

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

    public async Task<bool> DeleteBoardAsync(Guid boardId)
    {
        try
        {
            var user = _client.Auth.CurrentUser;
            if (user == null)
            {
                MessageBox.Show("Пользователь не авторизован.");
                return false;
            }

            // Проверяем, является ли пользователь владельцем доски
            var board = await _client.From<Board>()
                .Where(b => b.Id == boardId)
                .Get();

            if (board.Models == null || !board.Models.Any())
            {
                MessageBox.Show("Доска не найдена.");
                return false;
            }

            var boardToDelete = board.Models.First();

            if (boardToDelete.OwnerId != Guid.Parse(user.Id))
            {
                MessageBox.Show("Только владелец может удалить доску.");
                return false;
            }

            // Подтверждение удаления
            var result = MessageBox.Show(
                $"Вы уверены, что хотите удалить доску \"{boardToDelete.Title}\"?\nЭто действие нельзя отменить.",
                "Подтверждение удаления",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
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

            MessageBox.Show($"Доска \"{boardToDelete.Title}\" успешно удалена.");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при удалении доски: {ex.Message}");
            return false;
        }
    }

}