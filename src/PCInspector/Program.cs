namespace PCInspector;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        // The helper never creates MainForm or starts resource monitoring.
        if (args.Length > 0 && args[0] == "--task-manager")
            return Services.TaskManagerService.RunWorker(args);

        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
