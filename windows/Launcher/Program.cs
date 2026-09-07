using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

[assembly: AssemblyTitle("随译")]
[assembly: AssemblyProduct("Transit · 随译")]
[assembly: AssemblyDescription("英俄屏幕翻译 · Alpha 启动程序")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
[assembly: AssemblyInformationalVersion("0.1.0-alpha.1")]

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app");
        string executable = Path.Combine(directory, "Suiyi.exe");
        bool verify = args.Length == 1 && args[0] == "--verify";
        string[] required = { "Suiyi.exe", "Suiyi.dll", "Suiyi.runtimeconfig.json", "tessdata/eng.traineddata", "tessdata/rus.traineddata" };
        foreach (string file in required)
        {
            if (File.Exists(Path.Combine(directory, file))) continue;
            if (!verify) MessageBox.Show("随译程序文件不完整，请将 app 文件夹与随译.exe 放在同一目录，再重新启动。", "随译", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 2;
        }
        if (verify) return 0;
        try
        {
            var arguments = new StringBuilder();
            foreach (string argument in args)
            {
                if (arguments.Length > 0) arguments.Append(' ');
                arguments.Append(Quote(argument));
            }
            using (var child = Process.Start(new ProcessStartInfo(executable, arguments.ToString())
            {
                WorkingDirectory = directory,
                UseShellExecute = false
            })) { }
            return 0;
        }
        catch
        {
            MessageBox.Show("暂时无法启动随译，请确认程序已完整解压，或直接打开 app 文件夹内的 Suiyi.exe。", "随译", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }
    }

    private static string Quote(string value)
    {
        var quoted = new StringBuilder("\"");
        int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { slashes++; continue; }
            quoted.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            quoted.Append(character);
            slashes = 0;
        }
        quoted.Append('\\', slashes * 2).Append('"');
        return quoted.ToString();
    }
}
