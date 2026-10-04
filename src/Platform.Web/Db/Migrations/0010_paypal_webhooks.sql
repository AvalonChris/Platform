-- Every PayPal webhook received, so each is processed once and there's a record of what PayPal reported.
create table paypal_webhook_events (
    event_id     text primary key,
    event_type   text not null,
    resource_id  text,
    payload      jsonb not null,
    received_at  timestamptz not null default now(),
    processed_at timestamptz,
    error        text
);

create index paypal_webhook_events_received_idx on paypal_webhook_events (received_at desc);

-- Refunds and disputes reported by PayPal.
alter table orders
    add column refunded_total     numeric(12, 2) not null default 0,
    add column refunded_at        timestamptz,
    add column dispute_id         text,
    add column dispute_status     text,
    add column dispute_reason     text,
    add column dispute_updated_at timestamptz;

-- Webhooks find orders by PayPal capture id or PayPal order id.
create index orders_payment_reference_idx on orders (payment_reference);
create index orders_payment_session_idx on orders (payment_session_id);
