-- The address the order ships to, so subscriptions can be started after an asynchronous payment.
-- Also the payment provider's checkout session (the PayPal order id), for reconciling payments.
alter table orders
    add column shipping_address_id bigint references addresses (id),
    add column payment_session_id  text;
