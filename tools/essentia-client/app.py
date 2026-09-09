"""Download a song with yt-dlp and tag it with the Essentia models."""

from __future__ import annotations

import os
import queue
import re
import shutil
import subprocess
import sys
import threading
from pathlib import Path
from tkinter import filedialog, messagebox
import tkinter as tk
from tkinter import ttk

from tag_library import essentia_available, tag_file, to_galaxy_payload, write_tags
from galaxy_client import GalaxyClient, GalaxyError, load_config, save_config

ROOT = Path(__file__).resolve().parent
DEFAULT_OUT = ROOT / "downloads"
YTDLP_FORMAT = "bestaudio[abr<=160]/bestaudio"
OUTPUT_TEMPLATE = "%(title)s.%(ext)s"

URL_RE = re.compile(
    r"(?i)^(https?://|www\.)|(youtube\.com|youtu\.be|music\.youtube\.com|soundcloud\.com|bandcamp\.com)/"
)


def looks_like_url(text: str) -> bool:
    return bool(URL_RE.search(text.strip()))


def resolve_target(query: str) -> str:
    query = query.strip()
    if looks_like_url(query):
        if query.startswith("www."):
            return "https://" + query
        return query
    return f"ytsearch1:{query}"


def _which_native(*names: str) -> str | None:
    """Find a binary this OS can execute. Windows .exe files do not run inside WSL."""
    bindir = Path(sys.executable).resolve().parent
    for name in names:
        if sys.platform != "win32" and name.lower().endswith(".exe"):
            continue
        path = shutil.which(name)
        if path:
            return path
        candidate = bindir / name
        if candidate.is_file() and os.access(candidate, os.X_OK):
            return str(candidate)
    return None


def find_yt_dlp() -> list[str]:
    try:
        import yt_dlp  # noqa: F401

        return [sys.executable, "-m", "yt_dlp"]
    except ImportError:
        path = _which_native("yt-dlp")
        if not path:
            raise FileNotFoundError(
                "yt-dlp was not found. In your venv run: pip install -U 'yt-dlp[default]'"
            )
        return [path]


def find_ffmpeg() -> str:
    path = _which_native("ffmpeg")
    if path:
        return path
    if sys.platform == "win32":
        raise FileNotFoundError("ffmpeg not found. Install ffmpeg and add it to PATH.")
    raise FileNotFoundError(
        "ffmpeg not found in WSL. Windows ffmpeg.exe cannot run here. Install Linux packages:\n"
        "  sudo apt update && sudo apt install ffmpeg nodejs"
    )


def js_runtime_args() -> list[str]:
    for name in ("node", "deno", "bun"):
        path = _which_native(name)
        if path:
            return ["--js-runtimes", f"{name}:{path}"]
    return []


def check_download_deps() -> None:
    find_ffmpeg()
    if not js_runtime_args():
        raise FileNotFoundError(
            "No JavaScript runtime found. YouTube needs Node (or Deno) inside WSL, not Windows node.exe:\n"
            "  sudo apt update && sudo apt install ffmpeg nodejs\n"
            "Then in the venv: pip install -U 'yt-dlp[default]'"
        )


