using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using CpuCount;

namespace Runner
{
    class Program
    {
        private static ReaderWriterLockSlim sProtectionMutex = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        private static Dictionary<Process, String> sProcessToName = new Dictionary<Process, String>();
        private static int sMaxLength = 0;
        private static string sVersion = "2.0.1";
        static void Main(string[] args)
        {
            bool printUsage = false;
            String commandsFileName = null;
            for (int i = 0; i < args.Length; i++)
            {
                String s = args[i].ToLower();
                if (s[0] == '/')
                {
                    s.Replace('/', '-');
                }

                if (s == "-v" || s == "-?" || s == "-version" || s == "--version")
                {
                    printUsage = true;
                }

                if (s == "-file")
                {
                    i++;
                    if (i < args.Length)
                    {
                        commandsFileName = args[i];
                    }
                }
            }

            if (printUsage || String.IsNullOrEmpty(commandsFileName) || !File.Exists(commandsFileName))
            {
                Console.WriteLine("Runner. Version:{0}", sVersion);
                Console.WriteLine(" Will run multiple command lines in parallel. Pass in a command file.");
                Console.WriteLine(" Each line in the file is a command line to execute in parallel.");
                Console.WriteLine(" There is also optionally a name for each process used when reporting.");
                Console.WriteLine("Usage: ");
                Console.WriteLine(" runner -file commandList.txt");
                Console.WriteLine("File Format: ");
                Console.WriteLine(": [name 1] : command1 args");
                Console.WriteLine(": [name 2] : command2 args");
                Console.WriteLine(": [name 3] : command3 args");

                return;
            }

            StreamReader processCommands = new StreamReader(commandsFileName);
            List<Process> processes = new List<Process>();

            int numCPUs = Machine.GetPhysicalProcessorCount();
            int numCores = Machine.GetPhysicalProcessorCores();
            int numThreads = Environment.ProcessorCount;
            Console.WriteLine("Num CPUs        = {0}", numCPUs.ToString());
            Console.WriteLine("Num CPU Cores   = {0}", numCores.ToString());
            Console.WriteLine("Num CPU Threads = {0}", numThreads.ToString());


            while (!processCommands.EndOfStream)
            {
                //-------------------------------------------------------------
                // Parse metadata and command line
                //-------------------------------------------------------------
                string processFile = null;
                string processName = null;
                string commandLine = processCommands.ReadLine().TrimStart();

                if (commandLine.StartsWith(":"))
                {
                    int endofName = commandLine.IndexOf(':', 1);
                    processName = commandLine.Substring(1, endofName - 1);
                    commandLine = commandLine.Remove(0, endofName + 1).TrimStart();
                }

                if (commandLine.StartsWith("\""))
                {
                    int endofName = commandLine.IndexOf('\"', 1);
                    processFile = commandLine.Substring(1, endofName - 1);
                    commandLine = commandLine.Remove(0, endofName + 1).TrimStart();
                }
                else
                {
                    int endofName = commandLine.IndexOf(' ', 1);
                    processFile = commandLine.Substring(0, endofName - 0);
                    commandLine = commandLine.Remove(0, endofName + 0).TrimStart();
                }

                //-------------------------------------------------------------
                // Build process description based on command line / metadata
                //-------------------------------------------------------------
                Process p = new Process();
                ProcessStartInfo psi = new ProcessStartInfo(processFile);
                psi.Arguments = commandLine;
                psi.CreateNoWindow = false;
                psi.UseShellExecute = false;
                psi.RedirectStandardInput = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                p.OutputDataReceived += new DataReceivedEventHandler(StdOutEventHandler);
                p.ErrorDataReceived += new DataReceivedEventHandler(StdErrEventHandler);
                p.StartInfo = psi;

                //-------------------------------------------------------------
                // Add to List
                //-------------------------------------------------------------
                processes.Add(p);
                String name = String.IsNullOrWhiteSpace(processName) ? p.Id.ToString() : processName;
                sProcessToName.Add(p, name);
                if (name.Length > sMaxLength)
                {
                    sMaxLength = name.Length;
                }
            }
            processCommands.Close();

            Mutex ExceptionMutex = new Mutex();
            System.Text.StringBuilder ExceptionInfo = new System.Text.StringBuilder();
            //-------------------------------------------------------------
            // Run Them!
            //-------------------------------------------------------------
            Parallel.ForEach(processes, p =>
            {
                try
                {
                    p.Start();
                    sProtectionMutex.EnterWriteLock();
                    Console.WriteLine("{0," + sMaxLength + "}:{1,8}-START", sProcessToName[p], p.StartTime.ToShortTimeString());
                    sProtectionMutex.ExitWriteLock();

                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    p.WaitForExit();
                    sProtectionMutex.EnterWriteLock();
                    Console.WriteLine("{0," + sMaxLength + "}:{1,8}-Exit Code:{2}", sProcessToName[p], p.ExitTime.ToShortTimeString(), p.ExitCode.ToString());
                    sProtectionMutex.ExitWriteLock();
                }
                catch (Exception e)
                {
                    ExceptionMutex.WaitOne();
                    ExceptionInfo.AppendFormat("{0," + sMaxLength + "}:{1,8}-EXCEPTION:{2}", sProcessToName[p], DateTime.Now.ToShortTimeString(), e.ToString());
                    ExceptionInfo.AppendLine();
                    sProcessToName.Remove(p);
                    ExceptionMutex.ReleaseMutex();
                }

            } //close lambda expression
                 ); //close method invocation

            Console.WriteLine("");
            Console.WriteLine("[All Operations Completed]");
            Console.WriteLine("Summary:");
            foreach (KeyValuePair<Process, String> p in sProcessToName)
            {
                if (p.Key.ExitCode != 0)
                {
                    BeginWriteError();
                    Console.Error.Write("{0," + sMaxLength + "}:{1,8}s ExitCode:{2}", p.Value, (p.Key.ExitTime - p.Key.StartTime).Seconds.ToString(), p.Key.ExitCode.ToString());
                    EndWriteError();
                    // This finishes off the color change
                    Console.Error.WriteLine();
                }
                else
                {
                    Console.WriteLine("{0," + sMaxLength + "}:{1,8}s ExitCode:{2}", p.Value, (p.Key.ExitTime - p.Key.StartTime).Seconds.ToString(), p.Key.ExitCode.ToString());
                }

            }
            if (ExceptionInfo.Length != 0)
            {
                BeginWriteError();
                Console.Error.Write(ExceptionInfo);
                EndWriteError();
            }
        }

