using System;

namespace DW2ModLauncher.XmlPatching
{
    /// <summary>
    /// What the in-game status widget shows for a patch item that could not be applied because its target is missing
    /// (the entity, list item or data file it addresses is not there). Every unapplied item is logged whatever the level.
    /// </summary>
    public enum MissingLevel
    {
        /// <summary>Nothing on the widget (the item is only logged).</summary>
        Ok = 0,
        /// <summary>A yellow warning.</summary>
        Warn = 1,
        /// <summary>A red error.</summary>
        Error = 2
    }

    /// <summary>
    /// The <c>missing="error|warn|ok"</c> setting and how its layers combine. Highest wins: the patch (the nearest element up to the
    /// file's root element that sets it), then the mod's dw2modlauncher.json "missing", then <see cref="Default"/>. The developer
    /// override of the launcher settings sits above all of them but can only escalate (see <see cref="Escalate"/>).
    /// </summary>
    public static class MissingPolicy
    {
        public const string AttributeName = "missing";

        /// <summary>The global default, used when neither the patch nor the mod says anything.</summary>
        public const MissingLevel Default = MissingLevel.Warn;

        public static readonly string[] Names = { "ok", "warn", "error" };

        /// <summary>Exactly ok, warn or error (any case, surrounding blanks ignored); false for anything else, including blank.</summary>
        public static bool TryParse(string text, out MissingLevel level)
        {
            switch (text?.Trim().ToLowerInvariant())
            {
                case "ok": level = MissingLevel.Ok; return true;
                case "warn": level = MissingLevel.Warn; return true;
                case "error": level = MissingLevel.Error; return true;
                default: level = MissingLevel.Ok; return false;
            }
        }

        /// <summary>The stricter of the declared level and the developer override; no override leaves the declared level.</summary>
        public static MissingLevel Escalate(MissingLevel declared, MissingLevel? floor)
        {
            return floor.HasValue && floor.Value > declared ? floor.Value : declared;
        }

        public static string Name(MissingLevel level)
        {
            return Names[(int)level];
        }
    }
}
