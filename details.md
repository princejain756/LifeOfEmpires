# 🏰 MASTER PROMPT — Build a Premium iPhone Strategy Game Using Claude

I'm going to structure this as a **production-level game development prompt**, not a basic prototype prompt.

The objective is to build a beautiful, polished, single-player iPhone strategy game inspired by **Age of Empires, Clash of Clans, and The Battle of Polytopia**, but with its own identity.

Your game will have real-time battles, intelligent AI opponents, civilization progression, a campaign, a persistent kingdom, and built-in monetization.

### My recommended technology

| Component | Choice |
|---|---|
| Game engine | Unity 6 LTS |
| Programming language | C# |
| Target device | iPhone |
| Graphics | Stylized 3D, isometric perspective |
| Rendering | Universal Render Pipeline (URP) |
| Enemy intelligence | Offline game AI |
| Advertisements | Google AdMob |
| In-app purchases | Unity IAP + Apple App Store |
| Save system | Local device storage |
| Backend/server | **None required** |
| Development | Claude Code on your Mac |
| Final build | Xcode → TestFlight → App Store |

I'm choosing Unity because it has established iOS deployment support, a maintained Google Mobile Ads integration, and an in-app purchase package. This reduces the technical risk of monetizing your first game. [Unity Docs](https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/iphone/getting-started/ios-environment-setup?utm_source=chatgpt.com)

**Important for October 2026:** Apple requires App Store submissions to use Xcode 26 or newer with an iOS 26-or-newer SDK. I've included this requirement in the prompt. [Apple Developer](https://developer.apple.com/news/upcoming-requirements/?utm_source=chatgpt.com)

The prompt below is written for **Claude Code**, where Claude can create and modify real project files on your Mac.

---

## 📋 COPY EVERYTHING BELOW INTO CLAUDE CODE

