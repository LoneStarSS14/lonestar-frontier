using System.Diagnostics.CodeAnalysis;
using Content.Client.DisplacementMap;
using Content.Shared.CCVar;
using System.Numerics;
using Content.Shared._CS;
using Content.Shared.DisplacementMap;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Humanoid;

public sealed class HumanoidAppearanceSystem : SharedHumanoidAppearanceSystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly MarkingManager _markingManager = default!;
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly DisplacementMapSystem _displacement = default!;

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
        UpdateSprite(uid, component, Comp<SpriteComponent>(uid));
    }

    private void OnCvarChanged(bool value)
    {
        var humanoidQuery = EntityManager.AllEntityQueryEnumerator<HumanoidAppearanceComponent, SpriteComponent>();
        while (humanoidQuery.MoveNext(out var uid, out var humanoidComp, out var spriteComp))
        {
            UpdateSprite(uid, humanoidComp, spriteComp);
        }
    }

    private void UpdateSprite(EntityUid uid, HumanoidAppearanceComponent component, SpriteComponent sprite)
    {

        UpdateLayers(component, sprite);
        ApplyMarkingSet(uid, component, sprite);
        // TODO: make this thing a more versatulate proc // todo: figure out what I meant by this
        var speciesPrototype = _prototypeManager.Index(component.Species);

        // Don't clamp height/width on client - the server already handles limits
        // Clamping here prevents temporary size effects (size gun, clothing, buffs) from displaying properly
        var height = component.Height;
        var width = component.Width;

        // Directly set sprite scale - this is the original approach that worked
        // Using SpriteSystem.SetScale() was causing issues with outline shader rendering
        sprite.Scale = new Vector2(width, height);

        UpdateLayersAgain(component, sprite); // cool

        sprite[sprite.LayerMapReserveBlank(HumanoidVisualLayers.Eyes)].Color = component.EyeColor;
    }

    private static bool IsHidden(HumanoidAppearanceComponent humanoid, HumanoidVisualLayers layer)
        => humanoid.HiddenLayers.ContainsKey(layer) || humanoid.PermanentlyHidden.Contains(layer);

    private void UpdateLayers(HumanoidAppearanceComponent component, SpriteComponent sprite)
    {
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
            {
                SetLayerData(
                    component,
                    sprite,
                    key,
                    id,
                    sexMorph: true);
            }
        }

        // add custom layers
        foreach (var (key, info) in component.CustomBaseLayers)
        {
            oldLayers.Remove(key);
            SetLayerData(
                component,
                sprite,
                key,
                info.Id,
                sexMorph: false,
                color: info.Color);
        }

        // hide old layers
        // TODO maybe just remove them altogether?
        foreach (var key in oldLayers)
        {
            if (sprite.LayerMapTryGet(key, out var index))
                sprite[index].Visible = false;
        }
    }

    /// <summary>
    /// Goes through the layers again and hides them if they are hidden by the comp's HiddenBaseLayers.
    /// Bite me
    /// </summary>
    private void UpdateLayersAgain(
        HumanoidAppearanceComponent component,
        SpriteComponent sprite)
    {
        foreach (var layer in component.HiddenBaseLayers)
        {
            if (sprite.LayerMapTryGet(layer, out var index))
            {
                sprite[index].Visible = false;
            }
        }
    }

    /// <summary>
    /// Sets the data for a specific layer on the sprite component.
    /// This is for the base layers only, so like, arms, legs, torso, head, etc.
    /// </summary>
    private void SetLayerData(
        HumanoidAppearanceComponent component,
        SpriteComponent sprite,
        HumanoidVisualLayers key,
        string? protoId,
        bool sexMorph = false,
        Color? color = null)
    {
        var layerIndex = sprite.LayerMapReserveBlank(key);
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
            SpriteSpecifier appropriateSprite = proto.BaseSprite;
            // Coyote: add support for cute digitigrade legs
            if (component.LegStyle != HumanoidLegStyle.Plantigrade
                && proto.AltSprites.Count > 0)
            {
                ProtoId<MarkingPrototype>? altMarkingProtoId = null;
                // we have to do two things: check if the leg style is supported, and if not, check if Digitigrade
                // is supported. At least one needs to be true!
                if (proto.AltSprites.TryGetValue(component.LegStyle, out ProtoId<MarkingPrototype> altSprite)
                    || proto.AltSprites.TryGetValue(HumanoidLegStyle.Digitigrade, out altSprite))
                {
                    altMarkingProtoId = altSprite;
                }
                if (altMarkingProtoId is not null
                    && _prototypeManager.TryIndex(altMarkingProtoId, out MarkingPrototype? altMarkingProto))
                {
                    // just use the first sprite, as base layers only have one sprite
                    // the markings should only have one sprite anyway
                    if (altMarkingProto.BaseLayerSprite is SpriteSpecifier.Rsi)
                    {
                        appropriateSprite = altMarkingProto.BaseLayerSprite;
                    }
                    else if (altMarkingProto.Sprites.Count > 0)
                    {
                        appropriateSprite = altMarkingProto.Sprites[0];
                    }
                }
                // shader will be appliesed lader
            }
            // Coyote End
            sprite.LayerSetSprite(layerIndex, appropriateSprite);
        }
    }

    /// <summary>
    /// Finds this list of keys in the <see cref="HumanoidAppearanceComponent.BaseLayers"/> and
    /// Hides them if they are present. To be used after markings are applied, to hide the base
    /// layer if something is set on the marking base layer. g
    /// </summary>
    public void HideBaseLayers(
        EntityUid uid,
        HumanoidAppearanceComponent? humanoid,
        IEnumerable<HumanoidVisualLayers> layers2Hide)
    {
        if (!Resolve(uid, ref humanoid))
            return;

        var sprite = Comp<SpriteComponent>(uid);
        var speciesProto = _prototypeManager.Index(humanoid.Species);
        var baseSprites = _prototypeManager.Index(speciesProto.SpriteSet);

        foreach (var layer in layers2Hide)
        {
            if (!baseSprites.Sprites.TryGetValue(layer, out var id))
                continue;
            if (humanoid.BaseLayers.TryGetValue(layer, out var baseLayer)
                && sprite.LayerMapTryGet(layer, out var index))
            {
                sprite[index].Visible = false;
                humanoid.PermanentlyHidden.Add(layer);
            }
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
    public override void LoadProfile(
        EntityUid uid,
        HumanoidCharacterProfile? profile,
        HumanoidAppearanceComponent? humanoid = null
        )
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

        markings.EnsureSpecies(
            profile.Species,
            profile.Appearance.SkinColor,
            _markingManager,
            _prototypeManager);

        markings.EnsureSexes(
            profile.Sex,
            _markingManager);

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
        humanoid.Gender = profile.Gender;
        humanoid.Age = profile.Age;
        humanoid.Species = profile.Species;
        humanoid.SkinColor = profile.Appearance.SkinColor;
        humanoid.EyeColor = profile.Appearance.EyeColor;
        humanoid.Height = profile.Height;
        humanoid.Width = profile.Width;
        humanoid.LegStyle = profile.Appearance.LegStyle;

        // Apply profile preview settings if they exist
        if (ProfilePreviewSettings != null)
        {
            if (!ProfilePreviewSettings.ShowUndies)
            {
                humanoid.PermanentlyHidden.Add(HumanoidVisualLayers.UndergarmentBottom);
                humanoid.PermanentlyHidden.Add(HumanoidVisualLayers.UndergarmentTop);
            }
            // Coyote Start
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
            // Coyote End
        }

        UpdateSprite(uid, humanoid, Comp<SpriteComponent>(uid));
    }

    private void ApplyMarkingSet(EntityUid uid, HumanoidAppearanceComponent humanoid, SpriteComponent sprite)
    {
        // Only nuke and rebuild sprite layers when the marking set actually changed.
        // Skipping ClearAllMarkings when markings are unchanged avoids expensive AddLayer/RemoveLayer churn.
        var markingsUnchanged =
            humanoid.MarkingSet.Markings.Count == humanoid.ClientOldMarkings.Markings.Count
            && humanoid.MarkingSet.Equals(humanoid.ClientOldMarkings);

        if (!markingsUnchanged)
            ClearAllMarkings(humanoid, sprite);

        // var censorNudity = _configurationManager.GetCVar(CCVars.AccessibilityClientCensorNudity) ||
        //                    _configurationManager.GetCVar(CCVars.AccessibilityServerCensorNudity);
        // The reason we're splitting this up is in case the character already has undergarment equipped in that slot.
        // var applyUndergarmentTop = censorNudity;
        // var applyUndergarmentBottom = censorNudity;

        // Coyote Start
        var facingDirection = sprite.EnableDirectionOverride
            ? sprite.DirectionOverride
            : Transform(uid).LocalRotation.GetCardinalDir();
        // Coyote End

        foreach (List<Marking> markingList in humanoid.MarkingSet.Markings.Values)
        {
            foreach (Marking marking in markingList)
            {
                if (!_markingManager.TryGetMarking(marking, out MarkingPrototype? markingPrototype))
                    continue;
                PreModifyMarking(
                    humanoid,
                    markingPrototype,
                    marking,
                    out Marking newMarking,
                    out MarkingPrototype newMarkingPrototype);
                ApplyMarking(
                    newMarkingPrototype,
                    newMarking.MarkingColors,
                    newMarking.MarkingGlow,
                    newMarking.Visible,
                    newMarking.RenderOverClothing,
                    humanoid,
                    sprite,
                    // Coyote Start
                    newMarking.MarkingScale,
                    newMarking.GetOffset(facingDirection));
                // Coyote End
            }
        }

        humanoid.ClientOldMarkings = new MarkingSet(humanoid.MarkingSet);

        // AddUndergarments(humanoid, sprite, applyUndergarmentTop, applyUndergarmentBottom);
    }

    /// <summary>
    /// Takes in the marking about to be applied, and allows modification of it before application.
    /// </summary>
    private void PreModifyMarking(
        HumanoidAppearanceComponent humanoid,
        MarkingPrototype markingPrototype,
        Marking marking,
        out Marking newMarking,
        out MarkingPrototype newPrototype
        )
    {
        newMarking = marking;
        newPrototype = markingPrototype;
        if (humanoid.LegStyle == HumanoidLegStyle.Plantigrade)
            return; // No need to modify anything for plantigrade legs.
        // Check if the marking has alternate sprites for the current leg style,
        // Or if they dont have that specific leg style, check if they have digitigrade paw instead
        // if neither are present, we just use the normal marking and proot
        if (markingPrototype.AlternateSprites.TryGetValue(humanoid.LegStyle, out var altMarkingProtoId)
            || (humanoid.LegStyle != HumanoidLegStyle.Digitigrade
                && markingPrototype.AlternateSprites.TryGetValue(HumanoidLegStyle.Digitigrade, out altMarkingProtoId)))
        {
            newPrototype = _prototypeManager.Index<MarkingPrototype>(altMarkingProtoId);
        }
    }

    private void ClearAllMarkings(HumanoidAppearanceComponent humanoid, SpriteComponent sprite)
    {
        foreach (var markingList in humanoid.ClientOldMarkings.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                RemoveMarking(marking, sprite);
            }
        }

        humanoid.ClientOldMarkings.Clear();

        foreach (var markingList in humanoid.MarkingSet.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                RemoveMarking(marking, sprite);
            }
        }

        // then we do it my way!
        foreach (var layid in humanoid.ClientElderMarkings)
        {
            if (sprite.LayerMapTryGet(layid, out var index))
            {
                sprite.LayerMapRemove(layid);
                sprite.RemoveLayer(index);
            }
        }
        humanoid.ClientElderMarkings.Clear();
    }

    private void RemoveMarking(Marking marking, SpriteComponent spriteComp)
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
            if (!spriteComp.LayerMapTryGet(layerId, out var index))
            {
                continue;
            }

            spriteComp.LayerMapRemove(layerId);
            spriteComp.RemoveLayer(index);

            var glowLayerId = $"{layerId}-glow";
            if (!spriteComp.LayerMapTryGet(glowLayerId, out var glowIndex))
                continue;

            spriteComp.LayerMapRemove(glowLayerId);
            spriteComp.RemoveLayer(glowIndex);
        }
    }

    private void ApplyMarking(
        MarkingPrototype markingPrototype,
        IReadOnlyList<Color>? colors,
        IReadOnlyList<float>? glowLevels,
        bool visible,
        bool renderOverClothing,
        HumanoidAppearanceComponent humanoid,
        SpriteComponent sprite,
        // Coyote Start
        float scale = 1.0f,
        Vector2? offset = null)
        // Coyote End
    {
        // Coyote Start
        var markingOffset = offset ?? Vector2.Zero;
        // Coyote End
        // FLOOF ADD START
        // make a handy dict of filename -> colors
        // cus we might need to access it by filename to link
        // one sprite's colors to another
        var colorDict = new Dictionary<string, Color>();
        var glowDict = new Dictionary<string, float>();
        for (var i = 0; i < markingPrototype.Sprites.Count; i++)
        {
            var spriteName = markingPrototype.Sprites[i] switch
            {
                SpriteSpecifier.Rsi rsi => rsi.RsiState,
                SpriteSpecifier.Texture texture => texture.TexturePath.Filename,
                _ => null
            };

            if (spriteName != null)
            {
                if (colors != null && i < colors.Count)
                    colorDict.Add(spriteName, colors[i]);
                else
                    colorDict.Add(spriteName, Color.White);

                if (glowLevels != null && i < glowLevels.Count)
                    glowDict.Add(spriteName, Math.Clamp(glowLevels[i], 0f, 1f));
                else
                    glowDict.Add(spriteName, 0f);
            }
        }
        // now, rearrange them, copying any parented colors to children set to
        // inherit them
        if (markingPrototype.ColorLinks != null)
        {
            foreach (var (child, parent) in markingPrototype.ColorLinks)
            {
                if (colorDict.TryGetValue(parent, out var color))
                {
                    colorDict[child] = color;
                }

                if (glowDict.TryGetValue(parent, out var glow))
                {
                    glowDict[child] = glow;
                }
            }
        }
        // and, since we can't rely on the iterator knowing where the heck to put
        // each sprite when we have one marking setting multiple layers,
        // lets just kinda sorta do that ourselves
        var layerDict = new Dictionary<string, int>();

        visible &= !humanoid.HiddenMarkings.Contains(markingPrototype.ID); // FLOOF ADD
        // FLOOF ADD END

        for (var j = 0; j < markingPrototype.Sprites.Count; j++)
        {
            // FLOOF CHANGE START
            var markingSprite = markingPrototype.Sprites[j];
            if (markingSprite is not SpriteSpecifier.Rsi rsi)
            {
                continue;
            }

            var layerSlot = markingPrototype.BodyPart;
            // first, try to see if there are any custom layers for this marking
            if (markingPrototype.Layering != null)
            {
                var name = rsi.RsiState;
                if (markingPrototype.Layering.TryGetValue(name, out var layerName))
                {
                    layerSlot = Enum.Parse<HumanoidVisualLayers>(layerName);
                }
            }

            // _CS Start: preserve explicit behind/front genital layering
            if ((markingPrototype.MarkingCategory == MarkingCategories.Genital
                    || markingPrototype.BodyPart == HumanoidVisualLayers.Genital)
                && layerSlot == HumanoidVisualLayers.Genital)
            {
                layerSlot = renderOverClothing
                    ? HumanoidVisualLayers.TailOversuit
                    : HumanoidVisualLayers.Genital;
            }
            else if (renderOverClothing && layerSlot == HumanoidVisualLayers.Genital)
            {
                layerSlot = HumanoidVisualLayers.TailOversuit;
            }
            // _CS End: preserve explicit behind/front genital layering
            // update the layerDict
            // if it doesnt have this, add it at 0, otherwise increment it
            if (layerDict.TryGetValue(layerSlot.ToString(), out var layerIndex))
            {
                layerDict[layerSlot.ToString()] = layerIndex + 1;
            }
            else
            {
                layerDict.Add(layerSlot.ToString(), 0);
            }

            if (!sprite.LayerMapTryGet(layerSlot, out var targetLayer))
            {
                continue;
            }

            // _CS Start: genital layer compatibility for species without an explicit Genital base-layer mapping
            var allowsLayerMarkings = humanoid.BaseLayers.TryGetValue(layerSlot, out var setting)
                ? setting.AllowsMarkings
                : layerSlot == HumanoidVisualLayers.Genital;

            visible &= !IsHidden(humanoid, layerSlot);
            visible &= allowsLayerMarkings;
            // _CS End: genital layer compatibility for species without an explicit Genital base-layer mapping

            var layerId = $"{markingPrototype.ID}-{rsi.RsiState}";
            var glowLayerId = $"{layerId}-glow";
            // FLOOF CHANGE END
            var targLayerAdj = targetLayer + layerDict[layerSlot.ToString()] + 1;

            // Keep genital markings beneath clothing when render-over-clothing is disabled.
            // Multi-state markings can otherwise increment past nearby clothing anchors.
            if (!renderOverClothing
                && (markingPrototype.MarkingCategory == MarkingCategories.Genital
                    || markingPrototype.BodyPart == HumanoidVisualLayers.Genital))
            {
                if (sprite.LayerMapTryGet("jumpsuit", out var jumpsuitLayer))
                    targLayerAdj = Math.Min(targLayerAdj, jumpsuitLayer - 1);

                if (sprite.LayerMapTryGet("outerClothing", out var outerClothingLayer))
                    targLayerAdj = Math.Min(targLayerAdj, outerClothingLayer - 1);

                if (targLayerAdj < 0)
                    targLayerAdj = 0;
            }


            if (!sprite.LayerMapTryGet(layerId, out _))
            {
                // for layers that are supposed to be behind everything,
                // adding 1 to the layer index makes it not be behind
                // everything. fun! FLOOF ADD =3
                // var targLayerAdj = targetLayer == 0 ? 0 + j : targetLayer + j + 1;
                var layer = sprite.AddLayer(markingSprite, targLayerAdj);
                sprite.LayerMapSet(layerId, layer);
                sprite.LayerSetSprite(layerId, rsi);
            }
            humanoid.ClientElderMarkings.Add(layerId);
            // impstation edit begin - check if there's a shader defined in the markingPrototype's shader datafield, and if there is...
            if (markingPrototype.Shader != null)
            {
                // use spriteComponent's layersetshader function to set the layer's shader to that which is specified.
                sprite.LayerSetShader(layerId, markingPrototype.Shader);
            }
            // impstation edit end
            sprite.LayerSetVisible(layerId, visible);
            // Coyote Start
            sprite.LayerSetScale(layerId, new Vector2(scale, scale));
            sprite.LayerSetOffset(layerId, markingOffset);
            // Coyote End

            if (!visible)
            {
                if (sprite.LayerMapTryGet(glowLayerId, out var hiddenGlowIndex))
                {
                    sprite.LayerMapRemove(glowLayerId);
                    sprite.RemoveLayer(hiddenGlowIndex);
                }
                continue;
            }

            // Okay so if the marking prototype is modified but we load old marking data this may no longer be valid
            // and we need to check the index is correct.
            // So if that happens just default to white?
            // FLOOF ADD =3
            var color = colorDict.TryGetValue(rsi.RsiState, out var targetColor) ? targetColor : Color.White;
            var glowFactor = glowDict.TryGetValue(rsi.RsiState, out var targetGlow) ? targetGlow : 0f;

            var clampedGlow = Math.Clamp(glowFactor, 0f, 1f);
            var baseAlpha = color.A;
            var glowAlpha = baseAlpha * clampedGlow;

            // Keep the final composed alpha stable while splitting between base + glow layers.
            // With standard alpha compositing: total = base + glow * (1 - base).
            // Solve for base so total remains equal to the original alpha.
            var denom = 1f - glowAlpha;
            var baseLayerAlpha = denom > 0f ? (baseAlpha - glowAlpha) / denom : 0f;
            baseLayerAlpha = Math.Clamp(baseLayerAlpha, 0f, 1f);

            sprite.LayerSetColor(layerId, color.WithAlpha(baseLayerAlpha));

            if (clampedGlow > 0f)
            {
                if (!sprite.LayerMapTryGet(glowLayerId, out _))
                {
                    var glowLayer = sprite.AddLayer(markingSprite, targLayerAdj + 1);
                    sprite.LayerMapSet(glowLayerId, glowLayer);
                    sprite.LayerSetSprite(glowLayerId, rsi);
                }

                sprite.LayerSetShader(glowLayerId, "unshaded");
                sprite.LayerSetVisible(glowLayerId, visible);
                sprite.LayerSetColor(glowLayerId, color.WithAlpha(glowAlpha));
                // Coyote Start
                sprite.LayerSetScale(glowLayerId, new Vector2(scale, scale));
                sprite.LayerSetOffset(glowLayerId, markingOffset);
                // Coyote End
                humanoid.ClientElderMarkings.Add(glowLayerId);
            }
            else if (sprite.LayerMapTryGet(glowLayerId, out var glowIndex))
            {
                sprite.LayerMapRemove(glowLayerId);
                sprite.RemoveLayer(glowIndex);
            }

            if (humanoid.MarkingsDisplacement.TryGetValue(markingPrototype.BodyPart, out DisplacementData? displacementData)
                && markingPrototype.CanBeDisplaced)
            {
                _displacement.TryAddDisplacement(
                    displacementData,
                    sprite,
                    targetLayer + j + 1,
                    layerId,
                    out _);
            }
        }

        if (MarkingCategoriesConversion.Category2Layer(
                markingPrototype.MarkingCategory,
                markingPrototype.BodyPart,
                out var whichCat))
        {
            // WEEOO WEEOO set the base layer to be hidden on the comp
            // but only if it is not already hidden
            if (!humanoid.HiddenBaseLayers.Contains(whichCat))
            {
                humanoid.HiddenBaseLayers.Add(whichCat);
            }
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

            var index = sprite.LayerMapReserveBlank(layer);
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
        if (!sprite.LayerMapTryGet(layer, out var index))
        {
            if (!visible)
                return;
            index = sprite.LayerMapReserveBlank(layer);
        }

        var spriteLayer = sprite[index];
        if (spriteLayer.Visible == visible)
            return;

        spriteLayer.Visible = visible;

        // I fucking hate this. I'll get around to refactoring sprite layers eventually I swear
        // Just a week away...

        foreach (var markingList in ent.Comp.MarkingSet.Markings.Values)
        {
            // Coyote Start
            var facingDirection = sprite.EnableDirectionOverride
                ? sprite.DirectionOverride
                : Transform(ent).LocalRotation.GetCardinalDir();
            // Coyote End
            foreach (var marking in markingList)
            {
                if (_markingManager.TryGetMarking(marking, out var markingPrototype) && markingPrototype.BodyPart == layer)
                {
                    // Coyote Start
                    ApplyMarking(markingPrototype, marking.MarkingColors, marking.MarkingGlow, marking.Visible, marking.RenderOverClothing, ent, sprite, marking.MarkingScale, marking.GetOffset(facingDirection));
                    // Coyote End
                }
            }
        }
    }

    public override void HideUndies(EntityUid ent, HumanoidAppearanceComponent? humanoid = null)
    {
        if (!Resolve(ent, ref humanoid))
            return;
        humanoid.PermanentlyHidden.Add(HumanoidVisualLayers.UndergarmentBottom);
        humanoid.PermanentlyHidden.Add(HumanoidVisualLayers.UndergarmentTop);
        base.HideUndies(ent, humanoid);
        UpdateSprite(ent, humanoid, Comp<SpriteComponent>(ent));
    }

    public override void HideGenitals(EntityUid ent, HumanoidAppearanceComponent? humanoid = null)
    {
        if (!Resolve(ent, ref humanoid))
            return;
        humanoid.PermanentlyHidden.Add(HumanoidVisualLayers.Genital);
        base.HideGenitals(ent, humanoid);
        UpdateSprite(ent, humanoid, Comp<SpriteComponent>(ent));
    }

    // displacementData = _humanoidSystem.GetDisplacementForLegStyle(
    //     slot,
    //     humanoidAppearance.LegStyle,
    //     inventory,
    //     displacementData);

    public void GetDisplacementForLegStyle(
        EntityUid uid,
        string slot,
        HumanoidAppearanceComponent? humanoidAppearance,
        DisplacementData? baseDisplacementDataIn,
        DisplacementData? maleDisplacementDataIn,
        DisplacementData? femaleDisplacementDataIn,
        out DisplacementData? baseDisplacementData,
        out DisplacementData? maleDisplacementData,
        out DisplacementData? femaleDisplacementData
        )
    {
        baseDisplacementData = baseDisplacementDataIn;
        maleDisplacementData = maleDisplacementDataIn;
        femaleDisplacementData = femaleDisplacementDataIn;
        if (!Resolve(uid, ref humanoidAppearance))
            return;
        if (!_prototypeManager.TryIndex(humanoidAppearance.Species, out SpeciesPrototype? sp)
            || !sp.AllowDigilegDisplacement)
        {
            return;
        }

        if (!humanoidAppearance.LegDisplacements.TryGetValue(
            humanoidAppearance.LegStyle,
            out ProtoId<LegDisplacementPrototype> displacement))
        {
            return;
        }

        if (!_prototypeManager.TryIndex(displacement, out LegDisplacementPrototype? ldp))
        {
            return;
        }

        baseDisplacementData = ldp.Displacements!.GetValueOrDefault(slot, baseDisplacementDataIn);
        maleDisplacementData = ldp.MaleDisplacements!.GetValueOrDefault(slot, maleDisplacementDataIn);
        femaleDisplacementData = ldp.FemaleDisplacements!.GetValueOrDefault(slot, femaleDisplacementDataIn);
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
