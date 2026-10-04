-- When an order shipped and how to track it, for the shipping email and the customer's order page.
alter table orders
    add column shipped_at       timestamptz,
    add column tracking_carrier text,
    add column tracking_number  text;
