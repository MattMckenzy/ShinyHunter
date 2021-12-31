using Microsoft.Extensions.Configuration;
using Renci.SshNet;
using Renci.SshNet.Common;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Xml.Serialization;
using static ShinyHunter.ShinyHunterModel;

namespace ShinyHunter
{
    public partial class ShinyHunter
    {
        private static readonly CancellationTokenSource GlobalCancellationTokenSource = new();

        private static readonly Queue<string> LastLines = new(100);
        private static string WaitingFor = string.Empty;
        private static CancellationTokenSource? WaitingForCancellationTokenSource = null;
        private static string[]? ErrorLines = null;
        private static CancellationTokenSource? ErrorCancellationTokenSource = null;

        private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ShinyHunter");
        private static readonly string LiveImagesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "live");

        private static readonly IConfiguration Configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddUserSecrets<ShinyHunter>()
            .Build();

        private static readonly string[] JoyControlErrors = new string[]
        {
            $"root@{Configuration["ServerHostName"]}:{Configuration["ServerScriptPath"]}#",
            "OSError:[Errno 107] Transport endpoint is not connected",
            "WARNING:joycontrol.protocol:Writer exited...",
            "Could not connect to client."
        };

        public static void Main()
        {
            using SftpClient sftpClient = new(Configuration["ServerAddress"], Configuration["ServerUsername"], Configuration["ServerPassword"]);
            sftpClient.Connect();
            sftpClient.UploadFile(File.OpenRead(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "autoshiny.py")), $"{Configuration["ServerScriptPath"]}/autoshiny.py", true);
            sftpClient.Dispose();

