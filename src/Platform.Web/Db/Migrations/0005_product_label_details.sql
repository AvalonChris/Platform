-- Label details shown on the product page, copied from the supplier's product information.
alter table products
    add column ingredients   text not null default '',
    add column suggested_use text not null default '',
    add column warnings      text not null default '';
