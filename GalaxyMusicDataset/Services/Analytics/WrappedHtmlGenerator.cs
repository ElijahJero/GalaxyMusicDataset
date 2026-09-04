using System.Globalization;
using System.Net;
using System.Text;

namespace GalaxyMusicDataset.Services.Analytics;

/// <summary>Builds a self-contained, click-through Wrapped HTML document for download.</summary>
public static class WrappedHtmlGenerator
{
    public static string Generate(WrappedHtmlExport data)
    {
        var sb = new StringBuilder(48_000);
        var year = data.Year.ToString(CultureInfo.InvariantCulture);
        var who = string.IsNullOrWhiteSpace(data.ListenerName) ? "Your" : Escape(data.ListenerName) + "'s";
        var minutes = (data.Overview.ListeningTimeMs / 60_000L).ToString("N0", CultureInfo.InvariantCulture);
        var hours = (data.Overview.ListeningTimeMs / 3_600_000.0).ToString("0.#", CultureInfo.InvariantCulture);
        var scrobbles = AnalyticsDisplay.Count(data.Overview.ScrobbleCount);
        var uniqueArtists = AnalyticsDisplay.Count(data.Overview.UniqueArtists);
        var uniqueTracks = AnalyticsDisplay.Count(data.Overview.UniqueTracks);

        sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<title>").Append(Escape(who)).Append(' ').Append(year).Append(" Wrapped</title>\n");
        sb.Append("""
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Syne:wght@600;700;800&family=Outfit:wght@400;500;600;700&display=swap" rel="stylesheet">
<style>
:root {
  --ink: #0b1210;
  --paper: #f4f7f2;
  --muted: rgba(244,247,242,.72);
}
* { box-sizing: border-box; margin: 0; padding: 0; }
html, body { height: 100%; }
body {
  font-family: Outfit, system-ui, sans-serif;
  background: #050807;
  color: var(--paper);
  overflow: hidden;
  -webkit-font-smoothing: antialiased;
}
.deck { position: relative; width: 100%; height: 100%; }
.slide {
  position: absolute; inset: 0;
  display: none;
  flex-direction: column;
  justify-content: center;
  padding: clamp(1.25rem, 4vw, 3.5rem);
  overflow: hidden;
  animation: none;
}
.slide.active { display: flex; animation: rise .45s ease both; }
@keyframes rise {
  from { opacity: 0; transform: translateY(18px) scale(.985); }
  to { opacity: 1; transform: none; }
}
.slide::before {
  content: "";
  position: absolute; inset: -20%;
  background:
    radial-gradient(ellipse at 20% 15%, rgba(255,255,255,.14), transparent 45%),
    radial-gradient(ellipse at 85% 80%, rgba(0,0,0,.35), transparent 50%);
  pointer-events: none;
}
.slide > * { position: relative; z-index: 1; max-width: 52rem; }
.t0 { background: linear-gradient(145deg, #0f3d32 0%, #1a6b52 42%, #c4f06a 140%); }
.t1 { background: linear-gradient(160deg, #1b2740 0%, #2f5d8c 48%, #f0b35a 130%); }
.t2 { background: linear-gradient(150deg, #3a1424 0%, #a13a4a 50%, #ffd2a1 125%); }
.t3 { background: linear-gradient(155deg, #14261f 0%, #2d6a4f 45%, #8fd3b0 120%); }
.t4 { background: linear-gradient(145deg, #1a2830 0%, #3d6e7a 48%, #c8e8d8 125%); }
.t5 { background: linear-gradient(160deg, #102830 0%, #1f6f7a 50%, #d7f6ff 125%); }
.t6 { background: linear-gradient(150deg, #2a1a0c 0%, #b45d1f 48%, #ffe0a8 125%); }
.t7 { background: linear-gradient(155deg, #0d1f2d 0%, #245b8a 50%, #9ad7ff 125%); }
.t8 { background: linear-gradient(145deg, #1a2a18 0%, #4a7c3f 48%, #d9f2a8 125%); }
.eyebrow {
  font-size: .85rem; letter-spacing: .18em; text-transform: uppercase;
  opacity: .8; margin-bottom: .75rem; font-weight: 600;
}
h1, h2 {
  font-family: Syne, Outfit, sans-serif;
  font-weight: 800; line-height: .95; letter-spacing: -.03em;
}
h1 { font-size: clamp(3rem, 11vw, 6.5rem); margin-bottom: 1rem; }
h2 { font-size: clamp(2rem, 7vw, 4rem); margin-bottom: 1.25rem; }
.lead { font-size: clamp(1.05rem, 2.4vw, 1.35rem); opacity: .92; max-width: 36rem; }
.stat {
  font-family: Syne, Outfit, sans-serif;
  font-size: clamp(3.2rem, 14vw, 7.5rem);
  font-weight: 800; line-height: .9; letter-spacing: -.04em;
}
.stat-label { margin-top: .6rem; font-size: 1.15rem; opacity: .85; }
.substats { display: flex; flex-wrap: wrap; gap: 1.5rem 2.5rem; margin-top: 2rem; }
.substat strong {
  display: block; font-family: Syne, Outfit, sans-serif;
  font-size: clamp(1.6rem, 4vw, 2.4rem); font-weight: 700;
}
.substat span { opacity: .75; font-size: .95rem; }
.rank-list { list-style: none; display: grid; gap: .85rem; margin-top: .25rem; width: min(100%, 36rem); }
.rank-item {
  display: grid; grid-template-columns: 2.2rem 4.25rem 1fr auto;
  gap: .85rem; align-items: center;
}
.rank-n {
  font-family: Syne, Outfit, sans-serif; font-weight: 800;
  font-size: 1.35rem; opacity: .7;
}
.art, .art-fallback {
  width: 4.25rem; height: 4.25rem; border-radius: .55rem;
  object-fit: cover; background: rgba(0,0,0,.25);
  box-shadow: 0 10px 28px rgba(0,0,0,.28);
}
.art-fallback {
  display: grid; place-items: center;
  font-family: Syne, Outfit, sans-serif; font-weight: 700; font-size: 1.25rem;
  background: rgba(255,255,255,.12);
}
.meta .name { font-weight: 600; font-size: 1.05rem; }
.meta .sub { opacity: .72; font-size: .9rem; margin-top: .1rem; }
.plays { font-variant-numeric: tabular-nums; opacity: .8; font-size: .95rem; white-space: nowrap; }
.hero-art {
  width: min(42vw, 16rem); height: min(42vw, 16rem);
  border-radius: 1rem; object-fit: cover; margin-bottom: 1.25rem;
  box-shadow: 0 22px 50px rgba(0,0,0,.35);
}
.genre-stack { display: flex; flex-wrap: wrap; gap: .65rem; margin-top: .5rem; }
.genre {
  font-family: Syne, Outfit, sans-serif; font-weight: 700;
  font-size: clamp(1.1rem, 3.5vw, 1.85rem);
  padding: .45rem .9rem; border-radius: .55rem;
  background: rgba(0,0,0,.22); backdrop-filter: blur(6px);
}
.nav {
  position: fixed; left: 0; right: 0; bottom: 0; z-index: 5;
  display: flex; align-items: center; justify-content: space-between;
  gap: 1rem; padding: 1rem clamp(1rem, 3vw, 2rem) 1.25rem;
  background: linear-gradient(transparent, rgba(0,0,0,.45));
  pointer-events: none;
}
.nav button, .dots { pointer-events: auto; }
.nav button {
  appearance: none; border: 0; cursor: pointer;
  font-family: Outfit, system-ui, sans-serif; font-weight: 600;
  padding: .7rem 1.1rem; border-radius: 999px;
  background: rgba(255,255,255,.16); color: #fff;
  backdrop-filter: blur(8px);
}
.nav button:disabled { opacity: .35; cursor: default; }
.dots { display: flex; gap: .4rem; flex-wrap: wrap; justify-content: center; }
.dot {
  width: .55rem; height: .55rem; border-radius: 999px;
  background: rgba(255,255,255,.35); border: 0; padding: 0; cursor: pointer;
}
.dot.on { background: #fff; transform: scale(1.15); }
.hint { position: fixed; top: 1rem; right: 1rem; z-index: 5; font-size: .8rem; opacity: .65; }
.empty { opacity: .8; font-size: 1.1rem; }
@media (max-width: 640px) {
  .rank-item { grid-template-columns: 1.6rem 3.4rem 1fr; }
  .plays { grid-column: 3; justify-self: start; }
  .art, .art-fallback { width: 3.4rem; height: 3.4rem; }
}
</style>
</head>
<body>
""");

        sb.Append("<div class=\"hint\">Click, tap, or use ← →</div>\n");
        sb.Append("<div class=\"deck\" id=\"deck\">\n");

        // 0 — title
        SlideOpen(sb, 0, "t0", active: true);
        sb.Append("<div class=\"eyebrow\">Galaxy Music Dataset</div>\n");
        sb.Append("<h1>").Append(who).Append("<br>").Append(year).Append(" Wrapped</h1>\n");
        sb.Append("<p class=\"lead\">A click-through year in review — tops, minutes, and the tracks that stuck.</p>\n");
        SlideClose(sb);

        // 1 — listening volume
        SlideOpen(sb, 1, "t1");
        sb.Append("<div class=\"eyebrow\">Listening time</div>\n");
        sb.Append("<div class=\"stat\">").Append(Escape(minutes)).Append("</div>\n");
        sb.Append("<div class=\"stat-label\">minutes · about ").Append(Escape(hours)).Append(" hours</div>\n");
        sb.Append("<div class=\"substats\">\n");
        SubStat(sb, scrobbles, "scrobbles");
        SubStat(sb, uniqueArtists, "artists");
        SubStat(sb, uniqueTracks, "tracks");
        sb.Append("</div>\n");
        SlideClose(sb);

        // 2 — top artists
        SlideOpen(sb, 2, "t2");
        sb.Append("<div class=\"eyebrow\">Top artists</div>\n");
        sb.Append("<h2>Your top 5 artists</h2>\n");
        RankList(sb, data.TopArtists);
        SlideClose(sb);

        // 3 — top tracks
        SlideOpen(sb, 3, "t3");
        sb.Append("<div class=\"eyebrow\">Top tracks</div>\n");
        sb.Append("<h2>Your top 5 tracks</h2>\n");
        RankList(sb, data.TopTracks);
        SlideClose(sb);

        // 4 — top albums
        SlideOpen(sb, 4, "t4");
        sb.Append("<div class=\"eyebrow\">Top albums</div>\n");
        sb.Append("<h2>Your top 5 albums</h2>\n");
        RankList(sb, data.TopAlbums);
        SlideClose(sb);

        // 5 — genres
        SlideOpen(sb, 5, "t5");
        sb.Append("<div class=\"eyebrow\">Sound</div>\n");
        sb.Append("<h2>Your top genres</h2>\n");
        if (data.TopGenres.Count == 0)
        {
            sb.Append("<p class=\"empty\">No genre tags for this year yet.</p>\n");
        }
        else
        {
            sb.Append("<div class=\"genre-stack\">\n");
            foreach (var g in data.TopGenres)
            {
                sb.Append("<span class=\"genre\">").Append(Escape(g.Name)).Append("</span>\n");
            }
            sb.Append("</div>\n");
        }
        SlideClose(sb);

        // 6 — most replayed
        SlideOpen(sb, 6, "t6");
        sb.Append("<div class=\"eyebrow\">On repeat</div>\n");
        if (data.MostReplayed is null)
        {
            sb.Append("<h2>No replay champion</h2>\n");
            sb.Append("<p class=\"empty\">Not enough plays to crown a track.</p>\n");
        }
        else
        {
            Art(sb, data.MostReplayed.ImageUrl, data.MostReplayed.Name, "hero-art");
            sb.Append("<h2>").Append(Escape(data.MostReplayed.Name)).Append("</h2>\n");
            if (!string.IsNullOrWhiteSpace(data.MostReplayed.Subtitle))
            {
                sb.Append("<p class=\"lead\">").Append(Escape(data.MostReplayed.Subtitle)).Append("</p>\n");
            }
            sb.Append("<div class=\"stat-label\" style=\"margin-top:1rem\">")
                .Append(AnalyticsDisplay.Count(data.MostReplayed.Plays))
                .Append(" plays — your most replayed track</div>\n");
        }
        SlideClose(sb);

        // 7 — streak + busiest hour
        SlideOpen(sb, 7, "t7");
        sb.Append("<div class=\"eyebrow\">Habits</div>\n");
        sb.Append("<h2>How you listened</h2>\n");
        sb.Append("<div class=\"substats\">\n");
        SubStat(sb, AnalyticsDisplay.Count(data.LongestStreak), "day streak");
        SubStat(sb, data.BusiestHourUtc.ToString("00", CultureInfo.InvariantCulture) + ":00 UTC", "busiest hour");
        SubStat(sb, AnalyticsDisplay.Count(data.BusiestHourCount), "plays that hour");
        SubStat(sb, AnalyticsDisplay.Count(data.Overview.DistinctDaysInRange), "active days");
        sb.Append("</div>\n");
        SlideClose(sb);

        // 8 — discoveries
        SlideOpen(sb, 8, "t8");
        sb.Append("<div class=\"eyebrow\">New to you</div>\n");
        sb.Append("<h2>First-heards &amp; new artists</h2>\n");
        if (data.NewArtists.Count == 0 && data.Discoveries.Count == 0)
        {
            sb.Append("<p class=\"empty\">No first-time finds this year.</p>\n");
        }
        else
        {
            if (data.NewArtists.Count > 0)
            {
                sb.Append("<p class=\"lead\" style=\"margin-bottom:.85rem\">New artists</p>\n");
                RankList(sb, data.NewArtists);
            }
            if (data.Discoveries.Count > 0)
            {
                sb.Append("<p class=\"lead\" style=\"margin:1.4rem 0 .85rem\">First-heard tracks</p>\n");
                sb.Append("<ul class=\"rank-list\">\n");
                var i = 1;
                foreach (var d in data.Discoveries)
                {
                    sb.Append("<li class=\"rank-item\">");
                    sb.Append("<div class=\"rank-n\">").Append(i.ToString(CultureInfo.InvariantCulture)).Append("</div>");
                    FallbackArt(sb, d.Name);
                    sb.Append("<div class=\"meta\"><div class=\"name\">").Append(Escape(d.Name)).Append("</div>");
                    if (!string.IsNullOrWhiteSpace(d.Subtitle))
                    {
                        sb.Append("<div class=\"sub\">").Append(Escape(d.Subtitle)).Append("</div>");
                    }
                    sb.Append("</div>");
                    sb.Append("<div class=\"plays\">").Append(AnalyticsDisplay.Count(d.PlaysInRange)).Append(" plays</div>");
                    sb.Append("</li>\n");
                    i++;
                }
                sb.Append("</ul>\n");
            }
        }
        SlideClose(sb);

        // 9 — close
        SlideOpen(sb, 9, "t0");
        sb.Append("<div class=\"eyebrow\">That's a wrap</div>\n");
        sb.Append("<h1>See you<br>next year</h1>\n");
        sb.Append("<p class=\"lead\">Generated from your Galaxy Music Dataset warehouse — offline analytics, shareable vibes.</p>\n");
        SlideClose(sb);

        sb.Append("</div>\n"); // deck

        sb.Append("""
<div class="nav">
  <button type="button" id="prev" aria-label="Previous">← Back</button>
  <div class="dots" id="dots"></div>
  <button type="button" id="next" aria-label="Next">Next →</button>
</div>
<script>
(function () {
  const slides = Array.from(document.querySelectorAll('.slide'));
  const dots = document.getElementById('dots');
  const prev = document.getElementById('prev');
  const next = document.getElementById('next');
  let i = 0;
  slides.forEach((_, idx) => {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'dot' + (idx === 0 ? ' on' : '');
    b.setAttribute('aria-label', 'Slide ' + (idx + 1));
    b.addEventListener('click', () => go(idx));
    dots.appendChild(b);
  });
  function go(n) {
    i = Math.max(0, Math.min(slides.length - 1, n));
    slides.forEach((s, idx) => s.classList.toggle('active', idx === i));
    Array.from(dots.children).forEach((d, idx) => d.classList.toggle('on', idx === i));
    prev.disabled = i === 0;
    next.disabled = i === slides.length - 1;
    next.textContent = i === slides.length - 1 ? 'Done' : 'Next →';
  }
  function step(d) { go(i + d); }
  prev.addEventListener('click', e => { e.stopPropagation(); step(-1); });
  next.addEventListener('click', e => { e.stopPropagation(); if (i < slides.length - 1) step(1); });
  document.addEventListener('keydown', e => {
    if (e.key === 'ArrowRight' || e.key === ' ' || e.key === 'Enter') { e.preventDefault(); step(1); }
    if (e.key === 'ArrowLeft') { e.preventDefault(); step(-1); }
  });
  document.getElementById('deck').addEventListener('click', e => {
    if (e.target.closest('button')) return;
    step(1);
  });
  go(0);
})();
</script>
</body>
</html>
""");

        return sb.ToString();
    }

