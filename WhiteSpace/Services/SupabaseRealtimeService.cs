using Supabase;
using Supabase.Realtime;
using Supabase.Realtime.PostgresChanges;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

public static class SupabaseRealtimeService
{
    private static Supabase.Client _supabaseClient;
    private static readonly Dictionary<Guid, RealtimeChannel> _channels = new Dictionary<Guid, RealtimeChannel>();

    public static Task ConnectAndSubscribe(
    Supabase.Client client,
    Guid boardId,
    Action<BoardShape> onInsert,
    Action<BoardShape> onUpdate,
    Action<BoardShape> onDelete)
    {
        if (client.Auth.CurrentUser == null)
        {
            MessageBox.Show("Ошибка: клиент не аутентифицирован");
            return Task.CompletedTask;
        }

        if (_channels.TryGetValue(boardId, out var existingChannel))
        {
            existingChannel.Unsubscribe(); // если Unsubscribe синхронный, иначе await
            _channels.Remove(boardId);
        }

        _supabaseClient = client;
        _supabaseClient.Realtime.Connect();

        var channel = _supabaseClient.Realtime.Channel("realtime:public:boardshape");

        channel.AddPostgresChangeHandler(PostgresChangesOptions.ListenType.Inserts, (sender, change) =>
        {
            var shape = change.Model<BoardShape>();
            if (shape.BoardId == boardId)
                Application.Current.Dispatcher.Invoke(() => onInsert?.Invoke(shape));
        });

        channel.AddPostgresChangeHandler(PostgresChangesOptions.ListenType.Updates, (sender, change) =>
        {
            var shape = change.Model<BoardShape>();
            if (shape.BoardId == boardId)
                Application.Current.Dispatcher.Invoke(() => onUpdate?.Invoke(shape));
        });

        channel.AddPostgresChangeHandler(PostgresChangesOptions.ListenType.Deletes, (sender, change) =>
        {
            var shape = change.OldModel<BoardShape>();
            if (shape.BoardId == boardId)
                Application.Current.Dispatcher.Invoke(() => onDelete?.Invoke(shape));
        });

        channel.Subscribe(); // синхронный вызов
        _channels[boardId] = channel;

        Console.WriteLine($"[Realtime] Подписка на доску {boardId} завершена");
        return Task.CompletedTask;
    }

    public static void Disconnect(Guid? boardId = null)
    {
        if (boardId.HasValue)
        {
            if (_channels.TryGetValue(boardId.Value, out var channel))
            {
                channel.Unsubscribe(); // синхронно
                _channels.Remove(boardId.Value);
            }
        }
        else
        {
            foreach (var channel in _channels.Values)
                channel.Unsubscribe();
            _channels.Clear();
        }
    }
}