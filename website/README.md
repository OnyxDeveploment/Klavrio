# Klavrio website

This folder contains the static Klavrio product website, published at [klavrio.app](https://klavrio.app/). It uses plain HTML, CSS, and JavaScript with no package manager, external font, or build step.

- Open `index.html` to preview the landing page.
- `privacy.html` documents the desktop apps' local processing, diagnostics, and optional network access.
- Release cards are configured in `app.js` and currently open the [latest Klavrio GitHub release](https://github.com/OnyxDeveploment/Klavrio/releases/latest).
- Repository, license, credits, and contribution links point to the official [OnyxDeveploment/Klavrio](https://github.com/OnyxDeveploment/Klavrio) repository.
- Search metadata, Open Graph/Twitter cards, SoftwareApplication and FAQ structured data, `robots.txt`, `llms.txt`, and the web manifest are included.

## Analytics and attribution

Analytics is disabled until a valid Google Analytics 4 web-stream Measurement ID is added to `site-config.js`:

```js
googleAnalyticsMeasurementId: "G-ABC123DE45"
```

When configured, the Google script is loaded only after the visitor chooses **Allow analytics**. The implementation records page views, sanitized referral/UTM attribution, release clicks, outbound domains, Share-button use, and the approximate geographic reporting provided by GA4. It does not read or send text entered in the layout demo. Advertising signals and ad personalization are disabled, and measurement cookies are limited to 90 days in the site code.

Use GA4's **Reports → Acquisition → Traffic acquisition** for standard source/medium and campaign reporting, and **Reports → User attributes → Demographic details** for approximate country/region reporting. To use the additional `landing_attribution` parameters in Explorations, register `traffic_source`, `traffic_medium`, `traffic_campaign`, `referrer_host`, and `landing_path` as event-scoped custom dimensions in GA4 Admin.

The Share button creates a URL containing:

```text
?utm_source=web_share&utm_medium=share&utm_campaign=site_share
```

Use equivalent tagged URLs for campaigns, for example:

```text
?utm_source=reddit&utm_medium=social&utm_campaign=launch
?utm_source=whatsapp&utm_medium=share&utm_campaign=launch
```

Messaging applications often remove the browser referrer. Those visits look like Direct traffic unless the shared URL contains UTM parameters.

Before enabling analytics, create the GA4 property, paste its Measurement ID, choose the desired data-retention period in GA4 Admin, and make sure the published privacy page and consent behavior meet the requirements that apply to your visitors.

## Production metadata

Canonical URLs, structured data, Open Graph metadata, `robots.txt`, `llms.txt`, and `sitemap.xml` use the official `https://klavrio.app/` origin. Keep these URLs synchronized if the production domain changes. The current social preview uses the Klavrio mark hosted on the official site; replace it with a dedicated 1200×630 image when available.
