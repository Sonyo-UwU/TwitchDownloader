using System.Diagnostics;

namespace TwitchDownloaderCore.Tools
{
    public static class FfprobeRunner
    {
        public static async Task Run(string FfprobePath, string inputFile, string entries, DataReceivedEventHandler dataReceivedEventHandler, CancellationToken cancellationToken)
        {
            using var process = new Process
            {
                StartInfo =
                {
                    FileName = FfprobePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = false,
                    RedirectStandardOutput = true,
                }
            };

            var args = new List<string>
            {
                "-v", "error",
                "-show_entries", entries,
                "-of", "default=noprint_wrappers=1",
                "-sexagesimal",
                inputFile
            };

            foreach (var arg in args)
            {
                process.StartInfo.ArgumentList.Add(arg);
            }

            process.OutputDataReceived += dataReceivedEventHandler;

            cancellationToken.ThrowIfCancellationRequested();
            cancellationToken.Register(process.Kill);
            process.Start();
            process.BeginOutputReadLine();

            await process.WaitForExitAsync(cancellationToken);
        }
    }
}
