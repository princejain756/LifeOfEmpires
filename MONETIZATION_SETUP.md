# Monetization Setup — Life of Empires

**Scope of this document: architecture and configuration only.** No advertising or in-app-purchase
SDK is present in this build. `Packages/manifest.json` contains no `com.unity.purchasing` and no
Google Mobile Ads package, no ad is shown, and no store product is queried. Everything below is what
already exists in the code to receive that integration, plus the exact identifiers and workflow
required to switch it on.

## 1. What already exists in the project

| Area | Where | Behaviour |
|---|---|---|
| Crowns | `SaveData.crowns` (`Scripts/Runtime/Core/SaveSystem.cs`) | campaign currency, persisted |
| Per-mission rewards | `MissionDefinition.crownReward`, `StarObjective.crownValue` (`Data/Mission_*.asset`) | 2 crowns (`mission_01`), 3 (`mission_02`); 1 crown per earned star |
| Entitlements | `SaveData.entitlements` + `GrantEntitlement(id)` / `HasEntitlement(id)` | idempotent grant; a repeated grant returns `false` and stores once |
| Purchased products | `SaveData.purchasedProducts` | raw store product ids, so entitlements can be re-derived after a restore |
| Persistence | `SaveSystem.Serialize` / `TryDeserialize` / `WriteToDisk` / `ReadFromDisk` | JSON at `Application.persistentDataPath/life_of_empires_save.json`; corrupt payloads degrade to defaults instead of throwing |

Nothing in the runtime currently reads `entitlements` to gate content — there is no premium content
to gate yet. The data model is the seam the store integration plugs into.

## 2. Advertising — planned shape (rewarded only)

Specified placements (`details.md` §"A. GOOGLE ADMOB"): rewarded ad **after victory** (optional
bonus on top of the base reward, which the player always receives without watching), rewarded ad
**after defeat** (recover part of the army, only where it cannot break mission objectives or save
state), and an optional post-battle bonus with a per-session/per-mission cap. **No forced
interstitials. No banners during a battle.**

A single abstraction owns the SDK so gameplay never calls it directly:

```
AdvertisingService
  Initialize()
  LoadRewardedAd()
  IsRewardedAdReady()
  ShowRewardedAd()
  HandleRewardEarned()
  HandleAdFailure()
  HandleAdClosed()
```

Mandatory behaviour when it is implemented:

- Grant a reward **only** from the SDK's reward-earned callback, exactly once per ad (guard duplicate
  callbacks), and marshal SDK callbacks onto the Unity main thread before touching scene objects.
- Disable the ad button when no ad is ready, and show an understandable unavailable state.
- Never block or break gameplay when an ad fails to load or to show; dispose ad objects on teardown.
- Configuration fields (never hardcoded personal credentials), with the App ID and rewarded ad unit
  id supplied per platform and environment.
- Use Google's documented **test** ad units during development; production unit ids only in a
  release build. Never run automated tests against live ads (Google policy).

### Google's documented test identifiers

Taken from Google's official test-ads documentation
(`https://developers.google.com/admob/unity/test-ads`, plus the platform quick-start pages for the
App IDs), retrieved **2026-10-11**. Re-check that page before use; Google may rotate these.

| Platform | Item | Test value |
|---|---|---|
| iOS | App ID | `ca-app-pub-3940256099942544~1458002511` |
| Android | App ID | `ca-app-pub-3940256099942544~3347511713` |
| iOS | Rewarded | `ca-app-pub-3940256099942544/1712485313` |
| iOS | Interstitial | `ca-app-pub-3940256099942544/4411468910` |
| iOS | Banner | `ca-app-pub-3940256099942544/2934735716` |
| iOS | Anchored adaptive banner | `ca-app-pub-3940256099942544/2435281174` |
| iOS | App open | `ca-app-pub-3940256099942544/5575463023` |
| Android | Rewarded | `ca-app-pub-3940256099942544/5224354917` |
| Android | Interstitial | `ca-app-pub-3940256099942544/1033173712` |
| Android | Banner | `ca-app-pub-3940256099942544/6300978111` |
| Android | Anchored adaptive banner | `ca-app-pub-3940256099942544/9214589741` |
| Android | App open | `ca-app-pub-3940256099942544/9257395921` |

Only the rewarded units are needed for the placements above; the others are listed so the shapes the
project will not use are still identifiable as test units rather than real ones.

## 3. In-app purchases — planned products

The specification proposes three non-consumables. **Final identifiers must match App Store
Connect**; they are proposed, not yet registered.

| Product | Proposed identifier | Type | Grants |
|---|---|---|---|
| Premium Campaign Expansion | `com.princejain756.lifeofempires.campaign.expansion1` | Non-consumable | Entitlement `campaign_expansion1` → unlocks the premium campaign's missions |
| Royal Castle Cosmetic Pack | `com.princejain756.lifeofempires.cosmetic.royalcastle` | Non-consumable | Entitlement `cosmetic_royalcastle` → cosmetic castle appearance |
| Founder's Edition | `com.princejain756.lifeofempires.founder` | Non-consumable | Entitlement `founder` → defined premium cosmetics + a specified premium campaign |

Consumers must be bundled normally through the App Store; no external payment system.

Entitlement rules to implement on top of the existing save model:

- Derive entitlements from `purchasedProducts` (product id → entitlement ids), and only add an
  entitlement once (`GrantEntitlement` already returns `false` for a repeat).
- Two products granting the same content must not double-grant it.
- Call "restore purchases" on demand and after a fresh install, then re-derive entitlements.
- Validate receipts (store/Apple) before granting anything beyond the local, offline-friendly path;
  a refund/revocation should remove the entitlement again.
- Show **localized prices read from the store**, never hardcoded price strings.
- Do not present purchase prompts during the first tutorial mission.

Note: the save-system test fixture references an example product id
`com.princejain756.lifeofempires.full_campaign` purely as a string round-trip value. It is not a registered
product; align product ids and entitlements to the table above (or to whatever is actually created in
App Store Connect) before shipping.

## 4. Publishing and account configuration workflow

1. **AdMob account** → register the app (iOS bundle id `com.princejain756.lifeofempires`) → create one
   rewarded ad unit → keep the App ID and unit id out of source; feed them through the
   `AdvertisingService` configuration fields.
2. **app-ads.txt** — host Google's required line on the developer website referenced by the App
   Store listing before requesting real ads.
3. **AdMob app-readiness review** — AdMob will not serve real ads until the app is published and
   approved. Test units only until then.
4. **App Store Connect** → Agreements, Tax, and Banking complete; create the three non-consumables
   with the identifiers in section 3; add localized metadata.
5. **Privacy** — declare the AdMob SDK's data usage in the privacy nutrition labels and request
   tracking authorisation (ATT) where required; keep the goal of a game that works with **zero
   downloads of advertising data** (no ad blocks gameplay).
6. **Unity packages** — add the official Google Mobile Ads Unity plugin and `com.unity.purchasing`
   in a later milestone, then re-run the EditMode/PlayMode suites before touching store code.

## 5. Explicitly not done in this milestone

- No ad SDK, no IAP SDK, no store initialisation, no purchase UI, no ad UI.
- No receipt validation, no server component, no entitlement gating of content.
- No production ad unit ids or store identifiers are committed; only Google's public test ids appear
  above, and they are documentation, not code.
