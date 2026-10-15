# NoitaCraft

A Magicraft mod that brings Noita's spells and Mina, the Noita witch, into Magicraft. Everything else is Magicraft as it is.

- **Noita's spells are new spells in Magicraft's loot.** 19 projectile spells and 11 modifiers drop from chests, rewards and shops next to all of Magicraft's own spells. Projectiles: Spark Bolt, Magic Arrow, Bouncing Burst, Energy Orb, Disc Projectile, Arrow, Triplicate Bolt, Firebolt, Fireball, Iceball, Magic Missile, Concentrated Light, Teleport Bolt, Bomb, Meteor, Lightning Bolt, Chainsaw, Black Hole and Nuke. Modifiers: Bounce, Homing, Piercing Shot, Damage Plus, Heavy Shot, Speed Up, Electric Charge, Freeze Charge, Reduce Spread, Double Spell and Divide by 2.
- **Each one is its own spell.** It has its own entry in Magicraft, so its tooltip, icon, name and description (in all of Noita's languages) are Noita's. Its stats come from Noita's own numbers, scaled to Magicraft's power level: speed, lifetime, damage and damage type, mana, cast delay, spread, crit chance and price.
- **Noita's behavior.** Bouncing Burst bounces 10 times, loses 10% of its speed on each bounce, and stops on an enemy or once it's too slow. Spark Bolt slows in the air. Magic Missile speeds up. Black Hole drifts through walls and pulls enemies in. Teleport Bolt moves you to where it lands. Bombs roll to a stop and blow up on their fuse. Explosive spells explode with Noita's radius and damage. Noita's modifiers change the next projectile, Noita's or Magicraft's.
- **Noita's look and sound.** Each spell uses Noita's projectile sprites and animations, trails in the colour of the material it emits, bounce sparks, and Noita's explosion sprites. Its cast, bounce and hit sounds are decoded from Noita's own sound bank.
- **Mina as a starting character.** She's a new pick on Magicraft's character (set) screen in the camp, drawn with Noita's player sprite and animations. She starts with a Noita wand of Spark Bolt, Bouncing Burst and Firebolt.
- **Single player**, like Magicraft.

Noita's files are read from your own Noita install while the game runs, and its sounds are decoded once into `%LOCALAPPDATA%\NoitaCraft\sounds`. Nothing from either game is shipped. Magicraft is top-down, so Noita's gravity is left out. Your saves stay clean: Mina's pick is kept in `%LOCALAPPDATA%\NoitaCraft`. If you remove the mod, finish any run that has Noita spells in it first.

Needs: Magicraft and Noita (Steam), and BepInEx 5 (Melty installs it).

## How it's built

`sheets/` holds the design: spells, modifiers, the Noita-to-Magicraft scaling, Mina, the hooks into Magicraft and the test checks. `python tools/gen.py --magicraft-dump <dump> --noita <Noita folder> --decomp <decompiled src>` runs the preflight over the sheets and writes `src/Generated.cs`. Then run `dotnet build -c Release`.

Credits: Magicraft by bolang; Noita by Nolla Games; BepInEx and Harmony (installed by Melty); vgmstream (ISC license, bundled unmodified in `vgmstream/`, with its codec libraries under their own licenses). Built with Claude and Melty's modding kit.
