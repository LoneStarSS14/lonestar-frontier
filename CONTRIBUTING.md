# Contributing to LoneStar Sector

If you're considering contributing to LoneStar Sector, [Wizard's Den's PR guidelines](https://docs.spacestation14.com/en/general-development/codebase-info/pull-request-guidelines.html) are a good starting point for code quality and version tracking etiquette. Note that we do not have the same master/stable branch distinction.

Importantly, do not make webedits. From the text above:
> Do not use GitHub's web editor to create PRs. PRs submitted through the web editor may be closed without review.

"Upstream" refers to the [new-frontiers-14/frontier-station-14](https://github.com/new-frontiers-14/frontier-station-14/) repository that this fork was created from.

# LoneStar-specific / 'Bespoke' content

In general, anything you create from scratch (vs. porting something that exists from upstream) should go in a LoneStar-specific subfolder, `_LoneStar`or `_LS` in some cases, such as the file path `Resources/Prototypes/_LoneStar/*`

# Changes to upstream files

If you make a change to an upstream C# or YAML file, **you must add comments on or around the changed lines**.
The comments should clarify what changed, to make conflict resolution simpler when a file is changed upstream.
If you make changes to values, leave a comment.

For YAML specifically

If you add a single component, add a comment to the end of the component's type line
```yml
  - type: Clothing # LoneStar
```
for a series of additional components, use a seperate line to denote all edits below the line are relevant to the prototype;
```yml
  # LoneStar below
  - Type: Clothing
  - Type: Item
```
if you are making limited edits to a component's datafields, ideally comment each line edited individually;
```yml
  - Type: Item
    size: Tiny # LoneStar
    shape: 0,0,1,1 # LoneStar
```
if you are making heavy edits to the entire file an alteernative approach is to denote at the top of the file that a large amount of edits have been made and a brief sentence on how-so.
```yml
## LoneStar: Edits all name fields to be consistent, removes X component's datafields
```

Try to refrain from adding comments explaining the descision for a change, as this is what gitblame and PRs are for, future contributors can see the reasoning for changes via the PRs about section, jokes or references are acceptable but use discretion where able, not every edit needs a line expressing the intent.

For C# files, if you are adding a lot of code, consider using a partial class when it makes sense.

If cherry-picking upstream features, it is best to comment with the PR number that was cherry-picked.

As an aside, fluent (.ftl) files **do not support comments on the same line** as a locale value - leave a comment on the line above if modifying values, and use block comments `# LoneStar start` | `# LoneStar end`.

## Examples of comments in upstream or ported files

A comment on a new imported namespace:
```cs
using Content.Client.Emp.Overlays; // LoneStar
```

A pair of comments enclosing a block of added code:
```cs
component.Capacity = state.Capacity;

component.UIUpdateNeeded = true;

// LoneStar start, added
if (TryComp<StampComponent>(uid, out var stamp))
{
    stamp.StampedColor = state.Color;
}
// LoneStar end
```

# Mapping

For ship submissons we do not have our own submission guidelines, please refer to the [Ship Submission Guidelines](https://frontierstation.wiki.gg/wiki/Ship_Submission_Guidelines) on the Frontier wiki for best practices and share in discord for advice.

In general:

Frontier uses specific prototypes for points of interest and ship maps (e.g. to store spawn information, station spawn data, or ship price and categories).  For ships, these are stored in the VesselPrototype (Resources/Prototypes/_NF/Shipyard) or PointOfInterestPrototype (Resources/Prototypes/_NF/PointsOfInterest).  If creating a new ship or POI, refer to existing prototypes.

If you create a new ship or points of interest, please put it into the `Resources/Maps/_LoneStar/` filepath.

If you are making changes to a shuttle or POI, be sure to check the open PRs for any other edits being proposed to it, as map files are typically not able to handle merge conflicts properly.

# Before you submit

Double-check your diff's on GitHub before submitting: look for unintended commits or changes, ideally avoid automatic on-save formatted whitespace changes as these add to the earlier mentioned issues of merge conflicts.

Additionally, for PRs that've been open for a long time, if you see `RobustToolbox` in the changed files, you have to revert it. Use `git checkout upstream/master RobustToolbox` (replacing `upstream` with the name of your remote repository)

# Changelogs

Currently, all changelogs go to the LoneStar changelog. The ADMIN: prefix does not currently function.

# Additional resources

If you are new to contributing to SS14 in general, have a look at the [SS14 docs](https://docs.spacestation14.io/) or ask for help in `#dev-general` on our [Discord](https://discord.gg/5FVsBUW8zw)!

## AI-Generated Content
You may use AI tools to assist with code, but we ask any AI-generated code must be thoroughly tested and audited before submission. Additionally when using AI for larger features, especially to C#, ensure you are disclosing as such in your PR.

AI-generated sprites and art are not allowed to be submitted outside of use for debug assets or administrative only content.

Failure to adhere to the previous conditions on AI-generated code or assets may result in being banned from contributing to our repository.
