-- What we pay the supplier per unit and the gross shipping weight, for the admin profit report.
-- Stacks have neither: their costs come from their components.
alter table products
    add column supplier_cost      numeric(12, 2) check (supplier_cost >= 0),
    add column shipping_weight_lb numeric(8, 3) check (shipping_weight_lb >= 0),
    add column costs_updated_at   timestamptz;

-- Store-wide cost rates. Exactly one row.
create table cost_settings (
    id                          integer primary key default 1 check (id = 1),
    fulfillment_first_unit      numeric(12, 2) not null,
    fulfillment_additional_unit numeric(12, 2) not null,
    supplier_processing_percent numeric(6, 3) not null,
    card_processing_percent     numeric(6, 3) not null,
    card_processing_fixed       numeric(12, 2) not null,
    shipping_extra_lb_rate      numeric(12, 2) not null,
    updated_at                  timestamptz not null default now()
);

-- Supliful's fees as of 2026-10; card fees are a typical 2.9% + $0.30 until a processor is chosen.
insert into cost_settings (fulfillment_first_unit, fulfillment_additional_unit, supplier_processing_percent,
                           card_processing_percent, card_processing_fixed, shipping_extra_lb_rate)
values (1.99, 1.29, 2.99, 2.9, 0.30, 1.99);

-- Supplier shipping price by package weight: a package up to max_weight_lb costs rate.
create table shipping_rates (
    max_weight_lb numeric(8, 3) primary key check (max_weight_lb > 0),
    rate          numeric(12, 2) not null check (rate >= 0)
);

-- Supliful USPS Ground Advantage rates as of 2026-09.
insert into shipping_rates (max_weight_lb, rate)
values (0.5, 5.29), (0.75, 6.19), (1, 7.49), (2, 9.99), (3, 12.49);