def download_audio(query: str, out_dir: Path, on_line) -> dict:
    """Download best audio at <=160 kbps and convert to mp3. Returns metadata."""
    check_download_deps()
    out_dir.mkdir(parents=True, exist_ok=True)
    target = resolve_target(query)
    yt_dlp = find_yt_dlp()
    ffmpeg = find_ffmpeg()

    before = {p.resolve() for p in out_dir.glob("*.mp3")}
    cmd = [
        *yt_dlp,
        "--ignore-config",
        "--no-playlist",
        "--windows-filenames",
        "--ffmpeg-location",
        ffmpeg,
        *js_runtime_args(),
        "--extractor-args",
        "youtube:player_client=web_embedded,default,-android_vr",
        "-f",
        YTDLP_FORMAT,
        "-x",
        "--audio-format",
        "mp3",
        "--audio-quality",
        "160K",
        "-o",
        str(out_dir / OUTPUT_TEMPLATE),
        "--embed-metadata",
        "--newline",
        "--print",
        "video:TITLE:%(title)s",
        "--print",
        "video:ARTIST:%(artist,uploader)s",
        "--print",
        "after_move:SAVED:%(filepath)s",
        target,
    ]

    popen_kwargs = {
        "stdout": subprocess.PIPE,
        "stderr": subprocess.STDOUT,
        "text": True,
        "bufsize": 1,
        "encoding": "utf-8",
        "errors": "replace",
    }
    if sys.platform == "win32":
        popen_kwargs["creationflags"] = subprocess.CREATE_NO_WINDOW

    on_line(f"Running yt-dlp ({'search' if target.startswith('ytsearch') else 'url'})...")
    proc = subprocess.Popen(cmd, **popen_kwargs)

    title = None
    artist = None
    saved = None
    for raw in proc.stdout:
        line = raw.rstrip()
        if not line:
            continue
        if line.startswith("TITLE:"):
            title = line[6:].strip() or title
            on_line(f"Found: {title}")
        elif line.startswith("ARTIST:"):
            artist = line[7:].strip() or artist
            if artist and artist != "NA":
                on_line(f"Artist: {artist}")
            else:
                artist = None
        elif line.startswith("SAVED:"):
            saved = Path(line[6:].strip())
            on_line(f"Saved: {saved.name}")
        elif line.startswith("[download]") and "%" in line:
            on_line(line, progress=True)
        elif line.startswith("[ExtractAudio]") or line.startswith("[Metadata]"):
            on_line(line)
        elif line.startswith("ERROR:") or line.startswith("WARNING:"):
            on_line(line)

    code = proc.wait()
    if code != 0:
        raise RuntimeError(f"yt-dlp exited with code {code}")

    if saved is None or not saved.exists():
        after = {p.resolve() for p in out_dir.glob("*.mp3")}
        created = after - before
        if created:
            saved = max(created, key=lambda p: p.stat().st_mtime)
        else:
            raise FileNotFoundError("Download finished but no mp3 file was found.")

    return {
        "path": saved,
        "title": title or saved.stem,
        "artist": artist,
    }


def tag_download(info: dict) -> dict:
    return tag_file(info["path"], title=info.get("title"), artist=info.get("artist"))


