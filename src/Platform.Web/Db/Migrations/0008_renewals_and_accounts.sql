-- Renewal tracking on subscriptions.
alter table subscriptions
    -- 'paypal' or 'card': which kind of saved PayPal payment method payment_method_reference is.
    add column payment_method_type text,
    add column failed_attempts     integer not null default 0,
    -- After a failed renewal, don't try again before this date.
    add column retry_after         date,
    add column last_failure        text,
    -- The renewal date the reminder email was last sent for.
    add column reminder_sent_for   date,
    add column paused_at           timestamptz;

-- A renewal order is for one subscription period, so a crash mid-renewal can't create a second one.
alter table orders add column renewal_period date;
create unique index orders_subscription_period_key on orders (subscription_id, renewal_period)
    where subscription_id is not null;

-- One-time sign-in links for customers. Only a hash of the token is stored.
create table customer_login_tokens (
    token_hash  text primary key,
    customer_id bigint not null references customers (id) on delete cascade,
    expires_at  timestamptz not null,
    used_at     timestamptz,
    created_at  timestamptz not null default now()
);

create index customer_login_tokens_customer_idx on customer_login_tokens (customer_id);
