using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Is there a newer BoE? Asks GitHub for the latest release (the same place
    /// v1 checked) and compares its tag ("v2.0.1") with this program's version.
    /// Pre-releases and drafts never count: GitHub's "latest" skips them.
    /// </summary>
    public static class ReleaseCheck
    {
        private const string LatestUrl = "https://api.github.com/repos/georgebrun/repos/releases/latest";

        /// <summary>The latest release: its version, its tag as written, and its page.</summary>
        public sealed record Release(Version Version, string Tag, string PageUrl);

        /// <summary>This program's version (major.minor.build, from the project's Version).</summary>
        public static Version Current
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
            }
        }

        public static string CurrentText => Text(Current);

        public static string Text(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";

        /// <summary>
        /// The latest release on GitHub. Throws when it can't be reached or read
        /// (the startup check ignores that; Check for Updates says why).
        /// </summary>
        public static async Task<Release> GetLatestAsync()
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.Add("User-Agent", "BreakersOfE");
            http.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            string json = await http.GetStringAsync(LatestUrl);

            using var doc = JsonDocument.Parse(json);
            string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            string page = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
            string number = tag.Trim().TrimStart('v', 'V');
            int dash = number.IndexOfAny(new[] { '-', '+', ' ' });
            if (dash > 0) number = number[..dash];
            if (!Version.TryParse(number, out var v))
                throw new FormatException($"the latest release's tag \"{tag}\" isn't a version number");
            return new Release(new Version(v.Major, v.Minor, Math.Max(v.Build, 0)), tag, page);
        }

        /// <summary>True when this release is newer than the one running.</summary>
        public static bool IsNewer(Release r) => r.Version > Current;

        /// <summary>Open a release's page in the browser.</summary>
        public static void OpenPage(Release r)
        {
            if (string.IsNullOrWhiteSpace(r.PageUrl)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(r.PageUrl) { UseShellExecute = true });
            }
            catch
            {
                /* no browser: nothing more to do */
            }
        }
    }
}
