# DIGtv — Roadmap / TODO

Tracking planned features. Status: 🔵 planned · 🟡 in progress · ✅ done.
Each item records the **goal**, **hard constraints** discovered in Jellyfin 10.10.7,
the **recommended approach**, and rough **phases/effort**. Nothing here is built yet.

---

## 🔵 #1 — Random mode that still drops a bumper every X videos

**Goal:** When a channel is played "shuffled," still insert a bumper (playlist B)
after every X music videos — preserve the MTV cadence even in random playback.

**Hard constraint:** A server plugin **cannot** influence a *client's* Shuffle button.
Web/Android TV/Roku each shuffle locally as a uniform random over *all* queue items, so
hitting client-Shuffle scatters the bumpers and destroys the every-X cadence. There is no
server hook to make a client's shuffle "bumper-aware." → The randomness must be **server-side**
(which DIGtv already half-does by pre-shuffling the source before baking the interleave).

**Recommended approach (cheap, works on every device):** make "random" a *server* behavior,
not the client toggle. The playlist is already pre-shuffled with cadence baked in, so the user
presses **Play** (not Shuffle) and gets random-order videos with a bumper every X. Add fresh
reshuffles so it feels new each sitting:

- **A — Auto-reshuffle interval (do this).** New per-channel field "Reshuffle every N minutes/hours."
  Register an **interval** scheduled trigger (`TaskTriggerInfo.TriggerInterval` + `IntervalTicks`,
  supported in 10.10). At 30–60 min the channel is in a fresh cadenced-random order almost every
  time you start watching. UI: one number field. Backend: extend the scheduled task's triggers.
- **B — UX guardrail (do this).** Relabel "Randomize" → "Random order (server-side)" with a note:
  *"Press Play, not Shuffle — Shuffle scatters bumpers."* Zero risk; prevents the foot-gun.
- **C — Stretch: reshuffle-on-play.** Subscribe to `ISessionManager` playback-start events; when a
  channel's playlist starts, queue a background reshuffle for the *next* session (can't reorder the
  in-progress client queue). Adds complexity; A already gets ~95% of the benefit. Skip unless needed.

