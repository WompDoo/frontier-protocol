# FRONTIER PROTOCOL
## Game Design Document — v0.4
*Living document. Update with every significant design decision.*
*Last updated: 2026-03-03 — Globe view golden baseline locked. High-detail procedural generation (2048×1024, 8-octave dual-layer noise, sqrt depth ocean gradient). Scout sprites integrated into ScoutView.*

---

## 0. The Real Pitch

Eight of Earth's wealthiest corporations — Amazon, Google, Blackwater, Tesla, Nestlé, Coca-Cola, Disney, and the remnants of the United Nations — have discovered a planet with a secret worth more than gold: its sap can theoretically grant immortality.

They have violated interplanetary law to send colonisation parties. They cannot send soldiers; they can send debtors.

You are directing a crew of people who owe money they will never be able to repay, dropped on a hostile alien planet, working for companies that have already abandoned one colonial expedition thirty years ago and left everyone to die.

The planet knows you are here. It is responding.

**Genre:** Roguelite / Strategy
**Platform:** PC (Steam)
**Target Price:** $12.99–$14.99

> **Design Note — On Tone:** This game is satirical, dark, and specific. It has the soul of Darkest Dungeon, the narrative momentum of Hades, and the corporate horror of a Black Mirror episode set on an alien world. The voice is dry, the humour is earned through tragedy, and the writing should always feel like it knows exactly what kind of game it is.

---

## 1. Concept Overview

Frontier Protocol is a sci-fi roguelite in which players direct scouting parties across a procedurally generated alien planet, returning between expeditions to grow a colony, operate a resource factory, and prepare for the next push into hostile wilderness.

The game is built around two interlocking compulsion loops:
- **The Run** — a tense isometric expedition into unknown territory with a squad of up to four named scouts.
- **The Meta** — colony-building and factory management where every discovery and every death translates into permanent progress.

### 1.1 Key Pillars

**Debt, not destiny.**
Scouts are not heroes on a grand mission. They are people who owe money to corporations that do not care if they survive. This is the emotional starting point. The player's job is to give them a reason to live anyway.

**Characters, not units.**
Each scout is procedurally generated but feels hand-crafted through accumulated experience, trauma, bonds, and scars. Their deaths must mean something. A veteran who has survived six expeditions and bonded with two squadmates is irreplaceable — and the game must make you feel that.

**A living world that fights back.**
The planet is a singular super-organism. Every path cut through the jungle will be reclaimed. Every threat escalates. The wilderness is not a dungeon — it is an immune system. The player is the virus.

**Every run matters.**
Failed expeditions are never wasted. Resources, scout XP, map data, and colony progress carry forward. A disastrous run that kills two scouts and yields nothing still changes the world state.

**Achievable scope.**
Designed from the ground up for a focused solo developer with the intent to ship. Scope is locked at each milestone. No new systems until the current ones work.

### 1.2 Elevator Pitch

XCOM's soldier permadeath and emotional investment. Hades' run-to-meta momentum. Darkest Dungeon's character trauma made survivable. Wrapped in an isometric roguelite with a corporate horror premise, a living alien planet, and the satirical observation that the people who destroyed Earth are now doing the same to somewhere else.

Completable in 15–20 hours. Immediately replayable.

---

## 2. The Corporate Sponsors — "The Infamous Eight"

Each campaign begins with selecting a Corporate Sponsor. In the lore, these are the only entities wealthy enough to bypass *The Treaty of Earth* and launch illegal Protocol-0 colonisation efforts.

This is not a cosmetic choice. The sponsor shapes the player's starting conditions, available gear, and — most importantly — their relationship to the colony's moral arc. Working for your sponsor efficiently makes you complicit in what they are actually trying to do.

| Sponsor | Real-World Parallel | Protocol Bonus | Lore |
|---|---|---|---|
| **A-Maze Logistics** | Amazon | +2 Inventory slots per scout; +15% Factory processing speed | "Everything, Everywhere, Instantly." They view the planet as one giant warehouse. |
| **G-Verse Analytics** | Google | Survey Office starts at Level 2; all chunk hazards (spore clouds, nests) immediately revealed | They don't want to live on the planet. They want to index it and sell the data. |
| **Aegis-Global Security** | Blackwater/PMC | Scouts start with +5 Precision; −20% cost on all combat fabrication | A private military group hired by the other seven for "security," who launched their own colony. They view botanical life as a training exercise. |
| **United Terran Administration** | Dying Governments | Landing Pad holds +2 recruits; Colony Buildings cost 10% less | The remnants of Earth's governments. Slow, paper-heavy, desperate to prove they are still relevant. |
| **X-Ether Motors** | Tesla/Musk | −15% Oxygen/Energy drain; +10 Ingenuity for all scouts | Run by a CEO who believes the planet is "a design flaw he can fix." Gear is sleek, white, and prone to beta-testing in the middle of a fight. |
| **Aqua-Pure Corp** | Nestlé | +20% Biological resource yield; Rations grant a temporary Speed buff | They have legally claimed the planet's water and sap as intellectual property. Scouts are "Hydration Technicians." |
| **Coke-Zero Defense** | Coca-Cola | Rec Room recovery 25% faster; Medical Bay costs −15% | "Aggressive Morale Management." A happy scout is a productive scout, even if the happiness is chemically induced. |
| **Magic-Hearth Entertainment** | Disney | Legacy Points generate 50% faster; Memorial Hall buffs doubled | They are filming the colonisation for a streaming series. They care less about survival and more about compelling character arcs and heroic sacrifices. |

> **Design Note — On Moral Framing:** The sponsor's bonus should feel useful but slightly uncomfortable. Aqua-Pure's resource yield is great — until you understand you're helping them claim a living planet's circulatory system. Magic-Hearth's Legacy bonus rewards your scouts' deaths. The game does not lecture the player about this. It just lets the mechanics speak.

> **Open Question:** Should sponsor choice lock you into a specific win condition or narrative ending, or remain a starting modifier? Leaning toward the former — each sponsor wants something specific from the planet, and their ending reflects whether you delivered it.