    private static void SlideOpen(StringBuilder sb, int index, string theme, bool active = false)
    {
        sb.Append("<section class=\"slide ").Append(theme);
        if (active)
        {
            sb.Append(" active");
        }
        sb.Append("\" data-i=\"").Append(index.ToString(CultureInfo.InvariantCulture)).Append("\">\n");
    }

    private static void SlideClose(StringBuilder sb) => sb.Append("</section>\n");

    private static void SubStat(StringBuilder sb, string value, string label)
    {
        sb.Append("<div class=\"substat\"><strong>").Append(Escape(value))
            .Append("</strong><span>").Append(Escape(label)).Append("</span></div>\n");
    }

    private static void RankList(StringBuilder sb, IReadOnlyList<WrappedMediaItem> items)
    {
        if (items.Count == 0)
        {
            sb.Append("<p class=\"empty\">Nothing ranked here yet.</p>\n");
            return;
        }

        sb.Append("<ul class=\"rank-list\">\n");
        foreach (var item in items)
        {
            sb.Append("<li class=\"rank-item\">");
            sb.Append("<div class=\"rank-n\">").Append(item.Rank.ToString(CultureInfo.InvariantCulture)).Append("</div>");
            Art(sb, item.ImageUrl, item.Name, "art");
            sb.Append("<div class=\"meta\"><div class=\"name\">").Append(Escape(item.Name)).Append("</div>");
            if (!string.IsNullOrWhiteSpace(item.Subtitle))
            {
                sb.Append("<div class=\"sub\">").Append(Escape(item.Subtitle)).Append("</div>");
            }
            sb.Append("</div>");
            sb.Append("<div class=\"plays\">").Append(AnalyticsDisplay.Count(item.Plays)).Append(" plays</div>");
            sb.Append("</li>\n");
        }
        sb.Append("</ul>\n");
    }

    private static void Art(StringBuilder sb, string? url, string name, string cssClass)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            FallbackArt(sb, name, cssClass == "hero-art" ? "hero-art art-fallback" : "art-fallback");
            return;
        }

        sb.Append("<img class=\"").Append(cssClass).Append("\" src=\"").Append(EscapeAttr(url))
            .Append("\" alt=\"").Append(EscapeAttr(name)).Append("\" loading=\"lazy\">");
    }

    private static void FallbackArt(StringBuilder sb, string name, string cssClass = "art-fallback")
    {
        var letter = string.IsNullOrWhiteSpace(name) ? "?" : char.ToUpperInvariant(name.Trim()[0]).ToString();
        sb.Append("<div class=\"").Append(cssClass).Append("\" aria-hidden=\"true\">")
            .Append(Escape(letter)).Append("</div>");
    }

    private static string Escape(string? value) => WebUtility.HtmlEncode(value ?? "");

    private static string EscapeAttr(string? value) => WebUtility.HtmlEncode(value ?? "").Replace("'", "&#39;", StringComparison.Ordinal);
}
