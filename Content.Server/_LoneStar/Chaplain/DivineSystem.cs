using Content.Server._LoneStar.Chaplain.Components;
using Content.Server.Bible.Components;
using Content.Server.Popups;
using Content.Shared.Damage;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Item.ItemToggle;
using Robust.Shared.Audio.Systems;
using Content.Shared._NF.Item;
using Content.Server.Atmos.Components;

namespace Content.Server._LoneStar.Divine
{
    public sealed class DivineSystem : EntitySystem
    {
        [Dependency] private readonly DamageableSystem _damageableSystem = default!;
        [Dependency] private readonly PopupSystem _popupSystem = default!;
        [Dependency] private readonly SharedAudioSystem _audio = default!;
        [Dependency] private readonly ItemToggleSystem _toggle = default!;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<DivineComponent, ItemToggledEvent>(OnInteract);
            SubscribeLocalEvent<DivineComponent, PickedUpEvent>(OnPickUp);
        }

        private void OnInteract(EntityUid uid, DivineComponent comp, ItemToggledEvent args)
        {
            // Check that we actually have a user, bail out if we do not
            if (args.User == null)
                return;

            // Our logic paths need to know if the user has the BibleUserComp
            if (IsBibleUser(args.User.Value))
            {
                HandleBibleUserInteraction(uid, comp, args.User.Value);
            }
            else
            {
                HandleNonBibleInteraction(uid, comp, args.User.Value);
            }
            return;
        }

        private void OnPickUp(EntityUid uid, DivineComponent comp, PickedUpEvent args)
        {
            // If a non-BibleUser picks up an activated Divine, it should treat the user
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
        /// If the user has the BibleUserComp it will add IgniteOnMeleeHitComp if specified in DivineComp
        /// </summary>
        /// <param name="uid">the Divine uid</param>
        /// <param name="comp">Divine component</param>
        /// <param name="userUid">uid of the user interacting with the Divine</param>
        private void HandleBibleUserInteraction(EntityUid uid, DivineComponent comp, EntityUid userUid)
        {
            if (IsBibleUser(userUid) && comp.ToggledIgnite)
            {
                var hasIgniteOnMeleeHit = HasComp<IgniteOnMeleeHitComponent>(uid);
                var isActivated = _toggle.IsActivated(uid);

                if (isActivated && !hasIgniteOnMeleeHit)
                {
                    AddIgniteOnMelee(uid, comp);
                }
                else
                {
                    RemoveIgniteOnMelee(uid, comp);
                }
            }

            return;
        }

        /// <summary>
        /// If the user does not have the BibleUserComp it will damage them, play a popup, deactivate the Divine and remove the IgniteOnMeleeHitComp
        /// </summary>
        /// <param name="uid">the Divine uid</param>
        /// <param name="comp">Divine component</param>
        /// <param name="userUid">uid of the user interacting with the Divine</param>
        private void HandleNonBibleInteraction(EntityUid uid, DivineComponent comp, EntityUid userUid)
        {
            if (!IsBibleUser(userUid))
            {
                if (_toggle.IsActivated(uid))
                {
                    _popupSystem.PopupEntity(Loc.GetString(comp.Sizzle), userUid, userUid);
                    _audio.PlayPvs(comp.SizzleSoundPath, userUid);
                    _damageableSystem.TryChangeDamage(userUid, comp.DamageOnUntrainedUse, true, origin: uid);
                    _toggle.TryDeactivate(uid);

                    RemoveIgniteOnMelee(uid, comp);
                }
            }

            return;
        }

        /// <summary>
        /// Adds the IgniteOnMeleeHitComp and sets the FireStacks applied to the stacks set in comp
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="comp">Used for adding the IgniteOnMeleeHit and setting it's params</param>
        private void AddIgniteOnMelee(EntityUid uid, DivineComponent comp)
        {
            AddComp<IgniteOnMeleeHitComponent>(uid);

            if (TryComp<IgniteOnMeleeHitComponent>(uid, out var igniteComp))
            {
                igniteComp.FireStacks = comp.FireStacks;
            }
        }

        /// <summary>
        /// Removes the IgniteOnMeleeHitComp if present
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="comp"></param>
        private void RemoveIgniteOnMelee(EntityUid uid, DivineComponent comp)
        {
            if (HasComp<IgniteOnMeleeHitComponent>(uid))
            {
                RemComp<IgniteOnMeleeHitComponent>(uid);
            }
        }
    }
}
