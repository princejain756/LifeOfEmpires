# How the Stress Ninja art was generated with ChatGPT + ego-browser

This documents the pipeline used on 2026-10-10 to produce every asset in
`icons/` (and `StressNinja/Assets/raw_cream/`): 29 spec images from `icons.md`
plus 2 calendar variants — 31 PNGs total, all through the ChatGPT web UI
(logged-in Plus account) driven by the `ego-browser` skill.

## 1. Prompts

Every object/icon prompt = icons.md object description + shared style block,
with ONE deliberate change: `transparent background` → `solid uniform flat
green (#00FF00)`, because ChatGPT can't reliably emit real alpha and our
`process_assets.py` already chroma-keys green → transparent.

```
Generate an image: Juicy casual-mobile-game icon of <OBJECT>, flat 2.5D
cartoon style, thick dark chocolate-brown outline (~6% of icon size), two-tone
cel shading with the light source at the top-left, soft warm drop shadow at
bottom-right, rounded friendly proportions, slightly exaggerated and
expressive, warm cream-and-terracotta palette (#FAF3E3, #FF5A3C, #FFB627,
#19B8A6, #B67B4A), playful and harmless, high detail, clean vector
illustration, centered on a solid uniform flat green background (#00FF00),
no watermark, no text
```

- Objects containing words by design (`DEADLINE` calendar, `OVERDUE` invoice,
  `URGENT` box) kept their text and dropped the "no text" clause.
- `bg_home` / `bg_game` / `app_icon` / `logo` used their own spec prompts —
  solid scene backgrounds, no green screen.
- All prompts went sequentially into ONE regular chat
  (`/c/6ac9c911-350c-83e8-acab-f269d2ab134c`) so the model kept style context.

## 2. Driving the UI (ego-browser)

```js
const task = await taskSpace("<id>");
const page = task.page("p1");
await page.goto("https://chatgpt.com");

await page.click("loc=css:.ProseMirror");          // composer is a ProseMirror
await page.keyboard.insertText(prompt);            // div, not a <textarea>
await page.click("loc=css:button[aria-label='Send']");

// wait for the image card to mount AND generation to finish
await page.waitForFunction(() => {
  const fresh = [...document.querySelectorAll(
    '[data-testid="generated-image-preview"]')]
    .filter(e => !e.dataset.oldgen && e.querySelector('img'));
  return !document.querySelector('button[aria-label="Stop"]') && fresh.length > 0;
}, undefined, { timeout: 420000 });
```

### Gotchas discovered

- **Composer** is `div.ProseMirror[contenteditable]` — `fill("textarea")`
  fails; use click + `keyboard.insertText` (or `paste`).
- **Temporary chats can't generate images** — the model replies "image
  generation isn't available in this temporary chat". Use a regular chat.
- **DOM is virtualized.** Only ~5 `img` elements stay mounted; old ones
  unmount and remount with NEW `blob:` URLs when scrolled. So
  `imgs.length > before` is NOT a reliable "new image" signal — mark existing
  `[data-testid="generated-image-preview"]` nodes (e.g. `dataset.oldgen`)
  and wait for an unmarked one instead.
- **"Edit" responses**: the model occasionally returns an image *edit* of a
  previous result instead of a fresh gallery entry — these still mount a new
  preview node and DO count as generated images (this is how we got 3
  calendar variants: `calendar`, `calendar_alt1`, `calendar_alt2`).
- **Generation is slow**: 40 s–4+ min per image. Use ≥ 420 s waits.
- **In-page work**: `page.evaluate` caps at ~15 s — drive any scroll/poll
  loop from Node, not inside the evaluated function.

## 3. Downloading — two methods

### Method A: blob URL (used for the first images)

The mounted `<img>`'s `src` is a `blob:` URL. `page.fetch` runs in the page
context and can fetch it directly:

```js
const src = await page.evaluate(() =>
  [...document.querySelectorAll('img[alt*="Generated image"]')].pop().src);
await page.fetch(src, { saveAs: "/abs/path/alarm_clock.png" });
```

Works, but depends on which imgs are currently mounted.

### Method B: backend API (the reliable one — used for the full harvest)

No DOM needed at all. The conversation JSON lists every generated file:

