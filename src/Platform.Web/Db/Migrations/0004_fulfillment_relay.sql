-- The Shopify product variant that Supliful fulfils for this product. Stacks have none:
-- they are sent as their component products.
alter table products add column fulfillment_variant_id text;

-- Tracks sending each paid order on to the fulfilment provider.
alter table orders
    add column fulfillment_status text not null default 'not_required'
        check (fulfillment_status in ('not_required', 'pending', 'sent', 'failed', 'skipped')),
    add column fulfillment_reference  text,
    add column fulfillment_error      text,
    add column fulfillment_attempts   integer not null default 0,
    add column fulfillment_updated_at timestamptz;

create index orders_fulfillment_due_idx on orders (id) where fulfillment_status in ('pending', 'failed');
