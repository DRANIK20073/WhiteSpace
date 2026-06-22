using System;

namespace WhiteSpace.Services;

/// <summary>Уведомляет открытые экраны об изменении отображаемого имени пользователя.</summary>
public static class ProfileIdentityHub
{
    public static event Action<Guid, string>? DisplayNameChanged;

    public static void NotifyDisplayNameChanged(Guid userId, string displayName)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(displayName))
        {
            return;
        }

        DisplayNameChanged?.Invoke(userId, displayName.Trim());
    }
}