---

## 3. The Three Loops

| Loop | Duration | Core Activity | Reward |
|---|---|---|---|
| **Short — The Run** | 20–40 min | Scout expedition, combat, discovery | Resources, scout XP, map data |
| **Medium — The Session** | 1–2 hrs | Colony building, factory management, squad prep | Unlocks, stronger expeditions, narrative beats |
| **Long — The Campaign** | 15–20 hrs | Reveal the planet, overcome the central threat, uncover the Lazarus Protocol | Story resolution, NG+ modifiers, true endings |

### 3.1 The Short Loop — Expedition

A run follows a clear rhythm: equip squad at colony → push into the procedural map chunk by chunk → gather resources, encounter events, fight creatures → decide when to retreat before supplies or morale collapse → debrief at colony.

**The tension dial is supply-based.** Scouts carry oxygen, rations, and medical supplies. Deeper exploration finds better rewards but burns through supplies faster. The decision to turn back is always uncomfortable. There is always something just over the next ridge.

### 3.2 The Medium Loop — Colony Session

Between expeditions the player is in the colony interface. Multiple things are always simultaneously in motion: the factory processing the last haul, a building under construction, a scout recovering from injury, a new recruit arrived with an unusual trait profile.

> **Design Note:** This is the Civilisation "one more turn" mechanism. The player should always have at least three things they want to see resolve before they quit.

### 3.3 The Long Loop — Campaign Arc

Each campaign has a macro goal: establish the colony to a point where the main colony ship can safely land. This requires pushing into increasingly dangerous zones, confronting an escalating planetary threat, and building the colony infrastructure to support endgame expeditions.

The true goal of the campaign is not the player's goal. The corporations sent the player to harvest the planet's Regenerative Sap — a substance that may grant immortality to whoever controls it. The colony ship is the delivery mechanism. What the player decides to do with this knowledge in the final act is the campaign's moral climax.

NG+ campaigns offer modifiers that change sponsor behaviours, starting conditions, and planet generation — preserving replayability after first story resolution.

---

## 4. The Expedition

### 4.1 Map Structure — Chunked Exploration

The planet surface is divided into procedurally generated chunks, each roughly one to two screens in size. Chunks are assembled from typed templates — jungle, rocky mesa, wetland, crystal flats, ancient ruin, crater — and populated with content according to their type and current world state.

Within a chunk, movement is free. Scouts walk to points of interest, interact with flora and fauna, harvest resource nodes, and trigger encounters by proximity. **No action point grid.** The feel is closer to Divinity: Original Sin's exploration than XCOM's tactical maps.

- Adjacent chunks are visible before entry, giving the player a rough sense of what lies ahead.
- Chunk geography is seeded and stable — the cliff face is always there. Chunk *activity* is rolled fresh each expedition.
- Cleared chunks gain a "surveyed" state — scouts move faster, resource locations are known, risk is lower.
- **If a chunk is not revisited within 3 expeditions, it loses its surveyed status.** The jungle reclaims it.

> **Design Note — On Familiarity:** A revisited chunk should feel like earned expertise, not repetitive grind. The geography is known. What's in it today is always a question.

### 4.2 Chunk Variation System

Each chunk combines four independent variation layers:

| Layer | Stability | Examples |
|---|---|---|
| **Geography** | Permanent (seeded) | Terrain shape, landmarks, resource deposit locations |
| **Activity** | Rerolled each expedition | Creature migration, rival scouts, spore bloom, storm aftermath |
| **Event** | Driven by meta state | Cleared nest now occupied; artifact site attracting rivals |
| **Temporal** | Time of day / season | Night visibility, wet season flooding, meteor debris fields |

### 4.3 Encounter Types

- **Combat** — creature or rival faction engagement. Positional but not grid-strict. Cover, flanking, and elevation matter.
- **Discovery** — artifact, crashed probe, alien structure. Triggers an event card with decisions and consequences.
- **Environmental** — hazard zone requiring navigation choices. Toxic pocket, unstable ground, bioluminescent trap.
- **Resource Node** — harvest point with optional risk. Richer nodes are often guarded or time-pressured.
- **Scout Event** — character-specific narrative moment triggered by a scout's trait, history, or relationship.
- **Faction Encounter** — non-hostile contact opportunity with a rival group. Trade, information, or conflict.

### 4.4 Supply and Retreat

Each expedition begins with a finite supply loadout configured at the colony: oxygen canisters, rations, medkits, and mission-specific equipment. Supply consumption is steady with spikes on combat and environmental hazards.

Supplies can be partially replenished from resource nodes in the field. Scouts can push on low supplies but risk arriving back in critical condition — or not arriving back at all.

---

## 5. The Scout System

### 5.1 Philosophy

Scouts are not volunteers. They are debt-bonded contractors who owe money to corporations that have already demonstrated they will leave colonial crews to die. The player's relationship with their scouts is one of the game's central tensions: these people are not heroes, but the player must treat them like they are.

Each scout is procedurally generated but accumulates genuine individuality through experience, trauma, relationships, and scars. The loss of a veteran should feel like losing a friend.

### 5.2 Scout Generation

New recruits arrive with a procedurally generated profile:

| Attribute | Nature | Detail |
|---|---|---|
| Name & background | Flavour + mechanical seed | Background affects starting trait pool |
| Base stats | 4–6 core values (scale 1–20 at recruit level) | END, PRC, ING, AWR, RES, STR |
| Starting traits | 1–2 at generation | Positive and/or Double-Edged |
| Specialisation seed | Hidden | Determines which advanced traits become available on level-up |

**HP** = 50 + (STR × 3) + (END × 2)

### 5.3 Core Stats

