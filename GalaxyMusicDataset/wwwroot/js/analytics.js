(() => {
  const charts = document.querySelectorAll("[data-chart]");
  if (!charts.length || typeof Chart === "undefined") {
    return;
  }

  const axisColor = "#6c757d";
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
            borderColor: "#1b6ec2",
            backgroundColor: "rgba(27, 110, 194, 0.15)",
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
              backgroundColor: "#1b6ec2"
            },
            {
              label: "Minutes",
              data: payload.map((p) => p.minutes),
              backgroundColor: "rgba(27, 110, 194, 0.35)"
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
    }
  }
})();
