using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

[CVarDefs]
public sealed partial class LoneStarCCVars
{
    /// <summary>
    ///     A Multiplier applied to the money rewarded to the guild bankaccount when an expedition is completed.
    /// </summary>
    public static readonly CVarDef<float> GuildExpedRewardMultiplier =
        CVarDef.Create("lonestar.guild_exped_reward_multiplier", 1f, CVar.SERVER | CVar.REPLICATED);
}