class DownloaderApp:
    def __init__(self, root: tk.Tk):
        self.root = root
        self.root.title("Download & Tag")
        self.root.minsize(560, 420)
        self.root.geometry("640x500")
        self.out_dir = tk.StringVar(value=str(DEFAULT_OUT))
        cfg = load_config()
        self.galaxy_url = tk.StringVar(value=cfg.get("url") or "http://localhost:5107")
        self.galaxy_key = tk.StringVar(value=cfg.get("apiKey") or "")
        self.query = tk.StringVar()
        self.status = tk.StringVar(
            value="Paste a URL or type a song name."
            if essentia_available()
            else "Paste a URL or type a song name. Genre/mood tagging needs Essentia."
        )
        self.genre_var = tk.StringVar(value="Genre: —")
        self.mood_var = tk.StringVar(value="Mood: —")
        self.profile_var = tk.StringVar(value="")
        self.busy = False
        self.messages: queue.Queue[tuple] = queue.Queue()

        self._build()
        self.root.after(80, self._drain_queue)
        self.entry.focus_set()

    def _build(self):
        pad = {"padx": 14, "pady": 6}
        main = ttk.Frame(self.root, padding=12)
        main.pack(fill=tk.BOTH, expand=True)

        ttk.Label(main, text="Song name or URL").pack(anchor="w", **pad)
        row = ttk.Frame(main)
        row.pack(fill=tk.X, padx=14)
        self.entry = ttk.Entry(row, textvariable=self.query, font=("Segoe UI", 11))
        self.entry.pack(side=tk.LEFT, fill=tk.X, expand=True)
        self.entry.bind("<Return>", lambda _e: self.start())
        self.go_btn = ttk.Button(row, text="Download & Tag", command=self.start)
        self.go_btn.pack(side=tk.LEFT, padx=(8, 0))

        dest = ttk.Frame(main)
        dest.pack(fill=tk.X, padx=14, pady=(10, 4))
        ttk.Label(dest, text="Save to").pack(side=tk.LEFT)
        ttk.Entry(dest, textvariable=self.out_dir).pack(
            side=tk.LEFT, fill=tk.X, expand=True, padx=8
        )
        ttk.Button(dest, text="Browse", command=self.browse).pack(side=tk.LEFT)

        galaxy = ttk.LabelFrame(main, text="Galaxy Music", padding=8)
        galaxy.pack(fill=tk.X, padx=14, pady=(10, 4))
        ttk.Label(galaxy, text="API URL").grid(row=0, column=0, sticky="w")
        ttk.Entry(galaxy, textvariable=self.galaxy_url).grid(
            row=0, column=1, sticky="ew", padx=8
        )
        ttk.Label(galaxy, text="API key").grid(row=1, column=0, sticky="w", pady=(6, 0))
        ttk.Entry(galaxy, textvariable=self.galaxy_key, show="•").grid(
            row=1, column=1, sticky="ew", padx=8, pady=(6, 0)
        )
        galaxy.columnconfigure(1, weight=1)

        ttk.Label(main, textvariable=self.status).pack(anchor="w", **pad)

        log_frame = ttk.Frame(main)
        log_frame.pack(fill=tk.BOTH, expand=True, padx=14, pady=4)
        self.log = tk.Text(
            log_frame,
            height=12,
            wrap="word",
            font=("Consolas", 9),
            relief="solid",
            borderwidth=1,
            state="disabled",
        )
        scroll = ttk.Scrollbar(log_frame, command=self.log.yview)
        self.log.configure(yscrollcommand=scroll.set)
        self.log.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        scroll.pack(side=tk.RIGHT, fill=tk.Y)

        result = ttk.Frame(main)
        result.pack(fill=tk.X, padx=14, pady=(6, 0))
        ttk.Label(result, textvariable=self.genre_var).pack(anchor="w")
        ttk.Label(result, textvariable=self.mood_var).pack(anchor="w")
        ttk.Label(result, textvariable=self.profile_var, wraplength=580).pack(anchor="w")

        actions = ttk.Frame(main)
        actions.pack(fill=tk.X, padx=14, pady=(8, 0))
        self.open_btn = ttk.Button(
            actions, text="Open folder", command=self.open_folder, state="disabled"
        )
        self.open_btn.pack(side=tk.LEFT)
        self.galaxy_btn = ttk.Button(
            actions, text="Profile pending Galaxy tracks", command=self.start_galaxy
        )
        self.galaxy_btn.pack(side=tk.LEFT, padx=(8, 0))

        hint = ttk.Label(
            main,
            text='Audio is limited to 160 kbps: yt-dlp -f "bestaudio[abr<=160]/bestaudio"',
            foreground="#666",
        )
        hint.pack(anchor="w", padx=14, pady=(8, 0))

    def browse(self):
        chosen = filedialog.askdirectory(initialdir=self.out_dir.get() or str(DEFAULT_OUT))
        if chosen:
            self.out_dir.set(chosen)

    def open_folder(self):
        path = Path(self.out_dir.get())
        path.mkdir(parents=True, exist_ok=True)
        if sys.platform == "win32":
            os.startfile(path)
        else:
            subprocess.Popen(["xdg-open", str(path)], start_new_session=True)

    def append_log(self, text: str):
        self.log.configure(state="normal")
        self.log.insert("end", text + "\n")
        self.log.see("end")
        self.log.configure(state="disabled")

    def _drain_queue(self):
        while True:
            try:
                item = self.messages.get_nowait()
            except queue.Empty:
                break
            kind = item[0]
            if kind == "log":
                self.append_log(item[1])
            elif kind == "status":
                self.status.set(item[1])
            elif kind == "result":
                self.genre_var.set(item[1])
                self.mood_var.set(item[2])
                self.profile_var.set(item[3] if len(item) > 3 else "")
            elif kind == "done":
                self.busy = False
                self.go_btn.configure(state="normal")
                self.galaxy_btn.configure(state="normal")
                self.open_btn.configure(state="normal")
            elif kind == "error":
                self.busy = False
                self.go_btn.configure(state="normal")
                self.galaxy_btn.configure(state="normal")
                self.status.set("Failed.")
                messagebox.showerror("Download failed", item[1])
        self.root.after(80, self._drain_queue)

    def emit(self, line: str, progress: bool = False):
        if progress:
            self.messages.put(("status", line))
        else:
            self.messages.put(("log", line))
            self.messages.put(("status", line))

    def start(self):
        if self.busy:
            return
        query = self.query.get().strip()
        if not query:
            messagebox.showinfo("Missing input", "Type a song name or paste a URL.")
            return
        out_dir = Path(self.out_dir.get().strip() or DEFAULT_OUT)
        self.busy = True
        self.go_btn.configure(state="disabled")
        self.galaxy_btn.configure(state="disabled")
        self.genre_var.set("Genre: —")
        self.mood_var.set("Mood: —")
        self.profile_var.set("")
        self.status.set("Working…")
        self.append_log(f"— {query}")
        threading.Thread(target=self._run, args=(query, out_dir), daemon=True).start()

    def _run(self, query: str, out_dir: Path):
        try:
            info = download_audio(query, out_dir, self.emit)
            self.emit("Analyzing with Essentia...")
            try:
                result = tag_download(info)
            except ModuleNotFoundError as exc:
                if "essentia" not in str(exc).lower():
                    raise
                self.emit(
                    "Essentia is not installed, so genre/mood tags were skipped. "
                    "The mp3 was still saved with title/artist from the download."
                )
                write_tags(
                    info["path"],
                    title=info.get("title"),
                    artist=info.get("artist"),
                )
                self.messages.put(("result", "Genre: (essentia not installed)", "Mood: —", ""))
            else:
                genres = ", ".join(result["genres"])
                mood = result["top_mood"]
                scores = ", ".join(
                    f"{k} {v:.2f}"
                    for k, v in sorted(result["moods"].items(), key=lambda kv: -kv[1])
                )
                self.emit(f"Tagged: {info['path'].name}")
                self.emit(f"Genres: {genres}")
                self.emit(f"Moods: {scores}")
                profile = result.get("profile") or ""
                if profile:
                    self.emit(profile)
                self.messages.put(
                    ("result", f"Genre: {genres}", f"Mood: {mood} ({scores})", profile)
                )
            self.messages.put(("status", f"Done — {info['path'].name}"))
            self.messages.put(("done",))
        except Exception as exc:
            self.messages.put(("log", f"Error: {exc}"))
            self.messages.put(("error", str(exc)))

    def start_galaxy(self):
        if self.busy:
            return
        url = self.galaxy_url.get().strip()
        key = self.galaxy_key.get().strip()
        out_dir = Path(self.out_dir.get().strip() or DEFAULT_OUT)
        take = 25
        try:
            save_config(url, key)
        except OSError as exc:
            messagebox.showerror("Galaxy Music", f"Could not save galaxy.json: {exc}")
            return
        self.busy = True
        self.go_btn.configure(state="disabled")
        self.galaxy_btn.configure(state="disabled")
        self.genre_var.set("Genre: —")
        self.mood_var.set("Mood: —")
        self.profile_var.set("")
        self.status.set("Profiling Galaxy tracks…")
        self.append_log("— Galaxy pending audio")
        threading.Thread(
            target=self._run_galaxy, args=(url, key, out_dir, take), daemon=True
        ).start()

    def _run_galaxy(self, url: str, key: str, out_dir: Path, take: int):
        try:
            uploaded = run_galaxy_sync(url, key, out_dir, take, self.emit)
            self.messages.put(("status", f"Done — uploaded {uploaded} profiles"))
            self.messages.put(("done",))
        except Exception as exc:
            self.messages.put(("log", f"Error: {exc}"))
            self.messages.put(("error", str(exc)))


