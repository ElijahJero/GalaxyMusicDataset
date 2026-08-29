(() => {
  const charts = document.querySelectorAll("[data-chart]");
  if (!charts.length || typeof Chart === "undefined") {
    return;
  }

  const styles = getComputedStyle(document.documentElement);
  const accent = (styles.getPropertyValue("--galaxy-accent") || "#5b9fd4").trim();
  const accentStrong = (styles.getPropertyValue("--galaxy-accent-strong") || "#1b6ec2").trim();
  const axisColor = (styles.getPropertyValue("--bs-secondary-color") || "#6c757d").trim();
  const genrePalette = [
    accentStrong,
    accent,
    "#c084fc",
    "#34d399",
    "#f59e0b",
    "#f472b6",
    "#22d3ee",
    "#fb7185"
  ];

  Chart.defaults.font.family = "inherit";
  Chart.defaults.color = axisColor;

  for (const canvas of charts) {
    let payload = [];
    try {
      payload = JSON.parse(canvas.getAttribute("data-payload") || "[]");
    } catch {
      continue;
    }

    const kind = canvas.getAttribute("data-chart");
    if (kind === "sparkline") {
      new Chart(canvas, {
        type: "line",
        data: {
          labels: payload.map((p) => p.day),
          datasets: [{
            data: payload.map((p) => p.count),
            borderColor: accentStrong,
            backgroundColor: "color-mix(in srgb, " + accentStrong + " 25%, transparent)",
            fill: true,
            tension: 0.3,
            pointRadius: payload.length > 40 ? 0 : 2,
            borderWidth: 2
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          plugins: { legend: { display: false } },
          scales: {
            x: { display: payload.length < 20, ticks: { maxRotation: 0 } },
            y: { beginAtZero: true, ticks: { precision: 0 } }
          }
        }
      });
    } else if (kind === "monthly") {
      new Chart(canvas, {
        type: "bar",
        data: {
          labels: payload.map((p) => p.label),
          datasets: [
            {
              label: "Scrobbles",
              data: payload.map((p) => p.count),
              backgroundColor: accentStrong
            },
            {
              label: "Minutes",
              data: payload.map((p) => p.minutes),
              backgroundColor: accent
            }
          ]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          plugins: { legend: { position: "bottom" } },
          scales: { y: { beginAtZero: true, ticks: { precision: 0 } } }
        }
      });
    } else if (kind === "genres") {
      new Chart(canvas, {
        type: "doughnut",
        data: {
          labels: payload.map((p) => p.label),
          datasets: [{
            data: payload.map((p) => p.count),
            backgroundColor: payload.map((_, i) => genrePalette[i % genrePalette.length]),
            borderWidth: 0
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          plugins: {
            legend: { position: "bottom" }
          }
        }
      });
    }
  }
})();
