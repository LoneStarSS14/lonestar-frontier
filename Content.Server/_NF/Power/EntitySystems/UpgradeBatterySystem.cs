using Content.Shared.Construction.Components;
using Content.Server.Power.Components;
using JetBrains.Annotations;
using Content.Server._NF.Power.Components;
using Content.Server.Power.EntitySystems;

namespace Content.Server._NF.Power.EntitySystems;

[UsedImplicitly]
public sealed class UpgradeBatterySystem : EntitySystem
{
    [Dependency] private readonly BatterySystem _batterySystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<UpgradeBatteryComponent, RefreshPartsEvent>(OnRefreshParts);
        SubscribeLocalEvent<UpgradeBatteryComponent, UpgradeExamineEvent>(OnUpgradeExamine);
    }

    public void OnRefreshParts(EntityUid uid, UpgradeBatteryComponent component, RefreshPartsEvent args)
    {
        var powerCellRating = GetNormalizedRating(args, component.MachinePartPowerCapacity, component.ExpectedPartCount);

        if (TryComp<BatteryComponent>(uid, out var batteryComp))
        {
            _batterySystem.SetMaxCharge(uid, MathF.Pow(component.MaxChargeMultiplier, powerCellRating - 1) * component.BaseMaxCharge, batteryComp);
        }
    }

    private static float GetNormalizedRating(RefreshPartsEvent args, string partType, int expectedPartCount)
    {
        var averageRating = args.PartRatings[partType];
        if (expectedPartCount <= 1)
            return averageRating;

        var quantity = 0;
        foreach (var state in args.Parts)
        {
            if (state.Part.PartType != partType)
                continue;

            quantity += state.Quantity();
        }

        var fillRatio = MathF.Min(1f, quantity / (float) expectedPartCount);
        return 1f + (averageRating - 1f) * fillRatio;
    }

    private void OnUpgradeExamine(EntityUid uid, UpgradeBatteryComponent component, UpgradeExamineEvent args)
    {
        // UpgradeBatteryComponent.MaxChargeMultiplier is not the actual multiplier, so we have to do this.
        if (TryComp<BatteryComponent>(uid, out var batteryComp))
        {
            args.AddPercentageUpgrade("upgrade-max-charge", batteryComp.MaxCharge / component.BaseMaxCharge);
        }
    }
}
