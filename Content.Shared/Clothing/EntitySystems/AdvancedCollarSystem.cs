using System.Linq;
using Content.Shared.Clothing.Components;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools;
using Content.Shared.Tools.Systems;
using Content.Shared.Tools.Components;
using Content.Shared.DoAfter;
using Content.Shared.Verbs;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Serialization.Manager;

namespace Content.Shared.Clothing.EntitySystems;

/// <summary>
/// System for handling advanced collar module installation and removal.
/// </summary>
public sealed class AdvancedCollarSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedToolSystem _tool = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly ISerializationManager _serializationManager = default!;

    public const string ModuleContainerName = "collar_module_container";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AdvancedCollarComponent, ComponentInit>(OnCollarInit);
        SubscribeLocalEvent<AdvancedCollarComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<AdvancedCollarComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<AdvancedCollarComponent, GetVerbsEvent<InteractionVerb>>(OnGetVerbs);
        SubscribeLocalEvent<AdvancedCollarComponent, AdvancedCollarRemoveModulesDoAfterEvent>(OnRemoveModulesComplete);
        SubscribeLocalEvent<AdvancedCollarComponent, EntInsertedIntoContainerMessage>(OnModuleInserted);
        SubscribeLocalEvent<AdvancedCollarComponent, EntRemovedFromContainerMessage>(OnModuleRemoved);

        SubscribeLocalEvent<AdvancedCollarModuleComponent, ExaminedEvent>(OnModuleExamined);
    }

    private void OnCollarInit(Entity<AdvancedCollarComponent> collar, ref ComponentInit args)
    {
        collar.Comp.ModuleContainer = _container.EnsureContainer<Container>(collar, ModuleContainerName);
    }

    private void OnModuleInserted(Entity<AdvancedCollarComponent> collar, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != ModuleContainerName)
            return;

        // Apply the module's effect
        if (TryComp<AdvancedCollarModuleComponent>(args.Entity, out var module))
        {
            ApplyModuleEffect(collar, (args.Entity, module));
        }
    }

    private void OnModuleRemoved(Entity<AdvancedCollarComponent> collar, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != ModuleContainerName)
            return;

        // Remove the module's effect
        if (TryComp<AdvancedCollarModuleComponent>(args.Entity, out var module))
        {
            RemoveModuleEffect(collar, (args.Entity, module));
        }
    }

    private void OnExamined(Entity<AdvancedCollarComponent> collar, ref ExaminedEvent args)
    {
        var moduleCount = collar.Comp.ModuleContainer.ContainedEntities.Count;
        
        if (moduleCount == 0)
        {
            args.PushMarkup(Loc.GetString("advanced-collar-examine-no-modules"));
        }
        else
        {
            args.PushMarkup(Loc.GetString("advanced-collar-examine-modules", 
                ("count", moduleCount),
                ("max", collar.Comp.MaxModules)));

            foreach (var moduleUid in collar.Comp.ModuleContainer.ContainedEntities)
            {
                if (TryComp<AdvancedCollarModuleComponent>(moduleUid, out var module) && 
                    !string.IsNullOrEmpty(module.ModuleDescription))
                {
                    args.PushMarkup(Loc.GetString("advanced-collar-examine-module-entry",
                        ("name", Name(moduleUid)),
                        ("description", module.ModuleDescription)));
                }
            }
        }
    }

    private void OnModuleExamined(Entity<AdvancedCollarModuleComponent> module, ref ExaminedEvent args)
    {
        if (!string.IsNullOrEmpty(module.Comp.ModuleDescription))
        {
            args.PushMarkup(Loc.GetString("advanced-collar-module-examine",
                ("description", module.Comp.ModuleDescription)));
        }

        if (module.Comp.InstalledIn != null)
        {
            args.PushMarkup(Loc.GetString("advanced-collar-module-already-in-collar"));
        }
    }

    private void OnInteractUsing(Entity<AdvancedCollarComponent> collar, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        // Try to install a module
        if (TryComp<AdvancedCollarModuleComponent>(args.Used, out var module))
        {
            args.Handled = true;
            TryInstallModule(collar, (args.Used, module), args.User);
            return;
        }

        // Try to remove modules with screwdriver
        if (_tool.HasQuality(args.Used, "Screwing"))
        {
            if (collar.Comp.ModuleContainer.ContainedEntities.Count == 0)
                return;

            args.Handled = true;

            // Start do-after for removing modules
            var doAfterArgs = new DoAfterArgs(EntityManager, args.User, 3f, new AdvancedCollarRemoveModulesDoAfterEvent(), collar.Owner, target: collar.Owner, used: args.Used)
            {
                BreakOnMove = true,
                NeedHand = true
            };

            _doAfter.TryStartDoAfter(doAfterArgs);
        }
    }

    private void OnGetVerbs(Entity<AdvancedCollarComponent> collar, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (collar.Comp.ModuleContainer.ContainedEntities.Count == 0)
            return;

        // Check if user has a screwdriver
        if (args.Using == null || !_tool.HasQuality(args.Using.Value, "Screwing"))
            return;

        // Capture values for lambda
        var user = args.User;
        var used = args.Using.Value;
        var collarUid = collar.Owner;

        InteractionVerb verb = new()
        {
            Act = () =>
            {
                // Start do-after for removing modules
                var doAfterArgs = new DoAfterArgs(EntityManager, user, 3f, new AdvancedCollarRemoveModulesDoAfterEvent(), collarUid, target: collarUid, used: used)
                {
                    BreakOnMove = true,
                    NeedHand = true
                };

                _doAfter.TryStartDoAfter(doAfterArgs);
            },
            Text = Loc.GetString("advanced-collar-remove-modules-verb"),
            Icon = new SpriteSpecifier.Texture(new ("/Textures/Interface/VerbIcons/eject.svg.192dpi.png")),
            Priority = 1
        };

        args.Verbs.Add(verb);
    }

    private void OnRemoveModulesComplete(Entity<AdvancedCollarComponent> collar, ref AdvancedCollarRemoveModulesDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        // Check if collar is being worn - can't remove modules while equipped
        if (TryComp<ClothingComponent>(collar, out var clothing) && clothing.InSlot != null)
        {
            _popup.PopupClient(Loc.GetString("advanced-collar-worn"), collar, args.User);
            return;
        }

        RemoveAllModules(collar, args.User);
    }

    public void TryInstallModule(Entity<AdvancedCollarComponent> collar, Entity<AdvancedCollarModuleComponent> module, EntityUid user)
    {
        // Check if module is already installed somewhere
        if (module.Comp.InstalledIn != null)
        {
            _popup.PopupClient(Loc.GetString("advanced-collar-module-already-installed"), collar, user);
            return;
        }

        // Check if collar is full
        if (collar.Comp.ModuleContainer.ContainedEntities.Count >= collar.Comp.MaxModules)
        {
            _popup.PopupClient(Loc.GetString("advanced-collar-full"), collar, user);
            return;
        }

        // Install the module
        if (_container.Insert(module.Owner, collar.Comp.ModuleContainer))
        {
            module.Comp.InstalledIn = collar;
            Dirty(module);
            
            _popup.PopupClient(Loc.GetString("advanced-collar-module-installed",
                ("module", Name(module))), collar, user);
            _audio.PlayPredicted(collar.Comp.ModuleInsertionSound, collar, user);
        }
    }

    private void ApplyModuleEffect(EntityUid collar, Entity<AdvancedCollarModuleComponent> module)
    {
        // Handle single component (legacy)
        if (!string.IsNullOrEmpty(module.Comp.ComponentToAdd))
        {
            ApplySingleComponent(collar, module.Owner, module.Comp.ComponentToAdd);
        }

        // Handle multiple components
        foreach (var componentName in module.Comp.ComponentsToAdd)
        {
            if (!string.IsNullOrEmpty(componentName))
            {
                ApplySingleComponent(collar, module.Owner, componentName);
            }
        }
    }

    private void ApplySingleComponent(EntityUid collar, EntityUid moduleEntity, string componentName)
    {
        // Try to get the component registration - it may not exist on the client for server-only components
        if (!_componentFactory.TryGetRegistration(componentName, out var registration))
        {
            // Component doesn't exist on this side (likely server-only component on client)
            // This is fine - server will add it when it processes the module
            return;
        }

        var componentType = registration.Type;

        // Check if the collar already has this component
        if (HasComp(collar, componentType))
            return;

        // Check if the module entity has this component with configuration
        if (EntityManager.TryGetComponent(moduleEntity, componentType, out var moduleComponent))
        {
            // Clone the component from the module to preserve configuration
            var component = (Component)_componentFactory.GetComponent(componentType);
            var temp = (object)component;
            _serializationManager.CopyTo(moduleComponent, ref temp);
            AddComp(collar, (Component)temp!);
        }
        else
        {
            // Add the component with default values
            var component = (Component)_componentFactory.GetComponent(componentType);
            AddComp(collar, component);
        }
    }

    private void RemoveModuleEffect(EntityUid collar, Entity<AdvancedCollarModuleComponent> module)
    {
        // Handle single component (legacy)
        if (!string.IsNullOrEmpty(module.Comp.ComponentToAdd))
        {
            RemoveSingleComponent(collar, module.Comp.ComponentToAdd);
        }

        // Handle multiple components
        foreach (var componentName in module.Comp.ComponentsToAdd)
        {
            if (!string.IsNullOrEmpty(componentName))
            {
                RemoveSingleComponent(collar, componentName);
            }
        }
    }

    private void RemoveSingleComponent(EntityUid collar, string componentName)
    {
        // Try to get the component registration - it may not exist on the client for server-only components
        if (!_componentFactory.TryGetRegistration(componentName, out var registration))
        {
            // Component doesn't exist on this side (likely server-only component on client)
            // This is fine - server will remove it when it processes the module removal
            return;
        }

        var componentType = registration.Type;

        // Remove the component from the collar
        RemComp(collar, componentType);
    }

    public void RemoveAllModules(Entity<AdvancedCollarComponent> collar, EntityUid user)
    {
        var moduleCount = collar.Comp.ModuleContainer.ContainedEntities.Count;
        
        if (moduleCount == 0)
        {
            _popup.PopupClient(Loc.GetString("advanced-collar-no-modules"), collar, user);
            return;
        }

        // Store modules before emptying container
        var modulesToClear = collar.Comp.ModuleContainer.ContainedEntities.ToList();
        
        // Remove all modules from the container
        _container.EmptyContainer(collar.Comp.ModuleContainer);
        
        // Eject each module to user's hands or drop nearby
        foreach (var moduleUid in modulesToClear)
        {
            if (TryComp<AdvancedCollarModuleComponent>(moduleUid, out var module))
            {
                module.InstalledIn = null;
            }
            
            _hands.PickupOrDrop(user, moduleUid, dropNear: true);
        }

        _popup.PopupClient(Loc.GetString("advanced-collar-modules-removed",
            ("count", moduleCount)), collar, user);
        _audio.PlayPredicted(collar.Comp.ModuleExtractionSound, collar, user);
    }
}