| Stat | Represents | Gameplay Impact |
|---|---|---|
| **END** Endurance | Physical stamina and lung capacity | Sprint duration; Oxygen consumption rate |
| **PRC** Precision | Hand-eye coordination, focus | Accuracy with ranged weapons; success rate of delicate harvesting |
| **ING** Ingenuity | Technical knowledge, problem-solving | Speed of repairing Ancient Constructs; hacking corporate locks |
| **AWR** Awareness | Senses, reaction time | Fog-of-war reveal radius; detection of hidden traps |
| **RES** Resolve | Mental fortitude, composure | Stress gain rate; resistance to mental snaps |
| **STR** Strength | Raw physical power | Inventory weight limit; melee damage |

**Stat scale:**
- Low (1–10): A liability. Needs a natural leader nearby to survive.
- Average (11–20): Most debt-bonded recruits.
- Elite (21–40): Veterans with multiple successful runs.
- Legendary (41–50+): Legacy Children or scouts with exceptional trait synergies.

### 5.4 Scout Backgrounds (30 examples)

Each background provides a narrative hook and a mechanical lean. 30 examples are defined; additional backgrounds added as content expands.

| # | Background | Narrative Hook | Mechanical Lean |
|---|---|---|---|
| 1 | Overworked Coder | Sent as manual QA after a bug cost G-Verse billions | High ING; Low RES |
| 2 | Disgraced Architect | Megacity collapsed; harvesting for A-Maze rebuild | High Colony Building Speed |
| 3 | Failed Influencer | Owes millions in fines for filming in Magic-Hearth restricted zones | High Morale impact; Low END |
| 4 | Bio-Tech Dropout | Couldn't pay Aqua-Pure's "Surgical License Subscription" | High Medical efficiency |
| 5 | Junior Liquidator | Blue-collar worker specialised in "clearing" corporate assets | High PRC |
| 6 | Warehouse Runner | High-speed logistics in zero-G A-Maze facilities | High Movement Speed |
| 7 | Actuarial Drone | Calculated the "acceptable loss" of the First Wave; now part of it | High AWR; Low PRC |
| 8 | Hydro-Technician | Drained local Earth lakes for Aqua-Pure; here to find more | Bonus to Water/Bio-harvesting |
| 9 | Sub-Level Janitor | Saw something they shouldn't have in an Aegis lab | High Stealth/Evasion |
| 10 | Corporate Chaplain | Provided "spiritual wellness" to Coke-Zero factory workers | High RES; Buffs party Morale |
| 11 | Patent Lawyer | Here to serve cease-and-desist orders to the planet's DNA | High ING; Bonus to Research Data |
| 12 | Luxury Concierge | Used to the whims of the 1%; now handles thorns | High Diplomacy/Faction Trade |
| 13 | Barista-Grade Chemist | Made performance-enhancing lattes for X-Ether executives | High Ration/Stamina efficiency |
| 14 | Defaulted Heir | Family estate seized by United Terran Administration | High Starting XP; Low END |
| 15 | Urban Forager | Lived in the trash-heaps of G-Verse server farms | High Scavenging yield |
| 16 | Sensory Tuner | Adjusted empathy levels for Magic-Hearth VR sims | High AWR |
| 17 | Industrial Welder | Built the hulls of the colony ships; knows where they break | Bonus to Ancient Construct repair |
| 18 | Debt-Collector | Used to hunt scouts; now is one | High PRC; Low RES |
| 19 | Night-Shift Guard | Ten years watching a static screen for Aegis-Global | High Night-Vision/AWR |
| 20 | Soil Analyst | Trying to make Coke-Zero crops grow in alien dirt | High Survey/Map Data yield |
| 21 | Crash-Test Pilot | X-Ether's favourite "disposable" asset for atmospheric entry | High Evasion; Starts with Injuries |
| 22 | Marketing Intern | Volunteered to "rebrand" the planet's hostile predators | High Morale; Low ING |
| 23 | Deep-Sea Miner | Transferred from Earth's trenches to the wetlands | High END; Wetland movement buff |
| 24 | History Curator | Recovering "Physical Artifacts" for Magic-Hearth's museum | High Legacy Point generation |
| 25 | Infrastructure Auditor | Investigating why the First Wave's buildings are failing | Bonus to Ruins Exploration |
| 26 | Traffic Controller | Managed drone-swarms over A-Maze capital cities | Bonus to Drone/Tech use |
| 27 | Gig-Worker | 15 certifications, none at master level | Balanced stats; no critical weaknesses |
| 28 | Exiled Reporter | Wrote a hit piece on X-Ether; offered a one-way scouting trip | High AWR; High Stress gain |
| 29 | Lab Assistant | "Accidentally" inhaled a prototype Aqua-Pure stimulant | High Stamina; Random Stat Spikes |
| 30 | Hobbyist Botanist | The only person who actually wants to be here | High Flora-Reading abilities |

### 5.5 Scout Progression — The Lifecycle

**Recruit**
Fresh arrival. Unknown potential. Cheap to field, fragile, but the trait profile hints at where they might excel. Veteran scouts comment on promising recruits.

**Field Scout**
Gains experience through successful expeditions. Levels up after milestone run counts. Each level-up offers a choice from a small pool of traits — never a stat menu. Available traits are influenced by what the scout has actually experienced: surviving creature attacks offers combat resilience; time in jungle biomes unlocks flora-reading.

**Veteran**
Accumulates scars — mechanical quirks from past trauma that are simultaneously drawbacks and marks of character. A scout who barely survived a creature ambush might have a phobia imposing a Resolve penalty near that creature type, but also a combat bonus from hard-earned experience. Scars make veterans feel genuinely individual.

**Retirement**
Scouts who accrue too many critical injuries, reach a campaign milestone, or are stood down by the player transition to colony roles. A retired scout does not disappear:

- Former combat specialists → **Drill Instructors** (new recruits gain starting XP bonus)
- Former medics → **Medical Bay staff** (improved recovery times for the whole roster)
- Former cartographers → **Survey Office** (improved map data quality from expeditions)
- Highly decorated veterans → may unlock unique colony projects or events

**Legacy**
When a scout dies, their name is added to the memorial. Their strongest trait may pass to a younger scout they mentored. Buildings can be named in their honour. The colony remembers.

