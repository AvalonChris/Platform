// Payment page: PayPal buttons and card fields. Our server creates the PayPal order and, once the customer
// approves it, captures it and checks it before marking our order paid.
(function () {
    const root = document.getElementById('paypal-checkout');
    if (!root || !window.paypal) {
        return;
    }

    const message = document.getElementById('payment-message');
    const cardSection = document.getElementById('card-section');
    const cardSubmit = document.getElementById('card-submit');

    function showMessage(text) {
        message.textContent = text || '';
        message.hidden = !text;
    }

    async function post(url, body) {
        const response = await fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': root.dataset.requestToken
            },
            body: JSON.stringify(body)
        });
        const data = await response.json().catch(() => ({}));
        if (!response.ok) {
            const error = new Error(data.error || 'Something went wrong. Please try again.');
            error.restart = data.restart === true;
            throw error;
        }
        return data;
    }

    function createOrder(source) {
        showMessage('');
        return post(root.dataset.createUrl, { source: source }).then(data => data.id);
    }

    async function onApprove(data, actions) {
        try {
            const result = await post(root.dataset.captureUrl, { orderId: data.orderID });
            window.location.href = result.redirect;
        } catch (error) {
            if (error.restart && actions && actions.restart) {
                return actions.restart();
            }
            showMessage(error.message);
            if (cardSubmit) {
                cardSubmit.disabled = false;
            }
        }
    }

    paypal.Buttons({
        // Only the PayPal button gets PayPal-specific settings (such as saving the account for subscriptions);
        // Venmo, Pay Later and the guest card button are sent without them.
        createOrder: data => createOrder(data && data.paymentSource && data.paymentSource !== 'paypal'
            ? 'button-' + data.paymentSource
            : 'paypal'),
        onApprove: onApprove,
        onCancel: () => showMessage('Payment was cancelled. You can try again whenever you are ready.'),
        onError: error => showMessage((error && error.message) || 'PayPal could not complete the payment. Please try again.')
    }).render('#paypal-buttons');

    const cardFields = paypal.CardFields({
        createOrder: () => createOrder('card'),
        onApprove: onApprove,
        onError: () => {
            showMessage('Your card could not be charged. Please check the details and try again.');
            cardSubmit.disabled = false;
        }
    });

    // Card fields appear only when the PayPal account is approved for advanced card payments.
    if (cardFields.isEligible()) {
        cardSection.hidden = false;
        cardFields.NameField().render('#card-name');
        cardFields.NumberField().render('#card-number');
        cardFields.ExpiryField().render('#card-expiry');
        cardFields.CVVField().render('#card-cvv');

        cardSubmit.addEventListener('click', () => {
            cardSubmit.disabled = true;
            showMessage('');
            cardFields.submit().catch(() => {
                showMessage('Please check your card details and try again.');
                cardSubmit.disabled = false;
            });
        });
    }
})();
