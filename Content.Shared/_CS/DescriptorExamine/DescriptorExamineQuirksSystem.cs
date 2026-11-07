using System.Linq;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._CS.DescriptorQuirks;

public sealed class DescriptorExamineQuirksSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IRobustRandom _rnd = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<DescriptorExamineQuirksComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, DescriptorExamineQuirksComponent component, ExaminedEvent args)
    {
        List<string> temperaments = new();
        var isSalf = args.Examiner == args.Examined;
        var targetIdent = ("target", Identity.Entity(args.Examined, EntityManager));
        var examinerIdent = ("examiner", Identity.Entity(args.Examiner, EntityManager));
        TryComp<DescriptorExamineQuirksComponent>(args.Examiner, out var examinerQuirks);
        using (args.PushGroup("ExtraExamineDetails"))
        {
            foreach (var showable in component.DescriptorShowables)
            {
                if (!_prototypeManager.TryIndex(showable, out var descriptorProto))
                    continue;

                if (isSalf)
                {
                    if (descriptorProto.SelfExamine)
                    {
                        goto ShowIt;
                    }

                    continue;
                }

                if (string.IsNullOrEmpty(descriptorProto.NeededTag))
                {
                    goto ShowIt;
                }

                // check if examiner has the needed tag
                if (examinerQuirks is not null
                    && !examinerQuirks.HasTagToShow(descriptorProto.NeededTag))
                {
                    continue;
                }

            ShowIt:
                temperaments.Add(descriptorProto.TextToShow);
            }

            if (temperaments.Count > 0)
            {
                foreach (var loc in temperaments)
                {
                    var translated = Loc.GetString(
                        loc,
                        targetIdent,
                        examinerIdent);
                    args.PushMarkup(translated, 0);
                }
            }

            string bodyWords = FormatulateBodyWords(uid, component);
            // stick body words at start
            if (!string.IsNullOrEmpty(bodyWords))
            {
                args.PushMarkup(bodyWords, 10);
            }
        }
    }

    /// <summary>
    /// Formatulate body words for descriptor quirks
    /// bodywords have two parts, descriptive and brief
    /// if its just one entry, just use the descriptive
    /// if its more than that, first one use descriptive, then rest use brief
    /// Supports up to infinity entries
    /// Example:
    /// Single entry: "She has bountiful flowing hair for days."
    /// two entries: "She has bountiful flowing hair for days, and broad shoulders."
    /// Three entries: "She has bountiful flowing hair for days, broad shoulders, and a cupcake."
    /// Four entries: "She has bountiful flowing hair for days, broad shoulders, a cupcake, and my dad."
    /// BUT WHICH ONE IS FIRST? its random, each time you examine!
    /// </summary>
    private string FormatulateBodyWords(EntityUid descriptor, DescriptorExamineQuirksComponent component)
    {
        if (component.DescriptorAppearances.Count == 0)
            return string.Empty;

        var appearances = component.DescriptorAppearances.ToList();
        _rnd.Shuffle(appearances);
        // localize all entries in the appearances list
        var quirk = Identity.Entity(descriptor, EntityManager);
        for (int i = 0; i < appearances.Count; i++)
        {
            var descriptiveLoc = Loc.GetString(
                appearances[i].Item1,
                ("them", quirk));
            var briefLoc = Loc.GetString(
                appearances[i].Item2,
                ("them", quirk));
            descriptiveLoc = $"[color=forestgreen]{descriptiveLoc}[/color]";
            briefLoc = $"[color=forestgreen]{briefLoc}[/color]";
            appearances[i] = (descriptiveLoc, briefLoc);
        }
        var descriptive = appearances[0].Item1;
        var briefList = appearances.Skip(1).Select(x => x.Item2).ToList();
        switch (briefList.Count)
        {
            case 0:
                return Loc.GetString(
                    "examine-quirk-bodyword-single",
                    ("them", quirk),
                    ("first", descriptive));
            // case: two
            case 1:
                return Loc.GetString(
                    "examine-quirk-bodyword-two",
                    ("them", quirk),
                    ("first", descriptive),
                    ("second", briefList[0]));
            default:
                {
                    var last = briefList.Last();
                    var middle = briefList.Take(briefList.Count - 1).ToList();
                    return Loc.GetString(
                        "examine-quirk-bodyword-multiple",
                        ("them", quirk),
                        ("first", descriptive),
                        ("second", string.Join(", ", middle)),
                        ("last", last));
                }
        }
    }
}
