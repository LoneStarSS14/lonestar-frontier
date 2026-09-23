using Content.Client.Gameplay;
using Content.Client.Lobby;
using Content.Client.UserInterface.Controls;
using Content.Shared.Chat;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Input;
using Content.Shared.Preferences;
using Content.Shared.Speech;
using Content.Shared.Speech.Components;
using Content.Shared.Whitelist;
using JetBrains.Annotations;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input.Binding;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Emotes;

[UsedImplicitly]
public sealed class EmotesUIController : UIController, IOnStateChanged<GameplayState>
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IClientPreferencesManager _preferencesManager = default!;

    private MenuButton? EmotesButton => UIManager.GetActiveUIWidgetOrNull<MenuBar.Widgets.GameTopMenuBar>()?.EmotesButton;
    private SimpleRadialMenu? _menu;

    private static readonly Dictionary<EmoteCategory, (string Tooltip, SpriteSpecifier Sprite)> EmoteGroupingInfo
        = new Dictionary<EmoteCategory, (string Tooltip, SpriteSpecifier Sprite)>
        {
            [EmoteCategory.Sex] = ("emote-menu-category-sex",
                new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/Emotes/lewdemotes.png"))),
            [EmoteCategory.General] = ("emote-menu-category-general",
                new SpriteSpecifier.Rsi(new ResPath("/Textures/Clothing/Head/Soft/mimesoft.rsi"), "icon")),
            [EmoteCategory.Hands] = ("emote-menu-category-hands",
                new SpriteSpecifier.Rsi(new ResPath("/Textures/Clothing/Hands/Gloves/latex.rsi"), "icon")),
            [EmoteCategory.Vocal] = ("emote-menu-category-vocal",
                new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/Emotes/vocal.png"))),
            [EmoteCategory.Harpy] = ("emote-menu-category-harpy",
                new SpriteSpecifier.Texture(new ResPath("/Textures/_CS/Emojis/cock.png"))),
            [EmoteCategory.Goblin] = ("emote-menu-category-goblin",
                new SpriteSpecifier.Texture(new ResPath("/Textures/_CS/Emojis/goblin.png"))),
            [EmoteCategory.Vulp] = ("emote-menu-category-vulp",
                new SpriteSpecifier.Texture(new ResPath("/Textures/_CS/Emojis/fox.png"))),
            [EmoteCategory.Rodentia] = ("emote-menu-category-rodentia",
                new SpriteSpecifier.Texture(new ResPath("/Textures/_CS/Emojis/rat.png"))),
            [EmoteCategory.Diona] = ("emote-menu-category-diona",
                new SpriteSpecifier.Texture(new ResPath("/Textures/_CS/Emojis/plant.png"))),
            [EmoteCategory.Sheleg] = ("emote-menu-category-sheleg",
                new SpriteSpecifier.Texture(new ResPath("/Textures/_CS/Emojis/icecream.png"))),
            [EmoteCategory.Male] = ("emote-menu-category-male",
                new SpriteSpecifier.Texture(new ResPath("/Textures/_CS/Emojis/male.png"))),
            [EmoteCategory.Female] = ("emote-menu-category-female",
                new SpriteSpecifier.Texture(new ResPath("/Textures/_CS/Emojis/female.png"))),
            [EmoteCategory.Avali] = ("emote-menu-category-avali",
                new SpriteSpecifier.Texture(new ResPath("/Textures/_CS/Emojis/dodo.png"))),
            [EmoteCategory.Lizard] = ("emote-menu-category-lizard",
                new SpriteSpecifier.Texture(new ResPath("/Textures/_CS/Emojis/trex.png"))),
            [EmoteCategory.Vox] = ("emote-menu-category-vox",
                new SpriteSpecifier.Texture(new ResPath("/Textures/_CS/Emojis/goose.png"))),
            [EmoteCategory.Moth] = ("emote-menu-category-moth",
                new SpriteSpecifier.Texture(new ResPath("/Textures/_CS/Emojis/butterfly.png"))),
            [EmoteCategory.Borg] = ("emote-menu-category-borg",
                new SpriteSpecifier.Texture(new ResPath("/Textures/_CS/Emojis/plug.png"))),
            [EmoteCategory.Felinid] = ("emote-menu-category-felinid",
                new SpriteSpecifier.Texture(new ResPath("/Textures/_CS/Emojis/cat.png"))),
            [EmoteCategory.Shadekin] = ("emote-menu-category-shadekin",
                new SpriteSpecifier.Texture(new ResPath("/Textures/_Starlight/Objects/Fun/Plushies/shadekin_plushie.rsi/icon.png"))),
        };

    private static readonly HashSet<EmoteCategory> AlwaysEnabledCategories = new()
    {
        EmoteCategory.Sex,
        EmoteCategory.Vocal,
    };

    public void OnStateEntered(GameplayState state)
    {
        CommandBinds.Builder
            .Bind(ContentKeyFunctions.OpenEmotesMenu,
                InputCmdHandler.FromDelegate(_ => ToggleEmotesMenu(false)))
            .Register<EmotesUIController>();
    }

    public void OnStateExited(GameplayState state)
    {
        CommandBinds.Unregister<EmotesUIController>();
    }

    private void ToggleEmotesMenu(bool centered)
    {
        if (_menu == null)
        {
            // setup window
            var prototypes = _prototypeManager.EnumeratePrototypes<EmotePrototype>();
            var models = ConvertToButtons(prototypes);

            _menu = new SimpleRadialMenu();
            _menu.SetButtons(models);

            _menu.Open();

            _menu.OnClose += OnWindowClosed;
            _menu.OnOpen += OnWindowOpen;

            if (EmotesButton != null)
                EmotesButton.SetClickPressed(true);

            if (centered)
            {
                _menu.OpenCentered();
            }
            else
            {
                _menu.OpenOverMouseScreenPosition();
            }
        }
        else
        {
            _menu.OnClose -= OnWindowClosed;
            _menu.OnOpen -= OnWindowOpen;

            if (EmotesButton != null)
                EmotesButton.SetClickPressed(false);

            CloseMenu();
        }
    }

    public void UnloadButton()
    {
        if (EmotesButton == null)
            return;

        EmotesButton.OnPressed -= ActionButtonPressed;
    }

    public void LoadButton()
    {
        if (EmotesButton == null)
            return;

        EmotesButton.OnPressed += ActionButtonPressed;
    }

    private void ActionButtonPressed(BaseButton.ButtonEventArgs args)
    {
        ToggleEmotesMenu(true);
    }

    private void OnWindowClosed()
    {
        if (EmotesButton != null)
            EmotesButton.Pressed = false;

        CloseMenu();
    }

    private void OnWindowOpen()
    {
        if (EmotesButton != null)
            EmotesButton.Pressed = true;
    }

    private void CloseMenu()
    {
        if (_menu == null)
            return;

        _menu.Dispose();
        _menu = null;
    }

    private IEnumerable<RadialMenuOption> ConvertToButtons(IEnumerable<EmotePrototype> emotePrototypes)
    {
        var whitelistSystem = EntitySystemManager.GetEntitySystem<EntityWhitelistSystem>();
        var player = _playerManager.LocalSession?.AttachedEntity;

        Dictionary<EmoteCategory, List<RadialMenuOption>> emotesByCategory = new();
        foreach (var emote in emotePrototypes)
        {
            if (emote.Category == EmoteCategory.Invalid)
                continue;

            if (IsHiddenCategory(emote.Category))
                continue;

            if (!emote.ShowInWheel)
                continue;

            // only valid emotes that have ways to be triggered by chat and player have access / no restriction on
            if (emote.Category == EmoteCategory.Invalid
                || emote.ChatTriggers.Count == 0
                || !(player.HasValue && whitelistSystem.IsWhitelistPassOrNull(emote.Whitelist, player.Value))
                || whitelistSystem.IsBlacklistPass(emote.Blacklist, player.Value))
                continue;

            if (!CanHasUseEmote(emote, player.Value))
                continue;

            if (!emotesByCategory.TryGetValue(emote.Category, out var list))
            {
                list = new List<RadialMenuOption>();
                emotesByCategory.Add(emote.Category, list);
            }

            var actionOption = new RadialMenuActionOption<EmotePrototype>(HandleRadialButtonClick, emote)
            {
                Sprite = emote.Icon,
                ToolTip = Loc.GetString(emote.Name)
            };
            list.Add(actionOption);
        }

        var sorted = new List<KeyValuePair<EmoteCategory, List<RadialMenuOption>>>(emotesByCategory);
        sorted.Sort((a, b) =>
            string.Compare(
                Loc.GetString(EmoteGroupingInfo[a.Key].Tooltip),
                Loc.GetString(EmoteGroupingInfo[b.Key].Tooltip),
                StringComparison.Ordinal));

        var models = new RadialMenuOption[sorted.Count];
        var i = 0;
        foreach (var pair in sorted)
        {
            var key = pair.Key;
            var list = pair.Value;
            var tuple = EmoteGroupingInfo[key];

            models[i] = new RadialMenuNestedLayerOption(list)
            {
                Sprite = tuple.Sprite,
                ToolTip = Loc.GetString(tuple.Tooltip)
            };
            i++;
        }

        return models;
    }

    private bool IsHiddenCategory(EmoteCategory category)
    {
        if (AlwaysEnabledCategories.Contains(category))
            return false;

        // Coyote Start
        // Read from selected profile in the common UI path so category visibility applies across all map/fork content.
        // Coyote End
        if (_preferencesManager.Preferences?.SelectedCharacter is not HumanoidCharacterProfile profile)
            return false;

        return profile.HiddenEmoteCategories.Contains(category);
    }

    private bool CanHasUseEmote(EmotePrototype emote, EntityUid player)
    {
        if (emote.Available)
            return true; // available emotes are always allowed
        if (!EntityManager.TryGetComponent<SpeechComponent>(player, out var speech))
            return false; // non-available emotes require speech component
        if (speech.AllowedEmotes.Contains(emote.ID))
            return true; // explicitly allowed emotes are allowed
        // check the supplemental sounds for vocal emotes
        if (!EntityManager.TryGetComponent<VocalComponent>(player, out var vocal))
            return false; // no vocal component, no vocal emotes
        if (!_prototypeManager.TryIndex<EmoteSoundsPrototype>(vocal.SupplementalSounds, out var esp))
            return false; // no supplemental sounds, no vocal emotes
        return esp.Sounds.ContainsKey(emote.ID); // only allow if we have sounds for them
    }

    private void HandleRadialButtonClick(EmotePrototype prototype)
    {
        _entityManager.RaisePredictiveEvent(new PlayEmoteMessage(prototype.ID));
    }
}
