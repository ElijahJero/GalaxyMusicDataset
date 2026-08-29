(() => {
  const key = "galaxy-theme";
  const root = document.documentElement;

  const apply = (theme) => {
    root.setAttribute("data-bs-theme", theme);
    try {
      localStorage.setItem(key, theme);
    } catch {
      /* ignore */
    }

    for (const button of document.querySelectorAll("[data-theme-toggle]")) {
      const dark = theme === "dark";
      button.setAttribute("aria-pressed", dark ? "true" : "false");
      button.textContent = dark ? "Light" : "Dark";
    }
  };

  apply(root.getAttribute("data-bs-theme") || "dark");
  for (const button of document.querySelectorAll("[data-theme-toggle]")) {
    button.addEventListener("click", () => {
      const next = root.getAttribute("data-bs-theme") === "dark" ? "light" : "dark";
      apply(next);
    });
  }
})();