**Also worth fixing while here:** today `Randomize` and `ReshuffleOnRegenerate` are redundant
(either one → a fresh shuffle every build, see [ChannelService.cs:189](ChannelService.cs#L189)).
Give them distinct meaning: `Randomize` = build in random vs source order; `ReshuffleOnRegenerate`
= new order each rebuild vs persist a saved seed.

**Recommendation:** ship **A + B** as **v1.0.2.0**. Small, all-device safe.

---

## 🔵 #2 — Per-playlist audio normalization (level out loud/quiet videos)

**Goal:** Even out volume across a channel's music videos, **without** changing global Jellyfin.

**Hard constraints (verified against 10.10.7):**
- Jellyfin's built-in normalization is **music-player, client-side, and global** — it applies a gain
  (`BaseItem.NormalizationGain`) in the *audio* player via the client. In 10.10.7 `EncodingHelper`
  applies **no** `loudnorm`/normalization audio filter for **video** playback, and there is **no**
  per-playlist scope anywhere. So "normalize only this playlist" is **not natively possible**, and
  the native feature doesn't reliably touch video at all.

**Options:**
- **A — Pre-normalized audio copies (recommended for true, cross-client result).** A DIGtv task runs
  Jellyfin's bundled ffmpeg with two-pass **`loudnorm`** (EBU R128, e.g. target `I=-14 LUFS`,
  `TP=-1.5dB`, `LRA=11`) on each channel video, **copying the video stream** (`-c:v copy`) and
  re-encoding only audio (`-c:a aac`) → fast, no video-quality loss. Normalized files live in a
  DIGtv-managed library folder; the channel playlist points to those copies.
  - Pros: truly playlist-scoped, identical on **every** client (Direct Play), originals untouched.
  - Cons: **duplicates storage** (video stream copied, so ~same file size), encode time per video,
    and the normalized files must live in a Jellyfin **library** to be playlist items (add a hidden
    "DIGtv Normalized" library or a folder under an existing one). ffmpeg path via `IMediaEncoder`.
  - Phases: (1) locate ffmpeg + measure loudness pass; (2) produce normalized remux into managed dir;
    (3) register/scan that dir as a library; (4) build the channel from normalized copies; (5) cache
    by source-item id + mtime so we don't re-encode unchanged videos.
- **B — Measure & store `NormalizationGain` on the items (cheap, low reliability).** Compute per-video
  LUFS and write `NormalizationGain`; rely on the client's "Audio Normalization" setting. Easy, no
  duplication — but won't apply on most **video** clients, and it's item-global not playlist-scoped.
  Document as "best effort," not the primary path.
- **C — Per-playlist transcode profile forcing loudnorm.** Not pluggable per-playlist; needs core
  changes. Not feasible.

**Recommendation:** plan **A** (with a per-channel "Normalize audio" toggle + target-LUFS field) and
clearly surface the **extra disk usage**. Offer **B** as a lightweight opt-in for users who keep
client normalization on and just want music-player parity. Likely **v1.1.0** (bigger feature).

---

## 🔵 #3 — Lower-third overlay: song title + artist (+ channel), like old music videos

**Goal:** Show an MTV-style lower-third in the bottom-left (Title / Artist / playlist name) at the
start of each video, on all devices.

**Hard constraint:** A server plugin can't draw on a client's video surface. The portable primitive
is **subtitles** — and Jellyfin recognizes external **`.ass`/`.ssa`** sidecars
([SubtitleFileExtensions], and the resolver honors **Forced**/**Default** filename tokens). ASS
supports precise positioning (`{\an1}` = bottom-left) + fonts/colors → a real lower-third.

**Recommended approach: forced external ASS sidecars.**
- A DIGtv task generates one `*.ass` per channel video with a styled `Dialogue` event for ~`0:00–0:12`
  (optionally re-shown near the end), positioned bottom-left, reading **"Artist — Title."** File named
  like `MyVideo.en.default.forced.ass` so Jellyfin flags it forced+default → **auto-displays** without
  the user picking a track. Then trigger a metadata refresh so it's indexed.
- Pros: native, no client app changes; works wherever subtitles do.
- **Caveats to design around:**
  - **Soft ASS positioning is client-dependent.** Web (libass/SSA.js), JMP/mpv, Kodi honor `\an1`
    placement; some embedded-TV/Roku clients have weak ASS support and may recenter or need burn-in.
    Offer a **burn-in mode** (transcode with the subtitle hardcoded) for guaranteed identical look,
    at the cost of forcing transcoding. Default = soft forced ASS; burn-in = opt-in per channel.
  - **Subtitles attach to the *item*, not the playlist.** Title+artist are per-video (always correct).
    **Playlist/channel name can't vary per-channel** for a video shared across channels. Default to
    **"Artist — Title"** only (correct everywhere); optionally append channel name with the caveat that
    a shared video shows whichever channel generated it last.
  - **Auto-display** still depends on the client honoring forced subs; a few clients require a one-time
    pick. Document.
  - **Write location/permissions:** sidecars go next to the media file (needs write access to the
    library dir — fine on the NAS) or a managed location; then run a library scan to detect them.
- **Stretch:** richer styling (channel "bug"/logo via ASS drawing or a PNG burn-in), fade in/out,
  re-show the lower-third at each track change.

**Recommendation:** plan soft **forced ASS** sidecars (Artist — Title, bottom-left, ~12s) with an
optional **burn-in** toggle for stubborn clients. Likely **v1.2.0**.

---

## 🔵 #4 — Dedicated home-screen section for DIGtv (config toggle)

**Goal:** Surface DIGtv channels in their own home-screen row so users don't dig through Playlists.
New config option to turn it on/off.

**Feasibility verdict (be honest with expectations):**
- **A literal new "media type" — NO.** Media types (`BaseItemKind`) are core enums; a plugin can't add
  one, and clients wouldn't render it.
- **A custom home section on *every* device — NO.** Home screens are **client-rendered** with a fixed
  set of sections on native apps (Android TV, Roku, Swiftfin, embedded TVs). No server plugin can inject
  a section into those. This is a hard Jellyfin limitation.
- **A dedicated "DIGtv" row on the *web client* + *Jellyfin Media Player* — YES**, via the **Home Screen
  Sections (HSS)** plugin you already added (`b8298e01-…`). HSS exposes
  `IHomeScreenManager.RegisterResultsDelegate<T>(T handler)`; DIGtv registers a `PluginDefinedSection`
  ("DIGtv", `OnGetResults` → `QueryResult<BaseItemDto>` of the channel playlists). Clicking a card opens/
  plays the channel. Requires HSS **+ File Transformation (`5e87cc92-…`) + Plugin Pages** installed, and
  note HSS **replaces** the whole web home screen (not just adds a row).

**Recommended approach:**
- **Config:** global `ShowOnHomeScreen` toggle (default **off**) + optional per-channel `ShowOnHome`.
- **Integration as an *optional* dependency (do this):** don't hard-link HSS (DIGtv must still load if HSS
  is absent). On startup, detect the HSS assembly/`IHomeScreenManager` via reflection; if present and the
  toggle is on, construct `PluginDefinedSection("digtv-channels", "DIGtv", …)` and call
  `RegisterResultsDelegate(section)`. If HSS isn't installed, show a note in the DIGtv config page linking
  the HSS + File Transformation + Plugin Pages install. (Simpler alt: declare HSS a manifest dependency
  and compile against it — but that forces the install and couples versions; reflection keeps us standalone.)
- **Native-client mitigation (works everywhere, do this too):** prefix generated playlists (e.g.
  `▶ DIGtv — <name>`) so they sort to the top of the Playlists view, and/or drop them in a **Collection**,
  so on TV/Roku they're one click away even without a custom section.

**Caveats:** web/JMP only; depends on three third-party plugins staying compatible; HSS is "Experimental"
on 10.10.7 and takes over the entire web home. Effort: medium. Likely **v1.3.0** (after #1–#3), unless you
want the home row sooner — then we can pull it forward.

---

## Notes
- Ship order suggestion: **#1 (v1.0.2.0)** → **#2 (v1.1.0)** → **#3 (v1.2.0)** → **#4 (v1.3.0)**.
- Core rule for #1–#3: server-side only, native playlists/subtitles, works on every client.
- #4 is the exception — a true home section is only achievable on web/JMP (via HSS); native TV apps get
  the naming/Collection mitigation instead.
