using Content.Shared.Construction;
using Content.Shared.Construction.Conditions;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Tag;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;


namespace Content.Shared._LoneStar.Construction.Conditions
{
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class NoBlockingOrSameFacingWindowDirectional : IConstructionCondition
    {

        private static readonly ProtoId<TagPrototype> WindowDirectionalTag = "WindowDirectional";

        public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
        {
            var entManager = IoCManager.Resolve<IEntityManager>();
            var sysMan = entManager.EntitySysManager;
            var tagSystem = sysMan.GetEntitySystem<TagSystem>();
            var lookupSys = sysMan.GetEntitySystem<EntityLookupSystem>();

            if (!location.TryGetTileRef(out var tile))
                return false;

            var fixtureQuery = entManager.GetEntityQuery<FixturesComponent>();
            foreach (var entity in lookupSys.GetLocalEntitiesIntersecting(tile.Value, flags: LookupFlags.Approximate | LookupFlags.Static))
            {
                if (tagSystem.HasTag(entity, WindowDirectionalTag))
                {
                    // Directional Windows, deny same direction
                    var entityDirection = entManager
                        .GetComponent<TransformComponent>(entity)
                        .LocalRotation
                        .GetCardinalDir();

                    if (entityDirection == direction)
                        return false;

                    continue;
                }

                if (!fixtureQuery.TryGetComponent(entity, out var fixtures))
                    continue;

                foreach (var fixture in fixtures.Fixtures.Values)
                {
                    if (fixture.Hard && (fixture.CollisionLayer & (int)CollisionGroup.Impassable) != 0)
                        return false;
                }
            }

            return true;
        }

        public ConstructionGuideEntry GenerateGuideEntry()
        {
            return new ConstructionGuideEntry
            {
                Localization = "construction-guide-condition-empty-or-window-valid-in-tile"
            };
        }
    }
}