using Content.Server.Humanoid;
using Content.Shared._CS;
using Content.Shared._CS.Needs;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Verbs;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._CS.Needs;

/// <summary>
/// This handles...
/// </summary>
public sealed class NeedSystem : SharedNeedsSystem
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly HumanoidAppearanceSystem _humanoid = default!;

    /// <summary>
    /// Gets the examine text for all needs on an entity.
    /// </summary>
    protected override FormattedMessage GetNeedsExamineText(
        EntityUid examinee,
        NeedsComponent needy,
        GetVerbsEvent<ExamineVerb> args)
    {
        // user is the one doing the examining
        // target is the one being examined
        var info = string.Empty;

        if (!needy.Ready
            || needy.Needs.Count == 0)
            return new FormattedMessage();
        // setup some vars for who can see what
        var examinerIsSelf = args.User == args.Target;
        var isAdminGhost = HasComp<AdminGhostComponent>(args.User);
        var showExtendedInfo = isAdminGhost || examinerIsSelf;
        var showNumbers = isAdminGhost;
        // get the mob's species, if possible
        // (for the "X is starving to death" examine text)
        var species = "Critter"; // default fallback
        if (_entMan.TryGetComponent(args.Target, out HumanoidAppearanceComponent? humanoid))
        {
            species = _humanoid.GetSpeciesRepresentation(humanoid.Species, humanoid.CustomSpecieName);
        }
        foreach (var need in needy.Needs.Values)
        {
            if (!isAdminGhost)
            {
                if (!needy.VisibleNeeds.TryGetValue(need.NeedType, out var visibility))
                    continue;
                if (visibility == NeedExamineVisibility.None)
                    continue;
                if (visibility == NeedExamineVisibility.Owner
                    && !examinerIsSelf)
                    continue;
            }
            var line = GetExamineText(
                args.User,
                args.Target,
                need,
                species,
                showNumbers,
                showExtendedInfo);
            if (string.IsNullOrEmpty(line))
                continue;
            info += line + "\n";
        }
        var message = new FormattedMessage();
        message.AddMarkupPermissive(info);
        return message;
    }

    /// <summary>
    /// Gets the description for the current threshold, if any
    /// </summary>
    public string GetExamineText(
        EntityUid examiner,
        EntityUid examinee,
        NeedDatum need,
        string species,
        bool showNumbers,
        bool showExtendedInfo)
    {
        var stringOut = string.Empty;
        var isSelf = examiner == examinee;
        var header = Loc.GetString(
            "examinable-need-header",
            ("color", need.NeedColor.ToHex()),
            ("needname", need.NeedName));
        stringOut += header + "\n";
        var meme = need.GetCurrentThreshold() == NeedThreshold.Low
                   && IoCManager.Resolve<IRobustRandom>().Prob(0.05f);
        if (!showExtendedInfo)
        {
            var locStr = $"examinable-need-{need.NeedType.ToString().ToLower()}-{need.GetCurrentThreshold().ToString().ToLower()}";
            if (meme)
                locStr += "-meme";
            if (isSelf)
            {
                locStr += "-self";
            }
            var locThing = Loc.GetString(
                locStr,
                ("entity", Identity.Entity(examinee, IoCManager.Resolve<IEntityManager>())));
            stringOut += locThing + "\n";
            return stringOut; // suckit
        }

        // self examine, far more detailed!
        var locStrSelf = $"examinable-need-{need.NeedType.ToString().ToLower()}-{need.GetCurrentThreshold().ToString().ToLower()}-self";
        if (meme)
            locStrSelf += "-meme";
        var textOutSelf = Loc.GetString(locStrSelf);
        stringOut += textOutSelf + "\n";
        if (showNumbers)
        {
            string textOutNumbers;
            if (isSelf)
            {
                textOutNumbers = Loc.GetString(
                    "examinable-need-hunger-numberized-self",
                    ("current", (int) need.CurrentValue),
                    ("max", (int) need.MaxValue));
            }
            else
            {
                textOutNumbers = Loc.GetString(
                    "examinable-need-hunger-numberized",
                    ("entity", Identity.Entity(examinee, IoCManager.Resolve<IEntityManager>())),
                    ("current", (int) need.CurrentValue),
                    ("max", (int) need.MaxValue));
            }

            stringOut += textOutNumbers + "\n";
        }

        // Now, add in the time until next threshold change, if applicable
        string needChungus;
        if (need.GetCurrentThreshold() == NeedThreshold.Critical)
        {
            // we need the entity's species, if we can get it
            // for meme reasons (Wizard needs food badly)
            needChungus = Loc.GetString(
                $"examinable-need-{need.NeedType.ToString().ToLower()}-timeleft-critical",
                ("creature", species));
            stringOut += needChungus + "\n";
        }
        else
        {
            var timeTillnext = need.GetTimeFromNowToNextThreshold();
            var timeString = need.Time2String(timeTillnext);
            if (isSelf)
            {
                needChungus = Loc.GetString(
                    $"examinable-need-{need.NeedType.ToString().ToLower()}-timeleft-{need.GetCurrentThreshold().ToString().ToLower()}-self");
            }
            else
            {
                needChungus = Loc.GetString(
                    $"examinable-need-{need.NeedType.ToString().ToLower()}-timeleft-{need.GetCurrentThreshold().ToString().ToLower()}",
                    ("entity", Identity.Entity(examinee, IoCManager.Resolve<IEntityManager>())));
            }

            stringOut += needChungus + "\n";
            stringOut += timeString + "\n";

            // var timeTillStarve = need.GetTimeToMinValue();
            // var timeStringStarve = need.Time2String(timeTillStarve);
            // if (isSelf)
            // {
            //     needChungus = Loc.GetString(
            //         $"examinable-need-{need.NeedType.ToString().ToLower()}-timeleft-tillcritical-self");
            // }
            // else
            // {
            //     needChungus = Loc.GetString(
            //         $"examinable-need-{need.NeedType.ToString().ToLower()}-timeleft-tillcritical",
            //         ("entity", Identity.Entity(examinee, IoCManager.Resolve<IEntityManager>())));
            // }
            // stringOut += needChungus + "\n";
            // stringOut += timeStringStarve + "\n";

        }
        need.GetBuffDebuffList(ref stringOut);
        // ANYTHING ELSE YOU WANT TO ADD?
        var ev = new NeedExamineInfoEvent(
            need,
            examinee,
            isSelf);
        RaiseLocalEvent(examinee, ev);
        ev.AppendAdditionalInfoLines(ref stringOut);
        return stringOut;
    }

}