### 5.6 Traits

Scouts carry up to 2 traits, drawn from either the Positive or Double-Edged pools. This produces four scout archetypes:

- **The Golden Child** (Positive + Positive): The reliable hero. Everyone wants them. Their death ends runs.
- **The Specialist** (Positive + Double-Edged): A professional who is a nightmare to work with.
- **The Wildcard** (Double-Edged + Double-Edged): Chaotic but surprisingly effective in a crisis.
- **Plain Joe** (Single Trait): The reliable baseline. Safe. Not exciting.

With 30 total traits (15 Positive + 15 Double-Edged), 8 sponsors, and 30 backgrounds: **216,000 distinct scout archetypes** before name, gender, or stat rolls.

**Positive Traits (15)**

| # | Trait | Effect |
|---|---|---|
| 1 | Natural Leader | Reduces Stress gain for the entire squad by 10% |
| 2 | Botany Savant | Can harvest Hazardous plants without taking damage |
| 3 | Efficient | Personal inventory stacks items (Ammo/Rations) 50% higher |
| 4 | Steady Hand | +15% Precision when Stamina is low |
| 5 | Thrill-Seeker | Temporary Speed boost after entering an unexplored Chunk |
| 6 | Eagle Eye | Increases fog-of-war reveal radius by 25% |
| 7 | Iron Stomach | Can consume raw botanical samples for HP without sickness |
| 8 | Light Footed | 50% reduced chance to trigger hidden Plant-Traps |
| 9 | Field Medic | 30% faster revive speed for downed teammates |
| 10 | Night Owl | No Precision or Movement penalty in dark/cave biomes |
| 11 | Green Thumb | 10% chance to double yield of any harvested Flora |
| 12 | Mechanical Soul | Ancient Constructs 25% less likely to be hostile |
| 13 | Sturdy | +20 Max HP; higher resistance to Vine-Lash injuries |
| 14 | Optimist | Small chance to heal Stress after a successful kill |
| 15 | Scrounger | Finds 15% more Scrap in non-resource containers |

**Double-Edged Traits (15)**

| # | Trait | The Pro | The Con |
|---|---|---|---|
| 1 | Paranoid | Detection range for hidden predators doubled | Gains Stress 20% faster |
| 2 | Hoarder | Can carry +1 Heavy Item without penalty | Consumes 2× Rations; refuses to drop loot |
| 3 | Adrenaline Junkie | +25% Damage when HP below 30% | 10% chance to auto-trigger traps |
| 4 | Corporate Loyalist | 15% discount on sponsor gear | Takes Stress when using rival corporate gear |
| 5 | Deep Sleeper | Heals 30% more HP and Stress during Rest | Takes 3× longer to deploy if camp is ambushed |
| 6 | Obsessive | 20% faster scanning of specific resource nodes | Refuses to leave Chunk until 100% surveyed |
| 7 | Heavy-Handed | +20% Melee damage; breaks wooden barriers | 10% chance to break delicate loot |
| 8 | Chatterbox | Keeps squad Morale high (+10% RES) | Noise level higher; attracts predators |
| 9 | Sucker for Beauty | Massive Morale boost in Bioluminescent biomes | −15% Precision in colourful areas |
| 10 | Claustrophobic | +15% Speed in wide open areas | Gains Stress rapidly in Caves/Ruins |
| 11 | Technophobe | +15% Endurance (Oxygen/Stamina) | Stress spikes when using Drones or tech gadgets |
| 12 | Glutton | Rations provide 50% more healing | Automatically eats a Ration every time they take damage |
| 13 | Martyr | Provides Morale buff to squad if they take damage | Gains Stress when the team stays safe too long |
| 14 | Clumsy | Finds 20% more loot by "stumbling" into it | 5% chance to trip and alert nearby enemies |
| 15 | Spore-Addict | Immune to Spore-Lung disability | Gains Stress unless in Toxic plant zones |

**Trait Clashes (10 examples)**

When two clashing scouts are in the same party, an additional friction mechanic activates:

| Trait A | Clashes With | Effect | Flavour |
|---|---|---|---|
| Chatterbox | Night Owl | Night Owl's stealth bonus negated; Chatterbox gains Stress from being shushed | "Can you shut up for five seconds?" |
| Obsessive | Thrill-Seeker | Thrill-Seeker loses speed buff if Obsessive forces the team to stay | "We've been in this swamp for an hour." |
| Corporate Loyalist | Scrounger | Loyalist refuses to use dirty scrap, increasing repair costs | "I'm not putting that junk in my Aegis rifle." |
| Technophobe | Mechanical Soul | Tamed constructs 50% more likely to glitch hostile | "I told you not to trust the toaster." |
| Glutton | Efficient | Efficient gains Stress every time Glutton wastes a ration | "That was three days of supplies for a scratch." |
| Paranoid | Optimist | Optimist's Morale-on-kill halved; Paranoid thinks the kill was "too easy" | "The big one is probably right behind us." |
| Clumsy | Light Footed | Clumsy has 50% chance to trigger a trap the Light Footed scout disarmed | "Watch your step! No — not there." |
| Sucker for Beauty | Heavy-Handed | Every barrier Heavy-Handed breaks costs the Beauty-lover Morale | "You smashed a 200-year-old crystal bloom for a shortcut." |
| Martyr | Field Medic | Martyr gains Stress when healed; Medic takes longer to revive them | "My sacrifice was supposed to mean something!" |
| Claustrophobic | Deep Sleeper | If camping in a Cave, Claustrophobic keeps Deep Sleeper awake | "I can feel the ceiling getting lower. How can you sleep?" |

### 5.7 Injuries and Scars

Injuries are acquired in the field. Treatable injuries resolve with time in the Medical Bay. Scars are permanent — they are what the planet has done to this person.

The key design principle: **every scar should feel like an evolution, not just a penalty.** The planet changes people. That change should be double-edged.

