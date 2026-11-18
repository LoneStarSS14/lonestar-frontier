using System.Numerics;
using Content.Client.DisplacementMap;
using Content.Shared.CCVar;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Humanoid;

public sealed class HumanoidAppearanceSystem : SharedHumanoidAppearanceSystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly MarkingManager _markingManager = default!;
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly DisplacementMapSystem _displacement = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public ProfilePreviewSettings? ProfilePreviewSettings = null;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HumanoidAppearanceComponent, AfterAutoHandleStateEvent>(OnHandleState);
        Subs.CVar(_configurationManager, CCVars.AccessibilityClientCensorNudity, OnCvarChanged, true);
        Subs.CVar(_configurationManager, CCVars.AccessibilityServerCensorNudity, OnCvarChanged, true);
    }

    private void OnHandleState(EntityUid uid, HumanoidAppearanceComponent component, ref AfterAutoHandleStateEvent args)
    {
        UpdateSprite((uid, component, Comp<SpriteComponent>(uid)));
    }

    private void OnCvarChanged(bool value)
    {
        var humanoidQuery = AllEntityQuery<HumanoidAppearanceComponent, SpriteComponent>();
        while (humanoidQuery.MoveNext(out var uid, out var humanoidComp, out var spriteComp))
        {
            UpdateSprite((uid, humanoidComp, spriteComp));
        }
    }

    private void UpdateSprite(Entity<HumanoidAppearanceComponent, SpriteComponent> entity)
    {
        UpdateLayers(entity);
        ApplyMarkingSet(entity);
        UpdateLayersAgain(entity);

        var humanoidAppearance = entity.Comp1;
        var sprite = entity.Comp2;

        sprite.Scale = new Vector2(humanoidAppearance.Width, humanoidAppearance.Height);
        sprite[_sprite.LayerMapReserve((entity.Owner, sprite), HumanoidVisualLayers.Eyes)].Color = humanoidAppearance.EyeColor;
    }

    private static bool IsHidden(HumanoidAppearanceComponent humanoid, HumanoidVisualLayers layer)
        => humanoid.HiddenLayers.ContainsKey(layer) || humanoid.PermanentlyHidden.Contains(layer);

    private void UpdateLayers(Entity<HumanoidAppearanceComponent, SpriteComponent> entity)
    {
        var component = entity.Comp1;
        var sprite = entity.Comp2;

        var oldLayers = new HashSet<HumanoidVisualLayers>(component.BaseLayers.Keys);
        component.BaseLayers.Clear();
        component.HiddenBaseLayers.Clear();

        // add default species layers
        var speciesProto = _prototypeManager.Index(component.Species);
        var baseSprites = _prototypeManager.Index(speciesProto.SpriteSet);
        foreach (var (key, id) in baseSprites.Sprites)
        {
            oldLayers.Remove(key);
            if (!component.CustomBaseLayers.ContainsKey(key))
                SetLayerData(entity, key, id, sexMorph: true);
        }

        // add custom layers
        foreach (var (key, info) in component.CustomBaseLayers)
        {
            oldLayers.Remove(key);
            SetLayerData(entity, key, info.Id, sexMorph: false, color: info.Color);
        }

        // hide old layers
        // TODO maybe just remove them altogether?
        foreach (var key in oldLayers)
        {
            if (_sprite.LayerMapTryGet((entity.Owner, sprite), key, out var index, false))
                sprite[index].Visible = false;
        }
    }

    private void UpdateLayersAgain(Entity<HumanoidAppearanceComponent, SpriteComponent> entity)
    {
        var component = entity.Comp1;
        var sprite = entity.Comp2;

        foreach (var layer in component.HiddenBaseLayers)
        {
            if (_sprite.LayerMapTryGet((entity.Owner, sprite), layer, out var index, false))
                sprite[index].Visible = false;
        }
    }

    public void HideBaseLayers(
        EntityUid uid,
        HumanoidAppearanceComponent? humanoid,
        IEnumerable<HumanoidVisualLayers> layersToHide)
    {
        if (!Resolve(uid, ref humanoid))
            return;

        var sprite = Comp<SpriteComponent>(uid);
        var species = _prototypeManager.Index(humanoid.Species);
        var baseSprites = _prototypeManager.Index(species.SpriteSet);

        foreach (var layer in layersToHide)
        {
            if (!baseSprites.Sprites.ContainsKey(layer)
                || !humanoid.BaseLayers.ContainsKey(layer)
                || !_sprite.LayerMapTryGet((uid, sprite), layer, out var index, false))
                continue;

            sprite[index].Visible = false;
            humanoid.PermanentlyHidden.Add(layer);
        }
    }

    private void SetLayerData(
        Entity<HumanoidAppearanceComponent, SpriteComponent> entity,
        HumanoidVisualLayers key,
        string? protoId,
        bool sexMorph = false,
        Color? color = null)
    {
        var component = entity.Comp1;
        var sprite = entity.Comp2;

        var layerIndex = _sprite.LayerMapReserve((entity.Owner, sprite), key);
        var layer = sprite[layerIndex];
        layer.Visible = !IsHidden(component, key);

        if (color != null)
            layer.Color = color.Value;

        if (protoId == null)
            return;

        if (sexMorph)
            protoId = HumanoidVisualLayersExtension.GetSexMorph(key, component.Sex, protoId);

        var proto = _prototypeManager.Index<HumanoidSpeciesSpriteLayer>(protoId);
        component.BaseLayers[key] = proto;

        if (proto.MatchSkin)
            layer.Color = component.SkinColor.WithAlpha(proto.LayerAlpha);

        if (proto.BaseSprite != null)
        {
            var appropriateSprite = proto.BaseSprite;
            if (component.LegStyle != HumanoidLegStyle.Plantigrade && proto.AltSprites.Count > 0)
            {
                ProtoId<MarkingPrototype>? altMarkingProtoId = null;
                if (proto.AltSprites.TryGetValue(component.LegStyle, out var altSprite)
                    || proto.AltSprites.TryGetValue(HumanoidLegStyle.Digitigrade, out altSprite))
                {
                    altMarkingProtoId = altSprite;
                }

                if (altMarkingProtoId is not null
                    && _prototypeManager.TryIndex(altMarkingProtoId, out MarkingPrototype? altMarkingProto))
                {
                    if (altMarkingProto.BaseLayerSprite is SpriteSpecifier.Rsi)
                        appropriateSprite = altMarkingProto.BaseLayerSprite;
                    else if (altMarkingProto.Sprites.Count > 0)
                        appropriateSprite = altMarkingProto.Sprites[0];
                }
            }

            _sprite.LayerSetSprite((entity.Owner, sprite), layerIndex, appropriateSprite);
        }
    }

    /// <summary>
    ///     Loads a profile directly into a humanoid.
    /// </summary>
    /// <param name="uid">The humanoid entity's UID</param>
    /// <param name="profile">The profile to load.</param>
    /// <param name="humanoid">The humanoid entity's humanoid component.</param>
    /// <remarks>
    ///     This should not be used if the entity is owned by the server. The server will otherwise
    ///     override this with the appearance data it sends over.
    /// </remarks>
    public override void LoadProfile(EntityUid uid, HumanoidCharacterProfile? profile, HumanoidAppearanceComponent? humanoid = null)
    {
        if (profile == null)
            return;

        if (!Resolve(uid, ref humanoid))
        {
            return;
        }

        var customBaseLayers = new Dictionary<HumanoidVisualLayers, CustomBaseLayerInfo>();

        var speciesPrototype = _prototypeManager.Index<SpeciesPrototype>(profile.Species);
        var markings = new MarkingSet(speciesPrototype.MarkingPoints, _markingManager, _prototypeManager);

        // Add markings that doesn't need coloring. We store them until we add all other markings that doesn't need it.
        var markingFColored = new Dictionary<Marking, MarkingPrototype>();
        foreach (var marking in profile.Appearance.Markings)
        {
            if (_markingManager.TryGetMarking(marking, out var prototype))
            {
                if (!prototype.ForcedColoring)
                {
                    markings.AddBack(prototype.MarkingCategory, marking);
                }
                else
                {
                    markingFColored.Add(marking, prototype);
                }
            }
        }

        // legacy: remove in the future?
        //markings.RemoveCategory(MarkingCategories.Hair);
        //markings.RemoveCategory(MarkingCategories.FacialHair);

        // We need to ensure hair before applying it or coloring can try depend on markings that can be invalid
        var hairColor = _markingManager.MustMatchSkin(profile.Species, HumanoidVisualLayers.Hair, out var hairAlpha, _prototypeManager)
            ? profile.Appearance.SkinColor.WithAlpha(hairAlpha)
            : profile.Appearance.HairColor;
        var hair = new Marking(profile.Appearance.HairStyleId,
            new[] { hairColor });

        var facialHairColor = _markingManager.MustMatchSkin(profile.Species, HumanoidVisualLayers.FacialHair, out var facialHairAlpha, _prototypeManager)
            ? profile.Appearance.SkinColor.WithAlpha(facialHairAlpha)
            : profile.Appearance.FacialHairColor;
        var facialHair = new Marking(profile.Appearance.FacialHairStyleId,
            new[] { facialHairColor });

        // Frontier: Match hair and facial hair colors to the forced color if it exists
        if (_markingManager.MustMatchColor(profile.Species, HumanoidVisualLayers.Hair, out var forcedHairAlpha, _prototypeManager) is Color forcedHairColor)
        {
            profile.Appearance.SkinColor.WithAlpha(forcedHairAlpha);
            hairColor = forcedHairColor;
        }
        if (_markingManager.MustMatchColor(profile.Species, HumanoidVisualLayers.FacialHair, out var forcedFacialHairAlpha, _prototypeManager) is Color forcedFacialHairColor)
        {
            profile.Appearance.SkinColor.WithAlpha(forcedFacialHairAlpha);
            facialHairColor = forcedFacialHairColor;
        }
        // End Frontier

        if (_markingManager.CanBeApplied(profile.Species, profile.Sex, hair, _prototypeManager))
        {
            markings.AddBack(MarkingCategories.Hair, hair);
        }
        if (_markingManager.CanBeApplied(profile.Species, profile.Sex, facialHair, _prototypeManager))
        {
            markings.AddBack(MarkingCategories.FacialHair, facialHair);
        }

        // Finally adding marking with forced colors
        foreach (var (marking, prototype) in markingFColored)
        {
            var markingColors = MarkingColoring.GetMarkingLayerColors(
                prototype,
                profile.Appearance.SkinColor,
                profile.Appearance.EyeColor,
                markings
            );
            markings.AddBack(prototype.MarkingCategory, new Marking(marking, markingColors));
        }

        markings.EnsureSpecies(profile.Species, profile.Appearance.SkinColor, _markingManager, _prototypeManager);
        markings.EnsureSexes(profile.Sex, _markingManager);
        markings.EnsureDefault(
            profile.Appearance.SkinColor,
            profile.Appearance.EyeColor,
            _markingManager);

        DebugTools.Assert(IsClientSide(uid));

        humanoid.MarkingSet = markings;
        humanoid.HiddenMarkings = new HashSet<string>();
        foreach (var markingList in humanoid.MarkingSet.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                if (!marking.ShowAtStart)
                    humanoid.HiddenMarkings.Add(marking.MarkingId);
            }
        }

        humanoid.PermanentlyHidden = new HashSet<HumanoidVisualLayers>();
        humanoid.HiddenLayers = new Dictionary<HumanoidVisualLayers, SlotFlags>();
        humanoid.CustomBaseLayers = customBaseLayers;
        humanoid.Sex = profile.Sex;
        humanoid.LegStyle = profile.Appearance.LegStyle;
        humanoid.Gender = profile.Gender;
        humanoid.Age = profile.Age;
        humanoid.CustomSpecieName = profile.Customspeciesname;
        humanoid.Species = profile.Species;
        humanoid.SkinColor = profile.Appearance.SkinColor;
        humanoid.EyeColor = profile.Appearance.EyeColor;

        if (ProfilePreviewSettings != null)
        {
            if (!ProfilePreviewSettings.ShowUndies)
            {
                humanoid.PermanentlyHidden.Add(HumanoidVisualLayers.UndergarmentBottom);
                humanoid.PermanentlyHidden.Add(HumanoidVisualLayers.UndergarmentTop);
            }

            if (!ProfilePreviewSettings.ShowGenitals)
            {
                humanoid.PermanentlyHidden.Add(HumanoidVisualLayers.Genital);
            }
            else if (humanoid.MarkingSet.Markings.TryGetValue(MarkingCategories.Genital, out var genitalMarkings))
            {
                foreach (var genitalMarking in genitalMarkings)
                {
                    humanoid.HiddenMarkings.Remove(genitalMarking.MarkingId);
                }
            }
        }

        UpdateSprite((uid, humanoid, Comp<SpriteComponent>(uid)));
    }

    public void HideUndies(EntityUid uid, HumanoidAppearanceComponent? humanoid = null)
    {
        if (!Resolve(uid, ref humanoid))
            return;

        humanoid.PermanentlyHidden.Add(HumanoidVisualLayers.UndergarmentBottom);
        humanoid.PermanentlyHidden.Add(HumanoidVisualLayers.UndergarmentTop);
        UpdateSprite((uid, humanoid, Comp<SpriteComponent>(uid)));
    }

    public void HideGenitals(EntityUid uid, HumanoidAppearanceComponent? humanoid = null)
    {
        if (!Resolve(uid, ref humanoid))
            return;

        humanoid.PermanentlyHidden.Add(HumanoidVisualLayers.Genital);
        UpdateSprite((uid, humanoid, Comp<SpriteComponent>(uid)));
    }

    private void ApplyMarkingSet(Entity<HumanoidAppearanceComponent, SpriteComponent> entity)
    {
        var humanoid = entity.Comp1;
        var sprite = entity.Comp2;

        // I am lazy and I CBF resolving the previous mess, so I'm just going to nuke the markings.
        // Really, markings should probably be a separate component altogether.
        ClearAllMarkings(entity);

        var censorNudity = _configurationManager.GetCVar(CCVars.AccessibilityClientCensorNudity) ||
                           _configurationManager.GetCVar(CCVars.AccessibilityServerCensorNudity);
        // The reason we're splitting this up is in case the character already has undergarment equipped in that slot.
        var applyUndergarmentTop = censorNudity;
        var applyUndergarmentBottom = censorNudity;

        foreach (var markingList in humanoid.MarkingSet.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                if (_markingManager.TryGetMarking(marking, out var markingPrototype))
                {
                    ApplyMarking(markingPrototype, marking, entity);
                    if (markingPrototype.BodyPart == HumanoidVisualLayers.UndergarmentTop)
                        applyUndergarmentTop = false;
                    else if (markingPrototype.BodyPart == HumanoidVisualLayers.UndergarmentBottom)
                        applyUndergarmentBottom = false;
                }
            }
        }

        humanoid.ClientOldMarkings = new MarkingSet(humanoid.MarkingSet);

        AddUndergarments(entity, applyUndergarmentTop, applyUndergarmentBottom);
    }

    private void ClearAllMarkings(Entity<HumanoidAppearanceComponent, SpriteComponent> entity)
    {
        var humanoid = entity.Comp1;
        var sprite = entity.Comp2;

        foreach (var markingList in humanoid.ClientOldMarkings.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                RemoveMarking(marking, (entity, sprite));
            }
        }

        humanoid.ClientOldMarkings.Clear();

        foreach (var markingList in humanoid.MarkingSet.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                RemoveMarking(marking, (entity, sprite));
            }
        }
    }

    private void RemoveMarking(Marking marking, Entity<SpriteComponent> spriteEnt)
    {
        if (!_markingManager.TryGetMarking(marking, out var prototype))
        {
            return;
        }

        foreach (var sprite in prototype.Sprites)
        {
            if (sprite is not SpriteSpecifier.Rsi rsi)
            {
                continue;
            }

            var layerId = $"{marking.MarkingId}-{rsi.RsiState}";
            if (_sprite.LayerMapTryGet(spriteEnt.AsNullable(), layerId, out var index, false))
            {
                _sprite.LayerMapRemove(spriteEnt.AsNullable(), layerId);
                _sprite.RemoveLayer(spriteEnt.AsNullable(), index);
            }

            var glowLayerId = $"{layerId}-glow";
            if (_sprite.LayerMapTryGet(spriteEnt.AsNullable(), glowLayerId, out var glowIndex, false))
            {
                _sprite.LayerMapRemove(spriteEnt.AsNullable(), glowLayerId);
                _sprite.RemoveLayer(spriteEnt.AsNullable(), glowIndex);
            }
        }
    }

    private void AddUndergarments(Entity<HumanoidAppearanceComponent, SpriteComponent> entity, bool undergarmentTop, bool undergarmentBottom)
    {
        var humanoid = entity.Comp1;

        if (undergarmentTop && humanoid.UndergarmentTop != null)
        {
            var marking = new Marking(humanoid.UndergarmentTop, new List<Color> { new Color() });
            if (_markingManager.TryGetMarking(marking, out var prototype))
            {
                // Markings are added to ClientOldMarkings because otherwise it causes issues when toggling the feature on/off.
                humanoid.ClientOldMarkings.Markings.Add(MarkingCategories.UndergarmentTop, new List<Marking> { marking });
                ApplyMarking(prototype, marking, entity);
            }
        }

        if (undergarmentBottom && humanoid.UndergarmentBottom != null)
        {
            var marking = new Marking(humanoid.UndergarmentBottom, new List<Color> { new Color() });
            if (_markingManager.TryGetMarking(marking, out var prototype))
            {
                humanoid.ClientOldMarkings.Markings.Add(MarkingCategories.UndergarmentBottom, new List<Marking> { marking });
                ApplyMarking(prototype, marking, entity);
            }
        }
    }

    private void ApplyMarking(
        MarkingPrototype markingPrototype,
        Marking marking,
        Entity<HumanoidAppearanceComponent, SpriteComponent> entity)
    {
        var humanoid = entity.Comp1;
        var sprite = entity.Comp2;
        var facingDirection = sprite.EnableDirectionOverride
            ? sprite.DirectionOverride
            : Transform(entity.Owner).LocalRotation.GetCardinalDir();
        var markingOffset = marking.GetOffset(facingDirection);
        var layerOrder = new Dictionary<HumanoidVisualLayers, int>();
        var colors = new Dictionary<string, Color>();
        var glowLevels = new Dictionary<string, float>();

        for (var i = 0; i < markingPrototype.Sprites.Count; i++)
        {
            var state = markingPrototype.Sprites[i] switch
            {
                SpriteSpecifier.Rsi rsi => rsi.RsiState,
                SpriteSpecifier.Texture texture => texture.TexturePath.Filename,
                _ => null
            };

            if (state == null)
                continue;

            colors[state] = i < marking.MarkingColors.Count ? marking.MarkingColors[i] : Color.White;
            glowLevels[state] = i < marking.MarkingGlow.Count
                ? Math.Clamp(marking.MarkingGlow[i], 0f, 1f)
                : 0f;
        }

        if (markingPrototype.ColorLinks != null)
        {
            foreach (var (child, parent) in markingPrototype.ColorLinks)
            {
                if (colors.TryGetValue(parent, out var color))
                    colors[child] = color;

                if (glowLevels.TryGetValue(parent, out var glow))
                    glowLevels[child] = glow;
            }
        }

        var visible = marking.Visible && !humanoid.HiddenMarkings.Contains(markingPrototype.ID);

        for (var j = 0; j < markingPrototype.Sprites.Count; j++)
        {
            var markingSprite = markingPrototype.Sprites[j];

            if (markingSprite is not SpriteSpecifier.Rsi rsi)
            {
                continue;
            }

            var layerSlot = markingPrototype.BodyPart;
            if (markingPrototype.Layering?.TryGetValue(rsi.RsiState, out var layerName) == true)
                layerSlot = Enum.Parse<HumanoidVisualLayers>(layerName);

            if ((markingPrototype.MarkingCategory == MarkingCategories.Genital
                    || markingPrototype.BodyPart == HumanoidVisualLayers.Genital)
                && layerSlot == HumanoidVisualLayers.Genital)
            {
                layerSlot = marking.RenderOverClothing
                    ? HumanoidVisualLayers.TailOversuit
                    : HumanoidVisualLayers.Genital;
            }
            else if (marking.RenderOverClothing && layerSlot == HumanoidVisualLayers.Genital)
            {
                layerSlot = HumanoidVisualLayers.TailOversuit;
            }

            var offset = layerOrder.GetValueOrDefault(layerSlot);
            layerOrder[layerSlot] = offset + 1;

            if (!_sprite.LayerMapTryGet((entity.Owner, sprite), layerSlot, out var targetLayer, false))
                continue;

            var layerVisible = visible && !IsHidden(humanoid, layerSlot);
            layerVisible &= humanoid.BaseLayers.TryGetValue(layerSlot, out var setting)
                ? setting.AllowsMarkings
                : layerSlot == HumanoidVisualLayers.Genital;

            var targetIndex = targetLayer + offset + 1;
            if (!marking.RenderOverClothing
                && (markingPrototype.MarkingCategory == MarkingCategories.Genital
                    || markingPrototype.BodyPart == HumanoidVisualLayers.Genital))
            {
                if (_sprite.LayerMapTryGet((entity.Owner, sprite), "jumpsuit", out var jumpsuitLayer, false))
                    targetIndex = Math.Min(targetIndex, jumpsuitLayer - 1);

                if (_sprite.LayerMapTryGet((entity.Owner, sprite), "outerClothing", out var outerClothingLayer, false))
                    targetIndex = Math.Min(targetIndex, outerClothingLayer - 1);

                targetIndex = Math.Max(targetIndex, 0);
            }

            var layerId = $"{markingPrototype.ID}-{rsi.RsiState}";
            var glowLayerId = $"{layerId}-glow";

            if (!_sprite.LayerMapTryGet((entity.Owner, sprite), layerId, out _, false))
            {
                var layer = _sprite.AddLayer((entity.Owner, sprite), markingSprite, targetIndex);
                _sprite.LayerMapSet((entity.Owner, sprite), layerId, layer);
                _sprite.LayerSetSprite((entity.Owner, sprite), layerId, rsi);
            }

            // impstation edit begin - check if there's a shader defined in the markingPrototype's shader datafield, and if there is...
            if (markingPrototype.Shader != null)
            {
                // use spriteComponent's layersetshader function to set the layer's shader to that which is specified.
                sprite.LayerSetShader(layerId, markingPrototype.Shader);
            }
            // impstation edit end

            _sprite.LayerSetVisible((entity.Owner, sprite), layerId, layerVisible);
            _sprite.LayerSetScale((entity.Owner, sprite), layerId, new Vector2(marking.MarkingScale));
            _sprite.LayerSetOffset((entity.Owner, sprite), layerId, markingOffset);

            if (!layerVisible)
            {
                if (_sprite.LayerMapTryGet((entity.Owner, sprite), glowLayerId, out var hiddenGlowIndex, false))
                {
                    _sprite.LayerMapRemove((entity.Owner, sprite), glowLayerId);
                    _sprite.RemoveLayer((entity.Owner, sprite), hiddenGlowIndex);
                }
                continue;
            }

            var color = colors.GetValueOrDefault(rsi.RsiState, Color.White);
            var glow = glowLevels.GetValueOrDefault(rsi.RsiState);
            var glowAlpha = color.A * glow;
            var denominator = 1f - glowAlpha;
            var baseLayerAlpha = denominator > 0f ? (color.A - glowAlpha) / denominator : 0f;
            baseLayerAlpha = Math.Clamp(baseLayerAlpha, 0f, 1f);
            _sprite.LayerSetColor((entity.Owner, sprite), layerId, color.WithAlpha(baseLayerAlpha));

            if (glow > 0f)
            {
                if (!_sprite.LayerMapTryGet((entity.Owner, sprite), glowLayerId, out _, false))
                {
                    var glowLayer = _sprite.AddLayer((entity.Owner, sprite), markingSprite, targetIndex + 1);
                    _sprite.LayerMapSet((entity.Owner, sprite), glowLayerId, glowLayer);
                    _sprite.LayerSetSprite((entity.Owner, sprite), glowLayerId, rsi);
                }

                sprite.LayerSetShader(glowLayerId, "unshaded");
                _sprite.LayerSetVisible((entity.Owner, sprite), glowLayerId, layerVisible);
                _sprite.LayerSetColor((entity.Owner, sprite), glowLayerId, color.WithAlpha(glowAlpha));
                _sprite.LayerSetScale((entity.Owner, sprite), glowLayerId, new Vector2(marking.MarkingScale));
                _sprite.LayerSetOffset((entity.Owner, sprite), glowLayerId, markingOffset);
                humanoid.ClientElderMarkings.Add(glowLayerId);
            }
            else if (_sprite.LayerMapTryGet((entity.Owner, sprite), glowLayerId, out var glowIndex, false))
            {
                _sprite.LayerMapRemove((entity.Owner, sprite), glowLayerId);
                _sprite.RemoveLayer((entity.Owner, sprite), glowIndex);
            }

            if (humanoid.MarkingsDisplacement.TryGetValue(layerSlot, out var displacementData) && markingPrototype.CanBeDisplaced)
            {
                _displacement.TryAddDisplacement(displacementData, (entity.Owner, sprite), targetIndex, layerId, out _);
            }
        }

        if (MarkingCategoriesConversion.Category2Layer(
                markingPrototype.MarkingCategory,
                markingPrototype.BodyPart,
                out var hiddenBaseLayer)
            && !humanoid.HiddenBaseLayers.Contains(hiddenBaseLayer))
        {
            humanoid.HiddenBaseLayers.Add(hiddenBaseLayer);
        }

    }

    public override void SetSkinColor(EntityUid uid, Color skinColor, bool sync = true, bool verify = true, HumanoidAppearanceComponent? humanoid = null)
    {
        if (!Resolve(uid, ref humanoid) || humanoid.SkinColor == skinColor)
            return;

        base.SetSkinColor(uid, skinColor, false, verify, humanoid);

        if (!TryComp(uid, out SpriteComponent? sprite))
            return;

        foreach (var (layer, spriteInfo) in humanoid.BaseLayers)
        {
            if (!spriteInfo.MatchSkin)
                continue;

            var index = _sprite.LayerMapReserve((uid, sprite), layer);
            sprite[index].Color = skinColor.WithAlpha(spriteInfo.LayerAlpha);
        }
    }

    public override void SetLayerVisibility(
        Entity<HumanoidAppearanceComponent> ent,
        HumanoidVisualLayers layer,
        bool visible,
        SlotFlags? slot,
        ref bool dirty)
    {
        base.SetLayerVisibility(ent, layer, visible, slot, ref dirty);

        var sprite = Comp<SpriteComponent>(ent);
        if (!_sprite.LayerMapTryGet((ent.Owner, sprite), layer, out var index, false))
        {
            if (!visible)
                return;
            index = _sprite.LayerMapReserve((ent.Owner, sprite), layer);
        }

        var spriteLayer = sprite[index];
        if (spriteLayer.Visible == visible)
            return;

        spriteLayer.Visible = visible;

        // I fucking hate this. I'll get around to refactoring sprite layers eventually I swear
        // Just a week away...

        foreach (var markingList in ent.Comp.MarkingSet.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                if (_markingManager.TryGetMarking(marking, out var markingPrototype) && markingPrototype.BodyPart == layer)
                    ApplyMarking(markingPrototype, marking, (ent, ent.Comp, sprite));
            }
        }

        UpdateLayersAgain((ent, ent.Comp, sprite));
    }
}

public sealed class ProfilePreviewSettings(
    bool showUndies = true,
    bool showGenitals = true
)
{
    public bool ShowUndies = showUndies;
    public bool ShowGenitals = showGenitals;
}
