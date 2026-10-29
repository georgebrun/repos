using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Phone lists by Dropbox (Settings → Phone lists → Connect Dropbox…):
    /// BoE sends "BoE Lists.json" straight to the user's own Dropbox, in the
    /// app's own folder (Apps › Breakers of E Lists), and the BoE Lists page on
    /// the phone fetches it by itself. No Dropbox desktop app is needed.
    ///
    /// Signing in: Dropbox's page opens in the browser, the user allows it, and
    /// Dropbox hands the answer back to BoE on this PC (http://localhost:53682).
    /// PKCE, so there's no secret in BoE. The lasting sign-in (refresh token)
    /// is kept in settings, encrypted for this Windows user.
    /// </summary>
    public static class DropboxService
    {
        /// <summary>BoE's registration with Dropbox (public: it identifies the app, not a user).</summary>
        public const string AppKey = "o285jtez789m1yd";
        public const string AppName = "Breakers of E Lists";
        public const int Port = 53682;
        public static readonly string RedirectUri = $"http://localhost:{Port}/boe";

        /// <summary>The file, in the app's own folder of the user's Dropbox.</summary>
        public const string DropboxPath = "/BoE Lists.json";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
        private static string? _access;
        private static DateTime _accessUntil;

        public static bool Connected => AppSettingsService.Current.DropboxToken.Length > 0;
        public static string AccountName => AppSettingsService.Current.DropboxAccount;

        // ── Connect ──────────────────────────────────────────────────────
        /// <summary>
        /// Sign in: opens Dropbox's page (through <paramref name="openBrowser"/>)
        /// and waits for the answer. Returns whose Dropbox it is. Throws with a
        /// message to show when it doesn't work out.
        /// </summary>
        public static async Task<string> ConnectAsync(Action<string> openBrowser, CancellationToken ct)
        {
            string verifier = B64Url(RandomNumberGenerator.GetBytes(48));
            string challenge = B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            string state = B64Url(RandomNumberGenerator.GetBytes(16));

            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{Port}/");
            try
            {
                listener.Start();
            }
            catch (HttpListenerException ex)
            {
                throw new InvalidOperationException(
                    $"BoE couldn't wait for Dropbox's answer (port {Port} is busy: {ex.Message}). Close other programs and try again.");
            }

            openBrowser("https://www.dropbox.com/oauth2/authorize" +
                        $"?client_id={AppKey}&response_type=code&token_access_type=offline" +
                        $"&code_challenge={challenge}&code_challenge_method=S256" +
                        $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}&state={state}");

            string code;
            using (ct.Register(() => { try { listener.Stop(); } catch { } }))
            {
                while (true)
                {
                    HttpListenerContext ctx;
                    try
                    {
                        ctx = await listener.GetContextAsync();
                    }
                    catch when (ct.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(ct);
                    }
                    var url = ctx.Request.Url;
                    if (url == null || !url.AbsolutePath.StartsWith("/boe", StringComparison.OrdinalIgnoreCase))
                    {
                        ctx.Response.StatusCode = 404;             // the browser asking for its icon
                        ctx.Response.Close();
                        continue;
                    }
                    var q = System.Web.HttpUtility.ParseQueryString(url.Query);
                    string? got = q["code"];
                    string? error = q["error"];
                    bool ok = !string.IsNullOrEmpty(got) && q["state"] == state;
                    Answer(ctx, ok
                        ? "Breakers of E is connected to your Dropbox. You can close this tab and go back to BoE."
                        : "Dropbox didn't connect. You can close this tab and try Connect Dropbox… in BoE again.");
                    if (!ok)
                        throw new InvalidOperationException(error == "access_denied"
                            ? "Dropbox wasn't connected (Cancel was chosen on Dropbox's page)."
                            : $"Dropbox didn't connect ({q["error_description"] ?? error ?? "no answer"}).");
                    code = got!;
                    break;
                }
            }

            using var doc = await TokenAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["code_verifier"] = verifier,
                ["client_id"] = AppKey,
                ["redirect_uri"] = RedirectUri,
            }, ct);
            string refresh = doc.RootElement.GetProperty("refresh_token").GetString() ?? "";
            if (refresh.Length == 0) throw new InvalidOperationException("Dropbox didn't give BoE a lasting sign-in.");
            KeepAccess(doc.RootElement);

            string name = "";
            try { name = await AccountAsync(ct); } catch { /* only for show */ }

            var s = AppSettingsService.Current;
            s.DropboxToken = Protect(refresh);
            s.DropboxAccount = name;
            AppSettingsService.Save(s);
            return name;
        }

        /// <summary>Disconnect: Dropbox forgets BoE's sign-in (when it can be reached), and so does BoE.</summary>
        public static async Task DisconnectAsync()
        {
            try
            {
                string token = await AccessTokenAsync(CancellationToken.None);
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/auth/token/revoke");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var _ = await Http.SendAsync(req);
            }
            catch
            {
                // Offline or already gone: forgetting it here is what matters.
            }
            Forget();
        }

        // ── Send the lists ───────────────────────────────────────────────
        /// <summary>Put the lists file in the user's Dropbox (replacing the last one).</summary>
        public static async Task UploadAsync(byte[] json, CancellationToken ct = default)
        {
            for (int attempt = 0; ; attempt++)
            {
                string token = await AccessTokenAsync(ct);
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://content.dropboxapi.com/2/files/upload");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Headers.TryAddWithoutValidation("Dropbox-API-Arg",
                    "{\"path\":\"" + DropboxPath + "\",\"mode\":\"overwrite\",\"mute\":true}");
                req.Content = new ByteArrayContent(json);
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using var r = await Http.SendAsync(req, ct);
                if (r.IsSuccessStatusCode) return;
                if (r.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    _access = null;                                  // ran out early: a fresh one, once
                    continue;
                }
                string body = await r.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"Dropbox didn't take the lists ({(int)r.StatusCode}: {Short(body)}).");
            }
        }

        // ── Sign-in plumbing ─────────────────────────────────────────────
        /// <summary>A short-lived access token, renewed from the lasting sign-in when needed.</summary>
        private static async Task<string> AccessTokenAsync(CancellationToken ct)
        {
            if (_access != null && DateTime.UtcNow < _accessUntil) return _access;
            string refresh = Unprotect(AppSettingsService.Current.DropboxToken);
            if (refresh.Length == 0)
                throw new InvalidOperationException("Not connected to Dropbox.");
            JsonDocument doc;
            try
            {
                doc = await TokenAsync(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refresh,
                    ["client_id"] = AppKey,
                }, ct);
            }
            catch (DropboxRefusedException)
            {
                // Removed on Dropbox's side (Settings → Connected apps) or expired.
                Forget();
                throw new InvalidOperationException("Dropbox no longer lets BoE in. Connect Dropbox… again in Settings → Phone lists.");
            }
            using (doc)
            {
                KeepAccess(doc.RootElement);
            }
            return _access!;
        }

        private sealed class DropboxRefusedException : Exception
        {
            public DropboxRefusedException(string m) : base(m) { }
        }

        private static async Task<JsonDocument> TokenAsync(Dictionary<string, string> form, CancellationToken ct)
        {
            using var r = await Http.PostAsync("https://api.dropboxapi.com/oauth2/token", new FormUrlEncodedContent(form), ct);
            string body = await r.Content.ReadAsStringAsync(ct);
            if (r.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                throw new DropboxRefusedException($"Dropbox refused the sign-in ({Short(body)}).");
            if (!r.IsSuccessStatusCode)
                throw new InvalidOperationException($"Dropbox didn't answer properly ({(int)r.StatusCode}: {Short(body)}).");
            return JsonDocument.Parse(body);
        }

        private static void KeepAccess(JsonElement token)
        {
            _access = token.GetProperty("access_token").GetString();
            int secs = token.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out int n) ? n : 3600;
            _accessUntil = DateTime.UtcNow.AddSeconds(Math.Max(60, secs - 120));
        }

        /// <summary>"George Brun" (or the email when there's no name).</summary>
        private static async Task<string> AccountAsync(CancellationToken ct)
        {
            string token = await AccessTokenAsync(ct);
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/users/get_current_account");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var r = await Http.SendAsync(req, ct);
            if (!r.IsSuccessStatusCode) return "";
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            if (root.TryGetProperty("name", out var nm) && nm.TryGetProperty("display_name", out var dn) && dn.GetString() is { Length: > 0 } d)
                return d;
            return root.TryGetProperty("email", out var em) ? em.GetString() ?? "" : "";
        }

        private static void Forget()
        {
            _access = null;
            var s = AppSettingsService.Current;
            s.DropboxToken = "";
            s.DropboxAccount = "";
            AppSettingsService.Save(s);
        }

        /// <summary>The page the browser shows once Dropbox hands the answer back.</summary>
        private static void Answer(HttpListenerContext ctx, string text)
        {
            try
            {
                byte[] html = Encoding.UTF8.GetBytes(
                    "<!doctype html><meta charset=\"utf-8\"><title>Breakers of E</title>" +
                    "<body style=\"font:16px Segoe UI,sans-serif;margin:60px auto;max-width:520px;text-align:center\">" +
                    "<h2>Breakers of E</h2><p>" + WebUtility.HtmlEncode(text) + "</p></body>");
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = html.Length;
                ctx.Response.OutputStream.Write(html, 0, html.Length);
                ctx.Response.Close();
            }
            catch
            {
                // The browser went away: the sign-in still carries on.
            }
        }

        private static string Protect(string s) =>
            Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser));

        private static string Unprotect(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s), null, DataProtectionScope.CurrentUser));
            }
            catch
            {
                return "";                         // another Windows user's, or damaged: connect again
            }
        }

        private static string B64Url(byte[] b) =>
            Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static string Short(string s)
        {
            s = (s ?? "").Replace('\n', ' ').Trim();
            return s.Length > 160 ? s[..160] + "…" : s;
        }
    }
}