```text
You are an elite game development team operating as one autonomous AI coding agent.

Your combined expertise includes:

- AAA mobile game direction
- Unity 6 game development
- C# engineering
- Real-time strategy game systems
- Game AI and pathfinding
- 3D environment design
- Premium mobile UI/UX
- iOS performance optimization
- Mobile game monetization
- Google AdMob integration
- Apple App Store in-app purchases
- Game economy and retention design
- App Store release engineering
- Game testing and debugging

YOUR MISSION:

Build a complete, beautiful, commercially viable,
single-player iPhone real-time strategy game.

This is not a request for a game design document,
prototype mockup, or theoretical architecture.

I want you to create an ACTUAL PLAYABLE GAME.

Write real code.
Create real Unity scenes.
Implement working gameplay.
Generate usable assets.
Connect game systems.
Test your implementations.
Fix discovered errors.
Prepare the project for iPhone deployment.

You have permission to make sensible engineering and
design decisions without repeatedly asking me questions.

Work autonomously.

If you have access to file editing and terminal tools,
use them to create and test the project.

If Unity, Xcode, or required packages are missing,
identify that accurately, implement everything possible,
and provide precise installation instructions.

Never claim that code was compiled, tested, or deployed
unless you actually performed those actions.

====================================================
SECTION 1 — THE GAME
====================================================

WORKING TITLE:

EMPIRE RUSH: KINGDOM CONQUEST

This is a temporary name.

The final name should be original, memorable,
easy to search, and suitable for the App Store.

Do not copy names, logos, copyrighted assets,
soundtracks, maps, or protected designs from
Age of Empires or any other game.

GAME GENRE:

Single-player real-time strategy
Empire building
Kingdom conquest
Medieval warfare
Offline strategy
Campaign progression

PLATFORM:

iPhone first.

Android may be supported later, so avoid unnecessary
platform-specific gameplay dependencies.

ENGINE:

Unity 6 LTS, using a supported stable release.

LANGUAGE:

C#

RENDERING:

Universal Render Pipeline.

CAMERA:

Premium isometric 3D camera.

ORIENTATION:

Portrait-first, optimized for modern iPhones.

Because this is a mobile RTS, carefully design
camera controls and the UI so that portrait orientation
does not make unit selection frustrating.

The player should be able to understand the battlefield
and command armies comfortably using touch controls.

TARGET PERFORMANCE:

60 FPS on reasonably modern iPhones.

Support a graceful 30 FPS fallback on lower-end devices.

The game must run without a backend or multiplayer server.

====================================================
SECTION 2 — CORE GAMEPLAY VISION
====================================================

Imagine a combination of:

- Age of Empires resource gathering
- Clash of Clans kingdom progression
- Polytopia's approachable strategy
- Satisfying arcade-style victories
- Premium mobile game presentation

However, this must be an ORIGINAL game.

The experience should feel strategically meaningful,
not like a generic idle clicker or an automatic battle
simulator.

CORE GAMEPLAY LOOP:

1. Enter a battlefield.
2. Start with a small settlement.
3. Gather wood, food, and gold.
4. Build structures.
5. Train military units.
6. Explore enemy territory.
7. Command armies.
8. Attack enemy defenses.
9. Destroy the enemy stronghold.
10. Win rewards.
11. Upgrade the permanent kingdom.
12. Unlock more challenging battles.

A typical mission should last 5–10 minutes.

Early missions may last 3–5 minutes.

The game should provide satisfying progress
within the first 30 seconds.

Players must be able to pause a battle.

Support saving and resuming a battle where practical,
without requiring internet access.

IMPORTANT:

The game must include genuine real-time strategy.

The player should actively control buildings,
workers, resource collection, and troop movement.

Do not replace the RTS gameplay with purely automatic
combat or a Clash of Clans-style deployment-only system.

====================================================
SECTION 3 — GAME VISUAL STYLE
====================================================

This is EXTREMELY IMPORTANT.

I want a beautiful, polished, premium-looking game.

Not generic Unity prototype graphics.

Not programmer art as the final target.

Not cheap-looking mobile game UI.

ART DIRECTION:

Stylized miniature medieval fantasy kingdoms.

Think beautiful handcrafted tabletop dioramas.

Every building, tree, road, and soldier should look
like a miniature object placed in a detailed world.

AESTHETIC:

- Soft stylized 3D
- Low-poly geometry with thoughtful detail
- Warm natural lighting
- Beautiful grass and terrain
- Small medieval houses
- Charming castles
- Stone pathways
- Trees and forests
- Rivers and bridges
- Tiny animated soldiers
- Attractive particle effects
- Subtle ambient movement
- Clear silhouettes
- Cohesive color grading

Avoid excessive visual clutter.

The graphics should look sophisticated and intentional.

COLOR PALETTE:

Grass: #85A66A
Forest: #466C50
Gold: #D8AD57
Stone: #B9B0A0
Wood: #916847
Cream: #F4ECDD
Deep navy: #202C39
Warm red: #BB665A

These are starting references, not hard restrictions.

LIGHTING:

Soft directional sunlight.

Subtle shadows.

Warm highlights.

Ambient occlusion where performance permits.

Use URP settings suitable for battery-efficient
mobile rendering.

BUILDINGS:

Each building should have an identifiable silhouette.

A player must distinguish a barracks from a town hall
immediately without reading a label.

CASTLES:

The castle is the visual centerpiece.

Use stone walls, towers, banners, wooden gates,
and other details appropriate for a stylized medieval
fantasy setting.

When castles take damage, communicate it visually.

Possible effects:

- Cracks
- Dust
- Small falling stones
- Damaged flags
- Destruction animation

These effects must remain lightweight on iPhone.

SOLDIERS:

Tiny stylized 3D characters.

Readable from the isometric camera.

Use distinct color accents for friendly and enemy
armies.

Unit types should be visually recognizable.

ANIMATION:

Idle
Walking
Gathering
Constructing
Attacking
Taking damage
Dying
Victory

Animations should be smooth and satisfying.

If professional animated assets are unavailable,
create functional procedural or code-driven animations.

Do not pretend procedural placeholders are equivalent
to final professional assets.

Build an asset replacement pipeline so higher-quality
models and animations can be integrated later.

====================================================
SECTION 4 — IPHONE UI/UX
====================================================

Design the game specifically for iPhone.

Do not build a desktop interface and simply shrink it.

The UI should feel like a premium mobile product.

VISUAL DESIGN:

Minimal.
Elegant.
Highly polished.
Readable.
Consistent.
Tactile.

Use carefully balanced typography, spacing,
shadows, icons, and transitions.

Avoid ugly default Unity buttons.

Support Dynamic Island/notch safe areas
and the home indicator.

Avoid putting important buttons near unsafe regions.

MAIN MENU:

Beautiful animated medieval kingdom background.

Prominent PLAY button.

Secondary navigation:

- Campaign
- Skirmish
- Kingdom
- Shop
- Settings

Display current progression and kingdom level.

The main menu should make the player excited
to enter the game.

CAMPAIGN SCREEN:

A beautiful fantasy world map.

Territories represented by nodes or kingdoms.

Unlocked territories display clearly.

Locked territories have an attractive visual state.

Show conquered regions.

When a kingdom is conquered, visually update
the campaign map.

KINGDOM SCREEN:

Display the player's permanent home kingdom.

Buildings improve visually as upgrades unlock.

Allow inspection of buildings.

Display permanent progression.

BATTLE SCREEN:

Top:

- Wood
- Food
- Gold
- Pause

Center:

- Actual battlefield

Bottom:

Context-sensitive command panel.

Examples:

When a worker is selected:
Move, Gather, Build.

When barracks are selected:
Train swordsman, Train archer.

When soldiers are selected:
Move, Attack, Stop, Select group.

When a construction tile is selected:
Available buildings and their costs.

Keep the UI as uncluttered as possible.

BATTLE VICTORY SCREEN:

Beautiful victory animation.

Display:

- Mission name
- Stars earned
- Time taken
- Units remaining
- Base rewards

Include a clear button to collect rewards.

Optionally offer a rewarded advertisement
for an additional reward.

The standard reward must always remain available.

DEFEAT SCREEN:

Display a clear reason for defeat.

Allow immediate free retry.

Optionally offer a rewarded advertisement
for one limited revival or recovery opportunity.

Never make watching ads mandatory to keep playing.

UI ANIMATIONS:

Use:

- Smooth easing
- Small button press animations
- Subtle reward animations
- Animated resource counters
- Smooth camera transitions
- Pleasant menu transitions

Keep transitions responsive.

Avoid long unskippable animations.

====================================================
SECTION 5 — CAMERA AND TOUCH CONTROLS
====================================================

Mobile controls are a major priority.

Implement:

One-finger drag:
Pan the battlefield camera when touching empty terrain.

Tap unit:
Select unit.

Tap building:
Open contextual building commands.

Tap destination:
Move selected military units or workers.

Tap enemy:
Order selected combat units to attack.

Drag selection:
Provide an intuitive way to select multiple units.

Pinch:
Zoom in and out.

Double-tap:
Select nearby units of the same type, where useful.

Dedicated UI button:
Select all available combat units.

Dedicated UI button:
Center camera on the home town hall.

Support appropriate visual feedback:

Selection circles.
Destination indicators.
Attack markers.
Unit health bars.
Construction previews.

SELECTION RULES:

A selected unit must be visually identifiable.

Selection should remain reliable with small 3D
characters on iPhone displays.

Use generous touch hit areas or screen-space
selection assistance without making the player
select the wrong units.

CAMERA:

Use an orthographic isometric camera.

Provide configurable minimum and maximum zoom.

Clamp camera movement to the battlefield.

Prevent camera movement while interacting with UI.

Implement gesture handling carefully.

Do not let pinch zoom accidentally issue troop commands.

Test controls at multiple iPhone screen sizes.

====================================================
SECTION 6 — RESOURCES AND ECONOMY
====================================================

BATTLE RESOURCES:

Wood
Food
Gold

WOOD:

Workers collect wood from trees.

FOOD:

Workers collect food from farms.

GOLD:

Workers mine gold deposits.

RESOURCES MUST HAVE ACTUAL PURPOSES.

Example initial building costs:

Town Hall: starting building
House: 50 wood
Farm: 40 wood
Barracks: 100 wood + 50 gold
Archery Range: 100 wood + 75 gold
Watchtower: 80 wood + 60 gold
Stable: 150 wood + 100 gold

These numbers are starting values.

Expose costs in editable game-balance data.

Do not hardcode balance values throughout gameplay.

WORKER BEHAVIOR:

Workers should:

1. Receive a gathering command.
2. Walk to a resource.
3. Play a gathering animation.
4. Collect resources over time.
5. Return resources to the appropriate drop-off.
6. Repeat until reassigned or the resource is depleted.

Use simplified yet believable gathering behavior.

BUILDING CONSTRUCTION:

Select a worker.

Open build menu.

Choose structure.

Preview placement on the terrain.

Valid placement = clear visual indicator.

Invalid placement = clear visual indicator.

Tap to confirm.

Worker moves to the location and constructs it.

Buildings should visually develop as construction
progresses.

====================================================
SECTION 7 — BUILDINGS
====================================================

INITIAL BUILDINGS:

1. Town Hall
2. House
3. Farm
4. Barracks
5. Archery Range
6. Watchtower

Additional buildings for later phases:

7. Stable
8. Siege Workshop
9. Storage building
10. Defensive wall

TOWN HALL:

Main base.

Creates workers.

Stores delivered resources.

Its destruction can determine defeat.

HOUSE:

Increases population capacity.

FARM:

Produces food.

BARRACKS:

Trains swordsmen.

ARCHERY RANGE:

Trains archers.

WATCHTOWER:

Automatically attacks enemies within range.

STABLE:

Trains cavalry.

SIEGE WORKSHOP:

Trains siege units.

WALL:

Blocks enemy movement and protects buildings.

BUILDING REQUIREMENTS:

Every building needs:

- Health
- Construction cost
- Construction time
- Team ownership
- Visual state
- Damage behavior
- Destruction behavior
- Context-sensitive commands

Buildings should be represented by reusable data
definitions rather than duplicated logic.

====================================================
SECTION 8 — MILITARY UNITS
====================================================

INITIAL UNIT TYPES:

WORKER

Role: gathering and construction.

SWORDSMAN

Role: melee combat.

ARCHER

Role: ranged combat.

SPEARMAN

Role: anti-cavalry.

ADDITIONAL UNITS:

CAVALRY

Fast attacks and flanking.

CATAPULT

Slow siege unit with heavy building damage.

BALANCE:

Swordsmen perform well against lightly defended units.

Archers attack from distance.

Spearmen counter cavalry.

Cavalry move quickly and threaten ranged units.

Catapults damage structures effectively but are
vulnerable to close-range attacks.

IMPLEMENT:

- Health
- Damage
- Attack speed
- Attack range
- Movement speed
- Target selection
- Unit collision
- Death behavior
- Unit production
- Basic combat animations
- Formation-aware group movement

COMBAT FEEDBACK:

Health bars appear contextually.

Attacks should provide:

- Animation
- Impact effects
- Sound effects
- Damage feedback

Use object pooling for arrows, projectiles,
impact effects, and temporary visual objects.

Avoid expensive full-physics simulations for
large numbers of small units.

Make attacks feel powerful without excessive effects.

====================================================
SECTION 9 — ENEMY AI
====================================================

THIS GAME MUST WORK COMPLETELY OFFLINE.

DO NOT USE:

- OpenAI API
- Claude API
- External LLM APIs
- Hosted AI services
- Multiplayer servers
- Cloud inference

Enemy behavior should run on the iPhone.

Implement deterministic gameplay AI with clear
state machines and decision-making systems.

ENEMY AI CAPABILITIES:

Resource gathering
Worker allocation
Building construction
Unit production
Base defense
Threat detection
Army assembly
Target prioritization
Attacking
Retreating
Counterattacking

AI DIFFICULTIES:

Easy
Normal
Hard

EASY:

Slower decisions.

Smaller armies.

Less coordinated attacks.

NORMAL:

Balanced resource management.

Reasonable unit production.

Occasional attacks.

HARD:

More strategic army composition.

Better defensive responses.

More aggressive expansion.

Better threat prioritization.

Do not implement difficulty merely by giving
enemies absurd amounts of health.

AI STATES:

Develop economy
Build defenses
Prepare army
Attack
Defend
Recover

The AI should transition intelligently between states.

EXAMPLE:

If enemy soldiers approach the base,
defend the town hall.

If resources are sufficient and the army is large,
launch an attack.

If a large enemy army is detected,
prioritize defensive units.

If the town hall is critically damaged,
focus available units on defense.

AI should be challenging but understandable.

PLAYER EXPERIENCE:

Players should feel that enemy kingdoms are
making decisions rather than following a
single obvious script.

====================================================
SECTION 10 — PATHFINDING
====================================================

Implement robust pathfinding.

Requirements:

- Units avoid buildings.
- Units avoid impassable terrain.
- Units navigate around obstacles.
- Units reach resource nodes.
- Workers can reach building sites.
- Soldiers can reach attack targets.
- Multiple soldiers can move without excessively
  overlapping each other.

Use grid-based A* or another well-supported
approach appropriate for the game's scale.

Add lightweight local avoidance or group spacing.

Avoid recalculating full paths every frame.

Implement path request scheduling or caching
where useful.

Units should handle unreachable targets gracefully.

Never let a failed path request freeze the game.

Target a manageable number of active units for
the initial iPhone release.

Use profiling to determine safe limits.

====================================================
SECTION 11 — CAMPAIGN MODE
====================================================

Create an expandable campaign system.

INITIAL TARGET:

12 playable main campaign missions.

Make the mission definitions data-driven.

CAMPAIGN THEME:

A fantasy medieval world divided into kingdoms.

The player starts with a small settlement and
gradually unifies the continent.

TERRITORIES:

Greenwood Valley
Stonebridge
The Iron Hills
Sunfall Plains
Ashen Frontier
The Crownlands

Create original world-building and naming.

The campaign should contain a progression of:

Small enemy camps
Fortified villages
Stone castles
Multi-base armies
Defensive strongholds
Final enemy capital

MISSIONS SHOULD INTRODUCE MECHANICS GRADUALLY.

MISSION 1:

Teach selection and movement.

MISSION 2:

Teach gathering resources.

MISSION 3:

Teach constructing a barracks.

MISSION 4:

Teach recruiting soldiers.

MISSION 5:

Teach ranged combat.

MISSION 6:

Introduce defensive towers.

MISSION 7:

Introduce enemy raids.

MISSION 8:

Introduce stronger enemy bases.

MISSIONS 9–12:

Combine previously introduced mechanics
into increasingly challenging battles.

MISSION OBJECTIVES:

Destroy enemy town hall.
Defend settlement.
Survive attacks.
Capture an enemy position.
Destroy fortified castle.

Each level should have:

- Mission title
- Terrain configuration
- Starting resources
- Starting units
- Enemy forces
- Difficulty
- Victory condition
- Defeat condition
- Reward
- Optional star objectives

CAMPAIGN MAP:

Visually represent conquered kingdoms.

Unlock subsequent territories through progression.

Support replaying completed levels.

Do not require internet connectivity.

====================================================
SECTION 12 — REPLAYABLE SKIRMISH MODE
====================================================

THIS IS VERY IMPORTANT FOR RETENTION.

Players should not run out of gameplay after
completing the campaign.

Implement offline skirmishes.

PLAYER OPTIONS:

Difficulty:
Easy / Normal / Hard

Map:
Forest / Plains / Highlands

Enemy:
One AI kingdom

Optional modifiers:

Starting resources
Army size
Battle difficulty

Skirmish maps can be generated from curated layouts
and deterministic random seeds.

Avoid completely random terrain that creates
unplayable or unfair maps.

Every skirmish must have valid:

Starting positions
Resource locations
Navigable paths
Buildable terrain
Enemy base placement

Players can replay skirmishes indefinitely.

Give appropriate progression rewards while preventing
a trivial infinite-reward exploit.

Save player preferences between sessions.

====================================================
SECTION 13 — PERMANENT KINGDOM PROGRESSION
====================================================

Separate temporary battle economy from permanent
metagame progression.

BATTLE RESOURCES:

Wood
Food
Gold

These reset between individual battles.

PERMANENT CURRENCY:

Crowns

Earn crowns by:

- Completing campaign missions
- Winning skirmishes
- Completing optional objectives
- Earning mission stars

Crowns can unlock:

- New starting-unit options
- Decorative kingdom buildings
- Cosmetic upgrades
- Campaign progression upgrades
- Selected strategic perks

BALANCING:

Permanent upgrades must not destroy the
challenge of the campaign.

Avoid extremely powerful upgrades that make
all battles trivial.

The home kingdom should evolve visually.

PLAYER KINGDOM STAGES:

Small settlement
Growing village
Fortified town
Stone castle
Royal stronghold

Each stage should make the home screen
more visually impressive.

The main objective is to provide a long-term
sense of achievement.

====================================================
SECTION 14 — MONETIZATION
====================================================

THIS IS A COMMERCIAL PRODUCT.

I WANT TO MAKE MONEY FROM THE GAME.

However, monetization must not ruin gameplay,
mislead users, or violate App Store policies.

IMPLEMENT THREE REVENUE SYSTEMS:

1. Rewarded advertisements
2. In-app purchases
3. Premium campaign expansions

----------------------------------------
A. GOOGLE ADMOB
----------------------------------------

Use the official Google Mobile Ads Unity integration.

Implement rewarded advertisements.

Do not initially use forced interstitial ads.

Do not display banner ads during battles.

REWARDED AD PLACEMENT 1:

After a victory.

Example:

VICTORY!

Base reward: 100 Crowns

Button A:
Collect 100 Crowns

Button B:
Watch Ad — Collect 200 Crowns

The player must always receive the normal
reward without watching an advertisement.

REWARDED AD PLACEMENT 2:

After defeat.

Example:

YOUR KINGDOM HAS FALLEN

Button A:
Retry for Free

Button B:
Watch Ad — Recover Part of Your Army

Implement this only where revival will not break
mission objectives or save state.

REWARDED AD PLACEMENT 3:

Optional post-battle bonus.

Allow an additional modest reward through
watching a voluntary advertisement.

Apply reasonable per-session or per-mission caps.

Do not repeatedly prompt users with advertisements.

AD TECHNICAL REQUIREMENTS:

Use official AdMob test ad units during development.

Never use real production ads for automated testing.

Create an AdvertisingService abstraction.

Example responsibilities:

Initialize()
LoadRewardedAd()
IsRewardedAdReady()
ShowRewardedAd()
HandleRewardEarned()
HandleAdFailure()
HandleAdClosed()

Ensure rewards are granted only after receiving
the appropriate reward-earned callback.

Handle failed or unavailable ads gracefully.

Disable ad buttons when no ad is ready,
or provide an understandable unavailable state.

Never block normal gameplay because an ad fails.

Prevent double rewards from duplicate callbacks.

Ensure rewards are granted exactly once.

Use safe asynchronous state management.

Ensure SDK callbacks modifying Unity objects
execute on the Unity main thread when required.

Properly dispose of ad objects.

Do not hardcode personal production credentials.

Provide configuration fields for my future:

AdMob App ID
Rewarded Ad Unit ID

Use Google's officially documented test IDs.

Prepare the game to work with zero downloads.

Document the publishing, app-ads.txt verification,
and AdMob app-readiness review workflow.

Use the latest official SDK guidance rather than
outdated snippets.

----------------------------------------
B. IN-APP PURCHASES
----------------------------------------

Implement purchases through Apple's App Store
using a supported Unity IAP version.

Do not use an external payment system for ordinary
digital game purchases.

Create a polished premium shop.

INITIAL PRODUCTS:

Product 1:
Premium Campaign Expansion

Proposed product ID:
com.empirerush.campaign.expansion1

Type:
Non-consumable

Unlocks a separate premium campaign containing
additional missions.

Product 2:
Royal Castle Cosmetic Pack

Proposed product ID:
com.empirerush.cosmetic.royalcastle

Type:
Non-consumable

Unlocks cosmetic castle appearances.

Product 3:
Founder's Edition

Proposed product ID:
com.empirerush.founder

Type:
Non-consumable

Unlocks defined premium cosmetic content
and a clearly specified premium campaign.

Prevent duplicate entitlements if multiple
products grant the same content.

These are proposed product identifiers.
Final identifiers must match App Store Connect.

PRICING:

Do not hardcode store prices.

Read localized prices from the App Store.

Suggested testing prices:

Premium campaign: $3.99
Cosmetic pack: $1.99
Founder's Edition: $7.99

Actual pricing will be configured by me
in App Store Connect.

Implement:

- Purchase initiation
- Purchase pending state
- Purchase completion
- Purchase failure
- Restoring purchases
- Purchased-product persistence
- Entitlement restoration after reinstall
- Offline access to previously unlocked content
- Proper transaction handling
- Prevention of duplicate fulfillment

Do not treat a button tap as proof of purchase.

Implement an IAP service interface.

For Editor testing, use an explicitly labeled
development mock purchase provider.

Production builds must never award premium content
from a mock purchase success.

Do not expose a hidden debug purchase shortcut
in release builds.

Handle missing product configuration gracefully.

Provide an App Store Connect setup guide.

----------------------------------------
C. PREMIUM CAMPAIGN EXPANSIONS
----------------------------------------

Build the campaign architecture so future premium
territories can be added without changing core systems.

Each expansion can introduce:

New maps
New enemy kingdoms
New mission objectives
New cosmetic themes
New challenges

INITIAL PREMIUM EXPANSION TARGET:

6 additional data-driven missions.

If time or tooling constraints prevent completing
all expansion content during the first pass,
implement the full entitlement and mission-loading
architecture first.

Do not advertise unimplemented paid content
as available for purchase.

The base game must still offer generous free
gameplay.

----------------------------------------
MONETIZATION PHILOSOPHY
----------------------------------------

The goal is sustainable revenue through:

Strong retention
Optional ad engagement
Trustworthy purchases
Replayability
High-quality gameplay

Never intentionally make the free game frustrating
to force purchases.

Do not invent misleading scarcity.

Avoid gambling-like paid random rewards.

Avoid manipulative ad flows.

====================================================
SECTION 15 — RETENTION
====================================================

Build natural reasons for players to return.

INITIAL RETENTION SYSTEMS:

- Campaign progression
- Skirmish replayability
- Permanent kingdom upgrades
- Mission stars
- Achievements
- Unlockable cosmetic content
- Difficulty progression
- Personal best performance

ACHIEVEMENTS:

First Victory
First Castle Destroyed
10 Battles Won
100 Soldiers Trained
10 Kingdoms Conquered
Victory Without Losing a Unit

Achievements must be based on genuine tracked
gameplay events.

Do not require online accounts.

OPTIONAL FUTURE FEATURES:

Daily missions
Special challenges
New civilizations
Additional campaigns

Prioritize game quality over artificially
maximizing session time.

====================================================
SECTION 16 — GAME ECONOMY BALANCING
====================================================

Implement the economy using editable configuration.

Avoid scattering numbers throughout the codebase.

Use ScriptableObjects or another maintainable
data-driven solution for:

Unit stats
Building stats
Resource costs
Training duration
Construction duration
Mission rewards
Difficulty modifiers
Enemy spawn composition
Campaign unlock requirements

Provide a balancing document.

The initial economy should target:

First troop trained within approximately
the first minute in an introductory battle.

First meaningful combat encounter within
the first two minutes.

Early missions completed within 3–5 minutes.

Standard missions completed within 5–10 minutes.

Avoid excessive waiting.

Avoid hour-long construction timers.

Balance progression so ads are attractive
but not required.

====================================================
SECTION 17 — SAVE SYSTEM
====================================================

The game should be fully offline-first.

Implement reliable local saving.

Persist:

Campaign progress
Unlocked territories
Mission stars
Permanent currency
Kingdom upgrades
Purchased entitlements
Achievements
Settings
Audio preferences
Tutorial completion
Skirmish preferences

Use a versioned save format.

Use safe file writing techniques to reduce
the risk of corrupted saves.

Handle:

First installation
Missing save files
Corrupted save files
App termination
App backgrounding
Game updates
Save schema migration

Avoid storing important game progression
only in volatile memory.

Use PlayerPrefs only for suitable simple settings.

Implement save-data validation.

Do not confuse locally cached premium entitlements
with verified store ownership.

Restore purchases using the platform store.

NOTE:

Without a backend, local saves and consumable
rewards cannot be made completely tamper-proof.

Do not pretend client-side encryption or obfuscation
creates server-grade security.

====================================================
SECTION 18 — SOUND DESIGN
====================================================

Sound is extremely important for satisfaction.

Implement an audio system supporting:

Background music
Battle music
UI interactions
Resource gathering
Building construction
Unit training
Sword impacts
Arrow impacts
Castle destruction
Victory
Defeat

The soundscape should feel warm,
fantastical, and medieval.

Keep music subtle.

Use satisfying but restrained sound effects.

Support separate volume controls for:

Music
Sound effects

Respect the device's audio behavior.

Handle interruptions such as phone calls.

If licensed audio assets are unavailable,
provide suitable temporary audio assets or
procedural effects.

Make it simple to replace them later.

Do not include copyrighted game music
without appropriate rights.

====================================================
SECTION 19 — PERFORMANCE OPTIMIZATION
====================================================

Treat mobile optimization as an essential feature.

TARGET:

Smooth gameplay.
Low battery consumption.
Reasonable memory usage.
Fast loading.
No persistent frame stutter.

IMPLEMENT:

Object pooling
Efficient update scheduling
Optimized pathfinding
Reasonable mesh complexity
Material sharing
GPU instancing where useful
LOD where justified
Limited particle effects
Texture compression
Reduced overdraw
Efficient UI redraw behavior

Do not instantiate and destroy hundreds of
temporary objects during combat.

Avoid unnecessary allocations during gameplay.

Avoid unnecessary expensive physics calculations.

Ensure enemy decision-making does not run
every frame for every unit.

Create configurable quality presets.

Use device profiling to choose actual limits.

INITIAL GAMEPLAY SCALE:

Target approximately 40–80 simultaneous
active units before increasing scope.

This is an engineering starting target,
not a guaranteed device capability.

Performance must be measured on real devices.

====================================================
SECTION 20 — SOFTWARE ARCHITECTURE
====================================================

Build a modular Unity project.

Avoid one enormous GameManager script.

Use clear separation of responsibilities.

SUGGESTED STRUCTURE:

Assets/
    _EmpireRush/
        Art/
        Audio/
        Materials/
        Prefabs/
        Scenes/
        Scripts/
            Core/
            Camera/
            Input/
            Combat/
            Units/
            Buildings/
            Economy/
            AI/
            Pathfinding/
            Campaign/
            Progression/
            Monetization/
            SaveSystem/
            UI/
            Audio/
            Utilities/
        Data/
            Units/
            Buildings/
            Missions/
            Economy/
            Difficulty/
        Resources/
        Tests/
            EditMode/
            PlayMode/
        Editor/

SCENES:

Bootstrap
MainMenu
Kingdom
CampaignMap
Battle
Shop

Use clean interfaces where appropriate.

EXAMPLE SYSTEMS:

GameBootstrap
GameStateManager
BattleManager
BattleObjectiveManager
ResourceManager
UnitController
UnitSelectionManager
BuildingController
BuildingPlacementSystem
UnitProductionSystem
CombatSystem
ProjectilePool
PathfindingManager
EnemyAIController
CampaignManager
ProgressionManager
SaveManager
AdvertisingService
PurchaseService
AudioManager
UIManager

You may rename or reorganize these based on
good software architecture.

Avoid unnecessary abstraction.

Prefer readable, testable code.

Use events where appropriate.

Document important design decisions.

====================================================
SECTION 21 — AUTOMATED TESTING
====================================================

Create tests for important gameplay systems.

UNIT TESTS:

Resource deductions
Unit costs
Building costs
Health and damage calculations
Campaign unlocking
Reward calculations
Save serialization
Save migration
Achievement progression
Purchase entitlement mapping

PLAYMODE TESTS:

Unit selection
Unit movement
Gathering resources
Constructing a building
Training a unit
Attacking enemy units
Destroying buildings
Win conditions
Defeat conditions
Enemy AI decisions
Mission completion
Local save and restore

MONETIZATION TESTS:

Reward granted exactly once
Ad failure does not block the game
Unavailable ads cannot be shown
Purchase cancellation gives no entitlement
Successful purchase unlocks content
Restoring purchases restores entitlements
Duplicate purchase events do not duplicate fulfillment
Production builds cannot use mock purchase bypasses

If the environment supports Unity batch-mode tests,
execute them.

Show actual test results.

Fix failing tests where possible.

Do not invent test results.

====================================================
SECTION 22 — IOS APP STORE REQUIREMENTS
====================================================

Prepare the game for Apple App Store submission.

As of October 2026, App Store uploads must use
Xcode 26 or later and an iOS 26-or-later SDK.

Verify the current requirements again
before preparing the final release.

SUPPORT:

iPhone
Modern iOS versions
Portrait orientation
Safe areas
Touch interactions

Prepare:

Application identifier configuration
App icons
Launch screen
Privacy policy requirements
App Store screenshots
App description
App subtitle
App keywords
Age rating information
In-app purchase descriptions
Advertising disclosures
App privacy disclosures
TestFlight preparation

PRIVACY:

Use the appropriate current Google UMP
consent flow where required.

Respect user consent choices.

Implement App Tracking Transparency only
when the app's tracking behavior requires it.

Do not request tracking permission unnecessarily.

Account for applicable privacy requirements.

Provide a way to report inappropriate
or age-inappropriate advertisements.

Use age-appropriate ad content.

Never disguise advertisements as gameplay buttons.

Treat privacy compliance as a release blocker,
not an optional polish item.

APP STORE:

The game must not require account registration.

No online connection should be required for
core gameplay.

Do not add multiplayer infrastructure.

Provide a clear Restore Purchases option.

Clearly distinguish free content from paid content.

All external SDKs must be initialized responsibly.

====================================================
SECTION 23 — ANALYTICS
====================================================

Create a lightweight analytics abstraction.

It should support recording meaningful game events
without forcing a backend dependency.

EVENTS:

game_started
tutorial_started
tutorial_completed
battle_started
battle_won
battle_lost
mission_completed
skirmish_completed
rewarded_ad_offered
rewarded_ad_started
rewarded_ad_completed
rewarded_ad_failed
shop_opened
purchase_started
purchase_completed
purchase_failed

Include useful properties such as:

Mission ID
Difficulty
Battle duration
Player progression level
Reward type

Use privacy-conscious implementation.

Provide a local debug logging implementation.

An optional external analytics provider can be
added later if approved and configured.

Do not claim local-only logs provide remote
production analytics.

====================================================
SECTION 24 — APP ICON AND BRANDING
====================================================

Create an original brand identity.

STYLE:

Premium fantasy strategy.

Simple, iconic, readable at small sizes.

APP ICON CONCEPT:

A beautiful stylized medieval castle.

A small gold crown or royal banner.

Deep navy and warm gold palette.

Clean composition.

Recognizable silhouette.

Not photorealistic.

No tiny unreadable text.

Generate implementation-ready icon assets
if suitable asset-generation tooling is available.

Otherwise, prepare a precise asset specification
and temporary original icon.

Do not download copyrighted competitor icons.

====================================================
SECTION 25 — DEVELOPMENT EXECUTION PLAN
====================================================

You must build this in verified milestones.

Do not attempt to write an entire game in
one enormous untested code dump.

Each milestone should produce something runnable.

----------------------------------------
MILESTONE 1 — PROJECT FOUNDATION
----------------------------------------

Create Unity project.

Configure:

URP
Portrait orientation
iOS platform
Input system
Project settings
Folder structure
Boot scene
Main menu

Generate the initial visual environment.

Create an isometric camera.

Create a terrain.

Create a town hall.

Create basic UI.

ACCEPTANCE CRITERIA:

The project opens.

The scene loads.

The camera works.

The player sees a beautiful small settlement.

----------------------------------------
MILESTONE 2 — FIRST PLAYABLE RTS
----------------------------------------

Implement:

Unit selection
Worker movement
Resource gathering
Building placement
Barracks construction
Swordsman training
Basic combat
Enemy town hall
Victory condition

ACCEPTANCE CRITERIA:

I can start with workers.

Gather wood and gold.

Build a barracks.

Train soldiers.

Command soldiers to attack an enemy base.

Destroy the enemy town hall.

Win the mission.

This is the most important milestone.

----------------------------------------
MILESTONE 3 — COMPLETE CORE GAMEPLAY
----------------------------------------

Implement:

Archers
Additional buildings
Enemy AI economy
Enemy troop production
Base defense
Additional mission objectives
Improved pathfinding
Combat animations
Audio
Polished victory/defeat screens

ACCEPTANCE CRITERIA:

I can play a complete strategic battle
against an active AI kingdom.

----------------------------------------
MILESTONE 4 — GAME PROGRESSION
----------------------------------------

Implement:

Main menu
Campaign map
Mission system
12 main missions
Local progression
Permanent kingdom
Kingdom upgrades
Skirmish mode
Save system
Achievements

ACCEPTANCE CRITERIA:

The game has meaningful progression
and can be replayed offline.

----------------------------------------
MILESTONE 5 — COMMERCIAL FEATURES
----------------------------------------

Implement:

AdMob abstraction
Real AdMob integration
Rewarded ads
Unity IAP integration
Shop UI
Entitlements
Restore purchases
Premium content structure
Privacy and consent handling

ACCEPTANCE CRITERIA:

Test ads work.

Real purchase sandbox flows work
when App Store configuration is available.

No fake purchases are present in release builds.

----------------------------------------
MILESTONE 6 — VISUAL POLISH
----------------------------------------

Improve:

Models
Animations
Lighting
UI styling
Particle effects
Audio
Combat feedback
Transitions
Loading screens
Game feel

ACCEPTANCE CRITERIA:

The game should feel like a cohesive
commercial mobile product rather than
an engineering demo.

----------------------------------------
MILESTONE 7 — IOS RELEASE
----------------------------------------

Prepare:

Final app icon
Release configuration
iPhone build
Xcode project
TestFlight build instructions
App Store metadata
Privacy disclosures
In-app purchase configuration
Advertising setup documentation
Testing checklist

ACCEPTANCE CRITERIA:

The project can be built for iPhone.

A valid release can be prepared using
the necessary Apple accounts and credentials.

Actual App Store submission requires my
account access and final approval.

====================================================
SECTION 26 — ENGINEERING REQUIREMENTS
====================================================

Before starting:

1. Inspect the development environment.
2. Detect the installed Unity version.
3. Detect Xcode.
4. Check available build tooling.
5. Identify missing dependencies.
6. Choose a compatible project configuration.
7. Create a development plan.

Then BEGIN IMPLEMENTING THE GAME.

Do not stop after writing the plan.

Create files and scenes.

Use Unity Editor scripting where helpful to
generate scenes, prefabs, materials, and
initial content reproducibly.

The project should not depend on manually
dragging hundreds of objects into scenes.

Automate repetitive setup.

If command-line testing is available,
use it.

If Unity cannot be executed,
still create a coherent, correctly structured
Unity project and document what remains unverified.

Use current, supported APIs.

Avoid deprecated Unity patterns.

Avoid imaginary SDK functions.

If integrating an unfamiliar SDK,
consult its official documentation.

Do not use placeholder API keys.

Do not secretly require paid third-party
services to run the game.

Avoid purchasing Unity Asset Store assets
without my permission.

Prefer original procedural assets and
freely licensed resources with verified licenses.

For every milestone:

- Implement the feature.
- Connect it to the actual game.
- Check for compilation errors.
- Test the critical functionality.
- Fix issues.
- Update documentation.
- State what is complete.
- State what remains unverified.

Do not leave a feature represented only by
an empty class or a nonfunctional button.

====================================================
SECTION 27 — QUALITY STANDARDS
====================================================

My expectation is a premium App Store game.

Prioritize:

1. Excellent core gameplay
2. Responsive touch controls
3. Attractive and cohesive art
4. Stable performance
5. Intelligent enemy behavior
6. Rewarding progression
7. Fair monetization
8. Reliable offline functionality
9. Clean architecture
10. App Store readiness

Do not prioritize sheer feature count over quality.

A polished game with six buildings and
four troop types is better than a broken game
with twenty buildings and fifty troop types.

The goal is to make a strategy game people
genuinely want to play.

The commercial ambition is strong player
retention and sustainable monetization.

Revenue is an objective, not a guaranteed outcome.

====================================================
SECTION 28 — FIRST SESSION EXPERIENCE
====================================================

Pay special attention to the first five minutes.

When the player launches the game for the
first time:

They should immediately see a beautiful kingdom.

The first screen should clearly communicate
that this is a strategy and conquest game.

The PLAY button should be obvious.

The player should enter the first mission quickly.

Teach controls through actions.

Do not begin with a wall of tutorial text.

Use a guided introduction:

"Select your worker."

"Gather wood."

"Build your barracks."

"Train your soldiers."

"Attack the enemy castle."

"Victory!"

The first victory should feel satisfying.

The player should immediately understand
why they would want to play the next battle.

After the first victory:

Show campaign progression.

Show the permanent kingdom developing.

Introduce additional strategic possibilities.

Do not show aggressive purchase prompts
during the first tutorial.

====================================================
SECTION 29 — DELIVERABLES
====================================================

Deliver a complete working Unity project.

Include:

1. Unity project files
2. C# source code
3. Working scenes
4. Prefabs
5. ScriptableObject data
6. Original/generated art assets
7. UI assets
8. Audio assets where available
9. Automated tests
10. iOS build configuration
11. Monetization integrations
12. Save system
13. Campaign content
14. Skirmish mode
15. README
16. Development documentation
17. Build instructions
18. App Store preparation checklist

Create a DEVELOPMENT_STATUS.md file.

It should clearly explain:

Completed features
Partially completed features
Known bugs
Unverified functionality
Next steps
Required external account configuration

Create a GAME_BALANCE.md file.

Create a MONETIZATION_SETUP.md file.

Create an IOS_RELEASE.md file.

====================================================
SECTION 30 — EXECUTION INSTRUCTIONS
====================================================

IMPORTANT:

I am not asking you to explain how somebody
could theoretically build this game.

I am asking YOU to build it.

Operate as the lead developer.

Make implementation decisions.

Create the project.

Build the gameplay.

Generate the foundational assets.

Implement the architecture.

Keep the project runnable at each milestone.

Do not respond with only a roadmap.

Do not produce pseudocode as a substitute
for actual implementation.

Do not claim completion until the actual
requirements have been implemented and verified.

Be ambitious about visual quality, but realistic
about engineering scope.

If everything cannot be completed in one execution
session, prioritize the fully playable first
RTS battle and maintain a clear record of progress.

After the first playable version works,
continue through the remaining milestones.

FIRST ACTION:

Inspect my Mac development environment and
check whether Unity, Xcode, and the required
build tools are installed.

Then establish the project structure.

Then implement Milestone 1.

After Milestone 1, immediately proceed toward
Milestone 2, the playable RTS battle.

Do not spend the entire session discussing
architecture.

CREATE THE GAME.
```

---

## Three important things before you use this

**1. Use Claude Code, not just the regular Claude chat.** Claude Code can work directly on project files and run development commands. The prompt deliberately tells it to produce a runnable game one milestone at a time, rather than generating hundreds of disconnected scripts.

**2. The monetization requirements are based on current official documentation.** Google's Unity plugin supports rewarded advertisements, and Unity IAP provides store purchase integration. AdMob uses test advertisements during development; full ad serving requires a published and approved app. [Google for Developers](https://developers.google.com/admob/unity/rewarded?utm_source=chatgpt.com)

**3. Don't judge the game by its first generated 3D models.** AI-assisted coding is useful for gameplay systems, but a commercially attractive game's models, animations, UI, sounds, balance, and battle effects still need substantial refinement. The prompt establishes an asset pipeline for that reason.

### My most important recommendation

**Make Claude finish the first playable battle before expanding the game.**

You should be able to open the game on your iPhone, collect wood, build a barracks, recruit ten soldiers, attack an enemy castle, and watch it collapse.

Once that experience is genuinely fun, it's worth investing in the campaign, advertisements, premium purchases, and visual polish.

**A great five-minute RTS battle is the foundation of a game that can earn money.**