            Task.Run(async () =>
            {
                while (true)
                {
                    ShellStream? shellStream = null;

                    try
                    {
                        using SshClient sshClient = new(Configuration["ServerAddress"], Configuration["ServerUsername"], Configuration["ServerPassword"]);
                        sshClient.Connect();
                        sshClient.KeepAliveInterval = TimeSpan.FromSeconds(10);

                        Console.WriteLine();
                        Console.WriteLine("Starting ShinyHunter!");
                        shellStream = sshClient.CreateShellStream("ShinyHunter Terminal", 0, 0, 0, 0, 1024);
                        shellStream.DataReceived += ShellStream_DataReceived;
                        shellStream.ErrorOccurred += ShellStream_ErrorOccurred;

                        RunCommand(shellStream, "sudo -i", $"[sudo] password for {Configuration["ServerUsername"]}:", TimeSpan.FromSeconds(10));
                        RunCommand(shellStream, Configuration["ServerPassword"], $"root@{Configuration["ServerHostName"]}:~#", TimeSpan.FromSeconds(10));
                        RunCommand(shellStream, "ps -ef | grep autoshiny.py | grep -v grep | awk '{print $2}' | xargs -r sudo kill", $"root@{Configuration["ServerHostName"]}:~#", TimeSpan.FromSeconds(10));
                        RunCommand(shellStream, $"cd {Configuration["ServerScriptPath"]}", $"root@{Configuration["ServerHostName"]}:{Configuration["ServerScriptPath"]}#", TimeSpan.FromSeconds(10), new string[] { $"root@{Configuration["ServerHostName"]}:~#" });

                        _ = Task.Run(() => CreateAndFillPool(1));

                        int attemptNumber = GetAttemptNumber();

                        Console.WriteLine();
                        Console.WriteLine();
                        Console.WriteLine($"Starting on attempt #{attemptNumber}!");
                        RunCommand(shellStream, $"python3 autoshiny.py {Configuration["SwitchAddress"]}", "Waiting for signal.", TimeSpan.FromMinutes(3), JoyControlErrors);

                        while (!GlobalCancellationTokenSource.IsCancellationRequested)
                        {
                            await Task.Delay(14500);

                            Console.WriteLine();
                            Console.WriteLine("Capturing live footage.");
                            Directory.CreateDirectory(LiveImagesPath);
                            RunWindowsCommand($"{AppDomain.CurrentDomain.BaseDirectory}ffmpeg.exe", $"-hide_banner -loglevel error -stats -f dshow -rtbufsize 1024M -t 1.75s -i video=\"{Configuration["FFMPEGVideoCaptureDevice"]}\" -r 30/1 %04d.jpg", LiveImagesPath);

                            Console.WriteLine("Preparing prediction inputs.");
                            ConcurrentStack<ModelInput> inputs = new();
                            foreach (FileInfo liveImage in new DirectoryInfo(LiveImagesPath).GetFiles("*.jpg"))
                            {
                                if (liveImage.LastWriteTime > DateTime.Now - TimeSpan.FromMinutes(5))
                                {
                                    inputs.Push(new()
                                    {
                                        ImageSource = liveImage.FullName
                                    });
                                }
                            }

                            if (!inputs.Any())
                            {
                                GlobalCancellationTokenSource.Cancel();
                                throw new TaskCanceledException("FFMPEG has not been producing updated live images. ShinyHunter will exit.");
                            }

                            Console.Write($"\rPredicting images...");
                            DateTime dateTime = DateTime.Now;

                            ConcurrentBag<string> results = new();
                            List<Task> predictionTasks = new();
                            while (inputs.TryPop(out ModelInput? modelInput))
                            {
                                predictionTasks.Add(Task.Run(() => results.Add(Predict(modelInput).Prediction)));
                            }

                            Task.WaitAll(predictionTasks.ToArray());

                            Console.WriteLine();
                            Console.WriteLine($"Got {results.Count(result => result.Equals("normal"))} normal frames and {results.Count(result => result.Equals("shiny"))} shiny frames in {DateTime.Now - dateTime}.");
                            Console.WriteLine();

                            if (results.Count(result => result.Equals("shiny")) > results.Count(result => result.Equals("normal")))
                            {
                                Console.WriteLine("Found more shiny frames than normal frames!"); 
                                Console.WriteLine();
                                Console.WriteLine("-----------------------------");
                                Console.WriteLine($"| Success on attempt #{attemptNumber}! |");
                                Console.WriteLine("-----------------------------");
                                Console.WriteLine();
                                Console.WriteLine("Shiny Hunter will exit.");                                    
                                GlobalCancellationTokenSource.Cancel();
                                throw new TaskCanceledException("Found more shiny frames than normal frames! Shiny Hunter will exit.");
                            }
                            else
                            {
                                Console.WriteLine("Found less shiny frames. ShinyHunter will retry in 3 seconds.");
                                await Task.Delay(5000);
                            }

                            SaveAttemptNumber(attemptNumber++);

                            Console.WriteLine();
                            Console.WriteLine("------------------------------------------------------------------------------");
                            Console.WriteLine();
                            Console.WriteLine($"Attempt #{attemptNumber}, retrying!");
                            RunCommand(shellStream, "", "Waiting for signal.", TimeSpan.FromMinutes(3), JoyControlErrors);
                        }
                    }
                    catch (Exception exception)
                    {
                        Console.WriteLine($"Received {exception.GetType()}: {exception.Message}\r\nWith stack trace: {exception.StackTrace}");
                        Console.WriteLine();
                        Console.WriteLine($"ShinyHunter will restart in 5 seconds.");
                        Console.WriteLine();
                        Console.WriteLine("------------------------------------------------------------------------------");
                        await Task.Delay(5000);
                    }
                    finally
                    {
                        if (shellStream != null)
                            shellStream.Dispose();
                    }
                }
            }, GlobalCancellationTokenSource.Token);

