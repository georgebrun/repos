using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BreakersOfE.Services
{
    /// <summary>One Help topic (a .md file in Resources\Help), or a group heading with topics under it.</summary>
    public sealed class HelpTopic
    {
        /// <summary>The file name without ".md" ("add-cards"); links and F1 use it. Empty for a group heading.</summary>
        public string Id { get; init; } = "";
        public string Title { get; init; } = "";
        public string Subtitle { get; init; } = "";
        /// <summary>The topic's text, without its title and subtitle lines.</summary>
        public string Body { get; init; } = "";
        public List<HelpTopic> Children { get; } = new();
        public bool IsGroup => Id.Length == 0;
    }

    /// <summary>
    /// The Help topics, read from the files built into the app
    /// (Resources\Help: index.txt gives the order and groups, one .md file
    /// per topic — plain text anyone can edit, see index.txt for the rules).
    /// </summary>
    public static class HelpTopics
    {
        private static List<HelpTopic>? _tree;
        private static Dictionary<string, HelpTopic>? _byId;

        /// <summary>The topic list as shown on the left of the Help window.</summary>
        public static IReadOnlyList<HelpTopic> Tree
        {
            get { Load(); return _tree!; }
        }

        /// <summary>A topic by its file name ("add-cards"), or null.</summary>
        public static HelpTopic? Find(string? id)
        {
            Load();
            return id != null && _byId!.TryGetValue(id, out var t) ? t : null;
        }

        /// <summary>Every topic (not group headings), in list order.</summary>
        public static IEnumerable<HelpTopic> All() =>
            Tree.SelectMany(t => t.IsGroup ? t.Children : new List<HelpTopic> { t });

        private static void Load()
        {
            if (_tree != null) return;
            _tree = new();
            _byId = new(StringComparer.OrdinalIgnoreCase);

            HelpTopic? group = null;
            foreach (string raw in (Read("index.txt") ?? "").Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                string text = line.Trim();
                if (text.Length == 0 || text.StartsWith('#')) continue;

                bool indented = line.StartsWith(' ') || line.StartsWith('\t');
                if (!text.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    group = new HelpTopic { Title = text };   // a group heading
                    _tree.Add(group);
                    continue;
                }

                var topic = LoadTopic(text);
                if (topic == null) continue;
                _byId[topic.Id] = topic;
                if (indented && group != null) group.Children.Add(topic);
                else { _tree.Add(topic); group = null; }
            }
        }

        /// <summary>A topic file: "# Title", an optional "> subtitle" line, then the text.</summary>
        private static HelpTopic? LoadTopic(string file)
        {
            string? content = Read(file);
            if (content == null) return null;
            var lines = content.Replace("\r\n", "\n").Split('\n').ToList();

            string title = Path.GetFileNameWithoutExtension(file);
            string subtitle = "";
            int start = 0;
            while (start < lines.Count && lines[start].Trim().Length == 0) start++;
            if (start < lines.Count && lines[start].StartsWith("# "))
            {
                title = lines[start][2..].Trim();
                start++;
                if (start < lines.Count && lines[start].StartsWith(">"))
                {
                    subtitle = lines[start][1..].Trim();
                    start++;
                }
            }
            return new HelpTopic
            {
                Id = Path.GetFileNameWithoutExtension(file),
                Title = title,
                Subtitle = subtitle,
                Body = string.Join("\n", lines.Skip(start)).Trim('\n'),
            };
        }

        /// <summary>A file built into the app from Resources\Help (null when it isn't there).</summary>
        private static string? Read(string file)
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("Help." + file);
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
