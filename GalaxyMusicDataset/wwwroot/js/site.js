(() => {
  const key = "galaxy-theme";
  const allowed = new Set(["dark", "light", "maloja"]);
  const root = document.documentElement;

  const normalize = (value) => {
    const theme = (value || "").trim().toLowerCase();
    return allowed.has(theme) ? theme : "dark";
  };

  const serverDefault = () => {
    const meta = document.querySelector('meta[name="galaxy-default-theme"]');
    return normalize(meta?.getAttribute("content") || root.getAttribute("data-bs-theme"));
  };

  const apply = (theme) => {
    const next = normalize(theme);
    root.setAttribute("data-bs-theme", next);
    try {
      localStorage.setItem(key, next);
    } catch {
      /* ignore */
    }

    for (const select of document.querySelectorAll("[data-theme-select]")) {
      select.value = next;
    }
  };

  let initial = serverDefault();
  try {
    const saved = localStorage.getItem(key);
    if (allowed.has(saved)) {
      initial = saved;
    }
  } catch {
    /* ignore */
  }

  apply(initial);

  for (const select of document.querySelectorAll("[data-theme-select]")) {
    select.addEventListener("change", () => apply(select.value));
  }
})();
