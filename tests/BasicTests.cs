using Microsoft.VisualStudio.TestTools.UnitTesting;
using parallel_runner;
using System;
using System.IO;
namespace tests
{
    [TestClass]
    public class BasicTests
    {
        string tempFile;

        [TestInitialize]
        public void init()
        {
            tempFile = Path.GetTempFileName();
        }

        [TestCleanup]
        public void destroy()
        {
            System.IO.File.Delete(tempFile);
        }

        public void MakeCommandFile(string contents)
        {
            FileStream commandFileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096);
            StreamWriter writer = new StreamWriter(commandFileStream);
            writer.Write(contents);
            writer.Flush();
            writer.Close();
        }

        public Int32 RunTest(string[] args, string contents)
        {
            Environment.ExitCode = 0;
            MakeCommandFile(contents);
            Program.Main(args);
            return Environment.ExitCode;
        }

        public void ExpectPass(string[] args, string contents)
        {
            Assert.AreEqual(RunTest(args, contents), 0);
        }

        public void ExpectFail(string[] args, string contents)
        {
            Assert.AreNotEqual(RunTest(args, contents), 0);
        }

        [TestMethod]
        public void TestHelp()
        {
            ExpectPass(new string[] { "-?" }, "");
        }

        [TestMethod]
        public void TestInvalidArg()
        {
            ExpectFail(new string[] { "asdfjl" }, "");
        }

        [TestMethod]
        public void TestEmptyCommandFile()
        {
            ExpectPass(new string[] { "-file", tempFile }, "");
        }

        [TestMethod]
        public void TestPassCommandFiles()
        {
            ExpectPass(new string[] { "-file", tempFile }, @": test1 : cmd.exe /c echo test program");

            ExpectPass(new string[] { "-file", tempFile }, @"
: test1 : cmd.exe /c echo test program");

            ExpectPass(new string[] { "-file", tempFile }, @"
: test1 : cmd.exe /c echo test program
");

            ExpectPass(new string[] { "-file", tempFile }, @"
: test 1 : cmd.exe /c echo test program 1
: test 2 : cmd.exe /c echo test program 2
: test 3 : cmd.exe /c echo test program 3
");

        }

        [TestMethod]
        public void TestFailCommandFiles()
        {
            ExpectFail(new string[] { "-file", tempFile }, @": test1 : ljk;hasdflhjk");
            ExpectFail(new string[] { "-file", tempFile }, ": test1 : cmd.exe /c cmd.exe /c \"echo fail 1>&2\"");

        }
    }
}
