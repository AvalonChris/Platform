create table admin_users (
    id            bigint generated always as identity primary key,
    email         text not null,
    password_hash text not null,
    created_at    timestamptz not null default now()
);

create unique index admin_users_email_key on admin_users (lower(email));
