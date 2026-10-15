namespace NoitaCraft
{
    // One struct per sheet row type; tools/gen.py fills Sheets from sheets/*.json.
    internal readonly struct SpellRow
    {
        public readonly string NoitaId, Special, ShootSound, HitSound, BounceSound;
        public readonly int NewBase, DropType, Copies;
        public readonly float ThrowSpeed;

        public SpellRow(string noitaId, int newBase, int dropType, int copies, string special, float throwSpeed, string shootSound, string hitSound, string bounceSound)
        {
            NoitaId = noitaId; NewBase = newBase; DropType = dropType; Copies = copies; Special = special; ThrowSpeed = throwSpeed;
            ShootSound = shootSound; HitSound = hitSound; BounceSound = bounceSound;
        }

        public string Icon => "noita_" + NoitaId;
    }

    internal readonly struct ModifierRow
    {
        public readonly string NoitaId;
        public readonly int NewBase, DropType, AddBounces, AddPierce, Copies, CastDelay;
        public readonly bool Homing, Electric;
        public readonly float DamageAdd, DamageMul, SpeedMul, SpreadAdd, FreezeSec;

        public ModifierRow(string noitaId, int newBase, int dropType, int addBounces, bool homing, int addPierce, float damageAdd, float damageMul, float speedMul, float spreadAdd, float freezeSec, bool electric, int copies, int castDelay)
        {
            NoitaId = noitaId; NewBase = newBase; DropType = dropType; AddBounces = addBounces; Homing = homing; AddPierce = addPierce;
            DamageAdd = damageAdd; DamageMul = damageMul; SpeedMul = speedMul; SpreadAdd = spreadAdd; FreezeSec = freezeSec; Electric = electric;
            Copies = copies; CastDelay = castDelay;
        }

        public string Icon => "noita_" + NoitaId;
    }

    internal readonly struct WandRow
    {
        public readonly int WandId, CloneOf;
        public readonly string NameKey, IconPath;
        public readonly int[] Slots;

        public WandRow(int wandId, int cloneOf, string nameKey, string iconPath, int[] slots)
        {
            WandId = wandId; CloneOf = cloneOf; NameKey = nameKey; IconPath = iconPath; Slots = slots;
        }

        public string Icon => "noita_wand_" + WandId;
    }

    internal readonly struct SetRow
    {
        public readonly int SetId, RelicId, UiHeight;
        public readonly string Name, Description, SpriteXml, IdleAnim, MoveAnim;
        public readonly int[] WandIds, UpgradeCosts;
        public readonly float WorldHeight;

        public SetRow(int setId, string name, string description, int[] wandIds, int[] upgradeCosts, int relicId, string spriteXml, string idleAnim, string moveAnim, float worldHeight, int uiHeight)
        {
            SetId = setId; Name = name; Description = description; WandIds = wandIds; UpgradeCosts = upgradeCosts; RelicId = relicId;
            SpriteXml = spriteXml; IdleAnim = idleAnim; MoveAnim = moveAnim; WorldHeight = worldHeight; UiHeight = uiHeight;
        }
    }
}
