#nullable enable
/// <summary>
/// Static tables for all scout traits, backgrounds, and clashes.
/// All data comes directly from GDD §5.4 – 5.8.
/// </summary>
public static class ScoutDatabase
{
	// ── Positive traits (15) ──────────────────────────────────────────────

	public static readonly TraitDef[] PositiveTraits =
	{
		new("NaturalLeader",  TraitType.Positive, "Natural Leader",
		    "Reduces Stress gain for the entire squad by 10%."),
		new("BotanySavant",   TraitType.Positive, "Botany Savant",
		    "Can harvest Hazardous plants without taking damage."),
		new("Efficient",      TraitType.Positive, "Efficient",
		    "Personal inventory stacks items (Ammo/Rations) 50% higher."),
		new("SteadyHand",     TraitType.Positive, "Steady Hand",
		    "+15% Precision when Stamina is low."),
		new("ThrillSeeker",   TraitType.Positive, "Thrill-Seeker",
		    "Temporary Speed boost after entering an unexplored Chunk."),
		new("EagleEye",       TraitType.Positive, "Eagle Eye",
		    "Increases fog-of-war reveal radius by 25%."),
		new("IronStomach",    TraitType.Positive, "Iron Stomach",
		    "Can consume raw botanical samples for HP without sickness."),
		new("LightFooted",    TraitType.Positive, "Light Footed",
		    "50% reduced chance to trigger hidden Plant-Traps."),
		new("FieldMedic",     TraitType.Positive, "Field Medic",
		    "30% faster revive speed for downed teammates."),
		new("NightOwl",       TraitType.Positive, "Night Owl",
		    "No Precision or Movement penalty in dark or cave biomes."),
		new("GreenThumb",     TraitType.Positive, "Green Thumb",
		    "10% chance to double yield of any harvested Flora."),
		new("MechanicalSoul", TraitType.Positive, "Mechanical Soul",
		    "Ancient Constructs 25% less likely to be hostile."),
		new("Sturdy",         TraitType.Positive, "Sturdy",
		    "+20 Max HP; higher resistance to Vine-Lash injuries."),
		new("Optimist",       TraitType.Positive, "Optimist",
		    "Small chance to heal Stress after a successful kill."),
		new("Scrounger",      TraitType.Positive, "Scrounger",
		    "Finds 15% more Scrap in non-resource containers."),
	};

	// ── Double-edged traits (15) ──────────────────────────────────────────