| # | Injury | Type | Penalty | Hidden Benefit |
|---|---|---|---|---|
| 1 | Spore-Lung | Treatable | Periodic coughing alerts enemies | Coughing releases an obscuring cloud |
| 2 | Chlorophyll-Veins | Scar | −15 Max HP | Heals 2 HP/10s in direct light |
| 3 | Vine-Lash Scars | Scar | +20% Stress in Jungle chunks | +10% Melee Damage (hardened skin) |
| 4 | Sap-Burn | Treatable | −10% Precision (hand tremors) | Melee attacks apply Slow to enemies |
| 5 | Fungal Blight | Treatable | Consumes 25% more Oxygen | Leaves spore trail that buffs squad Speed |
| 6 | Thorny Calluses | Scar | Cannot equip Glove/Fine-Tool items | Unarmed attacks deal high Bleed damage |
| 7 | Mycelium Link | Scar | −20% Resolve (planet "whispers") | Can sense resource nodes through walls (7m) |
| 8 | Photosynthetic | Scar | Ration drain 2× faster | Stamina regenerates 50% faster in daylight |
| 9 | Parasitic Bloom | Treatable | Morale capped at 70% | On death, releases a massive stun-spore explosion |
| 10 | Wooden Limb | Scar | −15% Movement Speed | Immune to Bleed and Limb-Crippling |
| 11 | Biolum-Eyes | Scar | 30% Precision penalty in daylight | Perfect night vision; 100% Precision in Caves |
| 12 | Pollen-Fever | Treatable | Random sneezes alert enemies | High resistance to all other toxic/gas hazards |
| 13 | Tangle-Foot | Scar | 10% chance to Root when hit | Immune to knockback; +20% carry capacity |
| 14 | Petrified Skin | Scar | Inventory reduced by 2 | +2 flat damage reduction |
| 15 | Seeding Wound | Treatable | Takes 1 damage per 50 steps | Drops Bio-Seeds granting +10 resources at run end |
| 16 | Nectar-Blood | Treatable | Predators attracted from larger radius | Reviving this scout takes 50% less time |
| 17 | Bark-Ribs | Scar | Cannot equip Heavy Armour | Resists 25% of all piercing damage |
| 18 | Willow-Spine | Scar | −10% Melee damage | +20% Evasion; ignores mud movement penalties |
| 19 | Fungal Ear-Drum | Treatable | 20% sound detection reduction | Immune to Sonic/Screamer enemy stuns |
| 20 | Creeping Ivy | Scar | Weapon swap 50% slower | Ignores Overgrowth movement penalties |

*(30 injuries total; full table in systems reference)*

### 5.8 Party Dynamics

Expeditions are undertaken by parties of up to four scouts. Party composition creates emergent dynamics:

- **Bond** — scouts who run together repeatedly develop a passive bonus when paired. Losing a bonded partner triggers a Resolve crisis.
- **Rivalry** — friction between two scouts creates small friction penalties but also competitive drive bonuses.
- **Mentorship** — a veteran and a recruit in the same party accelerates the recruit's trait development.
- **Morale** — parties track a shared morale stat during the run. Falls on casualties, scary events, and prolonged danger. Rises on discoveries, victories, and rest events.

**Bond Types and Heartbreak**

| # | Bond | Buff When Together | If Partner Dies |
|---|---|---|---|
| 1 | Battle-Hardened | +10% Precision for both | Survivor gains permanent Cold-Blooded (reduced Morale) |
| 2 | Lifeline | Reviving this partner 50% faster | Survivor enters Catatonic state for 2 missions |
| 3 | Scavenger Duo | +20% Resource yield harvesting together | Survivor loses 10% Scavenging efficiency permanently |
| 4 | Shared Trauma | −15% Stress gain in same Chunk | Survivor gains Paranoid double-edged trait |
| 5 | Protector/Ward | Protector takes 15% less damage; Ward +10 RES | Protector enters Berserk for the rest of the mission |
| 6 | Lovers | Shared Morale pool; Morale gains apply to both | Grief-Stricken: −50% Resolve; 25% chance to Refuse Deploy for 5 missions |
| 7 | Mentor/Protégé | Veteran grants +25% XP to the junior scout | Mentor loses ability to bond again; protégé loses direction |
| 8 | Blood Siblings | Can swap inventory items instantly across any chunk distance | Survivor loses 20 Max HP permanently |

### 5.9 Injury Outcomes — The Spectrum

Scouts are not simply alive or dead:

| State | Duration | Consequence |
|---|---|---|
| Light Wound | 1–2 expeditions | Reduced stats. Recovers with rest. |
| Serious Injury | 3–5 expeditions | Requires Medical Bay. May gain a scar trait. |
| Missing in Action | Timed window | Rescue mission available. Scout is recoverable. |
| Captured | Timed window | Rescue mission required. Difficulty scales with captor faction. |
| Dead | Permanent | Added to memorial. Legacy effects apply. |

> **Design Note — Rescue Missions:** These are a special expedition type with a known destination, a ticking timer, and enormous emotional stakes. The player is not rolling randomly through a dungeon — they are going to get their person back.

### 5.10 Example Scout — Sloane "Lech" Nwosu

| | |
|---|---|
| **Background** | Bio-Tech Dropout. Owed for stolen proprietary enzymes. |
| **Stats (Recruit)** | END 07 / PRC 16 / ING 15 / AWR 10 / RES 12 / STR 08 |
| **HP** | 50 + (8×3) + (7×2) = 88 |
| **Positive Trait** | Botany Savant — can harvest Hazardous plants without taking damage |
| **Double-Edged** | Hoarder — carries +1 Heavy Item; consumes 2× Rations, refuses to drop loot |
| **Sponsor** | Aqua-Pure Corp (Extraction Protocol) |

Sloane is precise and technically capable — a specialist for rare toxin harvesting. Her low Endurance means she burns Oxygen fast, and Hoarder makes deep expeditions risky. She is exactly the kind of scout who has one great run in her followed by one catastrophic one.

---

## 6. The Colony Meta

### 6.1 Overview

