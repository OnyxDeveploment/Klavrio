(() => {
  "use strict";

  // Replace these values with final HTTPS release URLs when they are available.
  const releaseLinks = Object.freeze({
    windows: "",
    macos: ""
  });

  const physicalKeys = "qwertyuiopasdfghjklzxcvbnm,.;/[]'";
  const layoutKeys = Object.freeze({
    he: "/'קראטוןםפשדגכעיחלךזסבהנמצתץף.][,",
    ar: "ضصثقفغعهخحشسيبلاتنمئءؤرلاىوزكظجدط",
    ru: "йцукенгшщзфывапролдячсмитьбюж.хъэ",
    de: "qwertzuiopasdfghjklyxcvbnm,.ö-ü+ä",
    fr: "azertyuiopqsdfghjklwxcvbn,;:m!^$ù"
  });

  const page = document.documentElement;
  const header = document.querySelector("[data-header]");
  const themeButton = document.querySelector("[data-theme-toggle]");
  const menuButton = document.querySelector("[data-menu-toggle]");
  const menu = document.querySelector("[data-menu]");
  const input = document.querySelector("[data-layout-input]");
  const output = document.querySelector("[data-layout-output]");
  const selector = document.querySelector("[data-layout-select]");
  const feedback = document.querySelector("[data-feedback]");

  function preferredTheme() {
    try {
      const saved = localStorage.getItem("klavrio-site-theme");
      if (saved === "light" || saved === "dark") return saved;
    } catch {
      // Storage can be disabled without affecting the website.
    }

    return window.matchMedia("(prefers-color-scheme: light)").matches ? "light" : "dark";
  }

  function applyTheme(theme) {
    page.dataset.theme = theme;
    themeButton?.setAttribute("aria-label", theme === "dark" ? "Use light theme" : "Use dark theme");
    document.querySelector('meta[name="theme-color"]')?.setAttribute("content", theme === "dark" ? "#07111f" : "#f7f9fd");
  }

  function rememberTheme(theme) {
    try {
      localStorage.setItem("klavrio-site-theme", theme);
    } catch {
      // A private browsing policy may reject storage. The theme still works for this page view.
    }
  }

  function translatePhysicalKeys(value, target) {
    const targetKeys = layoutKeys[target];
    if (!targetKeys) return value;

    const positions = new Map(Array.from(physicalKeys, (key, index) => [key, index]));
    return Array.from(value, character => {
      const lower = character.toLowerCase();
      const position = positions.get(lower);
      if (position === undefined) return character;

      const replacement = targetKeys[position];
      const isUpperLatin = character !== lower && /[a-z]/i.test(character);
      return isUpperLatin && /[a-z]/i.test(replacement) ? replacement.toUpperCase() : replacement;
    }).join("");
  }

  function refreshLayoutPreview() {
    if (!input || !output || !selector) return;
    output.textContent = translatePhysicalKeys(input.value, selector.value) || "Your converted text will appear here.";
    feedback.textContent = "Converted in this browser";
    window.setTimeout(() => {
      if (feedback.textContent === "Converted in this browser") feedback.textContent = "";
    }, 2200);
  }

  function chooseSample(button) {
    if (!input || !selector) return;
    input.value = button.dataset.sample ?? "";
    selector.value = button.dataset.target ?? "he";
    refreshLayoutPreview();
    input.focus();
  }

  async function copyPreview() {
    const value = output?.textContent?.trim();
    if (!value) return;

    try {
      await navigator.clipboard.writeText(value);
      feedback.textContent = "Copied";
    } catch {
      feedback.textContent = "Select the result to copy it";
      output?.focus();
    }
  }

  function configureReleaseCards() {
    document.querySelectorAll("[data-platform]").forEach(card => {
      const platform = card.dataset.platform;
      const configured = releaseLinks[platform];
      if (!configured) return;

      card.href = configured;
      card.querySelector(".platform-action").textContent = "Download →";
      card.setAttribute("rel", "noopener");
    });
  }

  function observeSections() {
    const items = document.querySelectorAll("[data-reveal]");
    if (!("IntersectionObserver" in window)) {
      items.forEach(item => item.classList.add("is-visible"));
      return;
    }

    const observer = new IntersectionObserver(entries => {
      entries.forEach(entry => {
        if (!entry.isIntersecting) return;
        entry.target.classList.add("is-visible");
        observer.unobserve(entry.target);
      });
    }, { threshold: 0.12, rootMargin: "0px 0px -40px" });

    items.forEach(item => observer.observe(item));
  }

  function collapseMenu() {
    menu?.classList.remove("is-open");
    menuButton?.setAttribute("aria-expanded", "false");
  }

  applyTheme(preferredTheme());
  configureReleaseCards();
  observeSections();
  refreshLayoutPreview();

  themeButton?.addEventListener("click", () => {
    const next = page.dataset.theme === "dark" ? "light" : "dark";
    applyTheme(next);
    rememberTheme(next);
  });

  menuButton?.addEventListener("click", () => {
    const expanded = menuButton.getAttribute("aria-expanded") === "true";
    menuButton.setAttribute("aria-expanded", String(!expanded));
    menu?.classList.toggle("is-open", !expanded);
  });

  menu?.querySelectorAll("a").forEach(link => link.addEventListener("click", collapseMenu));
  document.querySelector("[data-convert]")?.addEventListener("click", refreshLayoutPreview);
  document.querySelector("[data-copy]")?.addEventListener("click", copyPreview);
  document.querySelectorAll("[data-sample]").forEach(button => button.addEventListener("click", () => chooseSample(button)));
  selector?.addEventListener("change", refreshLayoutPreview);
  input?.addEventListener("keydown", event => {
    if ((event.ctrlKey || event.metaKey) && event.key === "Enter") refreshLayoutPreview();
  });

  window.addEventListener("scroll", () => header?.classList.toggle("is-scrolled", window.scrollY > 18), { passive: true });
  window.addEventListener("resize", () => {
    if (window.innerWidth > 760) collapseMenu();
  });
})();