        static ConsoleColor sSavedBgColor = ConsoleColor.Black;
        static ConsoleColor sSavedFgColor = ConsoleColor.White;
        private static void BeginWriteError()
        {
            sSavedBgColor = Console.BackgroundColor;
            sSavedFgColor = Console.ForegroundColor;
            Console.BackgroundColor = ConsoleColor.DarkRed;
            Console.ForegroundColor = ConsoleColor.White;
        }
        private static void EndWriteError()
        {
            Console.BackgroundColor = sSavedBgColor;
            Console.ForegroundColor = sSavedFgColor;
        }

        private static void StdOutEventHandler(object sendingProcess,
            DataReceivedEventArgs outLine)
        {
            Process p = (Process)sendingProcess;
            if (!String.IsNullOrEmpty(outLine.Data))
            {
                sProtectionMutex.EnterWriteLock();
                Console.Write("{0," + sMaxLength + "}:{1,8}-", sProcessToName[p], DateTime.Now.ToShortTimeString());
                Console.WriteLine(outLine.Data);
                sProtectionMutex.ExitWriteLock();
            }
        }

        private static void StdErrEventHandler(object sendingProcess,
            DataReceivedEventArgs outLine)
        {
            Process p = (Process)sendingProcess;
            if (!String.IsNullOrEmpty(outLine.Data))
            {
                sProtectionMutex.EnterWriteLock();
                BeginWriteError();

                Console.Error.Write("{0," + sMaxLength + "}:{1,8}-", sProcessToName[p], DateTime.Now.ToShortTimeString());
                Console.Error.Write(outLine.Data);

                EndWriteError();
                // This finishes off the color change
                Console.Error.WriteLine();
                sProtectionMutex.ExitWriteLock();
            }
        }
    }

}