The colony is the persistent world the player returns to between expeditions. It should never feel like a loading screen between runs. Something is always almost ready.

### 6.2 The Factory System

Resources gathered on expeditions feed a colony factory that runs continuously — even during an expedition, the factory is working. Production chain model inspired by Satisfactory but significantly reduced in complexity. The goal is interesting prioritisation decisions, not logistics puzzles.

- Raw resources (minerals, biological samples, alien compounds) → harvested in the field
- Processing buildings → convert raw resources into intermediate goods
- Advanced buildings → convert intermediate goods into equipment and upgrades
- Players configure production priorities before an expedition and return to collect results

### 6.3 Colony Buildings

| Building | Category | Function |
|---|---|---|
| Landing Pad | Core | Starting structure. Scout deployment and return. |
| Medical Bay | Support | Scout recovery. Upgrades reduce recovery time and improve injury outcomes. |
| Survey Office | Intelligence | Processes map data. Improves chunk preview information. |
| Research Lab | Progression | Analyses discoveries. Unlocks new equipment and building types. |
| Processing Plant | Factory | Converts raw resources into intermediate goods. |
| Fabrication Bay | Factory | Converts intermediate goods into equipment and components. |
| Barracks | Scout | Increases active roster size. Enables mentorship pairing. |
| Rec Room | Scout | Improves morale recovery between expeditions. |
| Memorial Hall | Legacy | Displays fallen scouts. Unlocks legacy events. |
| Signal Tower | Campaign | Required for colony ship contact. Staged construction. Campaign win condition. |

### 6.4 Meta Progression Currencies

| Currency | Source | Use |
|---|---|---|
| Raw Materials | Harvested in field | Factory input. Spent constantly. |
| Research Data | Discoveries and analysis | Unlocks tech and buildings. Medium pacing. |
| Colony Reputation | Successful expeditions, trade | Unlocks faction relationships and late-game options. |
| Survey Progress | Map exploration | Unlocks planetary map features and campaign content. |
| Scout Legacy Points | Veteran retirement, deaths | Unlocks memorial projects and cross-campaign bonuses. |

### 6.5 The Evolving World State — Pressure Systems

The colony is not a safe space:

- **Threat Escalation** — hostile factions and dangerous fauna expand territory over time. Neglected regions become increasingly dangerous.
- **Resource Depletion** — heavily farmed nodes slowly diminish, requiring scouts to push into new territory to maintain factory throughput.
- **Weather Events** — seasonal cycles and random planetary events affect expedition conditions and occasionally the colony itself.
- **Rival Factions** — other human survivor groups operate independently, competing for resources and territory.

---

## 7. The Planet

### 7.1 The Planet as Organism

The planet is not a backdrop. It is a singular super-organism that views the colony as a virus. Its "immune system" has several visible mechanics:

- **Rapid Reclaimers** — chunks not revisited within 3 expeditions lose their Surveyed status. The jungle literally swallows the paths you cut.
- **Vine-Wolves** — predators that use photosynthesis to stay invisible in brush. They do not eat scouts; they use them as fertiliser for their young.
- **Spore-Sentinels** — fungal growths that have "hacked" the nervous systems of Ancient Constructs, turning old robots into organic-metal hybrids.

The planet's escalation ties directly to the Lazarus Protocol: as the player pushes deeper and begins extracting resources critical to the sap-harvest, the planet's responses become more coordinated and more targeted.

### 7.2 Ancient Ruins — The First Wave

The ruins scattered across the planet are not alien. They are the First Wave — a colonisation attempt 30 years ago by the same eight corporations.

Finding an artifact might just be finding a 30-year-old employee ID badge. The logo on it belongs to the same company that sent you here. The player's current boss was the one who abandoned the last crew.

> **Design Note:** This should be discovered gradually through expedition events, not front-loaded exposition. The player should piece it together.

### 7.3 Biomes

| Biome | Resources | Hazards | Creatures |
|---|---|---|---|
| Fungal Lowlands | Biological, spore compounds | Toxic bloom, disorientation | Swarm species, ambush predators |
| Crystal Mesa | Rare minerals, energy compounds | Unstable ground, sharp terrain | Territorial megafauna |
| Thermal Wastes | Geothermal materials, exotic metals | Heat damage, eruptions | Heat-adapted apex predators |
| Wetland Basin | Biological, water filtration | Reduced visibility, mire | Pack hunters, flying species |
| Ancient Ruins | Alien technology, research data | Structural collapse, hostile AI | Construct guardians |
| Deep Wilderness | All types (rare) | Extreme versions of all hazards | Unknown — must be discovered |

> **Open Question — Biome Count for Early Access:** Full game targets 6 biomes. Early Access can launch with 2 fully realised biomes (recommend Fungal Lowlands + Crystal Mesa — maximum visual contrast). Wetland and Ruins in 1.0. Thermal Wastes and Deep Wilderness post-launch.

### 7.4 Faction Ecosystem

- **Creature Factions** — different species have territories and relationships with each other. Disrupting one species may drive another into previously safe areas.
- **Rival Survivors** — other human colonial parties with their own agendas. Hostile, neutral, or allied depending on player choices.
- **Ancient Constructs** — remnant automated systems from the First Wave (not truly alien). Neither hostile nor friendly by default — reactive to actions in their territory.

### 7.5 The Planetary Threat

Each campaign features a central escalating threat visible on the colony interface. Players can see it growing and understand it will become campaign-ending if ignored. This creates strategic pressure without railroading.

The threat ties into the Lazarus Protocol: as the corporations push toward harvesting the sap at the planet's core, the planet's immune response becomes the campaign's climactic enemy.

---

## 8. Narrative Arc — The Lazarus Protocol

The corporations are not here for gold, territory, or survival. They are here for the planet's **Regenerative Sap** — a substance that can theoretically grant immortality. The colony ship's landing is not a victory condition. It is a harvesting mechanism.

The player uncovers this gradually through:
- Expedition discoveries (Corporate Fossil ruins, archived communications)
- Event cards referencing the "Primary Objective" in corporate euphemism
- Veteran scouts who have read too many documents they were not supposed to

