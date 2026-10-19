using Content.Server._LoneStar.Censer.Components;
using Content.Server.Bible.Components;
using Content.Server.Popups;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Mobs.Systems;
using Content.Shared.Timing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Content.Shared._LoneStar.EntityEffects;
using Content.Shared._NF.Item;
using Content.Server.Atmos.Components;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
        [Dependency] private readonly ItemToggleSystem _toggle = default!;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<CenserComponent, ItemToggledEvent>(OnInteract);
            SubscribeLocalEvent<CenserComponent, PickedUpEvent>(OnPickUp);
        }

        private void OnInteract(EntityUid uid, CenserComponent comp, ItemToggledEvent args)
        {
            var isBibleUser = HasComp<BibleUserComponent>(args.User);

            if (!isBibleUser && args.User.HasValue)
            {
                _popupSystem.PopupEntity(Loc.GetString(comp.Sizzle), args.User.Value, args.User.Value);

                _audio.PlayPvs(comp.SizzleSoundPath, args.User.Value);
                _damageableSystem.TryChangeDamage(args.User, comp.DamageOnUntrainedUse, true, origin: uid);
                /// SLOP CODE OF SOMEONE WHO DOESN'T UNDERSTAND C# BELOW, BEWARE

                if (_toggle.IsActivated(uid)) /// how to point to entity???
                {
                    _toggle.TryDeactivate(uid);/// should also deactivate or not activate on untrained use; "TryDeactivate" seems to need "IsActivated" to work
                }
            }

            if (isBibleUser)
            {
                if (_toggle.IsActivated(uid) && comp.ToggledIgnite)
                {
                    AddComp<IgniteOnMeleeHitComponent>(uid);

                    if (TryComp<IgniteOnMeleeHitComponent>(uid, out var igniteComp))
                    {
                        igniteComp.FireStacks = comp.FireStacks;
                    }
                }
            }
            return;
        }

        private void OnPickUp(EntityUid uid, CenserComponent comp, PickedUpEvent args)
        {
            if (_toggle.IsActivated(uid) && !HasComp<BibleUserComponent>(args.User))
            {
                _popupSystem.PopupEntity(Loc.GetString(comp.Sizzle), args.User, args.User);
                _audio.PlayPvs(comp.SizzleSoundPath, args.User);
                _damageableSystem.TryChangeDamage(args.User, comp.DamageOnUntrainedUse, true, origin: uid);

                if (_toggle.IsActivated(uid)) /// how to point to entity???
                {
                    _toggle.TryDeactivate(uid);/// should also deactivate or not activate on untrained use; "TryDeactivate" seems to need "IsActivated" to work
                }
            }
            return;
        }
    }
}
