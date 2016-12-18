using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace DotnetPublishSsh
{
    internal sealed class Program
    {
        public static void Main(string[] args)
        {
            var options = PublishSshOptions.ParseArgs(args);
            if (options.PrintHelp)
            {
                PrintHelp();
                return;
            }

            PrepareOptions(options);

            var arguments = string.Join(" ", options.Args);

            if (!PublishLocal(arguments))
                return;

            var path = options.Path;
            var localPath = options.LocalPath;

            if (!path.EndsWith("/")) path = path + "/";

            localPath = Path.GetFullPath(localPath) + Path.DirectorySeparatorChar;

            var localFiles = GetLocalFiles(localPath);

            Console.WriteLine();
            Console.WriteLine($"Uploading {localFiles.Count} files to {options.User}@{options.User}:{options.Port}{options.Path}");

            var md5CheckSums = new Dictionary<string, string>();
            using (var md5 = MD5.Create())
            {
                foreach (var file in localFiles)
                {
                    using (var stream = File.OpenRead(file.RelativeName))
                    {
                        md5CheckSums.Add(file.RelativeName, BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", string.Empty));
                    }
                }
            }


            try
            {
                var uploader = new Uploader(options);
                Dictionary<string, string> md5CheckSumsServer = null;
                try
                {
                    string checkSums = null;
                    checkSums = uploader.DownloadFile(path + "/md5CheckSums.json");
                    md5CheckSumsServer = JsonConvert.DeserializeObject<Dictionary<string, string>>(checkSums);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error dowloading md5CheckSums from server: {ex.Message}");
                }
                if (md5CheckSumsServer != null)
                {
                    for (int i = 0; i < localFiles.Count; i++)
                    {
                        if (md5CheckSumsServer.ContainsKey(localFiles[i].RelativeName))
                        {
                            if(!md5CheckSumsServer[localFiles[i].RelativeName].Equals(md5CheckSums[localFiles[i].RelativeName]))
                            {
                                localFiles.RemoveAt(i);
                                i--;
                            }
                        }
                    }
                }
                uploader.UploadFiles(path, localFiles);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error uploading files to server: {ex.Message}");
            }
            Directory.Delete(localPath, true);
        }

        private static void PrepareOptions(PublishSshOptions options)
        {
            if (string.IsNullOrEmpty(options.LocalPath))
            {
                var tempPath = Path.Combine(Path.GetTempPath(), $"publish.{Guid.NewGuid()}");
                Directory.CreateDirectory(tempPath);
                options.LocalPath = tempPath;
            }

            options.Args = options.Args.Concat(new[] {"-o", options.LocalPath}).ToArray();
        }

        private static bool PublishLocal(string arguments)
        {
            Console.WriteLine($"Starting `dotnet {arguments}`");

            var info = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "publish " + arguments
            };

            var process = Process.Start(info);
            process.WaitForExit();
            var exitCode = process.ExitCode;

            Console.WriteLine($"dotnet publish exited with code {exitCode}");

            return exitCode == 0;
        }

        private static List<LocalFile> GetLocalFiles(string localPath)
        {
            var localFiles = Directory
                .EnumerateFiles(localPath, "*.*", SearchOption.AllDirectories)
                .Select(f => new LocalFile(localPath, f))
                .ToList();
            return localFiles;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Publish to remote server via SSH");
            Console.WriteLine();
            Console.WriteLine("Usage: dotnet publish-ssh [arguments] [options]");
            Console.WriteLine();
            Console.WriteLine("Arguments and options are the same as for `dotnet publish`");
            Console.WriteLine();
            Console.WriteLine("SSH specific options:");
            Console.WriteLine("  --ssh-host *              Host address");
            Console.WriteLine("  --ssh-port                Host port");
            Console.WriteLine("  --ssh-user *              User name");
            Console.WriteLine("  --ssh-password            Password");
            Console.WriteLine("  --ssh-path *              Publish path on remote server");
            Console.WriteLine();
        }
    }
}