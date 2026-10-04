# Go-live checklist

Work through the phases in order; later phases depend on earlier ones. Tick items off as you go. Steps marked
**(Claude)** are ones to hand to Claude Code.

---

## Phase 1: Business

These take the longest and gate PayPal live approval, insurance and the legal pages.

- [ ] Form the company (for example an LLC) and get an EIN from the IRS.
- [ ] Open a business bank account and link it to the PayPal business account.
- [ ] Register for sales tax in your home state. Check whether you need to collect it there from day one.
- [ ] Get product liability insurance quotes (Supliful's referral, Assureful, a specialist broker) and buy a policy.
      Mention NMN specifically.
- [ ] Have a lawyer review the policy pages (`/shipping-and-refunds`, `/subscription-terms`, `/privacy`, `/terms`)
      and the product descriptions.

## Phase 2: Brand, domain and email

- [ ] Choose the brand name.
- [ ] Buy the domain and a Private Email plan at Namecheap.
- [ ] Create the support mailbox (for example `support@yourdomain.com`).
- [ ] In Namecheap's DNS settings, add the email records Private Email lists (MX, SPF, DKIM) and a DMARC record,
      so store emails don't land in spam.
- [ ] Decide where `www` and the bare domain point (next phase).

## Phase 3: Products

- [ ] Ask Supliful for the NMN certificate of analysis.
- [ ] Order samples of the launch products (NMN, resveratrol, CoQ10, magnesium glycinate).
- [ ] Send an NMN sample to an independent lab for a purity test.
- [ ] Design the labels with the brand name in Supliful.
- [ ] Download Supliful's product mockups for product photos. They'll be uploaded or linked as each product's image.
- [ ] Confirm typical delivery times with Supliful (the policy page says most orders arrive in 5–7 business days).

## Phase 4: Server and site

The store is hosted like the other sites on this server: an IIS site on 74.208.133.196 with an HTTPS host-name
binding, running the app in-process.

**Database**
- [ ] Create a production database (for example `platform_prod`), separate from the development one, so no test
      orders go live.
- [ ] Create a dedicated Postgres login that owns that database, instead of using `postgres`.
- [ ] Set up nightly backups (a scheduled task running `pg_dump`), stored off the server as well.

**Publish**
- [ ] **(Claude)** Publish the app: `dotnet publish src\Platform.Web -c Release -o C:\Websites\<Brand>\app\v1`.
- [ ] Create `appsettings.Production.json` in that folder (template below). It holds the secrets, lives only on the
      server, and is excluded from git. Don't use a copy method that deletes files in the folder when you republish.
- [ ] Create the IIS site: physical path `C:\Websites\<Brand>\app\v1`, an app pool with **No Managed Code**,
      HTTPS bindings on 74.208.133.196:443 for `www.yourdomain.com` (and the bare domain), and an HTTP binding on 80
      if your certificate method needs it.
- [ ] Give the app pool identity (`IIS AppPool\<pool name>`) **Modify** permission on the site's `App_Data` folder.
      The app keeps its cookie-encryption keys there; without them, every restart signs everyone out.
- [ ] Set the HTTPS certificate the same way as your other sites.

**DNS**
- [ ] At Namecheap, point an A record for `www` (and `@`) to 74.208.133.196.

**First run**
- [ ] From the site folder, apply the database schema: `dotnet Platform.Web.dll migrate`.
- [ ] Load the launch catalogue: `dotnet Platform.Web.dll seed`. Run it **once only**; running it again overwrites
      product edits made in the admin.
- [ ] Create your admin login: `dotnet Platform.Web.dll create-admin you@yourdomain.com`.
- [ ] Browse to `https://www.yourdomain.com` and `/admin`. If the site doesn't start, temporarily set
      `stdoutLogEnabled="true"` in the site's `web.config` and read `logs\stdout*`.

### `appsettings.Production.json` template

```json
{
  "ConnectionStrings": {
    "Platform": "Host=localhost;Database=platform_prod;Username=platform_app;Password=..."
  },
  "Store": {
    "BrandName": "Your Brand",
    "PublicBaseUrl": "https://www.yourdomain.com",
    "LegalName": "Your Company LLC",
    "ContactEmail": "support@yourdomain.com",
    "LegalState": "Your State",
    "PoliciesUpdated": "Month D, YYYY"
  },
  "Payments": {
    "PayPal": {
      "Environment": "live",
      "ClientId": "...",
      "ClientSecret": "...",
      "WebhookId": "..."
    }
  },
  "Email": {
    "FromAddress": "support@yourdomain.com",
    "FromName": "Your Brand",
    "Smtp": {
      "Host": "mail.privateemail.com",
      "Port": 587,
      "UserName": "support@yourdomain.com",
      "Password": "...",
      "EnableSsl": true
    }
  },
  "Fulfillment": {
    "Enabled": false,
    "ShopDomain": "your-store.myshopify.com",
    "AccessToken": "shpat_..."
  }
}
```

Leave `Fulfillment:Enabled` false until Phase 6 is done.

## Phase 5: PayPal live

- [ ] At developer.paypal.com, switch to **Live** and create a **Merchant** app.
- [ ] Enable **Vault** (saving payment methods) and **Advanced Credit and Debit Card Payments** on the live app.
      Live card processing may need PayPal's approval, which can take a few days. Until then, cards still work
      through PayPal's own button.
- [ ] Put the live Client ID and Secret in `appsettings.Production.json`, with `"Environment": "live"`.
- [ ] Add a webhook to the live app at `https://www.yourdomain.com/webhooks/paypal` with these events:
      CHECKOUT.ORDER.APPROVED, PAYMENT.CAPTURE.COMPLETED, PAYMENT.CAPTURE.REFUNDED, PAYMENT.CAPTURE.REVERSED,
      CUSTOMER.DISPUTE.CREATED, CUSTOMER.DISPUTE.UPDATED, CUSTOMER.DISPUTE.RESOLVED, VAULT.PAYMENT-TOKEN.DELETED.
- [ ] Copy the webhook's ID into `WebhookId`, restart the site, and check **Admin → Webhooks** after the first
      payment.
- [ ] In **Admin → Costs**, set the card fee to what you expect to pay (PayPal button 3.49% + $0.49; card fields
      2.89% + $0.39).

## Phase 6: Supliful and Shopify

- [ ] Upgrade Supliful to **Pro**.
- [ ] Create a **Shopify Basic** store. The Starter plan doesn't work with Supliful.
- [ ] Install the Supliful app in Shopify and use **Publish to Shopify** for each launch product.
- [ ] In Shopify settings, turn on **Automatically fulfill the order's line items**.
- [ ] Create a Shopify custom app with order and customer read/write access, install it, and copy the Admin API
      access token into `Fulfillment:AccessToken`. Set `Fulfillment:ShopDomain`.
- [ ] In **Admin → Products**, paste each single product's Shopify variant ID into **Fulfilment variant ID**.
- [ ] Check each product's **supplier cost** and **shipping weight** against Supliful, then review **Admin → Profit**.
- [ ] Set `Fulfillment:Enabled` to true and restart the site.

## Phase 7: Store details

- [ ] Fill in the `Store` settings above, which updates the footer and every policy page.
- [ ] Decide on shipping charges (`FlatShippingRate` / `FreeShippingThreshold` in `appsettings.json`). Free
      shipping is the current setting.
- [ ] Add product images (Image URL on each product in the admin).
- [ ] Re-read every product description in the admin. Keep claims to permitted structure/function wording.

## Phase 8: Live test with real money

Use your own card or PayPal account and the cheapest product.

- [ ] Place a one-time order. Check:
  - you land on the confirmation page and receive the confirmation email,
  - the order appears in **Admin → Orders** as paid,
  - its fulfilment status changes to **sent** within a minute and the order appears in Shopify and Supliful,
  - Supliful ships it; then enter the tracking number with **Mark as shipped** and check the shipping email.
- [ ] Place a subscription order. Check that **Admin → Subscriptions** shows a saved payment method, and that you can
      sign in at `/account` with the emailed link and skip, pause and resume it. Cancel it afterwards.
- [ ] Refund one test order in PayPal and check the order shows **refunded** (this tests the webhook).

## Phase 9: Launch and first weeks

- [ ] Re-check the webhook log, subscriptions and orders daily for the first couple of weeks.
- [ ] Confirm backups are running and that you can restore one.
- [ ] Keep `docs/supliful-catalog.md` and **Admin → Costs** up to date when Supliful changes prices or fees.
- [ ] Watch for disputes in PayPal's Resolution Center; open disputes are also flagged in the admin.
