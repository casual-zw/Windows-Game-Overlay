# Milestone 3 — Luna translation

Implementation uses `gpt-5.6-luna` through `POST https://api.openai.com/v1/responses`,
with reasoning disabled, no tools, no conversation chain, and `store: false`.
The last flag disables response storage; it is not a promise of zero provider retention.
Local English OCR remains independent of network access. Target language is a typed
catalog and selector; Simplified Chinese (`zh-Hans`) is the only initial choice.
Game context, glossary, and history remain milestone 4.

## Use

1. Open **Translation settings**, paste your own key into the masked field, and Apply.
2. Optionally **Test connection**: this submits a short greeting to Luna and counts
   against the session request cap. The key needs model access and API billing.
3. Enable translation. Select a game window, start capture, and select a region.
   Selection and **Read again / Ctrl+Alt+G** recognize and translate immediately.
4. Enable **Auto read** for continuous detection. It compares a downscaled grayscale crop locally about every 200 ms.
   A large image change (at least 5% of sampled pixels) starts OCR on that check, when the OCR worker is free,
   without waiting for the region to settle. This early read does not establish text stability by itself.
   Only one early read occurs per burst of motion; a settled read rearms it.
   For smaller changes, after
   300 ms without a significant image change (normally 400 ms at 5 fps), it runs
   OCR once and queues new text immediately. Unchanged crops skip further OCR.
   Moving backgrounds fall back to OCR every 900 ms, with two matching text
   results at least 700 ms apart required for translation. OCR calls never overlap.
5. Turn off Auto to stop polling; manual reading still works. Disable translation
   to keep OCR local. Stop capture to cancel all work. Show/hide only changes visibility.

Image comparison only schedules OCR; recognized text still controls API requests,
so background animation alone need not create API traffic. Comparison uses a crop
scaled to at most 640 pixels on its longest side, ignoring grayscale differences
below 20 and requiring 0.1% changed pixels (minimum two) for the settled/fallback path,
or 5% (minimum two) for immediate OCR. The immediate threshold is 50 times higher;
smaller changes still get checked after settling so short text edits are not lost. These starting thresholds
need Windows tuning; very small or low-contrast edits can be missed. Read again
bypasses visual detection. Settled OCR results are discarded if a newer significant
image change was observed during recognition. Blank/changed text clears
old translations when recognized. Detection cannot see changes between samples;
long pauses in typewriter text can still produce a partial-line translation. Use
manual reading for rapid subtitles, OCR noise, or difficult animation. Auto latency
includes sampling, stabilization, OCR, and network delay; no 1–2 second guarantee.

Polling suspends during external-app focus, minimize, region selection, settings,
and overlay edit mode. Returning requires new observations. Capture and inference
must still be tested on Windows. The preview retains its existing 5 fps cap.

## Credential handling

The key stays in session memory unless **Remember on this Windows account** is
selected. Then DPAPI `CurrentUser` protects bytes stored at
`%LOCALAPPDATA%\GameOverlay\api-key.dpapi`. It is outside the repository and release
folder. Turning Remember off and applying removes saved credentials. **Forget key**
removes saved credentials, clears the active key, cancels pending work, and disables
translation. Cancel leaves the previously applied key unchanged.

The key goes only in the Authorization header to the fixed OpenAI HTTPS endpoint;
redirects are disabled. No key or raw provider error body is logged/displayed.
DPAPI protects stored data, not against malware running as the same user or memory
inspection. Forget removes the local copy; revoke a compromised key at the provider.
Remembered keys never automatically enable translation on startup.

## Bounds and accounting

- One active translation and one replaceable pending item. Generation checks reject
  obsolete responses even if cancellation is ignored. OCR does not wait for HTTP.
- 15-second API timeout, 6,000 source characters, 4,096 output tokens. Oversized,
  empty/refused/malformed/incomplete responses are not displayed as translations.
- At most 30 network attempts/minute; default 100 attempts per app session, adjustable
  from 1 to 10,000 in settings. Connection tests share these limits. Changing keys,
  capture targets, or enabling/disabling does not reset counters. Restarting does.
- No automatic HTTP retries. Read again is the explicit retry after errors/limits.
- Bounded session cache (200 entries), scoped to source text and target; model and
  prompt are fixed in this build. Target/key changes clear the cache. No disk history.
- Input/output usage comes from the API. Missing usage and failed/cancelled requests
  are marked unknown; cancellation may still cost money. Estimates use Luna rates
  checked during implementation: $0.20/M input, $0.02/M cached input, $1.20/M output.
  Rates may change; local estimates are not an exact billing total or monetary cap.
- Read-to-display timing starts at the first OCR observation for the source, not the
  moment the game first rendered it. Cache and network timings are labeled separately.

## Validation

Run the existing core/OCR harnesses plus:

```sh
dotnet run --project tests/Overlay.Translation.Tests -c Release
dotnet build src/Overlay.Windows/Overlay.Windows.csproj -c Release
```

Fake HTTP tests exercise request shape, usage, refusal, errors, cancellation, and
blank/oversize input. A dispatcher-like test context checks pending replacement,
stale-response suppression, cache hits, stop invalidation, and session limits.
Stable-text replay covers typewriter changes, blanks, repeated text, manual reads,
and reset. Tests never need a real key and never contact OpenAI.

### Windows acceptance checklist (user validation)

- Without a key: capture/OCR works; translation gives a useful settings message.
- Session key: Apply, enable, translate a short scene; restart and verify no saved key.
- Remember: Apply, restart, enable manually, translate; Forget, restart, verify deletion.
- Invalid/revoked key and offline network: clear error, no retries, no raw key exposed.
- Connection test: status and usage update, game overlay does not show the test greeting.
- Target selector: only Simplified Chinese is offered; names/numbers/choices remain legible.
- Auto: leave a static scene for five minutes and verify OCR stops after settling.
  Test a one-word edit, low-contrast text, blinking cursor, and animated background.
  A large change should start OCR on the next sampled check when the worker is free;
  continued animation should return to the 900 ms fallback rather than OCR on every frame.
  Compare time until OCR starts against the previous build; target roughly 400 ms
  after the last significant visual change, plus capture timing and recognition.
- Auto: advance several lines, pause typewriter animation, repeat dialogue, show blank text.
  Unchanged dialogue must not repeatedly call the API; manual retry remains available.
- Advance/reselect/stop during translation; old results must never reappear.
- Alt-tab/minimize/settings/edit mode: Auto suspends and input/focus behavior stays intact.
- Lower request cap to the current count; further calls stop. Raise it, Read again resumes.
- Measure a 30–50-block game scene: corrected OCR errors, translation quality, warm/cold
  delay, unknown usage, token counts, estimated cost, and game performance.

No real key, paid request, or live Windows/game validation is part of the local tests.

References: [Responses](https://developers.openai.com/api/docs/guides/text),
[Luna](https://developers.openai.com/api/docs/models/gpt-5.6-luna),
[Windows DPAPI](https://learn.microsoft.com/en-us/dotnet/standard/security/how-to-use-data-protection).
