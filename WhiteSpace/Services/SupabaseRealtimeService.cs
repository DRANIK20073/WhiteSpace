using Supabase;
using Supabase.Realtime;
using Supabase.Realtime.Interfaces;
using Supabase.Realtime.PostgresChanges;
using System;
using System.Threading.Tasks;
using System.Windows;
using static Supabase.Realtime.PostgresChanges.PostgresChangesOptions;

public static class SupabaseRealtimeService
{
    private static Supabase.Client _supabaseClient;
    private static IRealtimeChannel _channel;
    private static Guid _currentBoardId;

    public static async Task ConnectAndSubscribe(
        string supabaseUrl,
        string anonKey,
        Guid boardId,
        Action<BoardShape> onInsert,
        Action<BoardShape> onUpdate,
        Action<BoardShape> onDelete)
    {
        // Если уже подключены к другой доске — отключаемся
        if (_channel != null && _currentBoardId != boardId)
        {
            Disconnect();
        }

        if (_supabaseClient == null)
        {
            var options = new SupabaseOptions { AutoConnectRealtime = true };
            _supabaseClient = new Supabase.Client(supabaseUrl, anonKey, options);
            await _supabaseClient.InitializeAsync();
        }

        _currentBoardId = boardId;

        // Канал с фильтром по board_id
        _channel = _supabaseClient.Realtime.Channel($"public:boardshape:board_id=eq.{boardId}");

        // Обработчики событий (вызов через Dispatcher для UI)
        _channel.AddPostgresChangeHandler(ListenType.Inserts, (sender, change) =>
        {
            var shape = change.Model<BoardShape>();
            Application.Current.Dispatcher.Invoke(() => onInsert?.Invoke(shape));
        });

        _channel.AddPostgresChangeHandler(ListenType.Updates, (sender, change) =>
        {
            var shape = change.Model<BoardShape>();
            Application.Current.Dispatcher.Invoke(() => onUpdate?.Invoke(shape));
        });

        _channel.AddPostgresChangeHandler(ListenType.Deletes, (sender, change) =>
        {
            var shape = change.OldModel<BoardShape>();
            Application.Current.Dispatcher.Invoke(() => onDelete?.Invoke(shape));
        });

        await _channel.Subscribe();
    }

    public static void Disconnect()
    {
        if (_channel != null)
        {
            _channel.Unsubscribe();
            _channel = null;
        }
    }
}