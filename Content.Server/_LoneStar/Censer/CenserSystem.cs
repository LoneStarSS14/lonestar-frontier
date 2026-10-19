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
            // Check that we actually have a user, bail out if we do not
            if (args.User == null)
                return;

            // Our logic paths need to know if the user has the BibleUserComp
            var isBibleUser = IsBibleUser(args.User.Value);

            if (!isBibleUser)
            {
                HandleNonBibleInteraction(uid, comp, args.User.Value);
            }

            if (isBibleUser)
            {
                HandleBibleUserInteraction(uid, comp, args.User.Value);
            }
            return;
        }

        private void OnPickUp(EntityUid uid, CenserComponent comp, PickedUpEvent args)
        {
            // If a non-BibleUser picks up an activated censer, it should treat the user
            // as though they tried to activate it, and then turn off
            if (!IsBibleUser(uid) && _toggle.IsActivated(uid))
            {
                HandleNonBibleInteraction(uid, comp, args.User);
            }

            return;
        }

        /// <summary>
        /// Checks if the user has the BibleUserComp
        /// </summary>
        /// <param name="uid">user uid</param>
        /// <returns></returns>
        private bool IsBibleUser(EntityUid uid)
        {
            return HasComp<BibleUserComponent>(uid);
        }

        /// <summary>
        /// If the user has the BibleUserComp it will add IgniteOnMeleeHitComp if specified in CenserComp
        /// </summary>
        /// <param name="uid">the censer uid</param>
        /// <param name="comp">censer component</param>
        /// <param name="userUid">uid of the user interacting with the censer</param>
        private void HandleBibleUserInteraction(EntityUid uid, CenserComponent comp, EntityUid userUid)
        {
            if (IsBibleUser(userUid))
            {
                if (_toggle.IsActivated(uid) && comp.ToggledIgnite && !HasComp<IgniteOnMeleeHitComponent>(uid))
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

        /// <summary>
        /// If the user does not have the BibleUserComp it will damage them, play a popup, deactivate the censer and remove the IgniteOnMeleeHitComp
        /// </summary>
        /// <param name="uid">the censer uid</param>
        /// <param name="comp">censer component</param>
        /// <param name="userUid">uid of the user interacting with the censer</param>
        private void HandleNonBibleInteraction(EntityUid uid, CenserComponent comp, EntityUid userUid)
        {
            if (!IsBibleUser(userUid))
            {
                _popupSystem.PopupEntity(Loc.GetString(comp.Sizzle), userUid, userUid);
                _audio.PlayPvs(comp.SizzleSoundPath, userUid);
                _damageableSystem.TryChangeDamage(userUid, comp.DamageOnUntrainedUse, true, origin: uid);

                if (_toggle.IsActivated(uid))
                {
                    _toggle.TryDeactivate(uid);

                    if (HasComp<IgniteOnMeleeHitComponent>(uid))
                    {
                        RemComp<IgniteOnMeleeHitComponent>(uid);
                    }
                }
            }

            return;
        }
    }
}