def run_cli(query: str, out_dir: Path | None = None) -> Path:
    out_dir = out_dir or DEFAULT_OUT

    def log(line: str, progress: bool = False):
        if not progress:
            print(line)
        else:
            print(line, end="\r")

    info = download_audio(query, out_dir, log)
    print()
    try:
        result = tag_download(info)
    except ModuleNotFoundError as exc:
        if "essentia" not in str(exc).lower():
            raise
        print("Essentia is not installed; skipped genre/mood tags.")
        write_tags(
            info["path"],
            title=info.get("title"),
            artist=info.get("artist"),
        )
        return info["path"]
    print(f"{info['path'].name}: {result['genres']} | top mood: {result['top_mood']}")
    if result.get("profile"):
        print(result["profile"])
    return info["path"]


def run_galaxy_sync(url: str, key: str, out_dir: Path, take: int, on_line) -> int:
    client = GalaxyClient(url, key)
    pending = client.pending(take)
    if not pending:
        on_line("No tracks waiting for an Essentia profile.")
        return 0

    uploaded = 0
    for item in pending:
        track_id = item["id"]
        artist = item.get("artist") or ""
        title = item.get("title") or ""
        query = f"{artist} {title}".strip()
        on_line(f"— {artist} — {title} (#{track_id})")
        try:
            info = download_audio(query, out_dir, on_line)
            on_line("Analyzing with Essentia...")
            result = tag_download(info)
            payload = to_galaxy_payload(result)
            client.put_profile(track_id, payload)
            uploaded += 1
            on_line(f"Uploaded profile for #{track_id}")
            if result.get("profile"):
                on_line(result["profile"])
        except GalaxyError as exc:
            on_line(f"Galaxy error: {exc}")
        except Exception as exc:
            on_line(f"Error: {exc}")
    return uploaded


