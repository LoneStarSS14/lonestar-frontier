using Content.Client.Rotation;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Rotation;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;

namespace Content.Client.Buckle;

internal sealed class BuckleSystem : SharedBuckleSystem
{
    private const int BehindViewTopDepth = Robust.Shared.GameObjects.DrawDepth.Default + 7;

    [Dependency] private readonly RotationVisualizerSystem _rotationVisualizerSystem = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly SharedTransformSystem _xformSystem = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    private readonly Dictionary<EntityUid, int> _originalStrapDepth = new();
    private readonly List<EntityUid> _staleDepthKeys = new();
    private float _cleanupAccumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StrapComponent, MoveEvent>(OnStrapMoveEvent);
        SubscribeLocalEvent<BuckleComponent, BuckledEvent>(OnBuckledEvent);
        SubscribeLocalEvent<BuckleComponent, UnbuckledEvent>(OnUnbuckledEvent);
        SubscribeLocalEvent<BuckleComponent, AttemptMobCollideEvent>(OnMobCollide);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _cleanupAccumulator += frameTime;
        if (_cleanupAccumulator >= 1f)
        {
            _cleanupAccumulator = 0f;
            CleanupMissingStrapDepthEntries();
        }

        var query = EntityQueryEnumerator<StrapComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var strap, out var strapSprite))
        {
            UpdateBehindViewStrapDepth(uid, strap, strapSprite);

            if (strap.BuckledEntities.Count == 0)
                continue;

            UpdateBuckledDrawDepth(uid, strap, strapSprite);
        }
    }

    private void OnMobCollide(Entity<BuckleComponent> ent, ref AttemptMobCollideEvent args)
    {
        if (ent.Comp.Buckled)
        {
            args.Cancelled = true;
        }
    }

    private void OnStrapMoveEvent(EntityUid uid, StrapComponent component, ref MoveEvent args)
    {
        // I'm moving this to the client-side system, but for the sake of posterity let's keep this comment:
        // > This is mega cursed. Please somebody save me from Mr Buckle's wild ride

        // The nice thing is its still true, this is quite cursed, though maybe not omega cursed anymore.
        // This code is garbage, it doesn't work with rotated viewports. I need to finally get around to reworking
        // sprite rendering for entity layers & direction dependent sorting.

        // Future notes:
        // Right now this doesn't handle: other grids, other grids rotating, the camera rotation changing, and many other fun rotation specific things
        // The entire thing should be a concern of the engine, or something engine helps to implement properly.
        // Give some of the sprite rotations their own drawdepth, maybe as an offset within the rsi, or something like this
        // And we won't ever need to set the draw depth manually

        if (args.NewRotation == args.OldRotation)
            return;

        // Frontier: maintain sprite order
        if (component.MaintainSpriteLayers)
            return;
        // End Frontier

        if (!TryComp<SpriteComponent>(uid, out var strapSprite))
            return;

        UpdateBuckledDrawDepth(uid, component, strapSprite);
    }

    private void UpdateBuckledDrawDepth(EntityUid uid, StrapComponent component, SpriteComponent strapSprite)
    {
        var isNorth = IsFacingScreenNorth(uid);
        foreach (var buckledEntity in component.BuckledEntities)
        {
            if (!TryComp<BuckleComponent>(buckledEntity, out var buckle))
                continue;

            if (!TryComp<SpriteComponent>(buckledEntity, out var buckledSprite))
                continue;

            if (isNorth && component.HasChairBack) // Everdream Change
            {
                // This will only assign if empty, it won't get overwritten by new depth on multiple calls, which do happen easily
                buckle.OriginalDrawDepth ??= buckledSprite.DrawDepth;
                _sprite.SetDrawDepth((buckledEntity, buckledSprite), strapSprite.DrawDepth - 1);
            }
            else if (buckle.OriginalDrawDepth.HasValue)
            {
                _sprite.SetDrawDepth((buckledEntity, buckledSprite), buckle.OriginalDrawDepth.Value);
                buckle.OriginalDrawDepth = null;
            }
        }
    }

    private bool IsFacingScreenNorth(EntityUid uid)
    {
        var angle = _xformSystem.GetWorldRotation(uid) + _eye.CurrentEye.Rotation; // Get true screen position, or close enough
        return angle.GetCardinalDir() == Direction.North;
    }

    private void UpdateBehindViewStrapDepth(EntityUid uid, StrapComponent strap, SpriteComponent strapSprite)
    {
        if (!strap.RenderOnTopWhenBehind)
        {
            RestoreStrapDrawDepth(uid, strapSprite);
            return;
        }

        if (!IsFacingScreenNorth(uid))
        {
            RestoreStrapDrawDepth(uid, strapSprite);
            return;
        }

        if (!_originalStrapDepth.ContainsKey(uid))
            _originalStrapDepth[uid] = strapSprite.DrawDepth;

        if (strapSprite.DrawDepth < BehindViewTopDepth)
            _sprite.SetDrawDepth((uid, strapSprite), BehindViewTopDepth);
    }

    private void RestoreStrapDrawDepth(EntityUid uid, SpriteComponent? strapSprite = null)
    {
        if (!_originalStrapDepth.TryGetValue(uid, out var originalDepth))
            return;

        if (strapSprite == null && !TryComp(uid, out strapSprite))
        {
            _originalStrapDepth.Remove(uid);
            return;
        }

        _sprite.SetDrawDepth((uid, strapSprite), originalDepth);
        _originalStrapDepth.Remove(uid);
    }

    private void CleanupMissingStrapDepthEntries()
    {
        if (_originalStrapDepth.Count == 0)
            return;

        _staleDepthKeys.Clear();
        foreach (var uid in _originalStrapDepth.Keys)
        {
            if (!EntityManager.EntityExists(uid))
                _staleDepthKeys.Add(uid);
        }

        foreach (var uid in _staleDepthKeys)
        {
            _originalStrapDepth.Remove(uid);
        }
    }

    /// <summary>
    /// Lower the draw depth of the buckled entity without needing for the strap entity to rotate/move.
    /// Only do so when the entity is facing screen-local north
    /// </summary>
    private void OnBuckledEvent(Entity<BuckleComponent> ent, ref BuckledEvent args)
    {
        if (!TryComp<SpriteComponent>(args.Strap, out var strapSprite))
            return;

        if (!TryComp<SpriteComponent>(ent.Owner, out var buckledSprite))
            return;

        // Frontier: maintain sprite order
        if (args.Strap.Comp.MaintainSpriteLayers)
            return;
        // End Frontier

        if (!IsFacingScreenNorth(args.Strap))
            return;

        ent.Comp.OriginalDrawDepth ??= buckledSprite.DrawDepth;
        _sprite.SetDrawDepth((ent.Owner, buckledSprite), strapSprite.DrawDepth - 1);
    }

    /// <summary>
    /// Was the draw depth of the buckled entity lowered? Reset it upon unbuckling.
    /// </summary>
    private void OnUnbuckledEvent(Entity<BuckleComponent> ent, ref UnbuckledEvent args)
    {
        if (!TryComp<SpriteComponent>(ent.Owner, out var buckledSprite))
            return;

        if (!ent.Comp.OriginalDrawDepth.HasValue)
            return;

        _sprite.SetDrawDepth((ent.Owner, buckledSprite), ent.Comp.OriginalDrawDepth.Value);
        ent.Comp.OriginalDrawDepth = null;
    }
}
