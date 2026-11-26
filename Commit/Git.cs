using System.ComponentModel;
using System.Diagnostics;

namespace Commit;

internal static class Git
{
    internal static async Task<GitResult> RunAsync(string path, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["LC_ALL"] = "C";
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Win32Exception error)
        {
            throw new InvalidOperationException($"Não foi possível iniciar o Git. Verifique se ele está instalado e disponível no PATH.\n\n{error.Message}", error);
        }

        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string operation = arguments[0] == "config" ? "config " + arguments[1] : arguments[0];
        return new GitResult(operation, process.ExitCode, await output, await errors);
    }

    internal sealed record GitResult(string Operation, int ExitCode, string Output, string Errors)
    {
        public string Details => (Output + "\n" + Errors).Trim();

        public string RequireSuccess()
        {
            if (ExitCode != 0)
                throw new GitFailure(Operation, ExitCode, Details);
            return Output;
        }
    }
}