def main():
    if sys.platform == "win32":
        try:
            from ctypes import windll

            windll.shcore.SetProcessDpiAwareness(1)
        except Exception:
            pass

    args = sys.argv[1:]
    if "--galaxy" in args or "-g" in args:
        rest = [a for a in args if a not in {"--galaxy", "-g"}]
        take = 25
        url = None
        key = None
        i = 0
        leftover = []
        while i < len(rest):
            if rest[i] == "--take" and i + 1 < len(rest):
                take = int(rest[i + 1])
                i += 2
            elif rest[i] == "--url" and i + 1 < len(rest):
                url = rest[i + 1]
                i += 2
            elif rest[i] == "--key" and i + 1 < len(rest):
                key = rest[i + 1]
                i += 2
            else:
                leftover.append(rest[i])
                i += 1
        cfg = load_config()
        url = url or cfg.get("url") or ""
        key = key or cfg.get("apiKey") or ""
        if leftover:
            print("Unexpected arguments:", " ".join(leftover), file=sys.stderr)
        def log(line: str, progress: bool = False):
            if not progress:
                print(line)
            else:
                print(line, end="\r")

        uploaded = run_galaxy_sync(url, key, DEFAULT_OUT, take, log)
        print(f"Uploaded {uploaded} profiles.")
        return

    args = [a for a in args if not a.startswith("-")]
    if args:
        run_cli(" ".join(args))
        return

    root = tk.Tk()
    try:
        root.call("tk", "scaling", 1.2)
    except tk.TclError:
        pass
    DownloaderApp(root)
    root.mainloop()


if __name__ == "__main__":
    main()