            GlobalCancellationTokenSource.Token.WaitHandle.WaitOne();
        }

        private static void SaveAttemptNumber(int attemptNumber)
        {
            XmlSerializer writer = new(typeof(int));

            Directory.CreateDirectory(SettingsPath);
            using FileStream file = File.Create(Path.Combine(SettingsPath, "attempts.xml"));

            writer.Serialize(file, attemptNumber);
            file.Close();
        }

        private static int GetAttemptNumber()
        {
            if (File.Exists(Path.Combine(SettingsPath, "attempts.xml")))
            {
                XmlSerializer reader = new(typeof(int));
                using StreamReader file = new(Path.Combine(SettingsPath, "attempts.xml"));
                int attemptNumber = (int?)reader.Deserialize(file) ?? 1;
                file.Close();
                return attemptNumber;
            }
            else
                return 1;
        }


        private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs consoleCancelEventArgs)
        {
            Console.WriteLine("Received cancelation key press. Exiting ShinyHunter.");
            consoleCancelEventArgs.Cancel = true;
            GlobalCancellationTokenSource.Cancel();
        }

        private static void ShellStream_ErrorOccurred(object? sender, ExceptionEventArgs exceptionEventArgs)
        {
            Console.WriteLine(exceptionEventArgs.Exception);
        }

        private static void ShellStream_DataReceived(object? sender, ShellDataEventArgs shellDataEventArgs)
        {
            string line = Encoding.UTF8.GetString(shellDataEventArgs.Data);
            LastLines.Enqueue(line);

            if (!string.IsNullOrEmpty(line))
            {
                Console.Write(line);

                foreach(string item in line.Split("\n"))
                {
                    if (!string.IsNullOrWhiteSpace(item.AlphaNumerics()))
                    {
                        if (WaitingForCancellationTokenSource != null && item.AlphaNumerics().Equals(WaitingFor.AlphaNumerics(), StringComparison.InvariantCultureIgnoreCase))
                        {
                            WaitingForCancellationTokenSource.Cancel();
                        }
                        if (ErrorCancellationTokenSource != null && (ErrorLines?.Any(errorLine => errorLine.AlphaNumerics().Equals(item.AlphaNumerics(), StringComparison.InvariantCultureIgnoreCase)) ?? false))
                        {
                            ErrorCancellationTokenSource.Cancel();
                        }
                    }
                }
            }
        }

        public static void RunCommand(ShellStream shellStream, string commandString, string? waitingFor = null, TimeSpan? timeout = null, string[]? errorLines = null)
        {
            if (timeout is null)
            {
                timeout = TimeSpan.Zero;
            }

            if (string.IsNullOrEmpty(waitingFor))
            {
                WaitingForCancellationTokenSource = null;
                WaitingFor = string.Empty;
                ErrorLines = null;
            }
            else
            {
                WaitingForCancellationTokenSource = new();
                WaitingFor = waitingFor;
                ErrorLines = errorLines;
            }

            ErrorCancellationTokenSource = new();
            Console.WriteLine();
            shellStream.WriteLine(commandString);

            if (WaitingForCancellationTokenSource != null && CancellationTokenSource.CreateLinkedTokenSource(WaitingForCancellationTokenSource.Token, ErrorCancellationTokenSource.Token, GlobalCancellationTokenSource.Token).Token.WaitHandle.WaitOne(timeout.Value))
            {
                if (GlobalCancellationTokenSource.Token.IsCancellationRequested)
                {
                    throw new TaskCanceledException();
                }
                else if (ErrorCancellationTokenSource.Token.IsCancellationRequested)
                {
                    throw new CommandException($"ERROR: There was an error while writing the command: \"{commandString}\".\n");
                }
            }
            else if (WaitingForCancellationTokenSource != null)
            {
                throw new CommandException($"ERROR: The waited line \"{waitingFor}\" expected after \"{commandString}\" was never written.\n");
            }
        }

        public static void RunWindowsCommand(string executable, string arguments, string workingDirectory)
        {
            ProcessStartInfo processStartInfo = new(executable);
            processStartInfo.Arguments = arguments;
            processStartInfo.WorkingDirectory = workingDirectory;
            Process? process = Process.Start(processStartInfo);
            process?.WaitForExit();
        }
    }
}