**The final act presents a genuine choice.** The player has spent 15 hours building a colony that will be used to destroy a living planet. They can:
- Deliver the sap and complete the corporate mission
- Destroy the harvest mechanism and strand the colony permanently
- Find a third option (NG+ unlocks additional endings per sponsor)

The moral weight of this decision should feel earned — not because the game preaches, but because the player has spent the whole campaign watching what the corporations do to the people they send here.

---

## 9. Art Direction

### 9.1 Visual Style

Isometric perspective with a stylised, readable art approach. Strong visual identity over photorealism. Achievable by a solo developer with a clear visual language.

**The core visual contrast:** clean geometric human structures versus weird organic alien wilderness. Anything the colonists built is angular, functional, colour-coded. Everything the planet grew is asymmetric, bioluminescent, and visually surprising.

| Element | Direction |
|---|---|
| Colony / UI | Clean lines, functional palette, blueprint aesthetic. Ordered and legible. |
| Alien terrain | Organic, asymmetric, high colour contrast. Each biome has a distinct palette. |
| Scout characters | Strong silhouettes in isometric view. Personality readable from equipment and posture. |
| Portraits | Detailed character portraits used in dialogue, events, and colony roster screen. |
| Lighting | Dynamic time-of-day and weather. Bioluminescence at night. Storm visual effects. |

### 9.2 Globe View — The Planet from Space

Before each expedition campaign begins (and as a persistent backdrop), the player sees the planet rendered as a photorealistic 3D globe. This is not a menu screen — it is a living preview of the world they are entering.

**Design intent:** The globe immediately communicates that this is a *living* planet — dense with organic growth, strange bioluminescent patches, thick cloud cover. It should feel like looking down at something that does not want to be looked at.

**Implemented rendering — Golden Baseline (2026-03-03):**
- **Terrain texture:** 2048×1024 equirectangular RGB + L8 heightmap. Sharp coastlines, crisply resolved biome edges and river channels.
- **Continent noise:** 8-octave FBM Perlin primary layer (75%) blended with a second Perlin layer at 2× frequency (25%) — produces peninsula detail and sub-continent variation without global regularity.
- **Domain warp:** amplitude `0.55 + AlienFactor × 0.20` applied to continent noise for organic landmass shapes.
- **Moisture:** frequency `contFreq × 0.95` (tracks continent scale); detail jitter `0.12` for rough biome boundary edges.
- **Water coverage:** histogram percentile pre-pass (cos-lat area-weighted) calibrates `effectiveSeaLevel` to enforce exactly 50–80% water per planet. No more all-ocean or all-land edge seeds.
- **Ocean depth gradient:** `sqrt(raw_depth)` curve compresses the shallow zone — most open ocean reads as dark navy, bright teal only at the shoreline. Four zones: coastal → shallow → ocean → deep.
- **Planet types:** 3 archetypes deterministic from seed (Archipelago / Continental / Jungle). Each has tuned sea level, vegetation bias, alien factor, and contFreq.
- **Mountains:** `ridgeNoise` (Ridged 5-octave, freq=0.55) boosted only where land is above sea level — no underwater mountain spikes.
- **Terrain palette:** Desaturated olive/moss (Grassland `#4A5D3E`, Forest `#3D4D2A`, Jungle `#263219`). Near-black teal seas. Dark volcanic mountains. Alien teal-moss frost at polar caps.
- **Bioluminescence:** AlienWilds biome patches glow with pulsing purple-violet emission — visible even from orbit.
- **Atmosphere:** Thin Fresnel-based teal-cyan halo at the limb. Accurate Rayleigh-style falloff using `1 - dot(VIEW, NORMAL)`.
- **Clouds:** Dense white animated cloud masses driven by a GPU Perlin FBM shader. Cloud sphere radius 1.18 — floats visibly above displaced mountain peaks.
- **Sphere mesh:** 512 radial segments × 256 rings — vertex displacement smooth at all zoom levels.
- **Interaction:** Scroll to zoom globe (camera Z: 1.4–3.5), drag to spin, auto-rotation 0.05 rad/s. F1 = lat/lon grid overlay with per-cell region survey panel.

**Scout sprites integrated** (Soldier_1, Soldier_2, Soldier_3) — animated portrait panel in ScoutView (Idle @ 8fps, Tab to toggle). Variant chosen by `(|seed| % 3) + 1`.

### 9.2 Audio Direction

The planet should sound alive and slightly wrong — familiar rhythms in unfamiliar timbres. The colony should sound like a functioning human space: mechanical, purposeful, warm.

Scout voice lines are a priority. Characters should comment on their situation, react to events, and reflect their trait personalities. A scout with dark humour sounds different from one with a fearful disposition reacting to the same creature.

### 9.3 Key References

- **Hades** (Supergiant) — isometric character expressiveness, reactive narrative, UI clarity
- **Darkest Dungeon** — party morale system, character trauma, grim tone handled with dry wit
- **XCOM 2** — soldier personalisation, emotional investment in procedural characters
- **Into the Breach** — readability of tactical information; elegance of small-scale encounters
- **Wildermyth** — character lifecycle narrative, emergent story from procedural traits
- **Divinity: Original Sin 2** — isometric free exploration combined with tactical combat

---

## 10. Monetisation

Single upfront price. No microtransactions, no loot boxes, no energy timers, no pay-to-win. Every mechanical system is available to every player.

| Stage | Price | Content |
|---|---|---|
| Early Access | $9.99 | Core loop. 2 biomes, 1 sponsor tier, base scout system. |
| Version 1.0 | $12.99 | Full campaign, all biomes, complete scout lifecycle, full faction set. |
| Expansion | $4.99–$6.99 | New biome, new planetary threat type, new scout specialisation tree. |

Post-launch content sold as flat-price expansions only. No individual cosmetic items or randomised bundles. Any cosmetics bundled with mechanical content — never sold separately.

