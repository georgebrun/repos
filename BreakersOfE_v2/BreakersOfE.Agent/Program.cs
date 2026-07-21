namespace BreakersOfE.Agent
{
    /// <summary>
    /// BreakersOfE.Agent — Background system tray app.
    /// 
    /// This is a placeholder for Phase 7. When built out it will:
    ///   - Run as a system tray icon on Windows startup
    ///   - Schedule automatic price updates (daily)
    ///   - Schedule automatic backups (weekly)
    ///   - Pre-cache card images for your collection
    ///   - Rebuild keyword and synergy indexes after pool updates
    ///   - Notify you of significant price changes
    ///   - Coordinate with the main app via agent_status.json
    /// 
    /// For now it just starts and exits cleanly.
    /// </summary>
    internal class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Phase 7: This will be replaced with:
            //   - Microsoft.Extensions.Hosting setup
            //   - BackgroundService workers
            //   - Hardcodet.NotifyIcon tray icon
            //   - AgentConfig loading
            System.Console.WriteLine("BreakersOfE Agent — placeholder (Phase 7)");
        }
    }
}
