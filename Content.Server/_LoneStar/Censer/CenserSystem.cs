using Content.Server._LoneStar.Censer.Components;
using Content.Server.Bible.Components;
using Content.Server.Popups;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Timing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;

namespace Content.Server._LoneStar.Censer
{
    public sealed class CenserSystem : EntitySystem
    {
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly ActionBlockerSystem _blocker = default!;
        [Dependency] private readonly DamageableSystem _damageableSystem = default!;
        [Dependency] private readonly InventorySystem _invSystem = default!;
        [Dependency] private readonly MobStateSystem _mobStateSystem = default!;
        [Dependency] private readonly PopupSystem _popupSystem = default!;
        [Dependency] private readonly SharedActionsSystem _actionsSystem = default!;
        [Dependency] private readonly SharedAudioSystem _audio = default!;
        [Dependency] private readonly UseDelaySystem _delay = default!;
        [Dependency] private readonly SharedTransformSystem _transform = default!;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<CenserComponent, ItemToggledEvent>(OnInteract);
        }

        private void OnInteract(EntityUid uid, CenserComponent comp, ItemToggledEvent args)
        {
            if (!HasComp<BibleUserComponent>(args.User) && args.User.HasValue)
            {
                _popupSystem.PopupEntity(Loc.GetString("bible-sizzle"), args.User.Value, args.User.Value);

                _audio.PlayPvs(comp.SizzleSoundPath, args.User.Value);
                _damageableSystem.TryChangeDamage(args.User, comp.DamageOnUntrainedUse, true, origin: uid);

                return;
            }
        }
    }
}
