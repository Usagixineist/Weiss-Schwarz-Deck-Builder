using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.Data
{
    public sealed class CommonEffectDefinition
    {
        public string Name { get; set; }

        /// <summary>
        /// Name without the parameter list, so "Revenge(POWER)" is "Revenge".
        /// Plain definitions simply repeat their name.
        /// </summary>
        public string BaseName { get; set; }

        /// <summary>
        /// Parameter names of a Define line, empty for a plain definition.
        /// </summary>
        public List<string> Parameters { get; private set; }

        public CardColor? Color { get; set; }
        public TriggerIcon? Trigger { get; set; }
        public List<string> TextLines { get; private set; }

        public CommonEffectDefinition()
        {
            Name = string.Empty;
            BaseName = string.Empty;
            Parameters = new List<string>();
            TextLines = new List<string>();
        }
    }

    /// <summary>
    /// Reads top-level Color/Trigger/Text commands from CommonEffects.txt.
    /// The effect tree itself is not needed for search, but these commands
    /// affect the card fields used by text search and the trait sort.
    /// </summary>
    public sealed class CommonEffectLibrary
    {
        private readonly Dictionary<string, CommonEffectDefinition> _definitions;
        private readonly Dictionary<string, List<CommonEffectDefinition>> _byBase;
        private readonly Dictionary<string, CommonEffectDefinition> _resolved;

        private CommonEffectLibrary()
        {
            _definitions = new Dictionary<string, CommonEffectDefinition>(
                StringComparer.OrdinalIgnoreCase);
            _byBase = new Dictionary<string, List<CommonEffectDefinition>>(
                StringComparer.OrdinalIgnoreCase);
            _resolved = new Dictionary<string, CommonEffectDefinition>(
                StringComparer.OrdinalIgnoreCase);
        }

        public static CommonEffectLibrary Empty
        {
            get { return new CommonEffectLibrary(); }
        }

        public static CommonEffectLibrary Load(string path)
        {
            var library = new CommonEffectLibrary();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return library;

            CommonEffectDefinition current = null;
            int depth = 0;

            using (var reader = new StreamReader(path, Encoding.UTF8, true))
            {
                string raw;
                while ((raw = reader.ReadLine()) != null)
                {
                    string line = raw.Trim().TrimStart('\uFEFF');
                    if (line.Length == 0)
                        continue;

                    if (line.StartsWith("//", StringComparison.Ordinal) ||
                        line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (line.StartsWith("Define:", StringComparison.OrdinalIgnoreCase))
                    {
                        string name = line.Substring("Define:".Length).Trim();
                        current = new CommonEffectDefinition();
                        current.Name = name;
                        current.BaseName = name;
                        ParseSignature(name, current);
                        library._definitions[name] = current;

                        List<CommonEffectDefinition> variants;
                        if (!library._byBase.TryGetValue(current.BaseName, out variants))
                        {
                            variants = new List<CommonEffectDefinition>();
                            library._byBase[current.BaseName] = variants;
                        }
                        variants.Add(current);

                        depth = 0;
                        continue;
                    }

                    if (current == null)
                        continue;

                    if (line.StartsWith("{", StringComparison.Ordinal))
                    {
                        depth++;
                        continue;
                    }

                    if (line.StartsWith("}", StringComparison.Ordinal))
                    {
                        if (depth > 0)
                            depth--;
                        continue;
                    }

                    if (depth != 0)
                        continue;

                    int space = line.IndexOf(' ');
                    string key = space < 0 ? line : line.Substring(0, space);
                    string value = space < 0 ? string.Empty : line.Substring(space + 1).Trim();

                    if (string.Equals(key, "Color", StringComparison.OrdinalIgnoreCase))
                    {
                        CardColor color;
                        if (TryParseColor(value, out color))
                            current.Color = color;
                    }
                    else if (string.Equals(key, "Trigger", StringComparison.OrdinalIgnoreCase))
                    {
                        TriggerIcon trigger;
                        if (TryParseTrigger(value, out trigger))
                            current.Trigger = trigger;
                    }
                    else if (string.Equals(key, "Text", StringComparison.OrdinalIgnoreCase))
                    {
                        current.TextLines.Add(DecodeText(value));
                    }
                }
            }

            return library;
        }

        /// <summary>
        /// Looks a shorthand up. Exact names win, then a parameterised call such
        /// as "OnSentDiscard1Check4(Magic)" is matched against the Define that
        /// declares the same number of parameters and its text is filled in.
        /// </summary>
        public CommonEffectDefinition Find(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            string key = name.Trim();
            int comment = key.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0)
                key = key.Substring(0, comment).Trim();
            if (key.Length == 0)
                return null;

            CommonEffectDefinition definition;
            if (_definitions.TryGetValue(key, out definition))
                return definition;

            lock (_resolved)
            {
                if (_resolved.TryGetValue(key, out definition))
                    return definition;
            }

            string baseName;
            List<string> arguments;
            if (!TryParseCall(key, out baseName, out arguments))
                return null;

            List<CommonEffectDefinition> variants;
            if (!_byBase.TryGetValue(baseName, out variants))
                return null;

            for (int i = 0; i < variants.Count; i++)
            {
                if (variants[i].Parameters.Count != arguments.Count)
                    continue;

                definition = Resolve(variants[i], arguments);
                lock (_resolved)
                {
                    _resolved[key] = definition;
                }
                return definition;
            }

            return null;
        }

        /// <summary>
        /// "Revenge(POWER)" becomes BaseName "Revenge" with the single parameter
        /// "POWER"; a name without parentheses stays untouched.
        /// </summary>
        private static void ParseSignature(
            string name,
            CommonEffectDefinition definition)
        {
            string baseName;
            List<string> parameters;
            if (!TryParseCall(name, out baseName, out parameters))
                return;

            definition.BaseName = baseName;
            definition.Parameters.Clear();
            definition.Parameters.AddRange(parameters);
        }

        /// <summary>
        /// Splits "Name(A, B)" into the name and its raw arguments. Text without
        /// a parenthesised list returns false.
        /// </summary>
        private static bool TryParseCall(
            string value,
            out string baseName,
            out List<string> arguments)
        {
            baseName = string.Empty;
            arguments = new List<string>();

            string text = value.Trim();
            if (text.Length == 0 || text[text.Length - 1] != ')')
                return false;

            int open = text.IndexOf('(');
            if (open <= 0)
                return false;

            baseName = text.Substring(0, open).Trim();
            if (baseName.Length == 0)
                return false;

            string inner = text.Substring(open + 1,
                text.Length - open - 2).Trim();
            if (inner.Length == 0)
                return true;

            SplitArguments(inner, arguments);
            return true;
        }

        /// <summary>
        /// Splits "A, B" on the commas that separate arguments. Card names may
        /// contain a comma written as "\," so escaped commas stay inside the
        /// argument and lose the backslash.
        /// </summary>
        private static void SplitArguments(string value, List<string> arguments)
        {
            var current = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\' && i + 1 < value.Length &&
                    (value[i + 1] == ',' || value[i + 1] == '|' ||
                     value[i + 1] == '\\'))
                {
                    current.Append(value[i + 1]);
                    i++;
                    continue;
                }

                if (c == ',')
                {
                    arguments.Add(current.ToString().Trim());
                    current.Length = 0;
                    continue;
                }

                current.Append(c);
            }

            arguments.Add(current.ToString().Trim());
        }

        /// <summary>
        /// Copy of a Define with every $PARAMETER (and bare parameter, as used
        /// by Revenge) replaced by the argument of the call.
        /// </summary>
        private static CommonEffectDefinition Resolve(
            CommonEffectDefinition source,
            List<string> arguments)
        {
            var result = new CommonEffectDefinition();
            result.Name = source.Name;
            result.BaseName = source.BaseName;
            result.Parameters.AddRange(source.Parameters);
            result.Color = source.Color;
            result.Trigger = source.Trigger;

            for (int i = 0; i < source.TextLines.Count; i++)
            {
                result.TextLines.Add(Substitute(
                    source.TextLines[i], source.Parameters, arguments));
            }

            return result;
        }

        private static string Substitute(
            string text,
            List<string> parameters,
            List<string> arguments)
        {
            if (string.IsNullOrEmpty(text) || parameters == null ||
                arguments == null)
            {
                return text;
            }

            string result = text;
            int count = Math.Min(parameters.Count, arguments.Count);
            for (int i = 0; i < count; i++)
            {
                string token = parameters[i];
                if (string.IsNullOrEmpty(token))
                    continue;

                string value = FormatArgument(arguments[i]);
                result = result.Replace("$" + token, value);
                result = ReplaceBareToken(result, token, value);
            }

            return result;
        }

        /// <summary>
        /// Trait lists are written with '|' in the CardData files, the card
        /// text shows them separated by '/'.
        /// </summary>
        private static string FormatArgument(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Trim().Replace("|", "/");
        }

        /// <summary>
        /// Replaces a parameter that is written without the '$' marker, for
        /// example "力量 +POWER" or "选择最多1个 TRAITLIST 角色".
        /// </summary>
        private static string ReplaceBareToken(
            string text,
            string token,
            string value)
        {
            int index = 0;
            while (index < text.Length)
            {
                int found = text.IndexOf(
                    token, index, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                    return text;

                int after = found + token.Length;
                bool leftOk = found == 0 || !IsWordChar(text[found - 1]);
                bool rightOk = after >= text.Length || !IsWordChar(text[after]);
                if (!leftOk || !rightOk)
                {
                    index = after;
                    continue;
                }

                text = text.Substring(0, found) + value + text.Substring(after);
                index = found + value.Length;
            }

            return text;
        }

        private static bool IsWordChar(char value)
        {
            return char.IsLetterOrDigit(value) || value == '_';
        }

        public static bool TryParseColor(string value, out CardColor color)
        {
            color = CardColor.Undefined;
            if (string.IsNullOrEmpty(value))
                return false;

            string text = value.Trim();
            switch (text.ToUpperInvariant())
            {
                case "Y":
                case "YELLOW":
                    color = CardColor.Yellow;
                    return true;
                case "G":
                case "GREEN":
                    color = CardColor.Green;
                    return true;
                case "R":
                case "RED":
                    color = CardColor.Red;
                    return true;
                case "B":
                case "BLUE":
                    color = CardColor.Blue;
                    return true;
                case "P":
                case "PURPLE":
                    color = CardColor.Purple;
                    return true;
                case "UNDEFINED":
                case "NONE":
                    color = CardColor.Undefined;
                    return true;
                default:
                    return Enum.TryParse(text, true, out color);
            }
        }

        public static bool TryParseTrigger(string value, out TriggerIcon trigger)
        {
            trigger = TriggerIcon.None;
            if (string.IsNullOrEmpty(value))
                return false;

            string text = value.Trim();
            int number;
            if (int.TryParse(text, out number) &&
                number >= (int)TriggerIcon.None &&
                number <= (int)TriggerIcon.CustomIcon6)
            {
                trigger = (TriggerIcon)number;
                return true;
            }

            return Enum.TryParse(text, true, out trigger);
        }

        public static string DecodeText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Replace("\\n", "\n");
        }
    }
}