> **Business Note:** The goal is a player who feels the game was worth more than they paid for it. That player recommends it. Word of mouth is the primary marketing strategy.

---

## 11. Technology

| Layer | Technology | Rationale |
|---|---|---|
| Engine | Godot 4.6.1-stable | Free, open source, excellent 2D/isometric pipeline, C# support |
| Language | C# (primary) | Strong typing, excellent Godot integration |
| Rendering — Isometric | Godot's built-in 2D renderer | Isometric support, shader access, custom mesh rendering |
| Rendering — Globe | Godot 3D (SubViewport) | SphereMesh + GLSL spatial shaders; Fresnel atmosphere; GPU cloud noise |
| Proc. generation | Custom C# systems | Chunk seeding, cylindrical lat/lon world, activity tables, event trees |
| Save system | Godot serialisation + JSON | Campaign state, scout roster, colony state, map progress |
| Distribution | Steam via Steamworks | Achievements, cloud saves, Workshop potential |

### 11.1 Key Technical Challenges

- **Cylindrical world wrapping** ✅ *Implemented* — longitude wraps at LonWidth=120 chunks using cylindrical 3D noise sampling (`nx = R·cos(lon)`, `ny = R·sin(lon)`, `nz = cy`). No seam artefact at lon=0°/360°. Latitude bounded at poles (LatMin=−30, LatMax=29).
- **Globe rendering** ✅ *Golden Baseline* — 3D sphere in transparent SubViewport. Two textures: albedo (2048×1024 RGB8) and height (L8) generated from dual-layer 8-octave FBm Perlin noise in C# with domain warp. Histogram area-weighted pre-pass enforces 50–80% water coverage. `sqrt(raw_depth)` 4-zone ocean gradient. Additional L8 glow map drives AlienWilds bioluminescent EMISSION. Fresnel atmosphere (cull_front, blend_add). GPU cloud shader (5-octave Perlin, sphere radius 1.18). Sphere mesh 512×256. Scroll-to-zoom (Z: 1.4–3.5).
- **Two-layer chunk generation** — geography (stable, seeded per campaign) and activity (rerolled each expedition) must be cleanly separated.
- **Scout data persistence** — full scout lifecycle, trait history, relationship graph, and injury record must persist reliably and be efficiently queryable for event triggers.
- **Isometric rendering order** — sprite depth sorting in isometric view. Well-understood problem with established Godot solutions. Chunk.cs uses single ArrayMesh per chunk (1 draw call per chunk, painter's order by x+y sum).
- **AI behaviour** — creatures and factions need to feel dynamic and reactive without being computationally expensive. Behaviour trees with lightweight background simulation.

---

## 12. Development Roadmap

> **Guiding Principle:** Build the core before building the content. The first milestone must produce something genuinely fun to play, even if it looks rough. Scope is locked at each milestone — no new systems until the current ones are complete and stable.

> **Scope Check:** This GDD describes an ambitious game. The scout system alone (lifecycle, traits, clashes, bonds, injuries, backgrounds) is months of work. The factory system is months more. The campaign narrative requires substantial writing. A realistic solo development timeline is 3–5 years to 1.0. Early Access is the right strategy — it provides feedback and revenue during the longest phases.

| Phase | Duration | Deliverable | Success Criteria |
|---|---|---|---|
| **0 — Foundation** | ✅ Complete — Golden Baseline | Scout moves across isometric chunked world. Globe view. Basic chunk transitions. | Movement feels good. Chunks load correctly. Globe is a convincing living planet. *Globe: 2048×1024 dual-layer 8-oct noise, histogram water calibration, sqrt depth ocean gradient, 512×256 sphere mesh, Fresnel atmosphere, GPU clouds, bioluminescent AlienWilds glow, vertex-displaced mountains, scroll-to-zoom. World: cylindrical wrapping, 120×60 chunk map, 13 biomes. ScoutView: animated portrait + trait card.* |
| **1 — The Run** | 3–4 months | Combat encounters. Supply system. Basic scout stats and traits. Expedition debrief screen. | A full expedition is playable and tense. |
| **2 — The Meta** | 3–4 months | Colony screen. Factory basics. Scout persistence across runs. Map persistence. | Running 5 expeditions in a row feels meaningful. |
| **3 — Depth** | 4–6 months | Full scout lifecycle. Rescue missions. Faction behaviour. 2 biomes. Narrative events. | A 10-hour campaign is completable. |
| **4 — Early Access** | 4–6 months | Polish, balance, art pass, audio. Steam page live. | Positive reviews. Active player feedback loop. |
| **5 — Version 1.0** | 6–12 months | Full content. Campaign complete. All systems finalised. | Full critical reception. Expansion planning begins. |

---

## 13. Open Questions (Unresolved Design)

These require decisions before implementation begins:

1. **Sponsor endings** — does each sponsor have a unique ending condition, or is the Lazarus Protocol choice the same regardless of sponsor?
2. **Planetary wrapping** — ✅ *Resolved: Cylindrical.* East-west wraps at LonWidth=120 chunks; latitude bounded at poles. Implemented and working.
3. **Factory complexity floor** — "simplified Satisfactory" could mean many things. What does one session of factory management actually look like? Define the minimum interesting decision before building it.
4. **Rival faction depth** — are rival human groups full factions with their own campaigns, or encounter-layer flavour? Full faction simulation is a large scope addition.
5. **Biome count for Early Access** — 2 biomes locks a lot of the tone early. Are Fungal Lowlands + Crystal Mesa the right two to launch with?
6. **Legacy Children** — mentioned in the stat table but undefined. What exactly is a Legacy Child? Are they unlockable named scouts? NG+ starting units?
7. **Scout sprite integration** — ✅ *Resolved:* Animated portrait panel in ScoutView (left column, Idle @ 8fps, scale 1.35×). Variant selected by `(|seed| % 3) + 1` from Soldier_1/2/3. Right column shows trait card + stats. Full-width trait section below.

---

*Frontier Protocol — GDD v0.4. Update with every significant design decision.*
