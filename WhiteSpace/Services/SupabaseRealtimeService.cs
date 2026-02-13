using Supabase;
using Supabase.Realtime;
using Supabase.Realtime.PostgresChanges;
using System;
using System.Threading.Tasks;
using System.Windows;
using static Supabase.Realtime.PostgresChanges.PostgresChangesOptions;

public static class SupabaseRealtimeService
{
    private static Supabase.Client _supabaseClient;
    private static RealtimeChannel _channel; // Канал для работы с событиями
    private static Guid _currentBoardId;

    // Метод для подключения и подписки на события
    public static async Task ConnectAndSubscribe(
        Supabase.Client client,
        Guid boardId,
        Action<BoardShape> onInsert,
        Action<BoardShape> onUpdate,
        Action<BoardShape> onDelete)
    {
        if (_supabaseClient != null && _supabaseClient != client)
            Disconnect();

        _supabaseClient = client;

        if (client.Auth.CurrentUser == null)
        {
            MessageBox.Show("Ошибка: клиент не аутентифицирован");
            return;
        }

        _currentBoardId = boardId;

        // Подключаемся к Supabase Realtime
        _supabaseClient.Realtime.Connect();
        await Task.Delay(5000); // Ожидаем 5 секунд для подключения

        // Создаем канал с фильтром по ID доски
        _channel = _supabaseClient.Realtime.Channel($"public:boardshape:board_id=eq.{boardId}");

        // Обрабатываем события INSERT, UPDATE, DELETE
        _channel.AddPostgresChangeHandler(ListenType.Inserts, (sender, change) =>
        {
            var shape = change.Model<BoardShape>();
            if (onInsert != null)
                Application.Current.Dispatcher.Invoke(() => onInsert(shape)); // Обновляем UI на главном потоке
        });

        _channel.AddPostgresChangeHandler(ListenType.Updates, (sender, change) =>
        {
            var shape = change.Model<BoardShape>();
            if (onUpdate != null)
                Application.Current.Dispatcher.Invoke(() => onUpdate(shape)); // Обновляем UI
        });

        _channel.AddPostgresChangeHandler(ListenType.Deletes, (sender, change) =>
        {
            var shape = change.OldModel<BoardShape>();
            if (onDelete != null)
                Application.Current.Dispatcher.Invoke(() => onDelete(shape)); // Обновляем UI
        });

        // Подписываемся на канал
        await _channel.Subscribe();
        Console.WriteLine($"[Realtime] Подписка на канал {_channel.Topic} завершена");
    }

    // Метод для отключения от канала
    public static void Disconnect()
    {
        if (_channel != null)
        {
            _channel.Unsubscribe();
            _channel = null;
        }
    }
}
