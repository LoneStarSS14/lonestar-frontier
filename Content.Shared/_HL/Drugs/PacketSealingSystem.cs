using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._HL.Drugs;

/// <summary>
/// System that handles packet sealing via right-click activation
/// </summary>
public sealed class PacketSealingSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PacketSealingComponent, ActivateInWorldEvent>(OnActivated);
        SubscribeLocalEvent<PacketSealingComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAltVerbs);
    }

    private void OnActivated(Entity<PacketSealingComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex || ent.Comp.State == PacketState.Sealed)
            return;

        if (CanSealPacket(ent))
        {
            TryStartSealDoAfter(ent, args.User);
            args.Handled = true;
        }
    }

    private void OnGetAltVerbs(Entity<PacketSealingComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        if (ent.Comp.State == PacketState.Sealed)
            return;

        var user = args.User;

        args.Verbs.Add(new AlternativeVerb()
        {
            Text = "Seal Packet",
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/lock.svg.192dpi.png")),
            Act = () => TryStartSealDoAfter(ent, user),
        });
    }

    private bool CanSealPacket(Entity<PacketSealingComponent> ent)
    {
        return CanSealPacketWithReason(ent).CanSeal;
    }

    private (bool CanSeal, string Reason) CanSealPacketWithReason(Entity<PacketSealingComponent> ent)
    {
        if (ent.Comp.State == PacketState.Sealed)
            return (false, "This packet is already sealed.");

        if (!_solution.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out _, out var solution))
            return (false, "This packet has no solution container.");

        // Check if solution is at full capacity
        if (solution.Volume < solution.MaxVolume)
        {
            var needed = solution.MaxVolume - solution.Volume;
            return (false, $"Packet needs {needed}u more reagent to be full ({solution.Volume}/{solution.MaxVolume}u).");
        }

        // Check if it's empty
        if (solution.Contents.Count == 0)
            return (false, "Packet is empty.");

        // Check if it's a single reagent
        if (solution.Contents.Count > 1)
            return (false, "Packet contains mixed reagents. Only pure drugs can be sealed.");

        var reagent = solution.Contents[0];
        if (GetWrappedPacketId(reagent.Reagent.Prototype) == null)
        {
            var reagentProto = _prototypeManager.Index<ReagentPrototype>(reagent.Reagent.Prototype);
            return (false, $"{reagentProto.LocalizedName} is not a valid drug for sealing.");
        }

        return (true, string.Empty);
    }

    private void TryStartSealDoAfter(Entity<PacketSealingComponent> ent, EntityUid user)
    {
        var (canSeal, reason) = CanSealPacketWithReason(ent);
        if (!canSeal)
        {
            _popup.PopupEntity(reason, ent.Owner, user);
            return;
        }

        var doAfterArgs = new DoAfterArgs(EntityManager, user, ent.Comp.SealDelay, new PacketSealDoAfterEvent(), ent.Owner)
        {
            BreakOnMove = true,
            NeedHand = true,
            BreakOnHandChange = true,
            MovementThreshold = 0.01f,
            DistanceThreshold = 1.0f,
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnSealDoAfter(Entity<PacketSealingComponent> ent, ref PacketSealDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !CanSealPacket(ent))
            return;

        // Mark as sealed to prevent further sealing attempts
        ent.Comp.State = PacketState.Sealed;
        Dirty(ent.Owner, ent.Comp);

        args.Handled = true;
    }


    private string? GetWrappedPacketId(string reagentId)
    {
        return reagentId switch
        {
            "Bake" => "WrappedBakePackage",
            "Rust" => "WrappedRustPackage",
            "Grit" => "WrappedGritPackage",
            "Breakout" => "WrappedBreakoutPackage",
            "Widow" => "WrappedWidowPackage",
            _ => null
        };
    }
}

[Serializable, NetSerializable]
public sealed partial class PacketSealDoAfterEvent : SimpleDoAfterEvent
{
}