	public static readonly TraitDef[] DoubleEdgedTraits =
	{
		new("Paranoid",        TraitType.DoubleEdged, "Paranoid",
		    "Detection range doubled; Stress builds faster.",
		    Pro: "Detection range for hidden predators doubled.",
		    Con: "Gains Stress 20% faster."),
		new("Hoarder",         TraitType.DoubleEdged, "Hoarder",
		    "Carries more; eats double and refuses to drop loot.",
		    Pro: "Can carry +1 Heavy Item without penalty.",
		    Con: "Consumes 2× Rations; refuses to drop loot."),
		new("AdrenalineJunkie",TraitType.DoubleEdged, "Adrenaline Junkie",
		    "Deadly when wounded; prone to triggering traps.",
		    Pro: "+25% Damage when HP below 30%.",
		    Con: "10% chance to auto-trigger traps."),
		new("CorporateLoyalist",TraitType.DoubleEdged, "Corporate Loyalist",
		    "Sponsor gear discount; stress with rival equipment.",
		    Pro: "15% discount on sponsor gear.",
		    Con: "Takes Stress when using rival corporate gear."),
		new("DeepSleeper",     TraitType.DoubleEdged, "Deep Sleeper",
		    "Exceptional rest recovery; dangerous to wake.",
		    Pro: "Heals 30% more HP and Stress during Rest.",
		    Con: "Takes 3× longer to deploy if camp is ambushed."),
		new("Obsessive",       TraitType.DoubleEdged, "Obsessive",
		    "Faster at scanning; won't leave until a chunk is 100% surveyed.",
		    Pro: "20% faster scanning of specific resource nodes.",
		    Con: "Refuses to leave Chunk until 100% surveyed."),
		new("HeavyHanded",     TraitType.DoubleEdged, "Heavy-Handed",
		    "Strong melee; breaks delicate loot.",
		    Pro: "+20% Melee damage; breaks wooden barriers.",
		    Con: "10% chance to break delicate loot."),
		new("Chatterbox",      TraitType.DoubleEdged, "Chatterbox",
		    "Boosts squad Morale; attracts predators.",
		    Pro: "Keeps squad Morale high (+10% RES).",
		    Con: "Noise level higher; attracts predators."),
		new("SuckerForBeauty", TraitType.DoubleEdged, "Sucker for Beauty",
		    "Thrives in bioluminescent biomes; distracted by colour.",
		    Pro: "Massive Morale boost in Bioluminescent biomes.",
		    Con: "−15% Precision in colourful areas."),
		new("Claustrophobic",  TraitType.DoubleEdged, "Claustrophobic",
		    "Fast in open terrain; panics underground.",
		    Pro: "+15% Speed in wide open areas.",
		    Con: "Gains Stress rapidly in Caves/Ruins."),
		new("Technophobe",     TraitType.DoubleEdged, "Technophobe",
		    "Strong stamina; stressed by gadgets.",
		    Pro: "+15% Endurance (Oxygen/Stamina).",
		    Con: "Stress spikes when using Drones or tech gadgets."),
		new("Glutton",         TraitType.DoubleEdged, "Glutton",
		    "Rations heal extra; automatically eats when wounded.",
		    Pro: "Rations provide 50% more healing.",
		    Con: "Automatically eats a Ration every time they take damage."),
		new("Martyr",          TraitType.DoubleEdged, "Martyr",
		    "Inspires the squad when hurt; stressed by inaction.",
		    Pro: "Provides Morale buff to squad if they take damage.",
		    Con: "Gains Stress when the team stays safe too long."),
		new("Clumsy",          TraitType.DoubleEdged, "Clumsy",
		    "Stumbles into extra loot; occasionally stumbles into enemies.",
		    Pro: "Finds 20% more loot by \"stumbling\" into it.",
		    Con: "5% chance to trip and alert nearby enemies."),
		new("SporeAddict",     TraitType.DoubleEdged, "Spore-Addict",
		    "Immune to Spore-Lung; uncomfortable outside toxic zones.",
		    Pro: "Immune to Spore-Lung disability.",
		    Con: "Gains Stress unless in Toxic plant zones."),
	};

	// ── Corporate sponsors — The Infamous Eight ──────────────────────────

	/// <summary>
	/// The eight corporations bankrolling the illegal colonisation effort.
	/// NarrativeHook strings use {corpo} as a placeholder; it is replaced
	/// at scout generation time with the scout's assigned sponsor.
	/// </summary>
	public static readonly string[] SponsorNames =
	{
		"A-Maze Logistics",
		"G-Verse Analytics",
		"Aegis-Global Security",
		"United Terran Administration",
		"X-Ether Motors",
		"Aqua-Pure Corp",
		"Coke-Zero Defense",
		"Magic-Hearth Entertainment",
	};

	// ── Backgrounds (30) — {corpo} resolved at generation ─────────────────

