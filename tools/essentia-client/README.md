# Essentia client (local Galaxy Music uploader)

Copy these files next to your existing `models/` folder (`C:\Users\elija\Music\Essentia`). Do **not** commit TensorFlow `.pb` models.

The Galaxy Music server never runs Essentia. This client downloads audio with yt-dlp, analyzes locally, then `PUT`s the profile.

## Configure

1. Sign in to Galaxy Music → Settings → create a **write** API key (`gmk_…`).
2. GUI: fill **API URL** (e.g. `http://localhost:5107` or your Docker host) and **API key**. They save to `galaxy.json`.
3. Or environment variables: `GALAXY_API_URL`, `GALAXY_API_KEY`.

## Run

```bash
# existing one-off tagger
python app.py "Shake fourfolium"

# GUI
python app.py

# pull pending library tracks, analyze, upload
python app.py --galaxy --take 25
python app.py --galaxy --url http://localhost:8080 --key gmk_…
```

In the GUI, **Profile pending Galaxy tracks** calls `GET /api/v1/tracks/pending-audio`, searches `{artist} {title}` with yt-dlp, runs `tag_file()`, then `PUT /api/v1/tracks/{id}/audio-profile`.

Essentia genres/themes/instruments are stored on the server as audio-profile labels, not as MusicBrainz/Last.fm tags.
