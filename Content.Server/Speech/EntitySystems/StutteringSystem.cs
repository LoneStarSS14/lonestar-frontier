using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Content.Server.Speech.Components;
using Content.Shared.Speech.EntitySystems;
using Content.Shared.StatusEffect;
using Robust.Shared.Random;

namespace Content.Server.Speech.EntitySystems
{
    public sealed partial class StutteringSystem : SharedStutteringSystem
    {
        [Dependency] private readonly StatusEffectsSystem _statusEffectsSystem = default!;
        [Dependency] private readonly IRobustRandom _random = default!;

        // Regex of characters to stutter.
        private static readonly Regex Stutter = StutterRegex();

        public override void Initialize()
        {
            SubscribeLocalEvent<StutteringAccentComponent, AccentGetEvent>(OnAccent);
        }

        public override void DoStutter(EntityUid uid, TimeSpan time, bool refresh, StatusEffectsComponent? status = null)
        {
            if (!Resolve(uid, ref status, false))
                return;

            _statusEffectsSystem.TryAddStatusEffect<StutteringAccentComponent>(uid, StutterKey, time, refresh, status);
        }

        private void OnAccent(EntityUid uid, StutteringAccentComponent component, AccentGetEvent args)
        {
            args.Message = Accentuate(args.Message, component);
        }

        public string Accentuate(string message, StutteringAccentComponent component)
        {
            var length = message.Length;

            var finalMessage = new StringBuilder();

            for (var i = 0; i < length; i++)
            {
                var newLetter = message[i].ToString();
                if (Stutter.IsMatch(newLetter))
                {
                    foreach (var prob in component.Probabilities.Where(prob => _random.Prob(prob.Value)))
                    {
                        newLetter = string.Concat(Enumerable.Repeat($"{newLetter}-", prob.Key + 1));
                        break;
                    }
                }

                finalMessage.Append(newLetter.TrimEnd('-'));
            }

            return finalMessage.ToString();
        }

        [GeneratedRegex("[b-df-hj-np-tv-wxyz]", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
        private static partial Regex StutterRegex();
    }
}