	public static readonly BackgroundDef[] Backgrounds =
	{
		new(1,  "Overworked Coder",
		    "Sent as manual QA after a bug cost {corpo} billions.",
		    "High ING; Low RES.",                                    "ING", "RES"),
		new(2,  "Disgraced Architect",
		    "Megacity collapsed; contracted to {corpo} for the rebuild.",
		    "High Colony Building Speed.",                           "ING", ""),
		new(3,  "Failed Influencer",
		    "Owes millions in fines for filming in {corpo} restricted zones.",
		    "High Morale impact; Low END.",                          "RES", "END"),
		new(4,  "Bio-Tech Dropout",
		    "Couldn't pay {corpo}'s Surgical License Subscription.",
		    "High Medical efficiency.",                              "ING", ""),
		new(5,  "Junior Liquidator",
		    "Blue-collar specialist in clearing assets on behalf of {corpo}.",
		    "High PRC.",                                             "PRC", ""),
		new(6,  "Warehouse Runner",
		    "High-speed logistics in zero-G {corpo} orbital facilities.",
		    "High Movement Speed.",                                  "END", ""),
		new(7,  "Actuarial Drone",
		    "Calculated {corpo}'s 'acceptable loss' projections for the First Wave; is now part of the data.",
		    "High AWR; Low PRC.",                                    "AWR", "PRC"),
		new(8,  "Hydro-Technician",
		    "Diverted water reserves to {corpo}'s refineries; here to find more.",
		    "Bonus to Water/Bio-harvesting.",                        "ING", ""),
		new(9,  "Sub-Level Janitor",
		    "Saw something they shouldn't have in a {corpo} sublevel.",
		    "High Stealth/Evasion.",                                 "AWR", ""),
		new(10, "Corporate Chaplain",
		    "Provided 'spiritual wellness' to {corpo} shift workers; they needed it.",
		    "High RES; Buffs party Morale.",                         "RES", ""),
		new(11, "Patent Lawyer",
		    "Here to file cease-and-desist orders on alien DNA for {corpo}.",
		    "High ING; Bonus to Research Data.",                     "ING", ""),
		new(12, "Luxury Concierge",
		    "Catered to {corpo} executives' every demand for a decade; now handles thorns.",
		    "High Diplomacy/Faction Trade.",                         "RES", ""),
		new(13, "Barista-Grade Chemist",
		    "Developed performance compounds for {corpo} senior staff.",
		    "High Ration/Stamina efficiency.",                       "END", ""),
		new(14, "Defaulted Heir",
		    "Family estate seized by {corpo} for unpaid licensing fees.",
		    "High Starting XP; Low END.",                            "",    "END"),
		new(15, "Urban Forager",
		    "Lived in the trash-heaps outside {corpo} server farms.",
		    "High Scavenging yield.",                                "AWR", ""),
		new(16, "Sensory Tuner",
		    "Calibrated emotional responses for {corpo} immersive experiences.",
		    "High AWR.",                                             "AWR", ""),
		new(17, "Industrial Welder",
		    "Built {corpo}'s colony ship hulls; knows exactly where they fail.",
		    "Bonus to Ancient Construct repair.",                    "STR", ""),
		new(18, "Debt-Collector",
		    "Used to track payment defaulters for {corpo}; now is one of them.",
		    "High PRC; Low RES.",                                    "PRC", "RES"),
		new(19, "Night-Shift Guard",
		    "Ten years watching static feeds for {corpo} security; now something moved.",
		    "High Night-Vision/AWR.",                                "AWR", ""),
		new(20, "Soil Analyst",
		    "Trying to grow {corpo}'s proprietary crop strains in alien dirt.",
		    "High Survey/Map Data yield.",                           "ING", ""),
		new(21, "Crash-Test Pilot",
		    "{corpo}'s preferred disposable asset for atmospheric re-entry testing.",
		    "High Evasion; Starts with Injuries.",                   "AWR", "RES"),
		new(22, "Marketing Intern",
		    "Volunteered to rebrand the planet's apex predators — for {corpo}'s PR team.",
		    "High Morale; Low ING.",                                 "RES", "ING"),
		new(23, "Deep-Sea Miner",
		    "Stripped Earth's ocean floors for {corpo} before the reassignment.",
		    "High END; Wetland movement buff.",                      "END", ""),
		new(24, "History Curator",
		    "Cataloguing 'retrievable assets' from the First Wave for {corpo}'s archive.",
		    "High Legacy Point generation.",                         "ING", ""),
		new(25, "Infrastructure Auditor",
		    "Sent by {corpo} to determine why their First Wave investment is rubble.",
		    "Bonus to Ruins Exploration.",                           "ING", ""),
		new(26, "Traffic Controller",
		    "Coordinated {corpo}'s drone-swarm logistics over four continents.",
		    "Bonus to Drone/Tech use.",                              "ING", ""),
		new(27, "Gig-Worker",
		    "Holds certifications across {corpo}'s contractor network; none at senior level.",
		    "Balanced stats; no critical weaknesses.",               "",    ""),
		new(28, "Exiled Reporter",
		    "Published an exposé on {corpo}; received a one-way deployment notice.",
		    "High AWR; High Stress gain.",                           "AWR", "RES"),
		new(29, "Lab Assistant",
		    "Accidentally inhaled an experimental {corpo} compound during a quality check.",
		    "High Stamina; Random Stat Spikes.",                     "END", ""),
		new(30, "Hobbyist Botanist",
		    "The only person on a {corpo} manifest who actually wants to be here.",
		    "High Flora-Reading abilities.",                         "ING", ""),
	};