```js
// 1. session token for API calls
const sess = await page.fetch("/api/auth/session");
const token = JSON.parse(sess.body).accessToken;
const H = { authorization: "Bearer " + token };

// 2. full conversation → image parts (each image appears ~2×; dedupe by id)
const convo = await page.fetch(`/backend-api/conversation/${CONV_ID}`, { headers: H });
// parts[] entries with content_type === "image_asset_pointer" carry
// asset_pointer: "sediment://file_XXXX", width/height/size_bytes,
// plus message metadata: image_gen_title (great for naming!)

// 3. resolve file_id → real download URL, then fetch bytes
const dl  = await page.fetch(`/backend-api/files/${fid}/download`, { headers: H });
const url = JSON.parse(dl.body).download_url;      // backend-api/estuary/content?...
await page.fetch(url, { saveAs: `icons/${name}.png` });
```

Endpoint notes:
- `/backend-api/conversations/{id}` (plural) → 401; singular works.
- Files list: `/backend-api/files/{fid}/download` returns
  `{ download_url }` → an estuary content URL on the same origin.
- Each image shows up in both tool + assistant messages → dedupe by
  `file_id`, sort by `create_time` for chronological order.

## 4. Final asset → file map (chronological order)

| # | file_id (suffix) | Saved as | image_gen_title |
|---|---|---|---|
| 0 | ed908211 | `alarm_clock.png` | Grumpy Battered Alarm Clock |
| 1 | c3c88209 | `coffee_cup.png` | Panicked Coffee Cup Mid-Spill |
| 2 | e0dc8207 | `email_envelope.png` | Bursting Flame-Sealed Envelope |
| 3 | 05248211 | `sticky_note.png` | Furious Sticky Note Icon |
| 4 | 14848211 | `smartphone.png` | Angry Cracked Smartphone Notifications |
| 5 | c8588207 | `calendar_alt1.png` | Panicked Deadline Calendar |
| 6 | 9bb88211 | `calendar_alt2.png` | Panicked Deadline Calendar Mascot |
| 7 | 24988211 | `calendar.png` | Deadline Calendar in Motion |
| 8 | 2f648211 | `notification_bubble.png` | Cheerful Chat Bubble Alert |
| 9 | 9bb08207 | `bill_invoice.png` | Overdue Invoice on Neon Green |
| 10 | accc8207 | `laptop.png` | Angry Pixel-Spewing Laptop |
| 11 | ecf48207 | `printer.png` | Angry Printer Paper Jam |
| 12 | 78648211 | `deadline_box.png` | Overloaded Box Bursting Open |
| 13 | c45c8211 | `golden_clock.png` | Smug Golden Pocket Watch Mascot |
| 14 | 18148207 | `rainbow_stress_ball.png` | Playful Rainbow Stress Ball Squeeze |
| 15 | 07188211 | `coin.png` | Glossy Gold Ninja Coin |
| 16 | 79a08207 | `bomb.png` | Menacing Glossy Cartoon Bomb Mascot |
| 17 | 0d248207 | `alarm_bomb.png` | Ringing Alarm Bomb Clock |
| 18 | e3148211 | `blade_classic.png` | Glossy Cartoon Kunai on Neon Green |
| 19 | a34c8207 | `blade_fire.png` | Flaming Kunai on Neon Green |
| 20 | 545c8207 | `blade_neon.png` | Glowing Teal Energy Kunai |
| 21 | c07c8211 | `blade_thunder.png` | Electrified Golden Kunai |
| 22 | 24b08207 | `mascot_pose.png` | Chibi Ninja Game Avatar |
| 23 | a1988211 | `mascot_action.png` | Chibi Ninja's Flaming Slash |
| 24 | 01d88211 | `clockzilla.png` | Stomping Alarm Clock Monster |
| 25 | edc08207 | `emblem_ninja.png` | Ninja Shuriken Dojo Crest |
| 26 | b0a08207 | `weak_point.png` | Glowing Teal Compass Emblem |
| 27 | 203c8207 | `bg_home.png` | Morning Light in a Zen Tatami Room |
| 28 | c5a08211 | `bg_game.png` | Golden Sky Over a Mountain Village |
| 29 | ee18820a | `app_icon.png` | Chibi Ninja Clock Slash |
| 30 | cbb8820a | `logo.png` | Stress Ninja: Dynamic Katana Logo |

Output: objects 1254×1254 PNG · backgrounds 941×1672 portrait (9:16 —
needs crop/resize to 1290×2796 before the catalog step).

## 5. Next step

`StressNinja/Assets/raw_cream/` mirrors the same 29 spec files. Point `RAW`
in `process_assets.py` at either folder, add `logo` to `OPAQUE` if the
wordmark should keep its cream background, and run it to rebuild
`Assets.xcassets`.
