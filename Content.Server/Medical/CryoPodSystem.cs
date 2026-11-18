using Content.Server.Administration.Logs;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Atmos.Piping.Unary.EntitySystems;
using Content.Server.Body.Systems;
using Content.Server.Medical.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.NodeGroups;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Temperature.Components;
using Content.Shared.Atmos;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Climbing.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.DragDrop;
using Content.Shared.Emag.Systems;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Medical.Cryogenics;
using Content.Shared.MedicalScanner;
using Content.Shared.Power;
using Content.Shared.Tools;
using Content.Shared.Tools.Systems;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Medical;

public sealed partial class CryoPodSystem : SharedCryoPodSystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private readonly GasAnalyzerSystem _gasAnalyzerSystem = default!;
    [Dependency] private readonly GasCanisterSystem _gasCanisterSystem = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlotsSystem = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly SharedToolSystem _toolSystem = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;

    private static readonly ProtoId<ToolQualityPrototype> PryingQuality = "Prying";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CryoPodComponent, AtmosDeviceUpdateEvent>(OnCryoPodUpdateAtmosphere);
        SubscribeLocalEvent<CryoPodComponent, GasAnalyzerScanEvent>(OnGasAnalyzed);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ActiveCryoPodComponent, CryoPodComponent>();

        while (query.MoveNext(out var uid, out _, out var cryoPod))
        {
            if (Timing.CurTime < cryoPod.NextUiUpdateTime)
                continue;

            cryoPod.NextUiUpdateTime += cryoPod.UiUpdateInterval;
            Dirty(uid, cryoPod);
            UpdateUi((uid, cryoPod));
        }
    }

    #region Interaction

    protected override void UpdateUi(Entity<CryoPodComponent> entity)
    {
        if (!_uiSystem.IsUiOpen(entity.Owner, CryoPodUiKey.Key)
            || !TryComp(entity, out CryoPodAirComponent? air))
            return;

        var patient = entity.Comp.BodyContainer.ContainedEntity;
        var gasMix = _gasAnalyzerSystem.GenerateGasMixEntry("Cryo pod", air.Air);
        var (beakerCapacity, beaker) = GetBeakerInfo(entity);
        var injecting = GetInjectingReagents(entity);

        TryComp<TemperatureComponent>(patient, out var temperature);
        TryComp<BloodstreamComponent>(patient, out var bloodstream);
        var bloodLevel = patient is { } patientUid
                         && bloodstream != null
                         && _solutionContainerSystem.ResolveSolution(patientUid,
                             bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out var bloodSolution)
            ? bloodSolution.FillFraction
            : 0;
        var health = new HealthAnalyzerScannedUserMessage(
            GetNetEntity(patient),
            temperature?.CurrentTemperature ?? 0,
            bloodLevel,
            true,
            null,
            null,
            null);

        _uiSystem.ServerSendUiMessage(
            entity.Owner,
            CryoPodUiKey.Key,
            new CryoPodUserMessage(gasMix, health, beakerCapacity, beaker, injecting));
    }

    private void OnInteractUsing(Entity<CryoPodComponent> entity, ref InteractUsingEvent args)
    {
        if (args.Handled || !entity.Comp.Locked || entity.Comp.BodyContainer.ContainedEntity == null)
            return;

        args.Handled = _toolSystem.UseTool(args.Used, args.User, entity.Owner, entity.Comp.PryDelay, PryingQuality, new CryoPodPryFinished());
    }

    private void OnExamined(Entity<CryoPodComponent> entity, ref ExaminedEvent args)
    {
        var container = _itemSlotsSystem.GetItemOrNull(entity.Owner, entity.Comp.SolutionContainerName);
        if (args.IsInDetailsRange && container != null && _solutionContainerSystem.TryGetFitsInDispenser(container.Value, out _, out var containerSolution))
        {
            using (args.PushGroup(nameof(CryoPodComponent)))
            {
                args.PushMarkup(Loc.GetString("cryo-pod-examine", ("beaker", Name(container.Value))));
                if (containerSolution.Volume == 0)
                {
                    args.PushMarkup(Loc.GetString("cryo-pod-empty-beaker"));
                }
            }
        }
    }

    private void OnPowerChanged(Entity<CryoPodComponent> entity, ref PowerChangedEvent args)
    {
        // Needed to avoid adding/removing components on a deleted entity
        if (Terminating(entity))
        {
            return;
        }

        if (args.Powered)
        {
            EnsureComp<ActiveCryoPodComponent>(entity);
        }
        else
        {
            RemComp<ActiveCryoPodComponent>(entity);
            _uiSystem.CloseUi(entity.Owner, HealthAnalyzerUiKey.Key);
        }
        UpdateAppearance(entity.Owner, entity.Comp);
    }

    #endregion

    #region Atmos handler

    private void OnCryoPodUpdateAtmosphere(Entity<CryoPodComponent> entity, ref AtmosDeviceUpdateEvent args)
    {
        if (!_nodeContainer.TryGetNode(entity.Owner, entity.Comp.PortName, out PortablePipeNode? portNode))
            return;

        if (!TryComp(entity, out CryoPodAirComponent? cryoPodAir))
            return;

        _atmosphereSystem.React(cryoPodAir.Air, portNode);

        if (portNode.NodeGroup is PipeNet { NodeCount: > 1 } net)
        {
            _gasCanisterSystem.MixContainerWithPipeNet(cryoPodAir.Air, net.Air);
        }
    }

    private void OnGasAnalyzed(Entity<CryoPodComponent> entity, ref GasAnalyzerScanEvent args)
    {
        if (!TryComp(entity, out CryoPodAirComponent? cryoPodAir))
            return;

        args.GasMixtures ??= new List<(string, GasMixture?)>();
        args.GasMixtures.Add((Name(entity.Owner), cryoPodAir.Air));
        // If it's connected to a port, include the port side
        // multiply by volume fraction to make sure to send only the gas inside the analyzed pipe element, not the whole pipe system
        if (_nodeContainer.TryGetNode(entity.Owner, entity.Comp.PortName, out PipeNode? port) && port.Air.Volume != 0f)
        {
            var portAirLocal = port.Air.Clone();
            portAirLocal.Multiply(port.Volume / port.Air.Volume);
            portAirLocal.Volume = port.Volume;
            args.GasMixtures.Add((entity.Comp.PortName, portAirLocal));
        }
    }

    #endregion
}
