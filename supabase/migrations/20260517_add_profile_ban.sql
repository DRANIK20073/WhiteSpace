-- Блокировка аккаунтов администратором.
-- Supabase → SQL Editor → New query → вставить и выполнить Run.

alter table public.profiles
    add column if not exists is_banned boolean not null default false;

alter table public.profiles
    add column if not exists banned_at timestamptz;

alter table public.profiles
    add column if not exists ban_reason text;

create index if not exists profiles_is_banned_idx on public.profiles (is_banned)
    where is_banned = true;

-- После миграции: Dashboard → Settings → API → Reload schema (или подождать ~1 мин).
