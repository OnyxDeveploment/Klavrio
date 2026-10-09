(() => {
  "use strict";

  const config = window.KLAVRIO_SITE_CONFIG ?? {};
  const measurementId = String(config.googleAnalyticsMeasurementId ?? "").trim().toUpperCase();
  if (!/^G-[A-Z0-9]{6,20}$/.test(measurementId)) return;

  const consentKey = "klavrio-analytics-consent";
  let analyticsLoaded = false;
  let consentPanel;

  window.dataLayer = window.dataLayer || [];
  window.gtag = window.gtag || function gtag() { window.dataLayer.push(arguments); };
  window.gtag("consent", "default", {
    analytics_storage: "denied",
    ad_storage: "denied",
    ad_user_data: "denied",
    ad_personalization: "denied",
    wait_for_update: 500
  });

  function readConsent() {
    try {
      const value = localStorage.getItem(consentKey);
      return value === "granted" || value === "denied" ? value : null;
    } catch {
      return null;
    }
  }

  function saveConsent(value) {
    try { localStorage.setItem(consentKey, value); } catch { /* Consent still applies to this page view. */ }
  }

  function cleanValue(value, limit = 100) {
    return String(value ?? "").replace(/[\r\n]/g, " ").trim().slice(0, limit);
  }

  function referringHost() {
    if (!document.referrer) return "";
    try { return new URL(document.referrer).hostname.toLowerCase(); } catch { return ""; }
  }

  function attribution() {
    const params = new URLSearchParams(location.search);
    const campaignSource = cleanValue(params.get("utm_source"), 50);
    const campaignMedium = cleanValue(params.get("utm_medium"), 50);
    const campaignName = cleanValue(params.get("utm_campaign"), 100);
    const referrerHost = referringHost();

    if (campaignSource) {
      return {
        traffic_source: campaignSource,
        traffic_medium: campaignMedium || "campaign",
        traffic_campaign: campaignName || "(not set)",
        referrer_host: referrerHost || "(none)"
      };
    }

    if (!referrerHost) {
      return { traffic_source: "direct", traffic_medium: "none", traffic_campaign: "(not set)", referrer_host: "(none)" };
    }

    const searchEngines = ["google.", "bing.com", "duckduckgo.com", "yahoo.", "yandex.", "baidu.com"];
    const socialSites = ["facebook.com", "instagram.com", "linkedin.com", "reddit.com", "t.co", "twitter.com", "x.com", "youtube.com"];
    const source = searchEngines.some(domain => referrerHost.includes(domain))
      ? "organic_search"
      : socialSites.some(domain => referrerHost === domain || referrerHost.endsWith(`.${domain}`))
        ? "organic_social"
        : "referral";

    return {
      traffic_source: referrerHost,
      traffic_medium: source,
      traffic_campaign: "(not set)",
      referrer_host: referrerHost
    };
  }

  function analyticsPageUrl() {
    const url = new URL(location.href);
    for (const key of Array.from(url.searchParams.keys())) {
      if (!key.toLowerCase().startsWith("utm_")) url.searchParams.delete(key);
    }
    url.hash = "";
    return url.href;
  }

  function analyticsReferrer() {
    if (!document.referrer) return "";
    try { return new URL(document.referrer).origin; } catch { return ""; }
  }

  function track(eventName, parameters = {}) {
    if (!analyticsLoaded || readConsent() !== "granted") return;
    window.gtag("event", eventName, parameters);
  }

  function loadAnalytics() {
    if (analyticsLoaded) return;
    analyticsLoaded = true;

    window.gtag("consent", "update", { analytics_storage: "granted" });
    window.gtag("js", new Date());
    window.gtag("config", measurementId, {
      allow_google_signals: false,
      allow_ad_personalization_signals: false,
      cookie_expires: 60 * 60 * 24 * 90,
      cookie_flags: "SameSite=Lax;Secure",
      page_location: analyticsPageUrl(),
      page_referrer: analyticsReferrer()
    });
    window.gtag("event", "landing_attribution", {
      ...attribution(),
      landing_path: location.pathname
    });

    const script = document.createElement("script");
    script.async = true;
    script.src = `https://www.googletagmanager.com/gtag/js?id=${encodeURIComponent(measurementId)}`;
    script.referrerPolicy = "strict-origin-when-cross-origin";
    document.head.append(script);
  }

  function removeAnalyticsCookies() {
    const hostParts = location.hostname.split(".");
    const domains = [location.hostname];
    if (hostParts.length > 2) domains.push(`.${hostParts.slice(-2).join(".")}`);

    document.cookie.split(";").forEach(cookie => {
      const name = cookie.split("=")[0].trim();
      if (!name.startsWith("_ga")) return;
      for (const domain of domains) {
        document.cookie = `${name}=; Max-Age=0; path=/; domain=${domain}; SameSite=Lax`;
      }
      document.cookie = `${name}=; Max-Age=0; path=/; SameSite=Lax`;
    });
  }

  function setConsent(value) {
    saveConsent(value);
    hideConsentPanel();
    if (value === "granted") {
      loadAnalytics();
      return;
    }

    window.gtag("consent", "update", { analytics_storage: "denied" });
    removeAnalyticsCookies();
    if (analyticsLoaded) location.reload();
  }

  function buildConsentPanel() {
    const panel = document.createElement("aside");
    panel.className = "analytics-consent";
    panel.setAttribute("role", "dialog");
    panel.setAttribute("aria-labelledby", "analytics-consent-title");
    panel.hidden = true;
    panel.innerHTML = `
      <div>
        <strong id="analytics-consent-title">Help improve Klavrio</strong>
        <p>Allow privacy-conscious analytics to measure visits, referral sources, release clicks, and approximate country or region. Typed text is never collected.</p>
        <a href="privacy.html#website-analytics">Privacy details</a>
      </div>
      <div class="analytics-consent-actions">
        <button class="button button-primary" type="button" data-consent-allow>Allow analytics</button>
        <button class="button button-quiet" type="button" data-consent-deny>Not now</button>
      </div>`;
    panel.querySelector("[data-consent-allow]").addEventListener("click", () => setConsent("granted"));
    panel.querySelector("[data-consent-deny]").addEventListener("click", () => setConsent("denied"));
    document.body.append(panel);
    return panel;
  }

  function showConsentPanel() {
    consentPanel ??= buildConsentPanel();
    consentPanel.hidden = false;
    consentPanel.querySelector("[data-consent-allow]")?.focus();
  }

  function hideConsentPanel() {
    if (consentPanel) consentPanel.hidden = true;
  }

  document.querySelectorAll("[data-analytics-settings]").forEach(button => {
    button.hidden = false;
    button.addEventListener("click", showConsentPanel);
  });

  document.addEventListener("click", event => {
    const link = event.target.closest?.("a");
    if (!link) return;
    if (link.matches("[data-platform]")) {
      track("release_link_click", { platform: cleanValue(link.dataset.platform, 20) });
      return;
    }
    if (link.hostname && link.hostname !== location.hostname) {
      track("outbound_link_click", { link_domain: cleanValue(link.hostname, 100), link_path: cleanValue(link.pathname, 100) });
    }
  });
  document.addEventListener("klavrio:share", event => track("share", event.detail ?? {}));

  if (readConsent() === "granted") loadAnalytics();
  else if (readConsent() === null) showConsentPanel();
})();
