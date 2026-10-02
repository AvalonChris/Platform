-- Customer-facing order numbers come from their own sequence.
create sequence order_number_seq start 10001;

alter table orders
    alter column order_number set default nextval('order_number_seq')::text;

-- Unguessable identifier used in order links, since order numbers are sequential.
alter table orders
    add column public_token uuid not null default gen_random_uuid();

create unique index orders_public_token_key on orders (public_token);