	// ── Trait clashes (10) ────────────────────────────────────────────────

	public static readonly ClashDef[] Clashes =
	{
		new("Chatterbox",       "NightOwl",
		    "Night Owl's stealth bonus negated; Chatterbox gains Stress from being shushed.",
		    "\"Can you shut up for five seconds?\""),
		new("Obsessive",        "ThrillSeeker",
		    "Thrill-Seeker loses speed buff if Obsessive forces the team to stay.",
		    "\"We've been in this swamp for an hour.\""),
		new("CorporateLoyalist","Scrounger",
		    "Loyalist refuses to use dirty scrap, increasing repair costs.",
		    "\"I'm not putting that junk in my Aegis rifle.\""),
		new("Technophobe",      "MechanicalSoul",
		    "Tamed constructs 50% more likely to glitch hostile.",
		    "\"I told you not to trust the toaster.\""),
		new("Glutton",          "Efficient",
		    "Efficient gains Stress every time Glutton wastes a ration.",
		    "\"That was three days of supplies for a scratch.\""),
		new("Paranoid",         "Optimist",
		    "Optimist's Morale-on-kill halved; Paranoid thinks the kill was too easy.",
		    "\"The big one is probably right behind us.\""),
		new("Clumsy",           "LightFooted",
		    "Clumsy has 50% chance to trigger a trap the Light Footed scout disarmed.",
		    "\"Watch your step! No — not there.\""),
		new("SuckerForBeauty",  "HeavyHanded",
		    "Every barrier Heavy-Handed breaks costs the Beauty-lover Morale.",
		    "\"You smashed a 200-year-old crystal bloom for a shortcut.\""),
		new("Martyr",           "FieldMedic",
		    "Martyr gains Stress when healed; Medic takes longer to revive them.",
		    "\"My sacrifice was supposed to mean something!\""),
		new("Claustrophobic",   "DeepSleeper",
		    "If camping in a Cave, Claustrophobic keeps Deep Sleeper awake.",
		    "\"I can feel the ceiling getting lower. How can you sleep?\""),
	};

	// ── Lookup helpers ────────────────────────────────────────────────────

	public static TraitDef? FindTrait(string id)
	{
		foreach (var t in PositiveTraits)   if (t.Id == id) return t;
		foreach (var t in DoubleEdgedTraits) if (t.Id == id) return t;
		return null;
	}

	public static BackgroundDef? FindBackground(int id)
	{
		foreach (var b in Backgrounds) if (b.Id == id) return b;
		return null;
	}

	/// <summary>
	/// Returns any active clashes for a pair of traits.
	/// Used when assembling a party to surface friction warnings.
	/// </summary>
	public static ClashDef? FindClash(string traitIdA, string traitIdB)
	{
		foreach (var c in Clashes)
			if ((c.TraitIdA == traitIdA && c.TraitIdB == traitIdB) ||
			    (c.TraitIdA == traitIdB && c.TraitIdB == traitIdA))
				return c;
		return null;
	}
}
